using System;
using System.Collections.Generic;
using System.Linq;
using LauncherUtility.Core.Abstractions;
using LauncherUtility.Core.Models;

namespace LauncherUtility.Core.Services
{
    /// <summary>
    /// Decides whether a game matches a rule's conditions. Conditions fold strictly left to right:
    /// <c>((c1 J1 c2) J2 c3) …</c>, where Jn is condition n's <see cref="RuleCondition.Joiner"/>.
    /// There is no operator precedence, which matches how the conditions grid reads top to bottom.
    /// </summary>
    public static class RuleEvaluator
    {
        public static bool Matches(IList<RuleCondition> conditions, IGameFacts game)
        {
            if (game == null)
            {
                return false;
            }

            var list = (conditions ?? new List<RuleCondition>()).Where(c => c != null).ToList();
            if (list.Count == 0)
            {
                return true;
            }

            var result = Evaluate(list[0], game);
            for (var i = 1; i < list.Count; i++)
            {
                var next = Evaluate(list[i], game);
                result = list[i - 1].Joiner == Joiner.Or ? result || next : result && next;
            }

            return result;
        }

        public static bool Evaluate(RuleCondition condition, IGameFacts game)
        {
            bool raw;
            switch (condition.Type)
            {
                case ConditionType.All:
                    raw = true;
                    break;
                case ConditionType.SpecificGame:
                    raw = condition.ItemId.HasValue && condition.ItemId.Value == game.GameId;
                    break;
                case ConditionType.Name:
                    raw = !string.IsNullOrWhiteSpace(condition.Text) &&
                          (game.Name ?? string.Empty).IndexOf(condition.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
                    break;
                default:
                    raw = (condition.ItemId.HasValue || !string.IsNullOrWhiteSpace(condition.Text)) &&
                          game.HasItem(condition.Type, condition.ItemId, condition.Text);
                    break;
            }

            return condition.Not ? !raw : raw;
        }
    }
}
