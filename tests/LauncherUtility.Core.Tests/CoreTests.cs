using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;
using LauncherUtility.Core.Persistence;
using LauncherUtility.Core.Services;
using Xunit;

namespace LauncherUtility.Core.Tests
{
    public class CatalogMergerTests
    {
        [Fact]
        public void Merges_both_sources_sorted_by_name_and_applies_overrides()
        {
            var swId = Guid.NewGuid();
            var data = new LauncherData
            {
                Tools = { new ToolEntry { Name = "obs", Path = @"C:\obs.exe", Category = "Streaming" } },
                Overrides = { new ToolOverride { SoftwareToolId = swId, Category = "Pokemon", RunAsAdmin = true, SingleInstance = false } }
            };
            var sw = new[] { new SoftwareToolInfo { Id = swId, Name = " PKHeX ", Path = @"C:\pkhex.exe" } };

            var result = ToolCatalogMerger.Merge(sw, data);

            Assert.Equal(new[] { "obs", "PKHeX" }, result.Select(r => r.Name));
            var pk = result.Single(r => r.Id == swId);
            Assert.Equal(ToolOrigin.SoftwareTool, pk.Origin);
            Assert.Equal("Pokemon", pk.Category);
            Assert.True(pk.RunAsAdmin);
            Assert.False(pk.SingleInstance);
        }

        [Fact]
        public void Hidden_software_tools_are_excluded_unless_requested()
        {
            var swId = Guid.NewGuid();
            var data = new LauncherData { Overrides = { new ToolOverride { SoftwareToolId = swId, Hidden = true } } };
            var sw = new[] { new SoftwareToolInfo { Id = swId, Name = "X", Path = "x.exe" } };

            Assert.Empty(ToolCatalogMerger.Merge(sw, data));
            Assert.Single(ToolCatalogMerger.Merge(sw, data, includeHidden: true));
        }

        [Fact]
        public void Software_tools_can_be_switched_off()
        {
            var data = new LauncherData { IncludeSoftwareTools = false, Tools = { new ToolEntry { Name = "Mine", Path = "m.exe" } } };
            var sw = new[] { new SoftwareToolInfo { Id = Guid.NewGuid(), Name = "Theirs", Path = "t.exe" } };

            Assert.Equal(new[] { "Mine" }, ToolCatalogMerger.Merge(sw, data).Select(r => r.Name));
        }

        [Fact]
        public void Script_tools_are_listed_but_not_launchable()
        {
            var sw = new[] { new SoftwareToolInfo { Id = Guid.NewGuid(), Name = "Script", IsScript = true } };
            var item = ToolCatalogMerger.Merge(sw, new LauncherData()).Single();
            Assert.False(item.CanLaunch);
        }

        [Fact]
        public void Null_inputs_produce_an_empty_list()
        {
            Assert.Empty(ToolCatalogMerger.Merge(null, null));
        }
    }

    public class ToolSearchTests
    {
        private static readonly List<LauncherItem> Items = new List<LauncherItem>
        {
            new LauncherItem { Name = "PKHeX", Category = "Pokemon" },
            new LauncherItem { Name = "OBS Studio", Category = "Streaming" },
            new LauncherItem { Name = "UltraPlusManager", Category = "Pokemon" },
            new LauncherItem { Name = "SCSKiller", Category = "" }
        };

        [Fact]
        public void Empty_query_returns_everything_alphabetically()
        {
            Assert.Equal(new[] { "OBS Studio", "PKHeX", "SCSKiller", "UltraPlusManager" },
                ToolSearch.Filter(Items, "", false).Select(i => i.Name));
        }

        [Fact]
        public void Category_matches_and_name_prefix()
        {
            Assert.Equal(new[] { "PKHeX", "UltraPlusManager" }, ToolSearch.Filter(Items, "po", false).Select(i => i.Name));
            Assert.Equal(new[] { "PKHeX" }, ToolSearch.Filter(Items, "pk", false).Select(i => i.Name));
        }

        [Fact]
        public void Name_prefix_ranks_above_name_contains_and_category()
        {
            var items = new List<LauncherItem>
            {
                new LauncherItem { Name = "Alpha", Category = "Studio tools" },
                new LauncherItem { Name = "Beta Studio" },
                new LauncherItem { Name = "Studio One" }
            };
            Assert.Equal(new[] { "Studio One", "Beta Studio", "Alpha" }, ToolSearch.Filter(items, "studio", false).Select(i => i.Name));
        }

        [Fact]
        public void All_words_must_match()
        {
            Assert.Equal(new[] { "UltraPlusManager" }, ToolSearch.Filter(Items, "pokemon ultra", false).Select(i => i.Name));
        }

