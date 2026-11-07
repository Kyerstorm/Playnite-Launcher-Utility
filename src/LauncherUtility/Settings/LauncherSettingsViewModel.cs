using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Playnite.SDK;
using LauncherUtility.Core.Models;
using LauncherUtility.UI;

namespace LauncherUtility.Settings
{
    /// <summary>One row of the tools list on the settings page.</summary>
    public sealed class SettingsToolRow
    {
        internal SettingsToolRow(LauncherItem item) => Item = item;

        internal LauncherItem Item { get; }

        public string Name => string.IsNullOrWhiteSpace(Item.Name) ? "(unnamed)" : Item.Name;

        public string Source => Item.Origin == ToolOrigin.SoftwareTool ? "Software Tool" : "Launcher";

        public string Category => Item.Category;

        public string Status
        {
            get
            {
                var parts = new List<string>();
                if (Item.Hidden)
                {
                    parts.Add("hidden");
                }

                if (Item.IsScript)
                {
                    parts.Add("script (not supported)");
                }

                if (Item.RunAsAdmin)
                {
                    parts.Add("admin");
                }

                var rule = Item.AutoLaunch;
                if (rule != null && rule.IsActive)
                {
                    var when = new List<string>();
                    if (rule.StartWithPlaynite)
                    {
                        when.Add("with Playnite");
                    }

                    if (rule.StartWithGame)
                    {
                        when.Add(rule.Conditions.Count == 0 ? "with every game" : "with matching games");
                    }

                    parts.Add("auto: " + string.Join(", ", when));
                }

                return string.Join(" · ", parts);
            }
        }

        public bool CanRemove => Item.Origin == ToolOrigin.Launcher;

        public bool IsSoftwareTool => Item.Origin == ToolOrigin.SoftwareTool;
    }

    /// <summary>Playnite settings contract (BeginEdit / CancelEdit / EndEdit / VerifySettings) over a working copy of the data.</summary>
    public sealed class LauncherSettingsViewModel : BindableBase, ISettings
    {
        private readonly LauncherContext context;
        private LauncherData editing;
        private SettingsToolRow selectedTool;

        internal LauncherSettingsViewModel(LauncherContext context)
        {
            this.context = context;
            editing = context.Data.Clone();

            AddToolCommand = new DelegateCommand(_ => AddTool());
            EditToolCommand = new DelegateCommand(p => EditTool(p as SettingsToolRow ?? SelectedTool), p => (p as SettingsToolRow ?? SelectedTool) != null);
            RemoveToolCommand = new DelegateCommand(p => RemoveTool(p as SettingsToolRow ?? SelectedTool), p => (p as SettingsToolRow ?? SelectedTool)?.CanRemove == true);
            ToggleHiddenCommand = new DelegateCommand(p => ToggleHidden(p as SettingsToolRow ?? SelectedTool), p => (p as SettingsToolRow ?? SelectedTool)?.IsSoftwareTool == true);
        }

        public ObservableCollection<SettingsToolRow> Tools { get; } = new ObservableCollection<SettingsToolRow>();

        public SettingsToolRow SelectedTool { get => selectedTool; set => Set(ref selectedTool, value); }

        public ICommand AddToolCommand { get; }

        public ICommand EditToolCommand { get; }

        public ICommand RemoveToolCommand { get; }

        public ICommand ToggleHiddenCommand { get; }

        public bool IncludeSoftwareTools
        {
            get => editing.IncludeSoftwareTools;
            set
            {
                editing.IncludeSoftwareTools = value;
                OnPropertyChanged();
                RefreshRows();
            }
        }

        public bool CloseAfterLaunch { get => editing.CloseAfterLaunch; set { editing.CloseAfterLaunch = value; OnPropertyChanged(); } }

        public bool GroupByCategory { get => editing.GroupByCategory; set { editing.GroupByCategory = value; OnPropertyChanged(); } }

        public bool RememberWindowBounds { get => editing.RememberWindowBounds; set { editing.RememberWindowBounds = value; OnPropertyChanged(); } }

        public int CloseTimeoutSeconds
        {
            get => editing.CloseTimeoutSeconds;
            set
            {
                editing.CloseTimeoutSeconds = Math.Max(LauncherData.MinCloseTimeoutSeconds, Math.Min(LauncherData.MaxCloseTimeoutSeconds, value));
                OnPropertyChanged();
            }
        }

        public string SoftwareToolsStatus { get; private set; } = string.Empty;

        public void BeginEdit()
        {
            editing = context.Data.Clone();
            RaiseAll();
        }

        public void CancelEdit()
        {
            editing = context.Data.Clone();
            RaiseAll();
        }

