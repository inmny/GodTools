using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private ScriptScenario _scenario;
    private string _newScenarioName = "";
    private string _focusStepID;
    private RectTransform _focusStepRow;

    private void BuildScenarioList()
    {
        Heading(L("scenario.list", "Scenarios"));
        InputAt(FieldRow(L("scenario.name", "Scenario name")), _newScenarioName, 206, value => _newScenarioName = value);
        ButtonAt(_body, L("scenario.add", "Create scenario"), 144, () =>
        {
            string name = _newScenarioName.Trim();
            if (name.Length == 0) return;
            var scenario = new ScriptScenario { Name = name };
            WorldScripts.Scenarios.Add(scenario);
            _newScenarioName = "";
            OpenScenario(scenario);
        }, NativeIcon("iconShovelPlus"), surface: Surface.Primary);

        foreach (var scenario in WorldScripts.Scenarios)
        {
            int count = WorldScripts.Scripts.Count(script => script.ScenarioID == scenario.ID);
            ButtonAt(_body, $"{scenario.Name} · {string.Format(L("scenario.script_count", "{0} scripts"), count)}",
                EditorWidth, () => OpenScenario(scenario), NativeIcon("iconWorldLaws"), surface: Surface.List);
        }
    }

    private void OpenScenario(ScriptScenario scenario)
    {
        _scenario = scenario;
        _expanded.Clear();
        Enter(Page.Scenario);
    }

    private void ChooseScenario()
    {
        var script = _selected;
        if (script == null) return;
        void Assign(string id)
        {
            script.ScenarioID = id;
            if (_drafts.TryGetValue(script, out var draft)) draft.ScenarioID = id;
            Enter(Page.Rules);
        }

        var options = WorldScripts.Scenarios.Select(scenario => new Option(scenario.Name, () => Assign(scenario.ID),
            NativeIcon("iconWorldLaws"), script.ScenarioID == scenario.ID)).ToList();
        options.Insert(0, new Option(L("scenario.none", "Separate script"), () => Assign(null)));
        OpenPicker(L("scenario.assign", "Move to scenario"), options);
    }

    private void BuildScenario()
    {
        var scenario = _scenario;
        if (scenario == null)
        {
            BuildScenarioList();
            return;
        }

        ButtonAt(_body, L("scenario.list", "Scenarios"), 104, () => Enter(Page.ScenarioList),
            NativeIcon("iconArrowLeft"), surface: Surface.Quiet);
        Heading(scenario.Name);
        if (scenario.PackageRevision > 0)
            Label(_body, $"{L("packages.revision", "Revision")} {scenario.PackageRevision}", EditorWidth);
        BuildSceneList(scenario);
        ButtonAt(_body, L("packages.export", "Export script package"), 150, () => OpenScenarioExport(scenario),
            NativeIcon("iconSaveLocal"), surface: Surface.Quiet)
            .interactable = WorldScripts.Scripts.Any(script => script.ScenarioID == scenario.ID);
        ButtonAt(_body, L("rehearsal.create", "Create rehearsal copies"), 150, () =>
            CreateRehearsal(scenario.Name, WorldScripts.Scripts.Where(script => script.ScenarioID == scenario.ID)
                .Select(script => _drafts.GetValueOrDefault(script, script)), scenario),
            NativeIcon("iconInspect"), surface: Surface.Quiet)
            .interactable = WorldScripts.Scripts.Any(script => script.ScenarioID == scenario.ID);
        var scripts = WorldScripts.Scripts.Where(script => script.ScenarioID == scenario.ID).ToArray();
        var byID = WorldScripts.Scripts.GroupBy(script => script.ID).ToDictionary(group => group.Key, group => group.First());
        if (FlowFold((scenario, "scripts"), string.Format(L("scenario.script_count", "{0} scripts"), scripts.Length), "iconWorldLaws"))
        {
            foreach (var script in scripts)
            {
                var trigger = WorldScriptRegistry.GetTrigger(script.TriggerCondition.TriggerID);
                string name = $"{script.Name} · {TriggerSummary(trigger, script.TriggerCondition.Parameters)}";
                ButtonAt(_body, name, EditorWidth, () => SelectScript(script), NativeIcon("iconPlay"), surface: Surface.List);
                foreach (string id in WorldScriptTemplates.EditableFields(script).Where(field => field.Definition == "script.schedule")
                    .Select(field => WorldScriptRegistry.GetString(field.Parameters, "script_id")).Distinct())
                {
                    if (byID.TryGetValue(id, out var next))
                        ButtonAt(_body, $"{L("parameter.script_id", "Follow-up script")} → {next.Name}", EditorWidth - 20,
                            () => SelectScript(next), NativeIcon("iconArrowRight"), surface: Surface.Quiet);
                    else
                        Message(L("validation.followup", "Choose a follow-up script."), true, indent: 20);
                }
            }
        }

        var pending = scripts.SelectMany(script => (script.Runtime.DelayedRuns ?? Enumerable.Empty<ScriptDelayedRun>())
            .Where(run => run != null).Select(run => (Script: script, Run: run))).OrderBy(item => item.Run.Due).ToArray();
        if (WorldScriptEngine.QueuedWork > 0)
            Label(_body, string.Format(L("scenario.scheduler_queue", "{0} queued events or follow-ups"), WorldScriptEngine.QueuedWork), EditorWidth);
        if (WorldScriptEngine.DroppedEvents > 0)
            Label(_body, string.Format(L("scenario.scheduler_dropped", "{0} events dropped at the queue limit"), WorldScriptEngine.DroppedEvents), EditorWidth);
        if (FlowFold((scenario, "tasks"), string.Format(L("scenario.task_count", "{0} pending follow-ups"), pending.Length), "iconClock"))
        {
            foreach (var item in pending.Take(40))
            {
                var run = item.Run;
                byID.TryGetValue(run.ScriptID, out var script);
                var subject = WorldScriptEngine.ScheduledSubject(run);
                string phase = script?.Enabled == false ? L("editor.paused", "Disabled") :
                    string.Format(L("scenario.task_due", "Due {0:0.##}"), run.Due);
                var row = Row(_body, EditorWidth, 22);
                ButtonAt(row, $"{script?.Name ?? run.ScriptID} · {SampleLabel(subject)} · {phase}",
                    EditorWidth - 52, () => OpenScriptHistory(item.Script), NativeIcon("iconClock"), surface: Surface.List);
                AddLocateButton(row, subject);
                IconCommand(row, L("scenario.cancel_task", "Cancel follow-up"), "iconDemolish", () =>
                    Confirm(L("scenario.cancel_task_question", "Cancel this pending follow-up?"), () =>
                    {
                        WorldScriptEngine.CancelFollowUp(item.Script, run);
                        Enter(Page.Scenario);
                    }, () => Enter(Page.Scenario)));
            }
            if (pending.Length > 40) Message(string.Format(L("editor.extra_targets", "+ {0} more targets"), pending.Length - 40), false);
        }
        var outcomes = scripts.SelectMany(script => script.Runtime.DelayedOutcomes ?? Enumerable.Empty<ScriptDelayedOutcome>())
            .Where(item => item?.Run != null).OrderByDescending(item => item.Time).Take(20).ToArray();
        if (outcomes.Length > 0 && FlowFold((scenario, "followUps"), L("scenario.follow_up_results", "Follow-up results"), "iconClock"))
            foreach (var outcome in outcomes)
            {
                var subject = WorldScriptEngine.ScheduledSubject(outcome.Run);
                var row = Row(_body, EditorWidth, 22);
                string result = L("scenario.task_outcome." + outcome.Outcome, RunOutcome(outcome.Outcome));
                Label(row, $"{SampleLabel(subject)} · {result} · {outcome.Time:0.##}", EditorWidth - 26);
                AddLocateButton(row, subject);
            }

        var recent = scripts.SelectMany(script => (script.Runtime.History ?? Enumerable.Empty<ScriptExecutionRecord>())
            .Where(record => record != null).Select(record => (Script: script, Record: record)))
            .OrderByDescending(item => item.Record.WorldTime).Take(20).ToArray();
        if (FlowFold((scenario, "recent"), L("scenario.recent", "Recent executions"), "iconWorldLog"))
            foreach (var item in recent)
            {
                var record = item.Record;
                var row = Row(_body, EditorWidth, 22);
                string subject = record.Subject == null ? "" : SampleLabel(record.Subject) + " · ";
                ButtonAt(row, $"{item.Script.Name} · {subject}{RunOutcome(record.Outcome)} · {record.WorldTime:0.##}",
                    EditorWidth - 26, () => OpenScriptHistory(item.Script, record), NativeIcon("iconWorldLog"), surface: Surface.List);
                AddLocateButton(row, record.Subject);
            }

        BuildScenarioState(scenario);
    }

    private static void AddLocateButton(Transform row, ScriptTargetSample subject)
    {
        WorldTile tile = subject?.Kind switch
        {
            ScriptTargetKind.City => World.world?.cities.get(subject.MetaID)?.getTile(),
            ScriptTargetKind.Kingdom => World.world?.kingdoms.get(subject.MetaID)?.capital?.getTile(),
            ScriptTargetKind.Actor => World.world?.units.get(subject.ActorID)?.current_tile,
            ScriptTargetKind.Tile => World.world?.GetTile(subject.X, subject.Y),
            _ => null
        };
        if (tile != null)
            IconCommand(row, L("scenario.locate", "Locate on map"), "iconArrowAttackTarget",
                () => WorldLog.locationJump(new Vector3(tile.x, tile.y, 0)));
    }

    private void OpenScriptHistory(ScriptInstance script, ScriptExecutionRecord record = null)
    {
        SelectScript(script);
        if (record != null) _expanded.Add(record);
        Enter(Page.History);
    }

    private void JumpToStep(string id)
    {
        ScriptStep step = Draft?.Flow?.Steps.Find(item => item.ID == id);
        if (step == null) return;
        _focusStepID = step.ID;
        _focusStepRow = null;
        _expanded.Add(step);
        Enter(Page.Rules);
        Canvas.ForceUpdateCanvases();
        if (_focusStepRow != null)
        {
            float room = _body.rect.height - _bodyScroll.viewport.rect.height;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_body, _focusStepRow);
            if (room > 0) _bodyScroll.verticalNormalizedPosition = Mathf.Clamp01(1 - (_body.rect.yMax - bounds.max.y) / room);
        }
        _focusStepID = null;
        _focusStepRow = null;
    }
}
