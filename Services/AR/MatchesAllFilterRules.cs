using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;

namespace PlugINPRO.Services.AR
{
    public class MatchesAllFilterRules
    {
        public static bool Match(Room room, List<RoomFilterRule> rules)
        {
            foreach (RoomFilterRule rule in rules)
            {
                if (string.IsNullOrEmpty(rule.ParameterName))
                    continue;

                Parameter param = room.LookupParameter(rule.ParameterName);
                string paramValue = param?.AsString() ?? "";
                string ruleValue = rule.Value ?? "";

                bool match = rule.Condition switch
                {
                    RoomFilterCondition.Equals => paramValue == ruleValue,
                    RoomFilterCondition.NotEquals => paramValue != ruleValue,
                    RoomFilterCondition.Contains => paramValue.Contains(ruleValue),
                    RoomFilterCondition.NotContains => !paramValue.Contains(ruleValue),
                    _ => true
                };

                if (!match)
                    return false;
            }
            return true;
        }
    }
}