        public void EndEdit()
        {
            // Keep the popup's live window position; it is saved outside the settings dialog.
            editing.WindowLeft = context.Data.WindowLeft;
            editing.WindowTop = context.Data.WindowTop;
            editing.WindowWidth = context.Data.WindowWidth;
            editing.WindowHeight = context.Data.WindowHeight;
            context.ReplaceData(editing);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = editing.Tools
                .Where(t => string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Path))
                .Select(t => $"Tool \"{(string.IsNullOrWhiteSpace(t.Name) ? "(unnamed)" : t.Name)}\" needs both a name and a program path.")
                .ToList();
            return errors.Count == 0;
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(IncludeSoftwareTools));
            OnPropertyChanged(nameof(CloseAfterLaunch));
            OnPropertyChanged(nameof(GroupByCategory));
            OnPropertyChanged(nameof(RememberWindowBounds));
            OnPropertyChanged(nameof(CloseTimeoutSeconds));
            RefreshRows();
        }

        private void RefreshRows()
        {
            var previous = SelectedTool?.Item.Id;
            var items = context.GetCatalog(editing, includeHidden: true);
            Tools.Clear();
            foreach (var item in items)
            {
                Tools.Add(new SettingsToolRow(item));
            }

            SelectedTool = Tools.FirstOrDefault(t => t.Item.Id == previous);

            var softwareCount = items.Count(i => i.Origin == ToolOrigin.SoftwareTool);
            SoftwareToolsStatus = !editing.IncludeSoftwareTools
                ? "Software Tools are not shown in the Launcher."
                : context.SoftwareTools.LastError != null
                    ? "Playnite's Software Tools could not be read: " + context.SoftwareTools.LastError
                    : $"{softwareCount} Software Tool(s) found in Library → Software Tools.";
            OnPropertyChanged(nameof(SoftwareToolsStatus));
        }

        private IEnumerable<string> KnownCategories() =>
            editing.Tools.Select(t => t.Category).Concat(editing.Overrides.Select(o => o.Category));

        private void AddTool()
        {
            var entry = new ToolEntry();
            var vm = ToolEditorViewModel.ForEntry(context, entry, KnownCategories());
            if (ShowEditor(vm))
            {
                vm.ApplyTo(entry);
                editing.Tools.Add(entry);
                RefreshRows();
                SelectedTool = Tools.FirstOrDefault(t => t.Item.Id == entry.Id);
            }
        }

        private void EditTool(SettingsToolRow row)
        {
            if (row == null)
            {
                return;
            }

            if (row.IsSoftwareTool)
            {
                var existing = editing.Overrides.FirstOrDefault(o => o.SoftwareToolId == row.Item.Id);
                var working = existing?.Clone() ?? new ToolOverride { SoftwareToolId = row.Item.Id };
                var vm = ToolEditorViewModel.ForSoftwareTool(context, row.Item, working, KnownCategories());
                if (ShowEditor(vm))
                {
                    vm.ApplyTo(working);
                    editing.Overrides.RemoveAll(o => o.SoftwareToolId == row.Item.Id);
                    editing.Overrides.Add(working);
                    RefreshRows();
                }
            }
            else
            {
                var entry = editing.Tools.FirstOrDefault(t => t.Id == row.Item.Id);
                if (entry == null)
                {
                    return;
                }

                var vm = ToolEditorViewModel.ForEntry(context, entry, KnownCategories());
                if (ShowEditor(vm))
                {
                    vm.ApplyTo(entry);
                    RefreshRows();
                }
            }
        }

        private void RemoveTool(SettingsToolRow row)
        {
            if (row == null || !row.CanRemove)
            {
                return;
            }

            var answer = context.Api.Dialogs.ShowMessage($"Remove \"{row.Name}\" from the Launcher?", LauncherContext.Title, MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            editing.Tools.RemoveAll(t => t.Id == row.Item.Id);
            RefreshRows();
        }

        private void ToggleHidden(SettingsToolRow row)
        {
            if (row == null || !row.IsSoftwareTool)
            {
                return;
            }

            var options = editing.GetOrAddOverride(row.Item.Id);
            options.Hidden = !options.Hidden;
            RefreshRows();
        }

        private bool ShowEditor(ToolEditorViewModel vm)
        {
            var window = context.Api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });
            window.Title = vm.WindowTitle;
            window.Width = 760;
            window.Height = 720;
            window.MinWidth = 560;
            window.MinHeight = 420;
            window.Content = new ToolEditorView { DataContext = vm };
            window.Owner = context.Api.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            vm.CloseRequested += ok =>
            {
                window.DialogResult = ok;
            };
            return window.ShowDialog() == true;
        }
    }
}
