using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features.WorldScript;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private static readonly ScriptTargetKind[] FlowKinds =
        { ScriptTargetKind.Actor, ScriptTargetKind.City, ScriptTargetKind.Kingdom, ScriptTargetKind.Tile, ScriptTargetKind.World };

    private RectTransform FlowHeading(string key, string name, string icon)
    {
        var row = Row(_body, EditorWidth, 24);
        var image = row.gameObject.AddComponent<Image>();
        PaintSurface(image, Surface.List, true);
        image.raycastTarget = false;
        RuleLabel(row, L(key, name), icon, TitleColor);
        return row;
    }

    private bool FlowFold(object entry, string label, string icon, int indent = 0)
    {
        bool open = _expanded.Contains(entry);
        var row = Row(_body, EditorWidth, 21);
        if (indent > 0) row.GetComponent<HorizontalLayoutGroup>().padding.left = indent;
        UnfoldAt(row, open, () => ToggleExpanded(entry));
        ButtonAt(row, label, EditorWidth - 24 - indent, () => ToggleExpanded(entry), NativeIcon(icon), surface: Surface.List);
        return open;
    }

    private void BuildFlowRules(ScriptFlow flow)
    {
        if (FlowFold(flow.Gate, TreeTitle(L("flow.gate", "Run only if"), flow.Gate), "iconInspect"))
            BuildConditionTree(flow.Gate, ScriptTargetKind.World, root => flow.Gate = root);

        BuildFlowQuery(flow.Query, FlowHeading("flow.who", "Who to find", "iconArrowAttackTarget"));

        var commands = FlowHeading("flow.do", "What to do", "iconPlay");
        ButtonAt(commands, L("flow.add_step", "Add step"), 116, () =>
        {
            var step = new ScriptStep();
            flow.Steps.Add(step);
            _expanded.Add(step);
            Render();
            // The new row can be below the visible area in a long script.
            Canvas.ForceUpdateCanvases();
            _bodyScroll.verticalNormalizedPosition = 0f;
        }, NativeIcon("iconShovelPlus"), surface: Surface.Quiet);
        for (int i = 0; i < flow.Steps.Count; i++) BuildStep(flow, i);
        BuildAdvancedFold();
    }

    private void BuildFlowQuery(ScriptQuery query, RectTransform header = null)
    {
        ScriptTargetKind kind = WorldScriptRegistry.GetTarget(query.Scope.TargetID)?.Kind ?? ScriptTargetKind.None;
        BuildScope(query.Scope, scope =>
        {
            query.Scope = scope;
            ScriptTargetKind nextKind = WorldScriptRegistry.GetTarget(scope.TargetID).Kind;
            if (nextKind == ScriptTargetKind.Actor) return;
            query.Group = "none";
            if (nextKind == ScriptTargetKind.World || query.Order is "health_asc" or "power_desc") query.Order = "registry";
        }, header);
        if (FlowFold(query.Filter, TreeTitle(L("flow.filter", "Target filters"), query.Filter), "iconInspect"))
            BuildConditionTree(query.Filter, kind, root => query.Filter = root);
        string summary = $"{L("flow.selection", "Selection")} · {L("query.order." + query.Order, query.Order)}";
        if (query.Group != "none") summary += " · " + L("editor.group." + query.Group, query.Group);
        if (query.Count > 0)
            summary += " · " + string.Format(query.Group == "none" ? L("flow.take_summary", "Take {0}")
                : L("flow.group_summary", "Take {0} per group"), query.Count);
        if (header != null) summary += $" · {L("flow.cap", "Total limit")} {Draft.MaxTargets}";
        if (FlowFold(query, summary, "iconOptions")) BuildQuerySettings(query, kind, header != null);
    }

    private static string TreeTitle(string title, ConditionNode node)
    {
        if (node.Operator == "condition") return $"{title} · {ConditionSummary(node, WorldScriptRegistry.GetCondition(node.ConditionID))}";
        if (node.Children.Count == 0) return title;
        if (node.Operator != "all") title += " · " + GroupLabel(node.Operator);
        string rules = string.Join(" / ", node.Children.Take(2).Select(child => child.Operator == "condition"
            ? ConditionSummary(child, WorldScriptRegistry.GetCondition(child.ConditionID)) : GroupLabel(child.Operator)));
        return node.Children.Count > 2 ? $"{title} · {rules} +{node.Children.Count - 2}" : $"{title} · {rules}";
    }

    private void BuildScope(TargetSelector scope, Action<TargetSelector> set, RectTransform header)
    {
        TargetDefinition definition = WorldScriptRegistry.GetTarget(scope.TargetID);
        string triggerID = Draft.TriggerCondition.TriggerID;
        ScriptTargetKind kind = definition?.Kind ?? ScriptTargetKind.None;
        string kindTitle = L("flow.kind", "Object type");
        FieldAt(header ?? FieldRow(kindTitle), KindLabel(kind), header == null ? 206 : 70, () => OpenPicker(kindTitle, FlowKinds.Select(value =>
            new Option(KindLabel(value), () =>
            {
                if (kind != value)
                {
                    var next = NewScope(value switch
                    {
                        ScriptTargetKind.Tile => WorldScriptRegistry.TargetTilePositionRadius,
                        ScriptTargetKind.City => WorldScriptRegistry.TargetCityAll,
                        ScriptTargetKind.Kingdom => WorldScriptRegistry.TargetKingdomAll,
                        ScriptTargetKind.World => WorldScriptRegistry.TargetWorld,
                        _ => WorldScriptRegistry.TargetActorAll
                    });
                    set(next);
                    _expanded.Add(next);
                }
                Enter(Page.Rules);
            }))));

        var row = header ?? FieldRow(L("flow.scope", "Search scope"));
        FieldAt(row, ScopeLabel(scope), 206, () =>
            OpenPicker(L("flow.scope", "Search scope"), WorldScriptRegistry.Targets
                .Where(target => (kind == ScriptTargetKind.None || target.Kind == kind) &&
                    CanUseFlowScope(target, triggerID))
                .Select(target => new Option(DefinitionName("target", target.ID, target.Name), () =>
                {
                    if (target.ID != scope.TargetID)
                    {
                        var next = NewScope(target.ID);
                        set(next);
                        _expanded.Add(next);
                    }
                    Enter(Page.Rules);
                }))));
        if (definition?.Parameters.Length > 0)
        {
            UnfoldAt(row, _expanded.Contains(scope), () => ToggleExpanded(scope));
            if (_expanded.Contains(scope)) BuildParameters(definition.ID, scope.Parameters, definition.Parameters);
        }
        if (definition != null && !CanUseFlowScope(definition, triggerID))
            Message(L("flow.scope_event_error", "This scope requires a matching actor event."), true);
    }

    private static bool CanUseFlowScope(TargetDefinition target, string triggerID) =>
        WorldScriptEngine.CanUseScope(target, triggerID) &&
        (target.ID != WorldScriptRegistry.TargetActorEvent || triggerID != WorldScriptRegistry.TriggerActorDied);

    private void BuildQuerySettings(ScriptQuery query, ScriptTargetKind kind, bool showCap)
    {
        string[] orders = WorldScriptEngine.QueryOrders.Where(value =>
            (kind == ScriptTargetKind.Actor || value is not ("health_asc" or "power_desc")) &&
            (kind != ScriptTargetKind.World || value == "registry")).ToArray();
        Choice("field.order_by", "Order by", "query.order.", query.Order, value => query.Order = value, orders);
        if (query.Order == "distance_asc") BuildDistanceOrigin(query);
        if (kind == ScriptTargetKind.Actor)
            Choice("flow.group", "Group by", "editor.group.", query.Group, value => query.Group = value, WorldScriptEngine.QueryGroups);
        Number(query.Group == "none" ? "flow.count" : "flow.per_group",
            query.Group == "none" ? "Take (0 = all)" : "Per group (0 = all)", query.Count, value => query.Count = Math.Max(value, 0));
        if (showCap) Number("flow.cap", "Total limit", Draft.MaxTargets, value => Draft.MaxTargets = Math.Max(value, 0));
    }

    private void BuildDistanceOrigin(ScriptQuery query)
    {
        string title = L("flow.distance_origin", "Distance from");
        string scopeCenter = L("flow.scope_center", "Center of scope");
        var scope = WorldScriptRegistry.GetTarget(query.Scope.TargetID);
        var options = WorldScriptRegistry.Targets.Where(target => target.Kind == ScriptTargetKind.Tile &&
                target.GetOrigin != null && WorldScriptEngine.CanUseScope(target, Draft.TriggerCondition.TriggerID))
            .Select(target => new Option(DefinitionName("target", target.ID, target.Name), () =>
            {
                if (query.DistanceOrigin?.TargetID != target.ID) query.DistanceOrigin = NewScope(target.ID);
                Enter(Page.Rules);
            })).ToList();
        if (scope?.GetOrigin != null)
            options.Insert(0, new Option(scopeCenter, () => { query.DistanceOrigin = null; Enter(Page.Rules); }));
        FieldAt(FieldRow(title), query.DistanceOrigin != null ? ScopeLabel(query.DistanceOrigin, false)
            : scope?.GetOrigin != null ? scopeCenter : L("value.unset", "Unset"), 206, () => OpenPicker(title, options));
        if (query.DistanceOrigin?.TargetID == WorldScriptRegistry.TargetTilePositionRadius)
        {
            var definition = WorldScriptRegistry.GetTarget(query.DistanceOrigin.TargetID);
            BuildParameters(definition.ID, query.DistanceOrigin.Parameters,
                definition.Parameters.Where(parameter => parameter.Key is "x" or "y").ToArray());
        }
    }

    private void BuildStep(ScriptFlow flow, int index)
    {
        ScriptStep step = flow.Steps[index];
        string summary = $"{index + 1}. {NamedStepSummary(step)}";
        if (step.HasBranch) summary += $" · {L("branch.title", "Conditional")}";
        if (step.Source != "query") summary += $" · {StepSourceLabel(step)}";
        if (step.Relation != null) summary += $" · {RelationName(step.Relation.ID)}";
        if (step.Relation?.Next != null) summary += $" → {RelationName(step.Relation.Next.ID)}";
        if (step.Mode == "once" && step.Source != WorldScriptEngine.GroupResultSource)
            summary += $" · {L(step.Relation == null ? "flow.mode.once" : "relation.once", "Once per batch")}";
        int row = _body.childCount;
        bool open = FlowFold(step, summary, "iconPlay");
        if (_focusStepID == step.ID) _focusStepRow = (RectTransform)_body.GetChild(row);
        if (!open) return;

        ScriptTargetKind input = WorldScriptEngine.StepInputKind(flow, index);
        ScriptTargetKind actionKind = step.Mode == "once" ? ScriptTargetKind.World : input;
        bool showSettings = _expanded.Contains(step.Numbers);
        if (showSettings)
        {
            InputAt(FieldRow(L("flow.step_name", "Step name")), step.Name ?? "", 206,
                name => step.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
            BuildStepSource(flow, index, step);
            if (step.Source == "scope") BuildFlowQuery(step.Query);
            if (step.Source == WorldScriptEngine.GroupResultSource || WorldScriptEngine.ResultSources.Contains(step.Source))
                BuildResultCondition(step);
        }
        if (step.Source != WorldScriptEngine.GroupResultSource)
        {
            BuildRelation(flow, index, step);
            if (showSettings)
                Choice("flow.mode", "Execution", step.Relation == null ? "flow.mode." : "relation.",
                    step.Mode, mode => step.Mode = mode, "each", "once");
        }
        if (step.HasBranch)
        {
            ScriptBranch branch = step.Branch;
            if (FlowFold(branch.Condition, TreeTitle(L("branch.if", "If"), branch.Condition), "iconInspect"))
                BuildConditionTree(branch.Condition, input, root => branch.Condition = root);
            BuildStepAction(L("branch.then", "Then"), step.Action, action => step.Action = action, step.Numbers,
                actionKind, foldParameters: true);
            BuildStepAction(L("branch.otherwise", "Otherwise"), branch.Otherwise, action => branch.Otherwise = action,
                branch.OtherwiseNumbers, actionKind, allowSkip: true, foldParameters: true);
        }
        else BuildStepAction(L("flow.action", "Action"), step.Action, action => step.Action = action, step.Numbers, actionKind);

        var commands = Row(_body, EditorWidth, 23);
        SwitchAt(commands, step.Action.Enabled, L("flow.step_enabled", "Enable step"), () =>
        {
            step.Action.Enabled = !step.Action.Enabled;
            Render();
        });
        IconCommand(commands, L("command.move_up", "Move up"), "iconArrowUP", () => MoveNode(flow.Steps, step, -1));
        IconCommand(commands, L("command.move_down", "Move down"), "iconArrowDOWN", () => MoveNode(flow.Steps, step, 1));
        ButtonAt(commands, L("command.duplicate", "Duplicate"), 62, () =>
        {
            var copy = JObject.FromObject(step).ToObject<ScriptStep>();
            copy.ID = Guid.NewGuid().ToString("N");
            flow.Steps.Insert(index + 1, copy);
            _expanded.Add(copy);
            Render();
        }, surface: Surface.Quiet);
        ButtonAt(commands, L("flow.step_settings", "Target and execution"), 116, () => ToggleExpanded(step.Numbers),
            NativeIcon("iconOptions"), surface: Surface.Quiet);
        if (step.Source != WorldScriptEngine.GroupResultSource)
            ButtonAt(commands, step.HasBranch ? L("branch.disable", "Disable branch") : L("branch.enable", "Add branch"), 88, () =>
            {
                bool enable = !step.HasBranch;
                step.Branch ??= new ScriptBranch();
                step.Branch.Enabled = enable;
                if (enable) _expanded.Add(step.Branch.Condition);
                Render();
            }, surface: Surface.Quiet);
        IconCommand(commands, L("command.delete", "Delete"), "iconDemolish", () =>
        {
            flow.Steps.Remove(step);
            _expanded.Remove(step);
            _expanded.Remove(step.Action);
            _expanded.Remove(step.Numbers);
            Render();
        }, Surface.Destructive);
    }

    private void BuildStepAction(string title, ScriptAction action, Action<ScriptAction> set,
        Dictionary<string, ScriptNumberBinding> bindings, ScriptTargetKind kind, bool allowSkip = false,
        bool foldParameters = false)
    {
        var definition = allowSkip && action?.Enabled != true ? null : WorldScriptRegistry.GetAction(action?.ActionID);
        var row = FieldRow(title);
        string skipped = L("branch.skip", "Skip");
        FieldAt(row, DefinitionName("action", definition?.ID,
            definition?.Name ?? (allowSkip && action?.Enabled != true ? skipped : L("value.unset", "Unset"))), 206, () =>
        {
            var options = WorldScriptRegistry.Actions.Where(candidate => (candidate.AllowedTargets & kind) != 0)
                .Select(candidate => new Option(DefinitionName("action", candidate.ID, candidate.Name), () =>
                {
                    ScriptAction selected = action ?? new ScriptAction();
                    if (allowSkip) selected.Enabled = true;
                    if (selected.ActionID != candidate.ID)
                    {
                        selected.ActionID = candidate.ID;
                        selected.Parameters = Defaults(candidate.Parameters);
                        bindings.Clear();
                    }
                    set(selected);
                    if (foldParameters) _expanded.Add(selected);
                    Enter(Page.Rules);
                })).ToList();
            if (allowSkip)
                options.Insert(0, new Option(skipped, () => { set(null); bindings.Clear(); Enter(Page.Rules); }));
            OpenPicker(title, options);
        });
        if (definition == null) return;
        if ((definition.AllowedTargets & kind) == 0)
            Message(L("warning.incompatible_target", "Incompatible with current target"), true);
        if (definition.Dangerous && !Draft.AllowDangerousActions)
            Message(L("flow.danger_help", "Enable destructive actions in Advanced settings to run this step."), true);
        if (foldParameters && definition.Parameters.Length > 0)
            UnfoldAt(row, _expanded.Contains(action), () => ToggleExpanded(action));
        if (!foldParameters || _expanded.Contains(action))
            BuildParameters(definition.ID, action.Parameters, definition.Parameters, bindings, kind);
    }

    private void BuildStepSource(ScriptFlow flow, int index, ScriptStep step)
    {
        var choices = new List<string> { "query", "scope" };
        if (WorldScriptEngine.PreviousOutput(flow.Steps, index) != ScriptTargetKind.None || step.Source == "previous")
            choices.Add("previous");
        bool canReport = WorldScriptEngine.CanReportGroups(flow, index);
        var prior = Enumerable.Range(0, index).Where(i => flow.Steps[i].Action.Enabled).ToArray();
        if (prior.Any(i => WorldScriptEngine.HasGroups(flow.Steps[i])) || step.Source == WorldScriptEngine.GroupResultSource)
            choices.Add(WorldScriptEngine.GroupResultSource);
        foreach (string result in WorldScriptEngine.ResultSources)
            if (step.Source == result || prior.Any(i => WorldScriptEngine.ResultSourceKind(flow, i, result) != ScriptTargetKind.None))
                choices.Add(result);
        Choice("flow.source", "Apply to", "flow.source.", step.Source, source =>
        {
            if (step.Source != source) step.SourceStepID = null;
            step.Source = source;
            if (source == "scope") step.Query ??= new ScriptQuery();
            if (source == WorldScriptEngine.GroupResultSource)
            {
                step.Relation = null;
                step.Mode = "once";
                if (step.Branch != null) step.Branch.Enabled = false;
            }
            else if (!WorldScriptEngine.ResultSources.Contains(source)) step.ResultCondition = null;
        }, choices.ToArray());
        if (step.Source == "previous" && WorldScriptEngine.PreviousOutput(flow.Steps, index) == ScriptTargetKind.None)
            Message(L("flow.no_output", "The preceding enabled step has no output."), true);
        if (step.Source == WorldScriptEngine.GroupResultSource && !canReport)
            Message(L("relation.report_requires_groups", "Choose a grouped step before reporting its results."), true);
        bool report = step.Source == WorldScriptEngine.GroupResultSource;
        if (!report && !WorldScriptEngine.ResultSources.Contains(step.Source)) return;

        string title = L("flow.reference_step", "Source step");
        var options = prior.Where(i => report ? WorldScriptEngine.HasGroups(flow.Steps[i])
                : WorldScriptEngine.ResultSourceKind(flow, i, step.Source) != ScriptTargetKind.None)
            .Select(i => new Option(ReferenceStepLabel(flow, i, step.Source), () =>
            {
                step.SourceStepID = flow.Steps[i].ID;
                Enter(Page.Rules);
            })).ToList();
        string previous = L("flow.immediately_previous", "Previous enabled step");
        if (report)
            options.Insert(0, new Option(previous, () => { step.SourceStepID = null; Enter(Page.Rules); }));
        string label = WorldScriptEngine.FindSourceStep(flow, index, implicitPrevious: report) is int sourceIndex
            ? ReferenceStepLabel(flow, sourceIndex, step.Source)
            : L("value.unset", "Unset");
        FieldAt(FieldRow(title), label, 206, () => OpenPicker(title, options));
    }

    private void BuildResultCondition(ScriptStep step)
    {
        string title = L("flow.result_condition", "Result count condition");
        var rule = step.ResultCondition;
        var row = FieldRow(title);
        SwitchAt(row, rule != null, title, () =>
        {
            step.ResultCondition = rule == null ? new ScriptCountRule() : null;
            Render();
        });
        if (rule == null) return;
        Choice("flow.result_measure", "Count", "flow.result.", rule.Measure,
            measure => rule.Measure = measure, WorldScriptEngine.ResultMeasures);
        Choice("parameter.operator", "Operator", "comparison.", rule.Operator,
            op => rule.Operator = op, WorldScriptEngine.Comparisons);
        Number("flow.result_threshold", "Threshold", rule.Value, value => rule.Value = Math.Max(value, 0));
    }

    private static string ReferenceStepLabel(ScriptFlow flow, int index, string source)
    {
        var step = flow.Steps[index];
        string kind = source == WorldScriptEngine.GroupResultSource ? L("relation.group_results", "Groups")
            : KindLabel(WorldScriptEngine.ResultSourceKind(flow, index, source));
        return $"{index + 1}. {NamedStepSummary(step)} · {kind}";
    }

    private static string NamedStepSummary(ScriptStep step) =>
        string.IsNullOrWhiteSpace(step.Name) ? StepSummary(step) : $"{step.Name} · {StepSummary(step)}";

    private void BuildRelation(ScriptFlow flow, int index, ScriptStep step)
    {
        ScriptTargetKind source = WorldScriptEngine.StepSourceKind(flow, index);
        var relations = WorldScriptEngine.Relations.Where(item => item.Parent == source).ToArray();
        if (relations.Length == 0 && step.Relation == null) return;
        string title = L("relation.field", "Find related objects");
        var options = relations.Select(item => new Option(RelationName(item.ID), () =>
        {
            if (step.Relation?.ID != item.ID)
            {
                step.Relation = new ScriptRelation { ID = item.ID };
                SetRelationMode(step, item.Target);
            }
            Enter(Page.Rules);
        })).ToList();
        options.Insert(0, new Option(RelationName(null), () =>
        {
            step.Relation = null;
            SetRelationMode(step, source);
            Enter(Page.Rules);
        }));
        FieldAt(FieldRow(title), RelationName(step.Relation?.ID), 206, () => OpenPicker(title, options));
        if (step.Relation is not ScriptRelation relation) return;
        ScriptTargetKind related = WorldScriptEngine.RelationTargetKind(source, relation);
        if (related == ScriptTargetKind.None)
        {
            Message(L("relation.invalid", "Choose a relation for the source object."), true);
            return;
        }
        BuildRelationFilter(relation, related, named: related == ScriptTargetKind.City);
        if (related == ScriptTargetKind.City) BuildNextRelation(step, relation);
    }

    private void BuildNextRelation(ScriptStep step, ScriptRelation relation)
    {
        string title = L("relation.continue", "Continue from each village");
        string stop = L("relation.stop", "Use the villages");
        var options = WorldScriptEngine.Relations.Where(item => item.Parent == ScriptTargetKind.City)
            .Select(item => new Option(RelationName(item.ID), () =>
            {
                if (relation.Next?.ID != item.ID)
                {
                    relation.Next = new ScriptRelation { ID = item.ID };
                    SetRelationMode(step, item.Target);
                }
                Enter(Page.Rules);
            })).ToList();
        options.Insert(0, new Option(stop, () =>
        {
            relation.Next = null;
            SetRelationMode(step, ScriptTargetKind.City);
            Enter(Page.Rules);
        }));
        FieldAt(FieldRow(title), relation.Next == null ? stop : RelationName(relation.Next.ID), 206,
            () => OpenPicker(title, options));
        if (relation.Next == null) return;
        ScriptTargetKind kind = WorldScriptEngine.RelationTargetKind(ScriptTargetKind.City, relation.Next);
        if (kind == ScriptTargetKind.None || relation.Next.Next != null)
            Message(L("relation.invalid_chain", "Continue from a village with one more relation."), true);
        else
            BuildRelationFilter(relation.Next, kind, named: true);
    }

    private void BuildRelationFilter(ScriptRelation relation, ScriptTargetKind kind, bool named)
    {
        string title = named ? L("relation.filter." + kind, KindLabel(kind)) : L("relation.filter", "Related object filters");
        if (!FlowFold(relation, TreeTitle(title, relation.Filter), "iconInspect")) return;
        BuildConditionTree(relation.Filter, kind, root => relation.Filter = root);
        Number("relation.limit", "Per group (0 = all)", relation.Limit, value => relation.Limit = Math.Max(value, 0));
    }

    private static void SetRelationMode(ScriptStep step, ScriptTargetKind kind)
    {
        if (!WorldScriptRegistry.Actions.Any(action => (action.AllowedTargets & kind) != 0))
            step.Mode = "once";
        else if (WorldScriptRegistry.GetAction(step.Action.ActionID) == null)
            step.Mode = "each";
    }

    private static string RelationName(string id) => id == null
        ? L("relation.direct", "Use selected objects") : L("relation." + id, id);

    private static TargetSelector NewScope(string id)
    {
        var scope = new TargetSelector { TargetID = id, Parameters = Defaults(WorldScriptRegistry.GetTarget(id).Parameters) };
        if (id == WorldScriptRegistry.TargetTilePositionRadius) scope.Parameters["radius"] = 0;
        return scope;
    }

    private static string ScopeLabel(TargetSelector scope, bool includeRadius = true)
    {
        var definition = WorldScriptRegistry.GetTarget(scope.TargetID);
        string label = DefinitionName("target", definition?.ID, definition?.Name ?? scope.TargetID ?? "?");
        var values = scope.Parameters;
        if (values["x"] != null && values["y"] != null) label += $" · ({values["x"]}, {values["y"]})";
        if (includeRadius && values["radius"] != null) label += $" · {L("parameter.radius", "Radius")} {values["radius"]}";
        if (values["asset_id"] != null) label += " · " + ActorAssetName(values["asset_id"].ToString());
        if (values["id"] != null && WorldScriptAffiliations.Kind(scope.TargetID) != null)
            label += " · " + ParameterDisplayName(scope.TargetID, "id", values["id"].ToString());
        return label;
    }

    private static string KindLabel(ScriptTargetKind kind) => L("flow.kind." + kind, kind.ToString());

    private static string StepSourceLabel(ScriptStep step) => step.Source == "scope"
        ? ScopeLabel(step.Query.Scope) : L("flow.source." + step.Source, step.Source);

    private static string StepSummary(ScriptStep step)
    {
        if (step.Numbers.Count == 0) return ActionSummary(step.Action);
        var definition = WorldScriptRegistry.GetAction(step.Action.ActionID);
        string summary = DefinitionName("action", definition?.ID, definition?.Name ?? step.Action.ActionID ?? "?");
        foreach (var binding in step.Numbers.Where(binding => binding.Value != null))
            summary += $" · {L("flow.number." + binding.Value.Source, binding.Value.Source)} × {binding.Value.Percent:0.##}%";
        return step.Action.Enabled ? summary : $"[{L("editor.paused", "Disabled")}] {summary}";
    }
}
