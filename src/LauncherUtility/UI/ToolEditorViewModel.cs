using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Playnite.SDK;
using LauncherUtility.Core.Models;
using LauncherUtility.Core.Services;
using LauncherUtility.Integration;

namespace LauncherUtility.UI
{
    /// <summary>One row of the conditions grid.</summary>
    internal sealed class ConditionRowViewModel : BindableBase
    {
        public static readonly List<Option<ConditionType>> TypeOptions = new List<Option<ConditionType>>
        {
            new Option<ConditionType>(ConditionType.All, "Every game"),
            new Option<ConditionType>(ConditionType.SpecificGame, "Game is"),
            new Option<ConditionType>(ConditionType.Name, "Name contains"),
            new Option<ConditionType>(ConditionType.Platform, "Platform"),
            new Option<ConditionType>(ConditionType.Source, "Source"),
            new Option<ConditionType>(ConditionType.Tag, "Tag"),
            new Option<ConditionType>(ConditionType.Category, "Category"),
            new Option<ConditionType>(ConditionType.Genre, "Genre"),
            new Option<ConditionType>(ConditionType.Feature, "Feature")
        };

        public static readonly List<Option<Joiner>> JoinerOptions = new List<Option<Joiner>>
        {
            new Option<Joiner>(Joiner.And, "and"),
            new Option<Joiner>(Joiner.Or, "or")
        };

        private readonly Func<ConditionType, List<Option<Guid>>> optionsProvider;
        private ConditionType type;
        private bool not;
        private Joiner joiner;
        private Guid? selectedId;
        private string text;
        private List<Option<Guid>> options = new List<Option<Guid>>();
        private bool isLast;

        public ConditionRowViewModel(RuleCondition model, Func<ConditionType, List<Option<Guid>>> optionsProvider)
        {
            this.optionsProvider = optionsProvider;
            not = model.Not;
            joiner = model.Joiner;
            text = model.Text ?? string.Empty;
            selectedId = model.ItemId;
            SetType(model.Type, keepSelection: true);
        }

        public ConditionType Type
        {
            get => type;
            set
            {
                if (type != value)
                {
                    SetType(value, keepSelection: false);
                }
            }
        }

        public bool Not { get => not; set => Set(ref not, value); }

        public Joiner Joiner { get => joiner; set => Set(ref joiner, value); }

        public Guid? SelectedId { get => selectedId; set => Set(ref selectedId, value); }

        public string Text { get => text; set => Set(ref text, value ?? string.Empty); }

        public List<Option<Guid>> Options { get => options; private set => Set(ref options, value); }

        public bool IsTextType => type == ConditionType.Name;

        public bool IsItemType => type != ConditionType.All && type != ConditionType.Name;

        /// <summary>The last row has no "and/or" because nothing follows it.</summary>
        public bool IsLast { get => isLast; set => Set(ref isLast, value); }

        public RuleCondition ToModel()
        {
            var label = IsItemType ? Options.FirstOrDefault(o => o.Value == selectedId)?.Label ?? text : text;
            return new RuleCondition
            {
                Type = type,
                Not = not,
                Joiner = joiner,
                ItemId = IsItemType ? selectedId : null,
                Text = (label ?? string.Empty).Replace(" (missing)", string.Empty)
            };
        }