        [Fact]
        public void Grouping_orders_by_category_with_uncategorised_last()
        {
            Assert.Equal(new[] { "PKHeX", "UltraPlusManager", "OBS Studio", "SCSKiller" },
                ToolSearch.Filter(Items, null, true).Select(i => i.Name));
        }
    }

    public class RuleEvaluatorTests
    {
        private static readonly Guid Pc = Guid.NewGuid();
        private static readonly Guid Steam = Guid.NewGuid();
        private static readonly Guid Tag = Guid.NewGuid();

        private static RuleCondition C(ConditionType t, Guid? id = null, string text = "", bool not = false, Joiner j = Joiner.And) =>
            new RuleCondition { Type = t, ItemId = id, Text = text, Not = not, Joiner = j };

        [Fact]
        public void Empty_conditions_match_every_game() => Assert.True(RuleEvaluator.Matches(new List<RuleCondition>(), new FakeGame()));

        [Fact]
        public void Single_item_condition()
        {
            var game = new FakeGame("Halo", (ConditionType.Platform, Pc));
            Assert.True(RuleEvaluator.Matches(new[] { C(ConditionType.Platform, Pc) }, game));
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.Source, Steam) }, game));
        }

        [Fact]
        public void Not_inverts()
        {
            var game = new FakeGame("Halo", (ConditionType.Platform, Pc));
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.Platform, Pc, not: true) }, game));
        }

        [Fact]
        public void And_and_Or_fold_left_to_right()
        {
            var game = new FakeGame("Halo", (ConditionType.Platform, Pc));
            // (PC AND Steam) OR Tag  -> (true && false) || false = false
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.Platform, Pc), C(ConditionType.Source, Steam, j: Joiner.Or), C(ConditionType.Tag, Tag) }, game));
            // (PC OR Steam) AND NOT Tag -> (true || false) && true = true
            Assert.True(RuleEvaluator.Matches(new[] { C(ConditionType.Platform, Pc, j: Joiner.Or), C(ConditionType.Source, Steam), C(ConditionType.Tag, Tag, not: true) }, game));
        }

        [Fact]
        public void Specific_game_and_name()
        {
            var game = new FakeGame("Pokemon Legends");
            Assert.True(RuleEvaluator.Matches(new[] { C(ConditionType.SpecificGame, game.GameId) }, game));
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.SpecificGame, Guid.NewGuid()) }, game));
            Assert.True(RuleEvaluator.Matches(new[] { C(ConditionType.Name, text: "legends") }, game));
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.Name, text: "  ") }, game));
        }

        [Fact]
        public void Item_condition_without_value_never_matches()
        {
            Assert.False(RuleEvaluator.Matches(new[] { C(ConditionType.Platform) }, new FakeGame()));
        }
    }

    public class ToolActivatorTests
    {
        [Fact]
        public void Focuses_running_single_instance_tool()
        {
            var launcher = new FakeLauncher();
            var tool = Make.Tool();
            launcher.Running.Add(tool.Id);

            var result = new ToolActivator(launcher).Activate(tool);

            Assert.Equal(ActivationStatus.Focused, result.Status);
            Assert.Empty(launcher.Started);
        }

        [Fact]
        public void Multi_instance_tool_always_starts()
        {
            var launcher = new FakeLauncher();
            var tool = Make.Tool();
            tool.SingleInstance = false;
            launcher.Running.Add(tool.Id);

            Assert.Equal(ActivationStatus.Started, new ToolActivator(launcher).Activate(tool).Status);
            Assert.Equal(0, launcher.FocusCalls);
        }

        [Fact]
        public void Starts_when_running_tool_has_no_window_to_focus()
        {
            var launcher = new FakeLauncher { FocusSucceeds = false };
            var tool = Make.Tool();
            launcher.Running.Add(tool.Id);

            Assert.Equal(ActivationStatus.Started, new ToolActivator(launcher).Activate(tool).Status);
        }

        [Fact]
        public void Reports_cancel_failure_and_unlaunchable()
        {
            var launcher = new FakeLauncher { NextStatus = StartStatus.Cancelled };
            Assert.Equal(ActivationStatus.Cancelled, new ToolActivator(launcher).Activate(Make.Tool()).Status);

            launcher.NextStatus = StartStatus.Failed;
            var failed = new ToolActivator(launcher).Activate(Make.Tool());
            Assert.Equal(ActivationStatus.Failed, failed.Status);
            Assert.Equal("boom", failed.Error);

            Assert.Equal(ActivationStatus.NotLaunchable, new ToolActivator(launcher).Activate(Make.Tool(path: "")).Status);
        }
    }

    public class AutoLaunchCoordinatorTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task Starts_matching_tool_and_closes_it_on_exit()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var tool = Make.Tool("OBS", Make.OnGame());
            var game = new FakeGame();

            await coordinator.OnGameStartingAsync(game, new[] { tool });
            Assert.Single(launcher.Started);
            Assert.Equal(1, coordinator.TrackedCount(game.GameId));

            await coordinator.OnGameStoppedAsync(game.GameId, Timeout);
            Assert.Single(launcher.Closed);
        }

        [Fact]
        public async Task Non_matching_and_disabled_rules_do_nothing()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var nonMatching = Make.Tool("A", Make.OnGame(new RuleCondition { Type = ConditionType.Platform, ItemId = Guid.NewGuid() }));
            var disabledRule = Make.OnGame();
            disabledRule.Enabled = false;
            var disabled = Make.Tool("B", disabledRule);

            await coordinator.OnGameStartingAsync(new FakeGame(), new[] { nonMatching, disabled });

            Assert.Empty(launcher.Started);
        }

        [Fact]
        public async Task Already_running_tool_is_not_started_or_closed()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var tool = Make.Tool("OBS", Make.OnGame());
            launcher.Running.Add(tool.Id);
            var game = new FakeGame();

            await coordinator.OnGameStartingAsync(game, new[] { tool });
            await coordinator.OnGameStoppedAsync(game.GameId, Timeout);

            Assert.Empty(launcher.Started);
            Assert.Empty(launcher.Closed);
        }

        [Fact]
        public async Task Close_on_exit_off_leaves_tool_running()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var rule = Make.OnGame();
            rule.CloseOnGameExit = false;
            var game = new FakeGame();

            await coordinator.OnGameStartingAsync(game, new[] { Make.Tool("OBS", rule) });
            await coordinator.OnGameStoppedAsync(game.GameId, Timeout);

            Assert.Single(launcher.Started);
            Assert.Empty(launcher.Closed);
        }

        [Fact]
        public async Task Game_stopping_during_delay_cancels_the_launch()
        {
            var launcher = new FakeLauncher();
            var gate = new TaskCompletionSource<bool>();
            var coordinator = new AutoLaunchCoordinator(launcher, delay: async (span, token) =>
            {
                using (token.Register(() => gate.TrySetCanceled()))
                {
                    await gate.Task;
                }
            });
            var rule = Make.OnGame();
            rule.DelayMs = 10000;
            var game = new FakeGame();

            var starting = coordinator.OnGameStartingAsync(game, new[] { Make.Tool("OBS", rule) });
            await coordinator.OnGameStoppedAsync(game.GameId, Timeout);
            await starting;

            Assert.Empty(launcher.Started);
        }

        [Fact]
        public async Task Tool_wanted_by_another_running_game_is_handed_over_not_closed()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var tool = Make.Tool("OBS", Make.OnGame());
            var first = new FakeGame("A");
            var second = new FakeGame("B");

            await coordinator.OnGameStartingAsync(first, new[] { tool });
            await coordinator.OnGameStartingAsync(second, new[] { tool }); // already running: not started again
            await coordinator.OnGameStoppedAsync(first.GameId, Timeout);

            Assert.Empty(launcher.Closed);
            Assert.Equal(1, coordinator.TrackedCount(second.GameId));

            await coordinator.OnGameStoppedAsync(second.GameId, Timeout);
            Assert.Single(launcher.Closed);
        }

        [Fact]
        public async Task Failures_and_access_denied_are_reported()
        {
            var warnings = new List<string>();
            var launcher = new FakeLauncher { CloseOutcome = CloseOutcome.AccessDenied };
            var coordinator = new AutoLaunchCoordinator(launcher, (item, msg) => { lock (warnings) { warnings.Add(item.Name + ": " + msg); } });
            var game = new FakeGame();

            await coordinator.OnGameStartingAsync(game, new[] { Make.Tool("OBS", Make.OnGame()) });
            await coordinator.OnGameStoppedAsync(game.GameId, Timeout);
            Assert.Contains(warnings, w => w.StartsWith("OBS: Windows denied access"));

            launcher.NextStatus = StartStatus.Failed;
            await coordinator.OnGameStartingAsync(new FakeGame(), new[] { Make.Tool("Broken", Make.OnGame()) });
            Assert.Contains("Broken: boom", warnings);
        }

        [Fact]
        public void Playnite_start_trigger_launches_only_tools_not_running()
        {
            var launcher = new FakeLauncher();
            var coordinator = new AutoLaunchCoordinator(launcher);
            var rule = new AutoLaunchRule { Enabled = true, StartWithPlaynite = true };
            var a = Make.Tool("A", rule);
            var b = Make.Tool("B", rule);
            var c = Make.Tool("C", Make.OnGame());
            launcher.Running.Add(b.Id);

            coordinator.OnPlayniteStarted(new[] { a, b, c });

            Assert.Equal(new[] { a.Id }, launcher.Started.ToArray());
        }
    }

    public sealed class JsonDataStoreTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "LauncherTests_" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(dir, "launcher.json");

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        [Fact]
        public void Legacy_file_is_adopted_when_new_file_is_missing()
        {
            Directory.CreateDirectory(dir);
            var legacy = Path.Combine(dir, "toolbox.json");
            File.WriteAllText(legacy, "{\"CloseAfterLaunch\": true}");
            var store = new JsonDataStore(FilePath);

            Assert.True(store.AdoptLegacyFile(legacy));

            Assert.False(File.Exists(legacy));
            Assert.True(store.Load().Data.CloseAfterLaunch);
        }

        [Fact]
        public void Legacy_file_is_ignored_when_new_file_exists()
        {
            Directory.CreateDirectory(dir);
            var legacy = Path.Combine(dir, "toolbox.json");
            File.WriteAllText(legacy, "{\"CloseAfterLaunch\": true}");
            File.WriteAllText(FilePath, "{\"CloseAfterLaunch\": false}");
            var store = new JsonDataStore(FilePath);

            Assert.False(store.AdoptLegacyFile(legacy));

            Assert.True(File.Exists(legacy));
            Assert.False(store.Load().Data.CloseAfterLaunch);
        }

        [Fact]
        public void Missing_file_gives_defaults()
        {
            var result = new JsonDataStore(FilePath).Load();
            Assert.Null(result.Warning);
            Assert.Empty(result.Data.Tools);
            Assert.True(result.Data.IncludeSoftwareTools);
        }

        [Fact]
        public void Round_trips()
        {
            var store = new JsonDataStore(FilePath);
            var data = new LauncherData { CloseAfterLaunch = true };
            data.Tools.Add(new ToolEntry { Name = "PKHeX", Path = @"C:\pk.exe", AutoLaunch = Make.OnGame(new RuleCondition { Type = ConditionType.Tag, ItemId = Guid.NewGuid(), Joiner = Joiner.Or }) });
            store.Save(data);
            store.Save(data); // second save exercises File.Replace

            var loaded = store.Load();
            Assert.Null(loaded.Warning);
            Assert.True(loaded.Data.CloseAfterLaunch);
            Assert.Equal("PKHeX", loaded.Data.Tools.Single().Name);
            Assert.Equal(Joiner.Or, loaded.Data.Tools.Single().AutoLaunch.Conditions.Single().Joiner);
        }

        [Fact]
        public void Corrupt_file_is_set_aside_not_overwritten()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, "{ not json");
            var store = new JsonDataStore(FilePath, () => new DateTime(2026, 11, 7, 10, 30, 0));

            var result = store.Load();

            Assert.NotNull(result.Warning);
            Assert.False(File.Exists(FilePath));
            Assert.Equal("{ not json", File.ReadAllText(FilePath + ".corrupt-20261107-103000"));
        }

        [Fact]
        public void Version_zero_and_nulls_are_repaired()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, "{ \"Tools\": [ null, { \"Name\": \"A\", \"AutoLaunch\": null } ], \"Overrides\": null, \"CloseTimeoutSeconds\": 999 }");

            var data = new JsonDataStore(FilePath).Load().Data;

            Assert.Equal(LauncherData.CurrentSchemaVersion, data.SchemaVersion);
            Assert.Single(data.Tools);
            Assert.NotNull(data.Tools[0].AutoLaunch);
            Assert.NotEqual(Guid.Empty, data.Tools[0].Id);
            Assert.Empty(data.Overrides);
            Assert.Equal(LauncherData.MaxCloseTimeoutSeconds, data.CloseTimeoutSeconds);
        }

        [Fact]
        public void Newer_schema_loads_with_warning()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, "{ \"SchemaVersion\": 99 }");
            var result = new JsonDataStore(FilePath).Load();
            Assert.NotNull(result.Warning);
            Assert.NotNull(result.Data);
        }

        [Fact]
        public void Unknown_enum_values_fall_back_safely()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, "{ \"Tools\": [ { \"Name\": \"A\", \"AutoLaunch\": { \"Conditions\": [ { \"Type\": 42, \"Joiner\": 7 } ] } } ] }");
            var c = new JsonDataStore(FilePath).Load().Data.Tools[0].AutoLaunch.Conditions[0];
            Assert.Equal(ConditionType.All, c.Type);
            Assert.Equal(Joiner.And, c.Joiner);
        }
    }
}
