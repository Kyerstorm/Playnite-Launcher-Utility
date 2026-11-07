using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Integration
{
    /// <summary>
    /// Windows implementation of process handling. Playnite is a 32-bit process, so full image paths are read with
    /// QueryFullProcessImageName (works across bitness and for most elevated processes) instead of Process.MainModule.
    /// </summary>
    internal sealed class WindowsProcessLauncher : IProcessLauncher
    {
        private const int ErrorCancelled = 1223;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int SwRestore = 9;

        private readonly string playniteDir;

        public WindowsProcessLauncher(string playniteDir) => this.playniteDir = playniteDir ?? string.Empty;

        /// <summary>Expands {PlayniteDir} and environment variables, and resolves paths relative to the Playnite folder.</summary>
        public string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))
                .Replace("{PlayniteDir}", playniteDir.TrimEnd('\\', '/'));

            try
            {
                if (!Path.IsPathRooted(expanded) && !string.IsNullOrEmpty(playniteDir))
                {
                    var candidate = Path.GetFullPath(Path.Combine(playniteDir, expanded));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // Not a file system path (URL, protocol handler); leave it for the shell.
            }

            return expanded;
        }

        public bool IsRunning(LauncherItem item)
        {
            var found = FindProcesses(item);
            var running = found.Count > 0;
            found.ForEach(p => p.Dispose());
            return running;
        }

        public bool TryFocusRunning(LauncherItem item)
        {
            var found = FindProcesses(item);
            try
            {
                foreach (var process in found)
                {
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (IsIconic(handle))
                    {
                        ShowWindow(handle, SwRestore);
                    }

                    SetForegroundWindow(handle);
                    return true;
                }

                return false;
            }
            finally
            {
                found.ForEach(p => p.Dispose());
            }
        }

        public StartResult Start(LauncherItem item)
        {
            var path = ResolvePath(item.Path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return StartResult.Failed("No path is set.");
            }

            if (LooksLikeFile(path) && !File.Exists(path))
            {
                return StartResult.Failed("File not found: " + path);
            }

            var info = new ProcessStartInfo(path)
            {
                Arguments = item.Arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDir(item.WorkingDir, path)
            };

            if (item.RunAsAdmin)
            {
                info.Verb = "runas";
            }

            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return StartResult.Started(null); // Handed to an existing process by the shell.
                    }

                    return StartResult.Started(new TrackedProcess(process.Id, TryGetStartTimeUtc(process) ?? DateTime.MinValue));
                }
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return StartResult.Cancelled();
            }
            catch (Exception e)
            {
                return StartResult.Failed(e.Message);
            }
        }

        public CloseOutcome Close(TrackedProcess tracked, TimeSpan gracefulTimeout)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(tracked.ProcessId);
            }
            catch (ArgumentException)
            {
                return CloseOutcome.NotRunning;
            }

            using (process)
            {
                try
                {
                    if (tracked.StartTimeUtc != DateTime.MinValue)
                    {
                        var started = TryGetStartTimeUtc(process);
                        if (started.HasValue && Math.Abs((started.Value - tracked.StartTimeUtc).TotalSeconds) > 2)
                        {
                            return CloseOutcome.NotRunning; // The PID now belongs to some other process.
                        }
                    }

                    if (process.CloseMainWindow() && process.WaitForExit((int)gracefulTimeout.TotalMilliseconds))
                    {
                        return CloseOutcome.ClosedGracefully;
                    }

                    process.Kill();
                    process.WaitForExit(3000);
                    return CloseOutcome.Killed;
                }
                catch (Win32Exception)
                {
                    return CloseOutcome.AccessDenied;
                }
                catch (InvalidOperationException)
                {
                    return CloseOutcome.NotRunning; // Exited while we were closing it.
                }
                catch (Exception)
                {
                    return CloseOutcome.Failed;
                }
            }
        }

        /// <summary>Which of the given tools are running, from a single process enumeration (for the 2-second UI refresh).</summary>
        public HashSet<Guid> GetRunningToolIds(IEnumerable<LauncherItem> items)
        {
            var result = new HashSet<Guid>();
            var byName = items
                .Where(i => i != null && i.CanLaunch)
                .Select(i => new { Item = i, Exe = ResolvePath(i.Path) })
                .Where(x => IsExe(x.Exe))
                .GroupBy(x => SafeFileName(x.Exe), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            if (byName.Count == 0)
            {
                return result;
            }

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (!byName.TryGetValue(SafeProcessName(process), out var candidates))
                    {
                        continue;
                    }

                    var image = TryGetImagePath(process.Id);
                    foreach (var c in candidates)
                    {
                        if (image == null || string.Equals(Normalize(image), Normalize(c.Exe), StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(c.Item.Id);
                        }
                    }
                }
            }

            return result;
        }

        private List<Process> FindProcesses(LauncherItem item)
        {
            var exe = ResolvePath(item?.Path);
            var result = new List<Process>();
            if (!IsExe(exe))
            {
                return result; // Shortcuts, scripts and URLs cannot be matched to a process.
            }

            foreach (var process in Process.GetProcessesByName(SafeFileName(exe)))
            {
                var image = TryGetImagePath(process.Id);
                // If the path cannot be read (rare: protected processes), fall back to matching by name.
                if (image == null || string.Equals(Normalize(image), Normalize(exe), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }

            return result;
        }

        private static string ResolveWorkingDir(string workingDir, string path)
        {
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                return Environment.ExpandEnvironmentVariables(workingDir.Trim().Trim('"'));
            }

            try
            {
                return LooksLikeFile(path) ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksLikeFile(string path)
        {
            try
            {
                return Path.IsPathRooted(path) && !path.Contains("://");
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExe(string path) =>
            !string.IsNullOrWhiteSpace(path) && LooksLikeFile(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        private static string SafeFileName(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return path;
            }
        }

        private static DateTime? TryGetStartTimeUtc(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime();
            }
            catch
            {
                return null; // Elevated processes deny this to a non-elevated Playnite.
            }
        }

        private static string TryGetImagePath(int processId)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var builder = new StringBuilder(1024);
                var size = builder.Capacity;
                return QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }

    /// <summary>Loads and caches tool icons: an explicit image file first, otherwise the exe's own icon.</summary>
    internal sealed class IconCache
    {
        private readonly ConcurrentDictionary<string, ImageSource> cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly WindowsProcessLauncher launcher;

        public IconCache(WindowsProcessLauncher launcher) => this.launcher = launcher;

        public ImageSource Get(LauncherItem item)
        {
            if (item == null)
            {
                return null;
            }

            var key = (item.IconPath ?? string.Empty) + "|" + (item.Path ?? string.Empty);
            return cache.GetOrAdd(key, _ => Load(item));
        }

        public void Clear() => cache.Clear();

        private ImageSource Load(LauncherItem item)
        {
            try
            {
                var icon = launcher.ResolvePath(item.IconPath);
                if (!string.IsNullOrEmpty(icon) && File.Exists(icon))
                {
                    if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return FromExe(icon);
                    }

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 48;
                    image.UriSource = new Uri(icon, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }

                var exe = launcher.ResolvePath(item.Path);
                return File.Exists(exe) ? FromExe(exe) : null;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource FromExe(string path)
        {
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
            {
                if (icon == null)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
        }
    }
}
