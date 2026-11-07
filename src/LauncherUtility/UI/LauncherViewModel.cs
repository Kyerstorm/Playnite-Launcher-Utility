using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LauncherUtility.Core.Models;
using LauncherUtility.Core.Services;

namespace LauncherUtility.UI
{
    /// <summary>One row in the Launcher list.</summary>
    internal sealed class ToolRowViewModel : BindableBase
    {
        private readonly LauncherContext context;
        private bool isRunning;
        private ImageSource icon;
        private bool iconLoaded;

        public ToolRowViewModel(LauncherItem item, LauncherContext context)
        {
            Item = item;
            this.context = context;
        }

        public LauncherItem Item { get; }

        public string Name => Item.Name;

        public string Category => Item.Category;

        public string GroupLabel => ToolSearch.GroupLabel(Item);

        public bool HasCategory => !string.IsNullOrWhiteSpace(Item.Category);

        public bool CanLaunch => Item.CanLaunch;

        public bool RunAsAdmin => Item.RunAsAdmin;

        public string ToolTip => Item.IsScript
            ? "Script-type Software Tools are not supported yet."
            : string.IsNullOrWhiteSpace(Item.Path) ? "No path set." : Item.Path + (string.IsNullOrWhiteSpace(Item.Arguments) ? string.Empty : " " + Item.Arguments);

        public ImageSource Icon
        {
            get
            {
                if (!iconLoaded)
                {
                    iconLoaded = true;
                    icon = context.Icons.Get(Item);
                }

                return icon;
            }
        }

        public bool IsRunning
        {
            get => isRunning;
            set => Set(ref isRunning, value);
        }
    }

    /// <summary>The Launcher popup: a searchable list of tools, launched with one click.</summary>
    internal sealed class LauncherViewModel : BindableBase, IDisposable
    {
        private readonly LauncherContext context;
        private readonly DispatcherTimer runningTimer;
        private List<LauncherItem> allItems = new List<LauncherItem>();
        private string query = string.Empty;
        private ToolRowViewModel selected;
        private bool refreshingRunning;

        public LauncherViewModel(LauncherContext context)
        {
            this.context = context;
            View = CollectionViewSource.GetDefaultView(Rows);
            LaunchCommand = new DelegateCommand(p => Launch(p as ToolRowViewModel ?? Selected));
            OpenSettingsCommand = new DelegateCommand(_ => context.OpenSettings());

            context.DataChanged += OnDataChanged;
            runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            runningTimer.Tick += (s, e) => RefreshRunning();
            Reload();
            runningTimer.Start();
        }

        public event EventHandler CloseRequested;

        public ObservableCollection<ToolRowViewModel> Rows { get; } = new ObservableCollection<ToolRowViewModel>();

        public ICollectionView View { get; }

        public ICommand LaunchCommand { get; }

        public ICommand OpenSettingsCommand { get; }

        public string Query
        {
            get => query;
            set
            {
                if (Set(ref query, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(IsQueryEmpty));
                    ApplyFilter();
                }
            }
        }

        public bool IsQueryEmpty => string.IsNullOrEmpty(query);

        public ToolRowViewModel Selected
        {
            get => selected;
            set => Set(ref selected, value);
        }

        public bool GroupByCategory
        {
            get => context.Data.GroupByCategory;
            set
            {
                if (context.Data.GroupByCategory == value)
                {
                    return;
                }

                context.Data.GroupByCategory = value;
                context.Save(); // Raises DataChanged, which re-applies the filter.
            }
        }

        public bool IsEmpty => Rows.Count == 0;

        public string EmptyMessage => allItems.Count == 0
            ? "No tools yet.\nAdd them in Library → Software Tools, or in Launcher Utility settings."
            : "No tools match \"" + query + "\".";

        public void MoveSelection(int delta)
        {
            var ordered = View.Cast<ToolRowViewModel>().ToList();
            if (ordered.Count == 0)
            {
                return;
            }

            var index = Selected == null ? -1 : ordered.IndexOf(Selected);
            index = Math.Max(0, Math.Min(ordered.Count - 1, index + delta));
            Selected = ordered[index];
        }

        public void LaunchSelected() => Launch(Selected ?? View.Cast<ToolRowViewModel>().FirstOrDefault(r => r.CanLaunch));

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public void Launch(ToolRowViewModel row)
        {
            if (row == null)
            {
                return;
            }

            if (context.Launch(row.Item))
            {
                if (context.Data.CloseAfterLaunch)
                {
                    RequestClose();
                    return;
                }

                row.IsRunning = row.Item.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || row.IsRunning;
            }
        }

        public void Dispose()
        {
            runningTimer.Stop();
            context.DataChanged -= OnDataChanged;
        }

        private void OnDataChanged(object sender, EventArgs e) => Reload();

        private void Reload()
        {
            allItems = context.GetCatalog();
            OnPropertyChanged(nameof(GroupByCategory));
            ApplyFilter();
            RefreshRunning();
        }

        private void ApplyFilter()
        {
            var running = new HashSet<Guid>(Rows.Where(r => r.IsRunning).Select(r => r.Item.Id));
            var previous = Selected?.Item.Id;
            var group = context.Data.GroupByCategory;

            Rows.Clear();
            foreach (var item in ToolSearch.Filter(allItems, query, group))
            {
                Rows.Add(new ToolRowViewModel(item, context) { IsRunning = running.Contains(item.Id) });
            }

            View.GroupDescriptions.Clear();
            if (group)
            {
                View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolRowViewModel.GroupLabel)));
            }

            // While typing, the best match is preselected so Enter launches it; otherwise keep the user's selection.
            var keep = string.IsNullOrEmpty(query) ? Rows.FirstOrDefault(r => r.Item.Id == previous) : null;
            Selected = keep ?? View.Cast<ToolRowViewModel>().FirstOrDefault(r => r.CanLaunch);
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyMessage));
        }

        private async void RefreshRunning()
        {
            if (refreshingRunning || allItems.Count == 0)
            {
                return;
            }

            refreshingRunning = true;
            try
            {
                var snapshot = allItems.ToList();
                var running = await Task.Run(() => context.Launcher.GetRunningToolIds(snapshot));
                foreach (var row in Rows)
                {
                    row.IsRunning = running.Contains(row.Item.Id);
                }
            }
            catch
            {
                // A failed refresh just leaves the dots as they were; the next tick tries again.
            }
            finally
            {
                refreshingRunning = false;
            }
        }
    }
}
