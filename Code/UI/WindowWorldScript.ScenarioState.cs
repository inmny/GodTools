using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private string _newScenarioParameter = "";

    private static string ScenarioParameterName(string name) => L("scenario.parameter." + name, name ?? "?");

    private static string ScenarioPhaseName(string name) =>
        string.IsNullOrEmpty(name) ? L("scenario.phase.none", "Not started") : L("scenario.phase." + name, name);

    private void BuildScenarioState(ScriptScenario scenario)
    {
        if (FlowFold((scenario, "parameters"), L("scenario.parameters", "Shared parameters"), "iconOptions"))
        {
            foreach (var entry in scenario.Parameters.OrderBy(item => item.Key))
            {
                var row = FieldRow(ScenarioParameterName(entry.Key));
                InputAt(row, entry.Value.ToString(CultureInfo.InvariantCulture), 120, value =>
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                        scenario.Parameters[entry.Key] = amount;
                }, InputField.CharacterValidation.Integer);
            }
            var add = FieldRow(L("scenario.new_parameter", "New parameter"));
            InputAt(add, _newScenarioParameter, 108, value => _newScenarioParameter = value);
            ButtonAt(add, L("scenario.parameter_add", "Add"), 64, () =>
            {
                string name = _newScenarioParameter.Trim();
                if (name.Length == 0 || scenario.Parameters.ContainsKey(name)) return;
                scenario.Parameters.Add(name, 0);
                _newScenarioParameter = "";
                Render();
            }, NativeIcon("iconShovelPlus"), surface: Surface.Quiet);
        }

        if (scenario.Progress?.Count > 0 &&
            FlowFold((scenario, "progress"), L("scenario.progress", "Progress by object"), "iconInspect"))
        {
            foreach (var item in scenario.Progress.Values.Where(item => item?.Subject != null)
                .OrderByDescending(item => item.ChangedAt).Take(40))
                StateRow(item.Subject, $"{ScenarioPhaseName(item.Phase)} · {item.ChangedAt:0.##}");
        }
        if (scenario.Timeline?.Count > 0 &&
            FlowFold((scenario, "timeline"), L("scenario.timeline", "Phase changes"), "iconWorldLog"))
            foreach (var item in scenario.Timeline.Where(item => item?.Subject != null).Reverse().Take(30))
                StateRow(item.Subject, $"{ScenarioPhaseName(item.From)} → {ScenarioPhaseName(item.To)} · {item.ScriptName} · {item.Time:0.##}");

        void StateRow(ScriptTargetSample subject, string details)
        {
            var row = Row(_body, EditorWidth, 22);
            Label(row, $"{SampleLabel(subject)} · {details}", EditorWidth - 26);
            AddLocateButton(row, subject);
        }
    }

    private void CreateReconstructionScenario()
    {
        var scenario = new ScriptScenario
        {
            Name = L("scenario.reconstruction", "Postwar reconstruction"),
            Parameters = { ["wood_grant"] = 30, ["wood_goal"] = 20, ["review_delay"] = 60 }
        };
        ScriptInstance Script(string key, string title, string trigger, string scope) =>
            ExampleScript(scenario, L("scenario.reconstruction." + key, title), trigger, scope);
        ConditionNode IsPhase(string phase) => new() { Operator = "condition", ConditionID = "scenario.phase",
            Parameters = new JObject { ["phase"] = phase } };
        ConditionNode Rebuilding() => new() { Operator = "any", Children = { IsPhase("awaiting_supplies"), IsPhase("extended") } };
        var start = Script("start", "Start reconstruction", WorldScriptRegistry.TriggerCityKingdomChanged, WorldScriptRegistry.TargetEventCity);
        var supplies = Script("supply", "Send supplies", WorldScriptRegistry.TriggerManual, WorldScriptRegistry.TargetCityAll);
        var review = Script("review", "Review reconstruction", WorldScriptRegistry.TriggerDelayed, WorldScriptRegistry.TargetEventCity);
        ScriptStep Schedule() => new()
        {
            Action = new ScriptAction { ActionID = "script.schedule", Parameters = new JObject
                { ["script_id"] = review.ID, ["delay"] = 60, ["replace_pending"] = true, ["ownership_policy"] = "cancel",
                    ["cancel_phase"] = "cancelled" } },
            Numbers = { ["delay"] = new ScriptNumberBinding { Source = "scenario.parameter", Parameter = "review_delay" } }
        };
        start.Flow.Steps.Add(new ScriptStep { Action = ExamplePhase("awaiting_supplies") });
        start.Flow.Steps.Add(Schedule());
        supplies.Flow.Query.Filter = Rebuilding();
        supplies.Flow.Steps.Add(new ScriptStep
        {
            Action = new ScriptAction { ActionID = "city.resources.change", Parameters = new JObject { ["resource_id"] = "wood", ["value"] = 30 } },
            Numbers = { ["value"] = new ScriptNumberBinding { Source = "scenario.parameter", Parameter = "wood_grant" } }
        });
        review.Flow.Query.Filter = Rebuilding();
        review.Flow.Steps.Add(new ScriptStep
        {
            Action = ExamplePhase("complete"),
            Branch = new ScriptBranch
            {
                Condition = new ConditionNode { Operator = "condition", ConditionID = "city.resources.scenario", Parameters = new JObject
                    { ["resource_id"] = "wood", ["operator"] = ">=", ["scenario_parameter"] = "wood_goal" } },
                Otherwise = ExamplePhase("extended")
            }
        });
        var retry = Schedule();
        retry.Branch = new ScriptBranch { Condition = IsPhase("extended") };
        review.Flow.Steps.Add(retry);
        WorldScripts.AddPackage(new List<ScriptInstance> { start, supplies, review }, scenario);
        OpenScenario(scenario);
    }
}
