using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Services
{
    /// <summary>
    /// Owns all auto-launch state: which tools each running game started, and closing exactly those on exit.
    /// Rules:
    /// <list type="bullet">
    /// <item>A tool that was already running is neither started again nor closed later.</item>
    /// <item>Only processes this coordinator started are ever closed (never "everything in that folder").</item>
    /// <item>If another running game also wants the tool, the process is handed over instead of closed.</item>
    /// </list>
    /// </summary>
    public sealed class AutoLaunchCoordinator
    {
        private readonly IProcessLauncher launcher;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Action<LauncherItem, string> warn;
        private readonly object sync = new object();
        private readonly Dictionary<Guid, GameSession> sessions = new Dictionary<Guid, GameSession>();

        public AutoLaunchCoordinator(
            IProcessLauncher launcher,
            Action<LauncherItem, string> warn = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            this.warn = warn ?? ((_, __) => { });
            this.delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        /// <summary>Starts every tool whose rule matches the starting game. The returned task completes when all launches are done.</summary>
        public Task OnGameStartingAsync(IGameFacts game, IEnumerable<LauncherItem> catalog)
        {
            if (game == null)
            {
                return Task.FromResult(0);
            }

            var candidates = (catalog ?? Enumerable.Empty<LauncherItem>())
                .Where(i => i != null && i.CanLaunch && i.AutoLaunch != null && i.AutoLaunch.Enabled && i.AutoLaunch.StartWithGame)
                .Where(i => SafeMatches(i, game))
                .ToList();

            GameSession session;
            lock (sync)
            {
                if (sessions.TryGetValue(game.GameId, out var previous))
                {
                    previous.Cancellation.Cancel();
                }

                session = new GameSession(candidates.Select(c => c.Id));
                sessions[game.GameId] = session;
            }

            return Task.WhenAll(candidates.Select(item => Task.Run(() => LaunchForGameAsync(item, session))));
        }

        /// <summary>Closes the tools that were started for this game (gracefully first, then forcefully).</summary>
        public Task OnGameStoppedAsync(Guid gameId, TimeSpan gracefulTimeout)
        {
            List<KeyValuePair<LauncherItem, TrackedProcess>> toClose;
            lock (sync)
            {
                if (!sessions.TryGetValue(gameId, out var session))
                {
                    return Task.FromResult(0);
                }

                sessions.Remove(gameId);
                session.Stopped = true;
                session.Cancellation.Cancel();

                toClose = new List<KeyValuePair<LauncherItem, TrackedProcess>>();
                foreach (var pair in session.Started)
                {
                    var heir = sessions.Values.FirstOrDefault(s => s.WantedToolIds.Contains(pair.Key.Id));
                    if (heir != null)
                    {
                        heir.Started.Add(pair); // Another running game wants this tool: it now owns the process.
                    }
                    else
                    {
                        toClose.Add(pair);
                    }
                }
            }

            return Task.WhenAll(toClose.Select(pair => Task.Run(() => CloseTracked(pair.Key, pair.Value, gracefulTimeout))));
        }

        /// <summary>Starts every tool with the "start with Playnite" trigger that is not already running.</summary>
        public void OnPlayniteStarted(IEnumerable<LauncherItem> catalog)
        {
            foreach (var item in (catalog ?? Enumerable.Empty<LauncherItem>())
                         .Where(i => i != null && i.CanLaunch && i.AutoLaunch != null && i.AutoLaunch.Enabled && i.AutoLaunch.StartWithPlaynite))
            {
                try
                {
                    if (launcher.IsRunning(item))
                    {
                        continue;
                    }

                    var result = launcher.Start(item);
                    if (result.Status == StartStatus.Failed)
                    {
                        warn(item, result.Error);
                    }
                }
                catch (Exception e)
                {
                    warn(item, e.Message);
                }
            }
        }

        /// <summary>For tests and diagnostics: how many processes are tracked for a game.</summary>
        internal int TrackedCount(Guid gameId)
        {
            lock (sync)
            {
                return sessions.TryGetValue(gameId, out var s) ? s.Started.Count : 0;
            }
        }

        private async Task LaunchForGameAsync(LauncherItem item, GameSession session)
        {
            try
            {
                var rule = item.AutoLaunch;
                var wait = Math.Max(0, Math.Min(rule.DelayMs, LauncherData.MaxDelayMs));
                if (wait > 0)
                {
                    try
                    {
                        await delay(TimeSpan.FromMilliseconds(wait), session.Cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // The game stopped (or restarted) before the delay ran out.
                    }
                }

                if (session.Cancellation.IsCancellationRequested || launcher.IsRunning(item))
                {
                    return;
                }

                var result = launcher.Start(item);
                if (result.Status == StartStatus.Failed)
                {
                    warn(item, result.Error);
                    return;
                }

                if (result.Status != StartStatus.Started || result.Process == null || !rule.CloseOnGameExit)
                {
                    return;
                }

                bool closeNow;
                lock (sync)
                {
                    closeNow = session.Stopped;
                    if (!closeNow)
                    {
                        session.Started.Add(new KeyValuePair<LauncherItem, TrackedProcess>(item, result.Process));
                    }
                }

                if (closeNow)
                {
                    // The game exited while the tool was starting; honour "close on exit" anyway.
                    CloseTracked(item, result.Process, TimeSpan.FromSeconds(LauncherData.MinCloseTimeoutSeconds));
                }
            }
            catch (Exception e)
            {
                warn(item, e.Message);
            }
        }

        private void CloseTracked(LauncherItem item, TrackedProcess process, TimeSpan timeout)
        {
            try
            {
                var outcome = launcher.Close(process, timeout);
                if (outcome == CloseOutcome.AccessDenied)
                {
                    warn(item, "Windows denied access when closing it (it may be running as administrator).");
                }
                else if (outcome == CloseOutcome.Failed)
                {
                    warn(item, "It could not be closed.");
                }
            }
            catch (Exception e)
            {
                warn(item, e.Message);
            }
        }

        private bool SafeMatches(LauncherItem item, IGameFacts game)
        {
            try
            {
                return RuleEvaluator.Matches(item.AutoLaunch.Conditions, game);
            }
            catch (Exception e)
            {
                warn(item, "Its auto-launch conditions could not be checked: " + e.Message);
                return false;
            }
        }

        private sealed class GameSession
        {
            public GameSession(IEnumerable<Guid> wanted) => WantedToolIds = new HashSet<Guid>(wanted);

            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

            public HashSet<Guid> WantedToolIds { get; }

            public List<KeyValuePair<LauncherItem, TrackedProcess>> Started { get; } = new List<KeyValuePair<LauncherItem, TrackedProcess>>();

            public bool Stopped { get; set; }
        }
    }
}
