using System;
using System.Collections.Generic;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    internal static ScriptScenario Scenario(string id) =>
        string.IsNullOrEmpty(id) ? null : _validationScenario?.ID == id ? _validationScenario
            : WorldScripts.Scenarios.Find(scenario => scenario.ID == id);

    internal static string ScenarioValueAddress(WorldScriptTarget target) => target.Kind switch
    {
        ScriptTargetKind.City => $"city/{target.MetaID}",
        ScriptTargetKind.Kingdom => $"kingdom/{target.MetaID}",
        _ => "world"
    };

    internal static string ScenarioPhase(WorldScriptExecutionContext context, WorldScriptTarget target) =>
        Scenario(context.Script.ScenarioID)?.Progress?.GetValueOrDefault(ScenarioValueAddress(target))?.Phase ?? "";

    internal static void ChangeScenarioPhase(WorldScriptExecutionContext context, WorldScriptTarget target, string phase)
    {
        var scenario = Scenario(context.Script.ScenarioID);
        if (scenario == null)
        {
            context.SkipAction("scenario_missing");
            return;
        }
        if (!SetScenarioPhase(scenario, ScenarioValueAddress(target), ScriptTargetSample.From(target), phase,
                context.Script.Name, context.WorldTime))
            context.SkipAction("no_change");
        else WorldScripts.NotifyRuntimeChanged(context.Script);
    }

    private static bool SetScenarioPhase(ScriptScenario scenario, string address, ScriptTargetSample subject,
        string phase, string scriptName, double time)
    {
        var progress = scenario.Progress ??= new Dictionary<string, ScriptScenarioProgress>();
        string previous = progress.GetValueOrDefault(address)?.Phase ?? "";
        if (string.Equals(previous, phase, StringComparison.Ordinal)) return false;
        progress[address] = new ScriptScenarioProgress { Phase = phase, ChangedAt = time, Subject = subject };
        var timeline = scenario.Timeline ??= new List<ScriptScenarioTransition>();
        timeline.Add(new ScriptScenarioTransition
        {
            Subject = subject, From = previous, To = phase, ScriptName = scriptName, Time = time
        });
        if (timeline.Count > 80) timeline.RemoveAt(0);
        return true;
    }
}
