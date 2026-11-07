using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Playnite.SDK;
using Playnite.SDK.Models;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Integration
{
    /// <summary>
    /// Reads Playnite's Library → Software Tools list. The SDK does not expose it, so this uses reflection:
    /// <c>IPlayniteAPI.Database</c> is a <c>Playnite.API.DatabaseAPI</c> wrapper whose private <c>database</c>
    /// field (<c>Playnite.Database.GameDatabase</c>) has a <c>SoftwareApps</c> collection of SDK
    /// <see cref="AppSoftware"/> objects (checked against Playnite 10 / SDK 6.17).
    /// Any failure is reported once and yields an empty list, so the plugin keeps working on its own tools.
    /// </summary>
    internal sealed class SoftwareToolsReflectionSource
    {
        private const string PropertyName = "SoftwareApps";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private bool failureReported;

        public SoftwareToolsReflectionSource(IPlayniteAPI api) => this.api = api;

        /// <summary>Set when the last read failed; the settings page shows it as a hint.</summary>
        public string LastError { get; private set; }

        public List<SoftwareToolInfo> Read()
        {
            try
            {
                var collection = FindCollection(api.Database);
                if (collection == null)
                {
                    throw new MissingMemberException("Playnite's database no longer has a '" + PropertyName + "' collection.");
                }

                var result = collection.OfType<AppSoftware>().Select(ToInfo).ToList();
                LastError = null;
                return result;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                if (!failureReported)
                {
                    failureReported = true;
                    Logger.Error(e, "Launcher Utility could not read Playnite's Software Tools.");
                    api.Notifications.Add(new NotificationMessage(
                        "LauncherUtility_SoftwareToolsUnavailable",
                        "Launcher Utility: Playnite's Software Tools could not be read, so only tools added in Launcher settings are shown. " +
                        "This can happen after a Playnite update.",
                        NotificationType.Info));
                }

                return new List<SoftwareToolInfo>();
            }
        }

        /// <summary>Looks for SoftwareApps on the object itself, then one level down in its fields.</summary>
        private static IEnumerable FindCollection(object database)
        {
            if (database == null)
            {
                return null;
            }

            var direct = database.GetType().GetProperty(PropertyName, Flags);
            if (direct != null)
            {
                return direct.GetValue(database) as IEnumerable;
            }

            for (var type = database.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(Flags | BindingFlags.DeclaredOnly))
                {
                    var inner = field.GetValue(database);
                    var prop = inner?.GetType().GetProperty(PropertyName, Flags);
                    if (prop != null)
                    {
                        return prop.GetValue(inner) as IEnumerable;
                    }
                }
            }

            return null;
        }

        private SoftwareToolInfo ToInfo(AppSoftware app) => new SoftwareToolInfo
        {
            Id = app.Id,
            Name = app.Name ?? string.Empty,
            Path = app.Path ?? string.Empty,
            Arguments = app.Arguments ?? string.Empty,
            WorkingDir = app.WorkingDir ?? string.Empty,
            IconPath = ResolveIcon(app.Icon),
            IsScript = app.AppType == AppSoftwareType.Script
        };

        /// <summary>Software Tool icons are stored as database file ids ("guid\file.png"); turn them into real paths.</summary>
        private string ResolveIcon(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return string.Empty;
            }

            try
            {
                if (Path.IsPathRooted(icon) && File.Exists(icon))
                {
                    return icon;
                }

                return api.Database.GetFullFilePath(icon) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
