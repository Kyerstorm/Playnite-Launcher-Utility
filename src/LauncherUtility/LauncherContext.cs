using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using LauncherUtility.Core.Models;
using LauncherUtility.Core.Persistence;
using LauncherUtility.Core.Services;
using LauncherUtility.Integration;
using LauncherUtility.UI;

namespace LauncherUtility
{
    /// <summary>
    /// The single owner of Launcher state: saved data, the merged catalog, launching, the popup window
    /// and the auto-launch coordinator. Everything else talks to Playnite through this class.
    /// </summary>
    internal sealed class LauncherContext : IDisposable
    {
        public const string Title = "Launcher Utility";
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly GenericPlugin plugin;
        private readonly JsonDataStore store;
        private readonly ToolActivator activator;
        private LauncherData data;
        private string loadWarning;
        private Window launcherWindow;

        public LauncherContext(GenericPlugin plugin, IPlayniteAPI api)
        {
            this.plugin = plugin;
            Api = api;
            Launcher = new WindowsProcessLauncher(SafePlayniteDir(api));
            Icons = new IconCache(Launcher);
            SoftwareTools = new SoftwareToolsReflectionSource(api);
            activator = new ToolActivator(Launcher);
            Coordinator = new AutoLaunchCoordinator(Launcher, ReportAutoLaunchProblem);

            var dataDir = plugin.GetPluginUserDataPath();
            store = new JsonDataStore(Path.Combine(dataDir, "launcher.json"));
            try
            {
                if (store.AdoptLegacyFile(Path.Combine(dataDir, "toolbox.json")))
                {
                    Logger.Info("Moved toolbox.json to launcher.json after the rename to Launcher Utility.");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Could not move toolbox.json to launcher.json.");
            }

            var loaded = store.Load();
            data = loaded.Data;
            loadWarning = loaded.Warning;
        }

        /// <summary>Raised on the UI thread after the saved data changed (settings saved, game menu toggle).</summary>
        public event EventHandler DataChanged;

        public IPlayniteAPI Api { get; }

        public WindowsProcessLauncher Launcher { get; }

        public IconCache Icons { get; }

        public SoftwareToolsReflectionSource SoftwareTools { get; }

        public AutoLaunchCoordinator Coordinator { get; }

        /// <summary>The live saved data. Edit through <see cref="ReplaceData"/> or followed by <see cref="Save"/>.</summary>
        public LauncherData Data => data;

        public List<LauncherItem> GetCatalog(bool includeHidden = false) => GetCatalog(data, includeHidden);

        public List<LauncherItem> GetCatalog(LauncherData source, bool includeHidden)
        {
            var softwareTools = source.IncludeSoftwareTools ? SoftwareTools.Read() : new List<SoftwareToolInfo>();
            return ToolCatalogMerger.Merge(softwareTools, source, includeHidden);
        }

        public void ReplaceData(LauncherData newData)
        {
            data = JsonDataStore.Sanitize(newData.Clone());
            Icons.Clear();
            Save();
        }

        public void Save()
        {
            try
            {
                store.Save(data);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility could not save its settings.");
                Api.Notifications.Add(new NotificationMessage("LauncherUtility_SaveFailed", Title + ": settings could not be saved. " + e.Message, NotificationType.Error));
            }

            RaiseDataChanged();
        }

        public void Initialize()
        {
            if (loadWarning != null)
            {
                Api.Notifications.Add(new NotificationMessage("LauncherUtility_LoadWarning", Title + ": " + loadWarning, NotificationType.Error));
                loadWarning = null;
            }

            var catalog = GetCatalog();
            Task.Run(() => Coordinator.OnPlayniteStarted(catalog)).ContinueWith(LogFault, TaskContinuationOptions.OnlyOnFaulted);
        }

        public void OnGameStarting(Game game)
        {
            if (game == null)
            {
                return;
            }

            var catalog = GetCatalog();
            if (!catalog.Any(i => i.AutoLaunch != null && i.AutoLaunch.Enabled && i.AutoLaunch.StartWithGame))
            {
                return;
            }

            Coordinator.OnGameStartingAsync(new PlayniteGameFacts(game, Api.Database), catalog)
                .ContinueWith(LogFault, TaskContinuationOptions.OnlyOnFaulted);
        }

        public void OnGameStopped(Game game)
        {
            if (game == null)
            {
                return;
            }

            Coordinator.OnGameStoppedAsync(game.Id, TimeSpan.FromSeconds(data.CloseTimeoutSeconds))
                .ContinueWith(LogFault, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Launches (or focuses) a tool. Returns true when the tool is now in front of the user.</summary>
        public bool Launch(LauncherItem item)
        {
            ActivationResult result;
            try
            {
                result = activator.Activate(item);
            }
            catch (Exception e)
            {
                result = new ActivationResult(ActivationStatus.Failed, e.Message);
            }

            switch (result.Status)
            {
                case ActivationStatus.Started:
                case ActivationStatus.Focused:
                    Api.Notifications.Remove("LauncherUtility_Launch_" + item.Id);
                    return true;
                case ActivationStatus.Cancelled:
                    return false;
                case ActivationStatus.NotLaunchable:
                    Api.Dialogs.ShowMessage(item != null && item.IsScript
                        ? "Script-type Software Tools are not supported by Launcher Utility yet. Run it from Library → Software Tools."
                        : "This tool has no path set. Edit it in Launcher Utility settings.", Title);
                    return false;
                default:
                    Logger.Warn($"Launcher Utility could not start {item?.Name}: {result.Error}");
                    Api.Notifications.Add(new NotificationMessage(
                        "LauncherUtility_Launch_" + item?.Id,
                        $"{Title}: could not start {item?.Name}. {result.Error}",
                        NotificationType.Error,
                        () => OpenSettings()));
                    return false;
            }
        }

        public void OpenSettings() => RunOnUi(() => plugin.OpenSettingsView());

        public void OpenLauncher()
        {
            RunOnUi(() =>
            {
                if (launcherWindow != null)
                {
                    if (launcherWindow.WindowState == WindowState.Minimized)
                    {
                        launcherWindow.WindowState = WindowState.Normal;
                    }

                    launcherWindow.Activate();
                    return;
                }

                var window = Api.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = true,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true
                });
                var viewModel = new LauncherViewModel(this);
                var view = new LauncherView { DataContext = viewModel };

                window.Title = Title;
                window.Width = 440;
                window.Height = 580;
                window.MinWidth = 300;
                window.MinHeight = 320;
                window.Content = view;
                window.Owner = Api.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                RestoreBounds(window);

                viewModel.CloseRequested += (s, e) => window.Close();
                window.Loaded += (s, e) => view.FocusSearch();
                window.Closing += (s, e) => RememberBounds(window);
                window.Closed += (s, e) =>
                {
                    viewModel.Dispose();
                    launcherWindow = null;
                };

                launcherWindow = window;
                window.Show();
            });
        }

        /// <summary>Game menu toggle: adds or removes a "this specific game" condition on the tool's rule.</summary>
        public void ToggleAutoLaunchForGame(LauncherItem item, Game game)
        {
            var rule = data.GetOrAddRule(item);
            if (rule == null)
            {
                return;
            }

            var existing = rule.Conditions.FirstOrDefault(c => c.Type == ConditionType.SpecificGame && c.ItemId == game.Id);
            if (existing != null)
            {
                rule.Conditions.Remove(existing);
                if (rule.Conditions.Count == 0)
                {
                    rule.StartWithGame = false; // An empty list would otherwise match every game.
                }
            }
            else
            {
                if (rule.Conditions.Count > 0)
                {
                    rule.Conditions[rule.Conditions.Count - 1].Joiner = Joiner.Or;
                }

                rule.Conditions.Add(new RuleCondition { Type = ConditionType.SpecificGame, ItemId = game.Id, Text = game.Name ?? string.Empty });
                rule.Enabled = true;
                rule.StartWithGame = true;
            }

            Save();
        }

        public static bool IsAutoLaunchedFor(LauncherItem item, Guid gameId) =>
            item.AutoLaunch != null && item.AutoLaunch.Enabled && item.AutoLaunch.StartWithGame &&
            item.AutoLaunch.Conditions.Any(c => c.Type == ConditionType.SpecificGame && c.ItemId == gameId);

        public void Dispose()
        {
            RunOnUi(() => launcherWindow?.Close());
        }

        private void ReportAutoLaunchProblem(LauncherItem item, string message)
        {
            Logger.Warn($"Launcher Utility auto-launch problem with {item?.Name}: {message}");
            Api.Notifications.Add(new NotificationMessage(
                "LauncherUtility_Auto_" + item?.Id,
                $"{Title}: {item?.Name}: {message}",
                NotificationType.Error));
        }

        private void RestoreBounds(Window window)
        {
            if (!data.RememberWindowBounds)
            {
                return;
            }

            if (data.WindowWidth.HasValue && data.WindowHeight.HasValue)
            {
                window.Width = data.WindowWidth.Value;
                window.Height = data.WindowHeight.Value;
            }

            if (data.WindowLeft.HasValue && data.WindowTop.HasValue)
            {
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var titleBar = new Rect(data.WindowLeft.Value, data.WindowTop.Value, Math.Max(100, window.Width), 40);
                if (screen.IntersectsWith(titleBar))
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = data.WindowLeft.Value;
                    window.Top = data.WindowTop.Value;
                }
            }
        }

        private void RememberBounds(Window window)
        {
            if (!data.RememberWindowBounds)
            {
                return;
            }

            var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            if (bounds.IsEmpty)
            {
                return;
            }

            data.WindowLeft = bounds.Left;
            data.WindowTop = bounds.Top;
            data.WindowWidth = bounds.Width;
            data.WindowHeight = bounds.Height;
            try
            {
                store.Save(data); // Quiet save: window position is not a settings change worth an event.
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Launcher Utility could not save the window position.");
            }
        }

        private void RaiseDataChanged() => RunOnUi(() => DataChanged?.Invoke(this, EventArgs.Empty));

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        private static void LogFault(Task task) => Logger.Error(task.Exception, "Launcher Utility background task failed.");

        private static string SafePlayniteDir(IPlayniteAPI api)
        {
            try
            {
                return api.Paths.ApplicationPath;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
