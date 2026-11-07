using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Services
{
    public enum ActivationStatus
    {
        Started,
        Focused,
        Cancelled,
        Failed,
        NotLaunchable
    }

    public sealed class ActivationResult
    {
        public ActivationResult(ActivationStatus status, string error = null)
        {
            Status = status;
            Error = error;
        }

        public ActivationStatus Status { get; }

        public string Error { get; }
    }

    /// <summary>What happens when you click a tool: focus it if it is a single-instance tool that is already open, otherwise start it.</summary>
    public sealed class ToolActivator
    {
        private readonly IProcessLauncher launcher;

        public ToolActivator(IProcessLauncher launcher) => this.launcher = launcher;

        public ActivationResult Activate(LauncherItem item)
        {
            if (item == null || !item.CanLaunch)
            {
                return new ActivationResult(ActivationStatus.NotLaunchable);
            }

            if (item.SingleInstance && launcher.TryFocusRunning(item))
            {
                return new ActivationResult(ActivationStatus.Focused);
            }

            var result = launcher.Start(item);
            switch (result.Status)
            {
                case StartStatus.Started:
                    return new ActivationResult(ActivationStatus.Started);
                case StartStatus.Cancelled:
                    return new ActivationResult(ActivationStatus.Cancelled);
                default:
                    return new ActivationResult(ActivationStatus.Failed, result.Error);
            }
        }
    }
}
