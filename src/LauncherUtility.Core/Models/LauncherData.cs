using System;
using System.Collections.Generic;
using System.Linq;

namespace LauncherUtility.Core.Models
{
    /// <summary>Everything Launcher Utility saves. Bump <see cref="CurrentSchemaVersion"/> and add a migration when the shape changes.</summary>
    public sealed class LauncherData
    {
        public const int CurrentSchemaVersion = 1;
        public const int MinCloseTimeoutSeconds = 1;
        public const int MaxCloseTimeoutSeconds = 60;
        public const int MaxDelayMs = 300000;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<ToolEntry> Tools { get; set; } = new List<ToolEntry>();
        public List<ToolOverride> Overrides { get; set; } = new List<ToolOverride>();

        /// <summary>Show Playnite's Software Tools in the Launcher.</summary>
        public bool IncludeSoftwareTools { get; set; } = true;

        /// <summary>Close the Launcher popup after launching a tool.</summary>
        public bool CloseAfterLaunch { get; set; }

        public bool GroupByCategory { get; set; }

        public bool RememberWindowBounds { get; set; } = true;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }

        /// <summary>How long a tool gets to close politely on game exit before it is force-closed.</summary>
        public int CloseTimeoutSeconds { get; set; } = 5;

        public LauncherData Clone()
        {
            var copy = (LauncherData)MemberwiseClone();
            copy.Tools = (Tools ?? new List<ToolEntry>()).Where(t => t != null).Select(t => t.Clone()).ToList();
            copy.Overrides = (Overrides ?? new List<ToolOverride>()).Where(o => o != null).Select(o => o.Clone()).ToList();
            return copy;
        }

        /// <summary>Finds the override for a Software Tool, creating (and adding) one if it does not exist yet.</summary>
        public ToolOverride GetOrAddOverride(Guid softwareToolId)
        {
            var existing = Overrides.FirstOrDefault(o => o.SoftwareToolId == softwareToolId);
            if (existing != null)
            {
                return existing;
            }

            var created = new ToolOverride { SoftwareToolId = softwareToolId };
            Overrides.Add(created);
            return created;
        }

        /// <summary>The auto-launch rule that belongs to a merged tool (own entry or Software Tool override), created if missing.</summary>
        public AutoLaunchRule GetOrAddRule(LauncherItem item)
        {
            if (item.Origin == ToolOrigin.SoftwareTool)
            {
                var o = GetOrAddOverride(item.Id);
                return o.AutoLaunch ?? (o.AutoLaunch = new AutoLaunchRule());
            }

            var entry = Tools.FirstOrDefault(t => t.Id == item.Id);
            if (entry == null)
            {
                return null;
            }

            return entry.AutoLaunch ?? (entry.AutoLaunch = new AutoLaunchRule());
        }
    }
}
