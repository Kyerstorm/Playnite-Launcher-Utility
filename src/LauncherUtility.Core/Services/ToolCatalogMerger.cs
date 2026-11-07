using System;
using System.Collections.Generic;
using System.Linq;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Services
{
    /// <summary>Builds the single list the Launcher shows from Playnite's Software Tools and Launcher's own entries.</summary>
    public static class ToolCatalogMerger
    {
        public static List<LauncherItem> Merge(IEnumerable<SoftwareToolInfo> softwareTools, LauncherData data, bool includeHidden = false)
        {
            data = data ?? new LauncherData();
            var result = new List<LauncherItem>();

            if (data.IncludeSoftwareTools && softwareTools != null)
            {
                var overrides = (data.Overrides ?? new List<ToolOverride>())
                    .Where(o => o != null)
                    .GroupBy(o => o.SoftwareToolId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var tool in softwareTools.Where(t => t != null))
                {
                    overrides.TryGetValue(tool.Id, out var o);
                    var item = new LauncherItem
                    {
                        Id = tool.Id,
                        Origin = ToolOrigin.SoftwareTool,
                        Name = Clean(tool.Name),
                        Path = Clean(tool.Path),
                        Arguments = Clean(tool.Arguments),
                        WorkingDir = Clean(tool.WorkingDir),
                        IconPath = Clean(tool.IconPath),
                        IsScript = tool.IsScript,
                        Category = Clean(o?.Category),
                        RunAsAdmin = o?.RunAsAdmin ?? false,
                        SingleInstance = o?.SingleInstance ?? true,
                        Hidden = o?.Hidden ?? false,
                        AutoLaunch = o?.AutoLaunch ?? new AutoLaunchRule()
                    };

                    if (includeHidden || !item.Hidden)
                    {
                        result.Add(item);
                    }
                }
            }

            var seen = new HashSet<Guid>(result.Select(r => r.Id));
            foreach (var entry in (data.Tools ?? new List<ToolEntry>()).Where(t => t != null))
            {
                if (!seen.Add(entry.Id))
                {
                    continue; // An id clash with a Software Tool: the Software Tool wins.
                }

                result.Add(new LauncherItem
                {
                    Id = entry.Id,
                    Origin = ToolOrigin.Launcher,
                    Name = Clean(entry.Name),
                    Path = Clean(entry.Path),
                    Arguments = Clean(entry.Arguments),
                    WorkingDir = Clean(entry.WorkingDir),
                    IconPath = Clean(entry.IconPath),
                    Category = Clean(entry.Category),
                    RunAsAdmin = entry.RunAsAdmin,
                    SingleInstance = entry.SingleInstance,
                    AutoLaunch = entry.AutoLaunch ?? new AutoLaunchRule()
                });
            }

            return result
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Origin)
                .ToList();
        }

        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
