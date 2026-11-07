using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using LauncherUtility.Settings;

namespace LauncherUtility
{
    /// <summary>
    /// Playnite entry point: a sidebar button that opens the Launcher popup, main and game menu entries,
    /// auto-launch hooks on game and application events, and the settings page.
    /// </summary>
    public class LauncherUtilityPlugin : GenericPlugin
    {
        private const string MainSection = "@" + LauncherContext.Title;
        private const string GameSection = LauncherContext.Title;
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly LauncherSettingsViewModel settingsViewModel;

        public LauncherUtilityPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            Context = new LauncherContext(this, api);
            settingsViewModel = new LauncherSettingsViewModel(Context);
        }

        public override Guid Id { get; } = Guid.Parse("9fb41b47-eabd-46cf-804b-0262d1ec364e");

        internal LauncherContext Context { get; }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                Context.Initialize();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility failed to initialise.");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) => Context.Dispose();

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            try
            {
                Context.OnGameStarting(args.Game);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility auto-launch failed on game start.");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                Context.OnGameStopped(args.Game);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility auto-close failed on game stop.");
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop)
            {
                yield break;
            }

            yield return new SidebarItem
            {
                Title = LauncherContext.Title,
                Type = SiderbarItemType.Button,
                Icon = CreateSidebarIcon(),
                Activated = () => Context.OpenLauncher()
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var items = new List<MainMenuItem>
            {
                new MainMenuItem { Description = "Open Launcher", MenuSection = MainSection, Action = _ => Context.OpenLauncher() }
            };

            try
            {
                foreach (var tool in Context.GetCatalog().Where(t => t.CanLaunch))
                {
                    var captured = tool;
                    items.Add(new MainMenuItem { Description = tool.Name, MenuSection = MainSection + "|Tools", Action = _ => Context.Launch(captured) });
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility failed to build its main menu.");
            }

            items.Add(new MainMenuItem { Description = "Settings", MenuSection = MainSection, Action = _ => Context.OpenSettings() });
            return items;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var items = new List<GameMenuItem>();
            if (args.Games == null || args.Games.Count == 0)
            {
                return items;
            }

            try
            {
                var catalog = Context.GetCatalog().Where(t => t.CanLaunch).ToList();
                items.Add(new GameMenuItem { Description = "Open Launcher", MenuSection = GameSection, Action = _ => Context.OpenLauncher() });

                foreach (var tool in catalog)
                {
                    var captured = tool;
                    items.Add(new GameMenuItem { Description = tool.Name, MenuSection = GameSection + "|Launch", Action = _ => Context.Launch(captured) });
                }

                if (args.Games.Count == 1)
                {
                    var game = args.Games[0];
                    foreach (var tool in catalog)
                    {
                        var captured = tool;
                        var on = LauncherContext.IsAutoLaunchedFor(tool, game.Id);
                        items.Add(new GameMenuItem
                        {
                            Description = (on ? "✓ " : string.Empty) + tool.Name,
                            MenuSection = GameSection + "|Auto-launch with this game",
                            Action = _ => Context.ToggleAutoLaunchForGame(captured, game)
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Launcher Utility failed to build its game menu.");
            }

            return items;
        }

        public override ISettings GetSettings(bool firstRunSettings) => settingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new LauncherSettingsView { DataContext = settingsViewModel };

        /// <summary>A launcher glyph drawn with the theme's text brush so it matches Playnite's other sidebar icons.</summary>
        private static FrameworkElement CreateSidebarIcon()
        {
            var path = new Path
            {
                Data = Geometry.Parse("M3,9 L21,9 L21,20 L3,20 Z M8,9 L8,6 Q8,4 10,4 L14,4 Q16,4 16,6 L16,9 M3,13 L21,13 M10,12 L14,12 L14,15 L10,15 Z"),
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round
            };
            path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Child = canvas, Width = 24, Height = 24 };
        }
    }
}
