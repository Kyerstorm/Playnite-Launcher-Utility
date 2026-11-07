using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace LauncherUtility.Core.Models
{
    /// <summary>
    /// One test in an auto-launch rule. Conditions are folded left to right; each condition's
    /// <see cref="Joiner"/> says how it combines with the next one (as in LaunchMate).
    /// </summary>
    public sealed class RuleCondition
    {
        public ConditionType Type { get; set; } = ConditionType.All;

        /// <summary>Database item id for Platform/Source/Tag/Category/Genre/Feature, or the game id for SpecificGame.</summary>
        public Guid? ItemId { get; set; }

        /// <summary>Free text for <see cref="ConditionType.Name"/>; display name for id-based conditions.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Inverts this condition's result.</summary>
        public bool Not { get; set; }

        /// <summary>How this condition joins the next one.</summary>
        public Joiner Joiner { get; set; } = Joiner.And;

        public RuleCondition Clone() => (RuleCondition)MemberwiseClone();
    }

    /// <summary>When a tool should start (and stop) by itself.</summary>
    public sealed class AutoLaunchRule
    {
        public bool Enabled { get; set; }

        /// <summary>Start the tool when a game matching <see cref="Conditions"/> starts.</summary>
        public bool StartWithGame { get; set; }

        /// <summary>Start the tool when Playnite starts.</summary>
        public bool StartWithPlaynite { get; set; }

        /// <summary>Close the tool when the game that started it stops (only if Launcher started it).</summary>
        public bool CloseOnGameExit { get; set; } = true;

        /// <summary>Wait this long after the game starts before launching the tool.</summary>
        public int DelayMs { get; set; }

        /// <summary>An empty list matches every game.</summary>
        public List<RuleCondition> Conditions { get; set; } = new List<RuleCondition>();

        [JsonIgnore]
        public bool IsActive => Enabled && (StartWithGame || StartWithPlaynite);

        public AutoLaunchRule Clone()
        {
            var copy = (AutoLaunchRule)MemberwiseClone();
            copy.Conditions = (Conditions ?? new List<RuleCondition>()).Where(c => c != null).Select(c => c.Clone()).ToList();
            return copy;
        }
    }
}
