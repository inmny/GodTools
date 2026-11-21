using System;
using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    [ThreadStatic] private static ScriptScenario _validationScenario;
    [ThreadStatic] private static Dictionary<string, ScriptInstance> _validationScripts;

    internal static ScriptInstance FindScriptForValidation(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_validationScripts?.TryGetValue(id, out var draft) == true) return draft;
        return WorldScripts.Scripts.Find(script => script?.ID == id);
    }

    internal static List<(ScriptInstance Script, List<string> Errors)> Preflight(
        IEnumerable<ScriptInstance> scripts, ScriptScenario scenario, bool export)
    {
        var plans = scripts.ToList();
        var previousScenario = _validationScenario;
        var previousScripts = _validationScripts;
        _validationScenario = scenario;
        _validationScripts = plans.ToDictionary(script => script.ID);
        try
        {
            var report = new List<(ScriptInstance, List<string>)>(plans.Count);
            foreach (var script in plans)
            {
                var errors = Validate(script).Errors;
                if (export)
                    foreach (string id in WorldScriptTemplates.EditableFields(script)
                        .Where(field => field.Definition == "script.schedule")
                        .Select(field => WorldScriptRegistry.GetString(field.Parameters, "script_id")).Distinct())
                    {
                        if (!string.IsNullOrEmpty(id) && !_validationScripts.ContainsKey(id))
                            errors.Add("Follow-up is outside this package");
                    }
                report.Add((script, errors));
            }
            return report;
        }
        finally
        {
            _validationScenario = previousScenario;
            _validationScripts = previousScripts;
        }
    }
}
