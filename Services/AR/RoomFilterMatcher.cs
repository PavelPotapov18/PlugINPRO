using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;

namespace PlugINPRO.Services.AR
{
    public class RoomFilterMatcher
    {
        public static bool Match(Room room, List<RoomFilterRule> rules, RoomFilterMatchMode mode)
        {
            if (room == null)
                return false;

            // Убираем пустые правила
            List<RoomFilterRule> validRules =
                rules
                    .FindAll(x =>
                        x != null &&
                        !string.IsNullOrWhiteSpace(x.ParameterName));

            // Если условий нет,
            // помещение считаем подходящим
            if (validRules.Count == 0)
                return true;

            if (mode == RoomFilterMatchMode.All)
            {
                // AND
                foreach (RoomFilterRule rule in validRules)
                {
                    if (!MatchRule(room, rule))
                        return false;
                }

                return true;
            }
            if (mode == RoomFilterMatchMode.Any)
            {
                // OR
                foreach (RoomFilterRule rule in validRules)
                {
                    if (MatchRule(room, rule))
                        return true;
                }

                return false;
            }

            return false;
        }

        private static bool MatchRule(
            Room room,
            RoomFilterRule rule)
        {
            Parameter param =
                room.LookupParameter(rule.ParameterName);

            string paramValue =
                param?.AsString() ?? string.Empty;

            string ruleValue =
                rule.Value ?? string.Empty;

            switch (rule.Condition)
            {
                case RoomFilterCondition.Equals:

                    return paramValue == ruleValue;

                case RoomFilterCondition.NotEquals:

                    return paramValue != ruleValue;

                case RoomFilterCondition.Contains:

                    return paramValue.Contains(ruleValue);

                case RoomFilterCondition.NotContains:

                    return !paramValue.Contains(ruleValue);

                default:

                    return true;
            }
        }
    }
}