        private void SetType(ConditionType value, bool keepSelection)
        {
            type = value;
            var list = IsItemType ? optionsProvider(value) : new List<Option<Guid>>();
            if (keepSelection && selectedId.HasValue && list.All(o => o.Value != selectedId.Value) && IsItemType)
            {
                // The saved item was deleted from the library (or renamed); keep it visible instead of silently dropping it.
                list = new List<Option<Guid>>(list) { new Option<Guid>(selectedId.Value, (string.IsNullOrWhiteSpace(text) ? selectedId.Value.ToString() : text) + " (missing)") };
            }

            if (!keepSelection)
            {
                selectedId = null;
                OnPropertyChanged(nameof(SelectedId));
            }

            Options = list;
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(IsTextType));
            OnPropertyChanged(nameof(IsItemType));
        }
    }

    /// <summary>Add/edit dialog for one tool: launch details, Launcher options and its auto-launch rule.</summary>
    internal sealed class ToolEditorViewModel : BindableBase
    {
        private readonly LauncherContext context;
        private readonly Dictionary<ConditionType, List<Option<Guid>>> optionCache = new Dictionary<ConditionType, List<Option<Guid>>>();
        private string name;
        private string path;
        private string arguments;
        private string workingDir;
        private string iconPath;
        private string category;
        private bool runAsAdmin;
        private bool singleInstance;
        private bool hidden;
        private bool ruleEnabled;
        private bool startWithGame;
        private bool startWithPlaynite;
        private bool closeOnGameExit;
        private int delaySeconds;
        private string matchPreview = string.Empty;
        private string validationError = string.Empty;

        private ToolEditorViewModel(LauncherContext context, ToolOrigin origin, IEnumerable<string> knownCategories)
        {
            this.context = context;
            Origin = origin;
            KnownCategories = knownCategories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

            BrowsePathCommand = new DelegateCommand(_ => BrowsePath());
            BrowseWorkingDirCommand = new DelegateCommand(_ => BrowseWorkingDir());
            BrowseIconCommand = new DelegateCommand(_ => BrowseIcon());
            ClearIconCommand = new DelegateCommand(_ => IconPath = string.Empty);
            AddConditionCommand = new DelegateCommand(_ => AddCondition(new RuleCondition { Type = ConditionType.Platform }));
            RemoveConditionCommand = new DelegateCommand(p =>
            {
                if (p is ConditionRowViewModel row)
                {
                    Conditions.Remove(row);
                    UpdateLastFlags();
                }
            });
            PreviewMatchesCommand = new DelegateCommand(_ => PreviewMatches());
            SaveCommand = new DelegateCommand(_ => TrySave());
            CancelCommand = new DelegateCommand(_ => CloseRequested?.Invoke(false));
        }

        public event Action<bool> CloseRequested;

        public ToolOrigin Origin { get; }

        public bool IsSoftwareTool => Origin == ToolOrigin.SoftwareTool;

        public bool CanEditBasics => !IsSoftwareTool;

        public string WindowTitle => IsSoftwareTool ? "Software Tool options" : string.IsNullOrWhiteSpace(name) ? "Add tool" : "Edit tool";

        public List<string> KnownCategories { get; }

        public string Name { get => name; set => Set(ref name, value ?? string.Empty); }
        public string Path { get => path; set => Set(ref path, value ?? string.Empty); }
        public string Arguments { get => arguments; set => Set(ref arguments, value ?? string.Empty); }
        public string WorkingDir { get => workingDir; set => Set(ref workingDir, value ?? string.Empty); }
        public string IconPath { get => iconPath; set => Set(ref iconPath, value ?? string.Empty); }
        public string Category { get => category; set => Set(ref category, value ?? string.Empty); }
        public bool RunAsAdmin { get => runAsAdmin; set => Set(ref runAsAdmin, value); }
        public bool SingleInstance { get => singleInstance; set => Set(ref singleInstance, value); }
        public bool Hidden { get => hidden; set => Set(ref hidden, value); }

        public bool RuleEnabled { get => ruleEnabled; set => Set(ref ruleEnabled, value); }
        public bool StartWithGame { get => startWithGame; set => Set(ref startWithGame, value); }
        public bool StartWithPlaynite { get => startWithPlaynite; set => Set(ref startWithPlaynite, value); }
        public bool CloseOnGameExit { get => closeOnGameExit; set => Set(ref closeOnGameExit, value); }

        public int DelaySeconds
        {
            get => delaySeconds;
            set => Set(ref delaySeconds, Math.Max(0, Math.Min(LauncherData.MaxDelayMs / 1000, value)));
        }

        public ObservableCollection<ConditionRowViewModel> Conditions { get; } = new ObservableCollection<ConditionRowViewModel>();

        public List<Option<ConditionType>> TypeOptions => ConditionRowViewModel.TypeOptions;

        public List<Option<Joiner>> JoinerOptions => ConditionRowViewModel.JoinerOptions;

        public string MatchPreview { get => matchPreview; private set => Set(ref matchPreview, value); }

        public string ValidationError { get => validationError; private set => Set(ref validationError, value); }

        public ICommand BrowsePathCommand { get; }
        public ICommand BrowseWorkingDirCommand { get; }
        public ICommand BrowseIconCommand { get; }
        public ICommand ClearIconCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }
        public ICommand PreviewMatchesCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public static ToolEditorViewModel ForEntry(LauncherContext context, ToolEntry entry, IEnumerable<string> knownCategories)
        {
            var vm = new ToolEditorViewModel(context, ToolOrigin.Launcher, knownCategories)
            {
                name = entry.Name,
                path = entry.Path,
                arguments = entry.Arguments,
                workingDir = entry.WorkingDir,
                iconPath = entry.IconPath,
                category = entry.Category,
                runAsAdmin = entry.RunAsAdmin,
                singleInstance = entry.SingleInstance
            };
            vm.LoadRule(entry.AutoLaunch);
            return vm;
        }

        public static ToolEditorViewModel ForSoftwareTool(LauncherContext context, LauncherItem item, ToolOverride options, IEnumerable<string> knownCategories)
        {
            var vm = new ToolEditorViewModel(context, ToolOrigin.SoftwareTool, knownCategories)
            {
                name = item.Name,
                path = item.Path,
                arguments = item.Arguments,
                workingDir = item.WorkingDir,
                iconPath = item.IconPath,
                category = options.Category,
                runAsAdmin = options.RunAsAdmin,
                singleInstance = options.SingleInstance,
                hidden = options.Hidden
            };
            vm.LoadRule(options.AutoLaunch);
            return vm;
        }

        public void ApplyTo(ToolEntry entry)
        {
            entry.Name = name.Trim();
            entry.Path = path.Trim();
            entry.Arguments = arguments.Trim();
            entry.WorkingDir = workingDir.Trim();
            entry.IconPath = iconPath.Trim();
            entry.Category = category.Trim();
            entry.RunAsAdmin = runAsAdmin;
            entry.SingleInstance = singleInstance;
            entry.AutoLaunch = BuildRule();
        }

        public void ApplyTo(ToolOverride options)
        {
            options.Category = category.Trim();
            options.RunAsAdmin = runAsAdmin;
            options.SingleInstance = singleInstance;
            options.Hidden = hidden;
            options.AutoLaunch = BuildRule();
        }

        private void LoadRule(AutoLaunchRule rule)
        {
            rule = rule ?? new AutoLaunchRule();
            ruleEnabled = rule.Enabled;
            startWithGame = rule.StartWithGame;
            startWithPlaynite = rule.StartWithPlaynite;
            closeOnGameExit = rule.CloseOnGameExit;
            delaySeconds = rule.DelayMs / 1000;
            foreach (var condition in rule.Conditions ?? new List<RuleCondition>())
            {
                AddCondition(condition);
            }
        }

        private AutoLaunchRule BuildRule() => new AutoLaunchRule
        {
            Enabled = ruleEnabled,
            StartWithGame = startWithGame,
            StartWithPlaynite = startWithPlaynite,
            CloseOnGameExit = closeOnGameExit,
            DelayMs = delaySeconds * 1000,
            Conditions = Conditions.Select(c => c.ToModel()).ToList()
        };

        private void AddCondition(RuleCondition model)
        {
            Conditions.Add(new ConditionRowViewModel(model, GetOptions));
            UpdateLastFlags();
        }

        private void UpdateLastFlags()
        {
            for (var i = 0; i < Conditions.Count; i++)
            {
                Conditions[i].IsLast = i == Conditions.Count - 1;
            }
        }

        private List<Option<Guid>> GetOptions(ConditionType type)
        {
            if (optionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var db = context.Api.Database;
            IEnumerable<Option<Guid>> items;
            switch (type)
            {
                case ConditionType.SpecificGame:
                    items = db.Games.Select(g => new Option<Guid>(g.Id, g.Name ?? "(unnamed)"));
                    break;
                case ConditionType.Platform:
                    items = db.Platforms.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                case ConditionType.Source:
                    items = db.Sources.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                case ConditionType.Tag:
                    items = db.Tags.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                case ConditionType.Category:
                    items = db.Categories.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                case ConditionType.Genre:
                    items = db.Genres.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                case ConditionType.Feature:
                    items = db.Features.Select(x => new Option<Guid>(x.Id, x.Name));
                    break;
                default:
                    items = Enumerable.Empty<Option<Guid>>();
                    break;
            }

            var list = items.Where(o => !string.IsNullOrWhiteSpace(o.Label))
                .OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            optionCache[type] = list;
            return list;
        }

        private void BrowsePath()
        {
            var selected = context.Api.Dialogs.SelectFile("Programs|*.exe;*.bat;*.cmd;*.lnk;*.url|All files|*.*");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            Path = selected;
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(selected);
            }
        }

        private void BrowseWorkingDir()
        {
            var selected = context.Api.Dialogs.SelectFolder();
            if (!string.IsNullOrEmpty(selected))
            {
                WorkingDir = selected;
            }
        }

        private void BrowseIcon()
        {
            var selected = context.Api.Dialogs.SelectIconFile();
            if (!string.IsNullOrEmpty(selected))
            {
                IconPath = selected;
            }
        }

        private void PreviewMatches()
        {
            var conditions = Conditions.Select(c => c.ToModel()).ToList();
            if (conditions.Count == 0)
            {
                MatchPreview = "No conditions: every game matches.";
                return;
            }

            try
            {
                var db = context.Api.Database;
                var matches = db.Games
                    .Where(g => RuleEvaluator.Matches(conditions, new PlayniteGameFacts(g, db)))
                    .Select(g => g.Name)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                MatchPreview = matches.Count == 0
                    ? "No games match these conditions."
                    : $"{matches.Count} game(s) match, e.g. {string.Join(", ", matches.Take(5))}{(matches.Count > 5 ? "…" : string.Empty)}";
            }
            catch (Exception e)
            {
                LogManager.GetLogger().Error(e, "Launcher Utility match preview failed.");
                MatchPreview = "Could not check matches: " + e.Message;
            }
        }

        private void TrySave()
        {
            if (!IsSoftwareTool)
            {
                if (string.IsNullOrWhiteSpace(Name))
                {
                    ValidationError = "Give the tool a name.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(Path))
                {
                    ValidationError = "Choose the program to launch.";
                    return;
                }
            }

            var incomplete = Conditions.FirstOrDefault(c => (c.IsItemType && !c.SelectedId.HasValue) || (c.IsTextType && string.IsNullOrWhiteSpace(c.Text)));
            if (incomplete != null)
            {
                ValidationError = "Every condition needs a value (or remove it).";
                return;
            }

            if (RuleEnabled && !StartWithGame && !StartWithPlaynite)
            {
                ValidationError = "Auto-launch is on: choose when it should start (with a game and/or with Playnite).";
                return;
            }

            ValidationError = string.Empty;
            CloseRequested?.Invoke(true);
        }
    }
}
