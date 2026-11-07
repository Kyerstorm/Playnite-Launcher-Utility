using System;

namespace LauncherUtility.Core.Models
{
    /// <summary>A tool added in Launcher Utility's own settings.</summary>
    public sealed class ToolEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;

        /// <summary>Optional image file; empty means "use the exe's own icon".</summary>
        public string IconPath { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; }

        /// <summary>Focus the running instance instead of starting another one.</summary>
        public bool SingleInstance { get; set; } = true;

        public AutoLaunchRule AutoLaunch { get; set; } = new AutoLaunchRule();

        public ToolEntry Clone()
        {
            var copy = (ToolEntry)MemberwiseClone();
            copy.AutoLaunch = (AutoLaunch ?? new AutoLaunchRule()).Clone();
            return copy;
        }
    }

    /// <summary>
    /// Launcher-only settings layered on top of a Playnite Software Tool. Name, path and arguments
    /// stay owned by Playnite; this holds everything Playnite has no field for.
    /// </summary>
    public sealed class ToolOverride
    {
        public Guid SoftwareToolId { get; set; }
        public bool Hidden { get; set; }
        public string Category { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; }
        public bool SingleInstance { get; set; } = true;
        public AutoLaunchRule AutoLaunch { get; set; } = new AutoLaunchRule();

        public ToolOverride Clone()
        {
            var copy = (ToolOverride)MemberwiseClone();
            copy.AutoLaunch = (AutoLaunch ?? new AutoLaunchRule()).Clone();
            return copy;
        }
    }

    /// <summary>A Playnite Software Tool, copied out of Playnite's database into a Playnite-free shape.</summary>
    public sealed class SoftwareToolInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;

        /// <summary>Already resolved to a full file path (or empty).</summary>
        public string IconPath { get; set; } = string.Empty;

        /// <summary>Script-type tools run PowerShell inside Playnite; not supported in v1.</summary>
        public bool IsScript { get; set; }
    }

    /// <summary>A tool as shown and launched by the Launcher, after merging both sources.</summary>
    public sealed class LauncherItem
    {
        public Guid Id { get; set; }
        public ToolOrigin Origin { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; }
        public bool SingleInstance { get; set; } = true;
        public bool IsScript { get; set; }
        public bool Hidden { get; set; }
        public AutoLaunchRule AutoLaunch { get; set; } = new AutoLaunchRule();

        public bool CanLaunch => !IsScript && !string.IsNullOrWhiteSpace(Path);

        public override string ToString() => Name;
    }
}
