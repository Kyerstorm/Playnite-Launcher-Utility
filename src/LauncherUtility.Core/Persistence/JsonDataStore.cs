using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Persistence
{
    public sealed class LoadResult
    {
        public LoadResult(LauncherData data, string warning)
        {
            Data = data;
            Warning = warning;
        }

        public LauncherData Data { get; }

        /// <summary>Non-null when something was wrong with the file (it was set aside, or came from a newer version).</summary>
        public string Warning { get; }
    }

    /// <summary>
    /// Reads and writes <c>launcher.json</c>. A corrupt file is renamed aside rather than overwritten,
    /// and saves go through a temporary file so a crash mid-write cannot destroy the previous copy.
    /// </summary>
    public sealed class JsonDataStore
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private readonly Func<DateTime> now;

        public JsonDataStore(string filePath, Func<DateTime> now = null)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            this.now = now ?? (() => DateTime.Now);
        }

        public string FilePath { get; }

        /// <summary>
        /// Moves a data file saved under an older name (the plugin was called "Toolbox Utility" and used
        /// <c>toolbox.json</c>) to <see cref="FilePath"/>. Does nothing if the new file already exists.
        /// </summary>
        /// <returns>True if the legacy file was moved.</returns>
        public bool AdoptLegacyFile(string legacyPath)
        {
            if (string.IsNullOrEmpty(legacyPath) || File.Exists(FilePath) || !File.Exists(legacyPath))
            {
                return false;
            }

            File.Move(legacyPath, FilePath);
            return true;
        }

        public LoadResult Load()
        {
            if (!File.Exists(FilePath))
            {
                return new LoadResult(new LauncherData(), null);
            }

            string text;
            try
            {
                text = File.ReadAllText(FilePath);
            }
            catch (Exception e)
            {
                // Unreadable (locked, permissions): use defaults for this session but leave the file alone.
                return new LoadResult(new LauncherData(), "Launcher settings could not be read: " + e.Message);
            }

            LauncherData data;
            try
            {
                data = JsonConvert.DeserializeObject<LauncherData>(text, SerializerSettings);
                if (data == null)
                {
                    throw new JsonException("The file is empty.");
                }
            }
            catch (Exception e)
            {
                var aside = SetAside();
                return new LoadResult(new LauncherData(),
                    "Launcher settings were damaged and have been reset. The old file was kept as " + Path.GetFileName(aside) + ". (" + e.Message + ")");
            }

            string warning = null;
            if (data.SchemaVersion > LauncherData.CurrentSchemaVersion)
            {
                warning = "Launcher settings were saved by a newer version of Launcher Utility; some options may be ignored.";
            }

            return new LoadResult(Sanitize(data), warning);
        }

        public void Save(LauncherData data)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(Sanitize(data.Clone()), SerializerSettings);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }

        /// <summary>Repairs nulls and out-of-range values so the rest of the code never has to.</summary>
        public static LauncherData Sanitize(LauncherData data)
        {
            data = data ?? new LauncherData();
            if (data.SchemaVersion < LauncherData.CurrentSchemaVersion)
            {
                data.SchemaVersion = LauncherData.CurrentSchemaVersion; // v0 (no field) has the same shape as v1.
            }

            data.Tools = (data.Tools ?? new List<ToolEntry>()).Where(t => t != null).ToList();
            var ids = new HashSet<Guid>();
            foreach (var tool in data.Tools)
            {
                if (tool.Id == Guid.Empty || !ids.Add(tool.Id))
                {
                    tool.Id = Guid.NewGuid();
                    ids.Add(tool.Id);
                }

                tool.Name = tool.Name ?? string.Empty;
                tool.Path = tool.Path ?? string.Empty;
                tool.Arguments = tool.Arguments ?? string.Empty;
                tool.WorkingDir = tool.WorkingDir ?? string.Empty;
                tool.IconPath = tool.IconPath ?? string.Empty;
                tool.Category = tool.Category ?? string.Empty;
                tool.AutoLaunch = SanitizeRule(tool.AutoLaunch);
            }

            data.Overrides = (data.Overrides ?? new List<ToolOverride>())
                .Where(o => o != null && o.SoftwareToolId != Guid.Empty)
                .GroupBy(o => o.SoftwareToolId)
                .Select(g => g.Last())
                .ToList();
            foreach (var o in data.Overrides)
            {
                o.Category = o.Category ?? string.Empty;
                o.AutoLaunch = SanitizeRule(o.AutoLaunch);
            }

            data.CloseTimeoutSeconds = Math.Max(LauncherData.MinCloseTimeoutSeconds, Math.Min(LauncherData.MaxCloseTimeoutSeconds, data.CloseTimeoutSeconds));
            data.WindowWidth = ValidSize(data.WindowWidth);
            data.WindowHeight = ValidSize(data.WindowHeight);
            data.WindowLeft = ValidNumber(data.WindowLeft);
            data.WindowTop = ValidNumber(data.WindowTop);
            return data;
        }

        private static double? ValidSize(double? value) =>
            value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value >= 200 ? value : null;

        private static double? ValidNumber(double? value) =>
            value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? value : null;

        private static AutoLaunchRule SanitizeRule(AutoLaunchRule rule)
        {
            rule = rule ?? new AutoLaunchRule();
            rule.DelayMs = Math.Max(0, Math.Min(LauncherData.MaxDelayMs, rule.DelayMs));
            rule.Conditions = (rule.Conditions ?? new List<RuleCondition>()).Where(c => c != null).ToList();
            foreach (var c in rule.Conditions)
            {
                c.Text = c.Text ?? string.Empty;
                if (!Enum.IsDefined(typeof(ConditionType), c.Type))
                {
                    c.Type = ConditionType.All;
                }

                if (!Enum.IsDefined(typeof(Joiner), c.Joiner))
                {
                    c.Joiner = Joiner.And;
                }
            }

            return rule;
        }

        private string SetAside()
        {
            var target = FilePath + ".corrupt-" + now().ToString("yyyyMMdd-HHmmss");
            try
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(FilePath, target);
            }
            catch
            {
                // If it cannot be moved, the next Save simply overwrites it; nothing more we can do.
            }

            return target;
        }
    }
}
