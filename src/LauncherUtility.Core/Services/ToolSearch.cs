using System;
using System.Collections.Generic;
using System.Linq;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Services
{
    /// <summary>Filters and orders tools for the Launcher list.</summary>
    public static class ToolSearch
    {
        public const string UncategorisedLabel = "Uncategorised";

        /// <summary>
        /// Every whitespace-separated word must appear in the name or category. Name prefix matches rank first,
        /// then other name matches, then category-only matches. With grouping, results are ordered by category first.
        /// </summary>
        public static List<LauncherItem> Filter(IEnumerable<LauncherItem> items, string query, bool groupByCategory)
        {
            var source = (items ?? Enumerable.Empty<LauncherItem>()).Where(i => i != null);
            var words = (query ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var trimmed = (query ?? string.Empty).Trim();

            var matches = source
                .Where(i => words.All(w => Contains(i.Name, w) || Contains(i.Category, w)))
                .Select(i => new { Item = i, Rank = Rank(i, trimmed, words) });

            if (groupByCategory)
            {
                return matches
                    .OrderBy(m => string.IsNullOrWhiteSpace(m.Item.Category) ? 1 : 0)
                    .ThenBy(m => GroupLabel(m.Item), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(m => m.Rank)
                    .ThenBy(m => m.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(m => m.Item)
                    .ToList();
            }

            return matches
                .OrderBy(m => m.Rank)
                .ThenBy(m => m.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(m => m.Item)
                .ToList();
        }

        public static string GroupLabel(LauncherItem item) =>
            string.IsNullOrWhiteSpace(item?.Category) ? UncategorisedLabel : item.Category.Trim();

        private static int Rank(LauncherItem item, string query, string[] words)
        {
            if (words.Length == 0)
            {
                return 0;
            }

            if ((item.Name ?? string.Empty).StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            {
                return 0;
            }

            return words.All(w => Contains(item.Name, w)) ? 1 : 2;
        }

        private static bool Contains(string haystack, string needle) =>
            (haystack ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
