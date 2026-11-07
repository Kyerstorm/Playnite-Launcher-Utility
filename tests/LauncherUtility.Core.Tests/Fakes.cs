using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Tests
{
    internal sealed class FakeGame : IGameFacts
    {
        public FakeGame(string name = "Game", params (ConditionType Type, Guid Id)[] items)
        {
            Name = name;
            Items = items.ToList();
        }

        public Guid GameId { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public List<(ConditionType Type, Guid Id)> Items { get; }

        public bool HasItem(ConditionType type, Guid? itemId, string text) =>
            itemId.HasValue && Items.Any(i => i.Type == type && i.Id == itemId.Value);
    }

    internal sealed class FakeLauncher : IProcessLauncher
    {
        private int nextPid = 1000;

        public HashSet<Guid> Running { get; } = new HashSet<Guid>();

        public ConcurrentQueue<Guid> Started { get; } = new ConcurrentQueue<Guid>();

        public ConcurrentQueue<int> Closed { get; } = new ConcurrentQueue<int>();

        public StartStatus NextStatus { get; set; } = StartStatus.Started;

        public bool ReturnNoProcess { get; set; }

        public CloseOutcome CloseOutcome { get; set; } = CloseOutcome.ClosedGracefully;

        public bool FocusSucceeds { get; set; } = true;

        public int FocusCalls { get; private set; }

        public bool IsRunning(LauncherItem item)
        {
            lock (Running)
            {
                return Running.Contains(item.Id);
            }
        }

        public bool TryFocusRunning(LauncherItem item)
        {
            FocusCalls++;
            return IsRunning(item) && FocusSucceeds;
        }

        public StartResult Start(LauncherItem item)
        {
            switch (NextStatus)
            {
                case StartStatus.Cancelled:
                    return StartResult.Cancelled();
                case StartStatus.Failed:
                    return StartResult.Failed("boom");
            }

            Started.Enqueue(item.Id);
            lock (Running)
            {
                Running.Add(item.Id);
            }

            return StartResult.Started(ReturnNoProcess ? null : new TrackedProcess(System.Threading.Interlocked.Increment(ref nextPid), DateTime.UtcNow));
        }

        public CloseOutcome Close(TrackedProcess process, TimeSpan gracefulTimeout)
        {
            Closed.Enqueue(process.ProcessId);
            return CloseOutcome;
        }
    }

    internal static class Make
    {
        public static LauncherItem Tool(string name = "Tool", AutoLaunchRule rule = null, string path = @"C:\Tools\tool.exe") => new LauncherItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            AutoLaunch = rule ?? new AutoLaunchRule()
        };

        public static AutoLaunchRule OnGame(params RuleCondition[] conditions) => new AutoLaunchRule
        {
            Enabled = true,
            StartWithGame = true,
            CloseOnGameExit = true,
            Conditions = conditions.ToList()
        };
    }
}
