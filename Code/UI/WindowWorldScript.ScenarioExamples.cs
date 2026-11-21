using System.Collections.Generic;
using GodTools.Features;
using GodTools.Features.WorldScript;
using Newtonsoft.Json.Linq;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private void CreateTrialScenario()
    {
        var scenario = new ScriptScenario { Name = L("scene.trial", "The centennial trial") };
        var opening = ExampleScript(scenario, L("scene.trial_open", "Announce the centennial trial"),
            WorldScriptRegistry.TriggerManual, WorldScriptRegistry.TargetWorld);
        opening.Flow.Steps.Add(new ScriptStep
        {
            Action = new ScriptAction
            {
                ActionID = "world.log",
                Parameters = new JObject { ["message"] = L("scene.trial_message", "The centennial trial begins.") }
            }
        });
        scenario.Scenes = new List<ScriptScene>
        {
            new() { Name = L("scene.trial_start", "The keeper's trial"), Mode = ScriptSceneMode.Pause,
                Year = Date.getYear(World.world?.getCurWorldTime() ?? 0d) + 100, StartScriptID = opening.ID,
                WatchDeath = true, WatchRuler = true },
            new() { Name = L("scene.trial_end", "The first year after the trial") }
        };
        WorldScripts.AddPackage(new List<ScriptInstance> { opening }, scenario);
        OpenScenario(scenario);
    }

    private void CreateReliefScenario()
    {
        var scenario = new ScriptScenario { Name = L("scenario.relief", "Famine relief"), Parameters = { ["relief_stock"] = 30 } };
        ScriptInstance Monitor(string direction, string name, string phase)
        {
            var script = ExampleScript(scenario, name, WorldScriptRegistry.TriggerCityThreshold, WorldScriptRegistry.TargetEventCity);
            script.TriggerCondition.Parameters = new JObject
            {
                ["threshold_metric"] = "stock", ["resource_id"] = "berries", ["low_threshold"] = 10,
                ["high_threshold"] = 24, ["sample_interval"] = 5, ["threshold_direction"] = direction
            };
            script.Flow.Steps.Add(new ScriptStep { Action = ExamplePhase(phase) });
            return script;
        }
        var start = Monitor("low", L("scenario.relief.start", "Start relief"), "receiving_relief");
        var finish = Monitor("recovered", L("scenario.relief.finish", "End relief"), "relief_recovered");
        var stock = ExampleScript(scenario, L("scenario.relief.supply", "Supply emergency food"),
            WorldScriptRegistry.TriggerInterval, WorldScriptRegistry.TargetCityAll);
        stock.Flow.Query.Filter.Children.Add(new ConditionNode
        {
            Operator = "condition", ConditionID = "scenario.phase", Parameters = new JObject { ["phase"] = "receiving_relief" }
        });
        stock.Flow.Steps.Add(new ScriptStep
        {
            Action = new ScriptAction { ActionID = "city.resources.change",
                Parameters = new JObject { ["resource_id"] = "berries", ["value"] = 30, ["stock_write"] = "at_least" } },
            Numbers = { ["value"] = new ScriptNumberBinding { Source = "scenario.parameter", Parameter = "relief_stock" } }
        });
        WorldScripts.AddPackage(new List<ScriptInstance> { start, stock, finish }, scenario);
        OpenScenario(scenario);
    }

    private void CreateDynastyScenario()
    {
        var scenario = new ScriptScenario { Name = L("scenario.dynasty", "Royal succession") };
        var script = ExampleScript(scenario, L("scenario.dynasty.record", "Record the change of ruler"),
            WorldScriptRegistry.TriggerKingdomKingChanged, WorldScriptRegistry.TargetEventKingdom);
        script.Flow.Steps.Add(new ScriptStep
        {
            Action = ExamplePhase("new_reign"),
            Branch = new ScriptBranch
            {
                Condition = new ConditionNode { Operator = "condition", ConditionID = "kingdom.has_king" },
                Otherwise = ExamplePhase("interregnum")
            }
        });
        script.Flow.Steps.Add(new ScriptStep
        {
            Mode = "once", Action = new ScriptAction
            {
                ActionID = "world.log", Parameters = new JObject
                {
                    ["message"] = L("scenario.dynasty.message", "{event_kingdom} · {event_old} → {event_new}"),
                    ["use_variables"] = true, ["log_location"] = "event"
                }
            }
        });
        WorldScripts.AddPackage(new List<ScriptInstance> { script }, scenario);
        OpenScenario(scenario);
    }

    private static ScriptInstance ExampleScript(ScriptScenario scenario, string name, string trigger, string scope) => new()
    {
        Name = name, Enabled = false, IsDraft = true, ScenarioID = scenario.ID,
        TriggerCondition = new TriggerCondition { TriggerID = trigger },
        Flow = new ScriptFlow { Query = new ScriptQuery { Scope = NewScope(scope) } }
    };

    private static ScriptAction ExamplePhase(string phase) => new()
    {
        ActionID = "scenario.phase.set", Parameters = new JObject { ["phase"] = phase }
    };
}
