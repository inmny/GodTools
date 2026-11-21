using System;
using System.Collections.Generic;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptRegistry
{
    private const ScriptTargetKind StateTargets = ScriptTargetKind.World | ScriptTargetKind.City | ScriptTargetKind.Kingdom;
    private static readonly ScriptParameterDefinition StateKey = Text("state_key", "State key", "");

    private static void RegisterStateActions()
    {
        AddCondition("scenario.phase", "Scenario phase", StateTargets, new[] { Text("phase", "Phase", "") },
            (context, target, values) => WorldScriptEngine.ScenarioPhase(context, target) == GetString(values, "phase").Trim());
        AddAction("scenario.phase.set", "Set scenario phase", StateTargets, false, new[] { Text("phase", "Phase", "") },
            (context, target, values) => WorldScriptEngine.ChangeScenarioPhase(context, target, GetString(values, "phase").Trim()));
        AddCondition("city.resources.scenario", "Village stock vs scenario parameter", ScriptTargetKind.City, new[]
        {
            Text("resource_id", "Resource", "wood"), Choice("operator", "Operator", ">=", WorldScriptEngine.Comparisons),
            Text("scenario_parameter", "Scenario parameter", "")
        }, (context, target, values) =>
        {
            string key = GetString(values, "scenario_parameter");
            return WorldScriptEngine.Scenario(context.Script.ScenarioID)?.Parameters.TryGetValue(key, out int threshold) == true &&
                Compare(((City)target.Meta).getResourcesAmount(GetString(values, "resource_id", "wood")),
                    GetString(values, "operator", ">="), threshold);
        });

        AddCondition("state.number", "Saved value", StateTargets, new[]
        {
            StateKey, Choice("operator", "Operator", ">=", WorldScriptEngine.Comparisons), Integer("value", "Value", 1)
        }, (context, target, values) => Compare(ReadState(context, StateAddress(target, GetString(values, "state_key"))), values));

        var parameters = new[] { StateKey, Integer("value", "Value", 1) };
        AddAction("state.set", "Set saved value", StateTargets, false, parameters,
            (context, target, values) => WriteState(context, StateAddress(target, GetString(values, "state_key")), GetInt(values, "value", 1)));
        AddAction("state.add", "Add to saved value", StateTargets, false, parameters, (context, target, values) =>
        {
            string key = StateAddress(target, GetString(values, "state_key"));
            long next = (long)ReadState(context, key) + GetInt(values, "value", 1);
            WriteState(context, key, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next)));
        });

        AddAction("script.schedule", "Schedule a follow-up script", StateTargets, false, new[]
        {
            Text("script_id", "Follow-up script", ""), Number("delay", "Delay", 60d),
            Choice("ownership_policy", "On village ownership change", "continue", "continue", "cancel"),
            Text("cancel_phase", "Phase when cancelled", ""),
            new ScriptParameterDefinition { Key = "replace_pending", Name = "Replace pending follow-up",
                Type = ScriptParameterType.Boolean, DefaultValue = true }
        }, (context, target, values) => WorldScriptEngine.Schedule(context, target, GetString(values, "script_id"),
            GetNumber(values, "delay", 60d), GetBool(values, "replace_pending", true),
            GetString(values, "ownership_policy", "continue") == "cancel", GetString(values, "cancel_phase")));
    }

    private static string StateAddress(WorldScriptTarget target, string name) => target.Kind == ScriptTargetKind.World
        ? "world/" + name.Trim() : $"{target.Kind}/{target.MetaID}/{name.Trim()}";

    private static int ReadState(WorldScriptExecutionContext context, string key) =>
        context.Script.Runtime?.State?.GetValueOrDefault(key, 0) ?? 0;

    private static void WriteState(WorldScriptExecutionContext context, string key, int value)
    {
        int before = ReadState(context, key);
        if (before == value)
        {
            context.SkipAction("no_change");
            return;
        }
        var state = context.Script.Runtime.State ??= new System.Collections.Generic.Dictionary<string, int>();
        if (value == 0) state.Remove(key);
        else state[key] = value;
        context.RecordChange((long)value - before);
    }
}
