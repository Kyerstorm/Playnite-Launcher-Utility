using System;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Abstractions
{
    /// <summary>What a rule can ask about the game that is starting. Implemented over Playnite's filter engine.</summary>
    public interface IGameFacts
    {
        Guid GameId { get; }

        string Name { get; }

        /// <summary>True when the game has the database item (platform, tag, …) named by <paramref name="itemId"/>, or by <paramref name="text"/> when no id is stored.</summary>
        bool HasItem(ConditionType type, Guid? itemId, string text);
    }

    /// <summary>Identifies a process Launcher started. The start time guards against Windows reusing the PID.</summary>
    public sealed class TrackedProcess
    {
        public TrackedProcess(int processId, DateTime startTimeUtc)
        {
            ProcessId = processId;
            StartTimeUtc = startTimeUtc;
        }

        public int ProcessId { get; }

        public DateTime StartTimeUtc { get; }
    }

    public enum StartStatus
    {
        Started,

        /// <summary>The user declined the UAC prompt.</summary>
        Cancelled,

        Failed
    }

    public sealed class StartResult
    {
        private StartResult(StartStatus status, TrackedProcess process, string error)
        {
            Status = status;
            Process = process;
            Error = error;
        }

        public StartStatus Status { get; }

        /// <summary>Null when Windows handed the launch to another process (shortcuts, documents, an already-running single-instance app).</summary>
        public TrackedProcess Process { get; }

        public string Error { get; }

        public static StartResult Started(TrackedProcess process) => new StartResult(StartStatus.Started, process, null);

        public static StartResult Cancelled() => new StartResult(StartStatus.Cancelled, null, null);

        public static StartResult Failed(string error) => new StartResult(StartStatus.Failed, null, error);
    }

    public enum CloseOutcome
    {
        NotRunning,
        ClosedGracefully,
        Killed,

        /// <summary>Typically an elevated tool that a non-elevated Playnite may not close.</summary>
        AccessDenied,

        Failed
    }

    /// <summary>Starts, finds, focuses and closes tool processes. The Windows implementation lives in the plugin.</summary>
    public interface IProcessLauncher
    {
        bool IsRunning(LauncherItem item);

        /// <summary>Brings a running instance to the front. False when none is running or it has no window.</summary>
        bool TryFocusRunning(LauncherItem item);

        StartResult Start(LauncherItem item);

        /// <summary>Asks the process to close, waits up to <paramref name="gracefulTimeout"/>, then kills it.</summary>
        CloseOutcome Close(TrackedProcess process, TimeSpan gracefulTimeout);
    }
}
