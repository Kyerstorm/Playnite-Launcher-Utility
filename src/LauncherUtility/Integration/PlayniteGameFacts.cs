using System;
using Playnite.SDK;
using Playnite.SDK.Models;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Integration
{
    /// <summary>
    /// Answers rule conditions with Playnite's own filter engine (GetGameMatchesFilter),
    /// so "Platform = PC" means exactly what it means in the library filter panel.
    /// </summary>
    internal sealed class PlayniteGameFacts : IGameFacts
    {
        private readonly Game game;
        private readonly IGameDatabaseAPI database;

        public PlayniteGameFacts(Game game, IGameDatabaseAPI database)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.database = database;
        }

        public Guid GameId => game.Id;

        public string Name => game.Name ?? string.Empty;

        public bool HasItem(ConditionType type, Guid? itemId, string text)
        {
            var filterItem = itemId.HasValue ? new IdItemFilterItemProperties(itemId.Value) : new IdItemFilterItemProperties(text ?? string.Empty);
            var filter = new FilterPresetSettings();
            switch (type)
            {
                case ConditionType.Platform:
                    filter.Platform = filterItem;
                    break;
                case ConditionType.Source:
                    filter.Source = filterItem;
                    break;
                case ConditionType.Tag:
                    filter.Tag = filterItem;
                    break;
                case ConditionType.Category:
                    filter.Category = filterItem;
                    break;
                case ConditionType.Genre:
                    filter.Genre = filterItem;
                    break;
                case ConditionType.Feature:
                    filter.Feature = filterItem;
                    break;
                default:
                    return false;
            }

            return database.GetGameMatchesFilter(game, filter, false);
        }
    }
}
