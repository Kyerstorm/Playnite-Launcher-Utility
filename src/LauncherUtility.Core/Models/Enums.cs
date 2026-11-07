namespace LauncherUtility.Core.Models
{
    // Values are persisted as integers: never renumber or reuse them.

    /// <summary>What a single auto-launch condition tests about the game.</summary>
    public enum ConditionType
    {
        All = 0,
        SpecificGame = 1,
        Name = 2,
        Platform = 3,
        Source = 4,
        Tag = 5,
        Category = 6,
        Genre = 7,
        Feature = 8
    }

    /// <summary>How a condition combines with the condition that follows it.</summary>
    public enum Joiner
    {
        And = 0,
        Or = 1
    }

    /// <summary>Where a tool shown in the Launcher comes from.</summary>
    public enum ToolOrigin
    {
        /// <summary>Added in Launcher Utility's own settings.</summary>
        Launcher = 0,

        /// <summary>Playnite's Library → Software Tools list.</summary>
        SoftwareTool = 1
    }
}
