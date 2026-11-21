using System.Linq;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private void BuildHistory()
    {
        Heading(L("editor.history", "Run history"));
        var runtime = _selected.Runtime;
        if (runtime.Batch is { } batch)
        {
            var script = _selected;
            Message(string.Format(L("history.batch_progress", "Batch · Step {0}/{1} · {2}/{3} targets"),
                batch.Step + 1, batch.Steps.Count, batch.Offset, batch.Targets.Count), false);
            if (World.world?.isPaused() == true)
                Message(L("history.batch_paused", "Queued work continues when the world resumes."), false);
            ButtonAt(_body, L("history.cancel_batch", "Cancel batch"), 120, () =>
                Confirm(L("history.cancel_batch_question", "Cancel the remaining work in this batch?"), () =>
                {
                    WorldScriptEngine.CancelBatch(script);
                    Enter(Page.History);
                }, () => Enter(Page.History)), NativeIcon("iconDemolish"), surface: Surface.Quiet);
        }
        if (runtime.TriggerCount > 0)
        {
            Message(string.Format(L("history.trigger_total", "Trigger events {0} · Automatic runs {1}"),
                runtime.TriggerCount, runtime.RunCount), false);
            if (runtime.LastTriggerWorldTime - runtime.FirstTriggerWorldTime is double span && span > 0d)
                Message(string.Format(L("history.trigger_rate", "{0:0.##} trigger events per game year"),
                    runtime.TriggerCount * 60d / span), false);
        }
        if (runtime.DelayedRuns?.Count > 0)
            Message(string.Format(L("history.pending_runs", "{0} pending follow-ups · Next at {1:0.##}"),
                runtime.DelayedRuns.Count, runtime.DelayedRuns.Min(run => run.Due)), false);
        var history = _selected.Runtime.History?.Where(entry => entry != null).ToArray();
        if (history?.Length is not > 0)
            Message(L("editor.history_empty", "No execution records yet."), false);
        else
            foreach (var entry in history.Reverse()) BuildRunRecord(entry);
        ButtonAt(_body, L("command.reset_runtime", "Reset runtime"), 126, () =>
            Confirm(L("editor.reset_question", "Clear history and reset cooldown and run-once state?"), () =>
            {
                _selected.Runtime = new ScriptRuntimeState();
                _expanded.RemoveWhere(item => item is ScriptExecutionRecord or ScriptStepExecution or ScriptTargetSample);
                Enter(Page.History);
            }, () => Enter(Page.History)), surface: Surface.Quiet);
    }

    private void BuildRunRecord(ScriptExecutionRecord entry)
    {
        TriggerDefinition trigger = WorldScriptRegistry.GetTrigger(entry.TriggerID);
        string source = entry.Manual ? L("editor.manual", "Manual") : DefinitionName("trigger", trigger?.Type.ToString(), entry.TriggerID);
        string result = RunOutcome(entry.Outcome);
        string title = entry.MatchCount.HasValue
            ? string.Format(L("history.run_line", "{0:0.##} · {1} · {2} · Matched {3} · Selected {4}"),
                entry.WorldTime, source, result, entry.MatchCount, entry.SelectedCount)
            : string.Format(L("editor.history_line", "{0:0.##} · {1} · {2} · Targets {3} · Actions {4} · Spawns {5}"),
                entry.WorldTime, source, result, entry.TargetCount, entry.ActionCount, entry.SpawnCount);
        if (!FlowFold(entry, title, "iconWorldLog")) return;
        if (entry.Cause != null)
            Message(string.Format(L("history.event_cause", "From script {0} · Step {1}"), entry.Cause.ScriptName, entry.Cause.Step), false);

        var totals = Row(_body, EditorWidth, 27);
        Readout(totals, L("editor.processing", "Processed"), entry.TargetCount, "iconArrowAttackTarget", 122);
        Readout(totals, L("history.actions", "Actions"), entry.ActionCount, "iconPlay", 122);
        Readout(totals, L("history.spawns", "Spawned"), entry.SpawnCount, "iconShovelPlus", 122);
        if (entry.DurationMilliseconds > 0d)
            Message(string.Format(L("history.duration", "Elapsed {0:0.##} ms"), entry.DurationMilliseconds), false);
        if (entry.Limits != null)
            foreach (var limit in entry.Limits.Where(limit => limit != null))
            {
                string position = limit.Step > 0 ? string.Format(L("history.limit_step", "Step {0}"), limit.Step)
                    : L("flow.main_selected", "Main query");
                Message($"{position} · {L("history.limit." + limit.Kind, limit.Kind)}", false);
            }

        if (!string.IsNullOrEmpty(entry.Message) && entry.Steps?.Any(step => step?.Groups == null && step?.Error == entry.Message) != true)
            Message(ErrorLabel(entry.Message), true);
        if (entry.Steps != null)
        {
            var steps = entry.Steps.Where(step => step != null).ToList();
            foreach (var step in steps.Where(step => step.Groups == null)) BuildStepRecord(step);
            var groups = steps.Where(step => step.Groups != null).SelectMany(step => step.Groups)
                .Where(group => group?.Parent != null).GroupBy(group => (group.Parent.Kind, group.Parent.MetaID)).ToList();
            if (groups.Count > 0)
                FlowHeading("relation.group_results", "Groups", "iconCityList");
            foreach (var group in groups)
            {
                var first = group.First();
                var villages = group.Where(step => step.Groups != null).SelectMany(step => step.Groups)
                    .Where(village => village?.Parent != null).GroupBy(village => village.Parent.MetaID).ToList();
                string amount = group.Any(step => step.Groups != null) ? VillageCount(villages.Count) : ProcessedCount(first.ProcessedCount);
                if (group.Any(step => step.OmittedGroups > 0)) amount += " + …";
                string groupTitle = SampleLabel(first.Parent) + " · " + amount;
                if (!FlowFold(first.Parent, groupTitle, GroupIcon(group.Key.Kind))) continue;
                foreach (var step in group.Where(step => step.Groups == null)) BuildStepRecord(step, grouped: true);
                foreach (var village in villages)
                {
                    var firstVillage = village.First();
                    string villageTitle = SampleLabel(firstVillage.Parent) + " · " + ProcessedCount(firstVillage.ProcessedCount);
                    if (!FlowFold(firstVillage.Parent, villageTitle, GroupIcon(ScriptTargetKind.City), VillageRowIndent)) continue;
                    foreach (var step in village) BuildStepRecord(step, grouped: true, indent: VillageDetailIndent);
                }
                foreach (var step in group.Where(step => step.OmittedGroups > 0))
                    BuildOmittedGroups(step, VillageRowIndent);
            }
            foreach (var step in steps.Where(step => step.Groups?.Count == 0))
                BuildStepRecord(step);
            foreach (var step in steps.Where(step => step.OmittedGroups > 0))
                BuildOmittedGroups(step);
        }
        if (entry.OmittedSteps > 0)
            Message(string.Format(L("history.omitted_steps", "{0} additional steps"), entry.OmittedSteps), false);
    }

    private void BuildOmittedGroups(ScriptStepExecution step, int indent = 0) =>
        Message($"{step.Step}. " + string.Format(L("history.omitted_groups", "{0} additional groups"), step.OmittedGroups),
            false, indent: indent);

    private static string ProcessedCount(int count) =>
        string.Format(L("relation.group_processed", "{0} processed"), count);

    private void BuildStepRecord(ScriptStepExecution step, bool grouped = false, int indent = 0)
    {
        string action = step.ThenCount.HasValue ? L("branch.title", "Conditional") : HistoryActionName(step.ActionID);
        string inputs = string.Format(L("flow.preview_inputs", "{0} inputs"), step.InputCount);
        string branches = step.ThenCount.HasValue
            ? " · " + string.Format(L("history.branch_counts", "Then {0} / Otherwise {1}"), step.ThenCount, step.OtherwiseCount) : "";
        string title = $"{step.Step}. {action} · {inputs}{branches} · {RunOutcome(step.Outcome)}";
        if (!string.IsNullOrWhiteSpace(step.Name)) title = $"{step.Step}. {step.Name} · {action} · {inputs}{branches} · {RunOutcome(step.Outcome)}";
        if (grouped) Message(title, false, indent: indent);
        else if (!FlowFold(step, title, "iconPlay")) return;
        if (!string.IsNullOrEmpty(step.Error))
        {
            Message(ErrorLabel(step.Error), true, indent: indent);
            if (Draft?.Flow?.Steps.Any(item => item.ID == step.StepID) == true)
                ButtonAt(_body, L("scenario.edit_step", "Edit this step"), 126, () => JumpToStep(step.StepID),
                    NativeIcon("iconEdit"), surface: Surface.Quiet);
        }
        if (step.ResultValue.HasValue)
            Message(string.Format(L("history.result_value", "Result count {0}"), step.ResultValue), false, indent: indent);
        if (step.Actions != null)
            foreach (var detail in step.Actions.Where(detail => detail != null))
            {
                string label = HistoryActionName(detail.ActionID);
                if (detail.Branch != null) label = L("branch." + detail.Branch, detail.Branch) + " · " + label;
                Message(label + " · " + string.Format(L("history.action_counts", "Applied to {0} · Actions {1} · Spawned {2}"),
                    detail.TargetCount, detail.ActionCount, detail.SpawnCount), false, indent: indent);
                if (detail.ChangeAmount.HasValue)
                    Message(string.Format(L("history.change_amount", "Actual change {0:+0;-0;0}"), detail.ChangeAmount),
                        false, indent: indent);
                if (detail.SkippedCount > 0)
                    Message(string.Format(L("history.skipped_count", "Skipped {0} · {1}"), detail.SkippedCount,
                        L("history.skip." + detail.SkippedReason, detail.SkippedReason)), false, indent: indent);
                if (detail.Writes?.Count > 0 && FlowFold(detail.Writes, L("history.write_records", "Changes by object"), "iconInspect", indent))
                    foreach (var change in detail.Writes)
                    {
                        string resource = change.Key == "kingdom" ? L("affiliation.kingdom", "Kingdom")
                            : ParameterDisplayName(detail.ActionID, "resource_id", change.Key);
                        string policy = L("history.write_policy." + change.Policy, change.Policy);
                        string reason = change.Reason == null ? "" : " · " + L("history.skip." + change.Reason, change.Reason);
                        Message($"{SampleLabel(change.Target)} · {resource} · {policy} · {change.Before} → {change.After}{reason}",
                            false, indent: indent + 12);
                    }
                if (!string.IsNullOrEmpty(detail.Message))
                    Message(detail.Message, false, richText: detail.ActionID == WorldScriptHistory.ActionID && !detail.MessageTruncated,
                        indent: indent);
            }

        var samples = step.Targets?.Where(target => target != null).Select(SampleLabel).ToArray();
        if (samples?.Length is > 0)
        {
            string targets = string.Join(" / ", samples);
            if (step.InputCount > samples.Length)
                targets += " · " + string.Format(L("editor.extra_targets", "+ {0} more targets"), step.InputCount - samples.Length);
            Message(targets, false, indent: indent);
        }
    }

    private static string RunOutcome(string outcome) => L("editor.outcome." + outcome, outcome);

    private static string HistoryActionName(string id) =>
        DefinitionName("action", id, WorldScriptRegistry.GetAction(id)?.Name ?? id ?? L("value.unset", "Unset"));

    private static string SampleLabel(ScriptTargetSample target)
    {
        string name = target.Kind switch
        {
            ScriptTargetKind.Actor => NamedSample(target.Name, target.ActorID),
            ScriptTargetKind.Tile => $"({target.X}, {target.Y})",
            ScriptTargetKind.City or ScriptTargetKind.Kingdom => NamedSample(target.Name, target.MetaID),
            _ => L("target.world", "Current world")
        };
        return target.Branch == null ? name : $"{name} · {L("branch." + target.Branch, target.Branch)}";
    }

    private static string NamedSample(string name, long id) => string.IsNullOrEmpty(name) ? $"#{id}" : $"{name} · #{id}";
}
