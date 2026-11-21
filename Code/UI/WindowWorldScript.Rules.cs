using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features.WorldScript;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private sealed class ConditionEditor
    {
        public ConditionNode Root;
        public ScriptTargetKind Kind;
        public Action<ConditionNode> ReplaceRoot;
    }

    private static readonly string[] ConditionGroups = { "all", "any", "none", "not" };
    private ScriptTargetKind TargetKind => WorldScriptRegistry.GetTarget(Draft.Target.TargetID)?.Kind ?? ScriptTargetKind.None;

    private void BuildRules()
    {
        ScriptInstance draft = Draft;
        if (_notice != null) Message(_notice, _noticeIsError);
        if (_showValidation) BuildValidationErrors(WorldScriptEngine.Validate(draft).Errors);
        BuildPendingBindings(draft);
        var nameRow = Row(_body, EditorWidth, 22);
        RuleLabel(nameRow, L("field.name", "Name"), "iconWorldLaws");
        InputAt(nameRow, draft.Name, 245, value => draft.Name = value);

        BuildTriggerRow(draft, draft.Flow == null ? null : FlowHeading("editor.when", "When", "iconClock"));
        if (draft.Flow != null)
        {
            BuildFlowRules(draft.Flow);
            return;
        }

        TargetDefinition target = WorldScriptRegistry.GetTarget(draft.Target.TargetID);
        var targetRow = Row(_body, EditorWidth, 22);
        RuleLabel(targetRow, L("editor.who", "Who"), "iconArrowAttackTarget");
        bool hasTargetFields = target?.Parameters.Length > 0;
        FieldAt(targetRow, DefinitionName("target", target?.ID, target?.Name ?? draft.Target.TargetID), 245,
            hasTargetFields ? () => ToggleExpanded(draft.Target) : ChooseTarget, _expanded.Contains(draft.Target));
        IconCommand(targetRow, L("editor.change", "Change"), "iconEdit", ChooseTarget);
        if (_expanded.Contains(draft.Target) && target != null)
            BuildParameters(target.ID, draft.Target.Parameters, target.Parameters);
        BuildConditionTree(draft.Conditions, TargetKind, root => draft.Conditions = root);

        FlowHeading("flow.do", "What to do", "iconPlay");
        var actions = Row(_body, EditorWidth, 21);
        ButtonAt(actions, L("command.add_action", "Add action"), 108, () => ChooseAction(draft.Actions),
            NativeIcon("iconShovelPlus"), surface: Surface.Quiet);
        foreach (ScriptAction action in draft.Actions)
            BuildAction(action, draft.Actions, 0);
        if (draft.Actions.Count == 0)
            Label(_body, L("editor.no_actions", "Add an action to complete the rule."), EditorWidth);
        BuildAdvancedFold();
    }

    private void BuildTriggerRow(ScriptInstance draft, RectTransform header = null)
    {
        TriggerDefinition trigger = WorldScriptRegistry.GetTrigger(draft.TriggerCondition.TriggerID);
        var triggerRow = header ?? Row(_body, EditorWidth, 22);
        if (header == null) RuleLabel(triggerRow, L("field.trigger", "Trigger"), "iconClock");
        bool hasTriggerFields = trigger?.Parameters.Length > 0;
        FieldAt(triggerRow, TriggerSummary(trigger, draft.TriggerCondition.Parameters), 245,
            hasTriggerFields ? () => ToggleExpanded(draft.TriggerCondition) : ChooseTrigger,
            _expanded.Contains(draft.TriggerCondition));
        if (hasTriggerFields) IconCommand(triggerRow, L("editor.change", "Change"), "iconEdit", ChooseTrigger);
        if (_expanded.Contains(draft.TriggerCondition) && trigger != null)
            BuildParameters(trigger.ID, draft.TriggerCondition.Parameters, trigger.Parameters);
    }

    private void BuildAdvancedFold()
    {
        var advanced = Row(_body, EditorWidth, 22);
        UnfoldAt(advanced, _advanced, () => { _advanced = !_advanced; Render(); });
        ButtonAt(advanced, L("editor.advanced", "Advanced settings") + AdvancedSummary(), EditorWidth - 24,
            () => { _advanced = !_advanced; Render(); }, NativeIcon("iconOptions"), surface: Surface.List);
        if (_advanced) BuildAdvanced();
    }

    private string AdvancedSummary()
    {
        ScriptInstance draft = Draft;
        var values = new List<string>();
        if (draft.Priority != 0) values.Add($"{L("field.priority", "Priority")} {draft.Priority}");
        if (draft.Cooldown > 0d) values.Add($"{L("field.cooldown", "Cooldown")} {draft.Cooldown:0.##}");
        if (draft.Flow == null)
        {
            if (draft.SortMode != "registry") values.Add(L($"query.order.{draft.SortMode}", draft.SortMode));
            if (draft.SampleMode != "all") values.Add($"{L($"query.sampling.{draft.SampleMode}", draft.SampleMode)} {draft.SampleCount}");
            if (draft.MaxTargets != 20) values.Add($"{L("field.max_targets", "Max targets")} {draft.MaxTargets}");
            if (draft.GroupMode != "none") values.Add(L("editor.group." + draft.GroupMode, draft.GroupMode));
        }
        if (draft.MaxSpawns != 20) values.Add($"{L("field.max_spawns", "Max spawns")} {draft.MaxSpawns}");
        if (draft.EmptyTargetPolicy != "skip") values.Add(L("query.empty." + draft.EmptyTargetPolicy, draft.EmptyTargetPolicy));
        if (draft.RunOnce) values.Add(L("field.run_once", "Run once"));
        if (draft.AllowDangerousActions) values.Add(L("field.dangerous", "Allow destructive actions"));
        return values.Count == 0 ? "" : " · " + string.Join(" / ", values);
    }

    private static string TriggerSummary(TriggerDefinition trigger, JObject values)
    {
        if (trigger == null) return L("value.unset", "Unknown trigger");
        return trigger.ID switch
        {
            WorldScriptRegistry.TriggerInterval => string.Format(L("editor.interval_text", "Every {0} world seconds"), values["interval"]),
            WorldScriptRegistry.TriggerWorldTimeReached => string.Format(L("editor.time_text", "At world time {0}"), values["time"]),
            _ => DefinitionName("trigger", trigger.Type.ToString(), trigger.Name)
        };
    }

    private void ChooseTrigger()
    {
        var script = Draft;
        OpenPicker(L("field.trigger", "Trigger"), WorldScriptRegistry.Triggers.Select(trigger =>
            new Option(DefinitionName("trigger", trigger.Type.ToString(), trigger.Name), () =>
            {
                if (script.TriggerCondition.TriggerID != trigger.ID)
                {
                    _expanded.Remove(script.TriggerCondition);
                    bool oldActorEvent = WorldScriptEngine.UsesEventActor(script.TriggerCondition.TriggerID);
                    bool newActorEvent = WorldScriptEngine.UsesEventActor(trigger.ID);
                    script.TriggerCondition = new TriggerCondition { TriggerID = trigger.ID, Parameters = Defaults(trigger.Parameters) };
                    if (script.Flow == null && !oldActorEvent && newActorEvent && script.Target.TargetID == WorldScriptRegistry.TargetActorAll)
                        script.Target = new TargetSelector { TargetID = WorldScriptRegistry.TargetActorEvent };
                    else if (script.Flow == null && oldActorEvent && !newActorEvent && script.Target.TargetID == WorldScriptRegistry.TargetActorEvent)
                        script.Target = new TargetSelector();
                }
                _sampleActor = _sampleKiller = null;
                _sampleSettlementID = _sampleOldKingdomID = null;
                _expanded.Add(script.TriggerCondition);
                Enter(Page.Rules);
            })));
    }

    private void ChooseTarget()
    {
        var script = Draft;
        OpenPicker(L("field.target", "Target"), WorldScriptRegistry.Targets
            .Where(target => WorldScriptEngine.CanUseScope(target, script.TriggerCondition.TriggerID))
            .Select(target => new Option(DefinitionName("target", target.ID, target.Name), () =>
            {
                if (script.Target.TargetID != target.ID)
                {
                    _expanded.Remove(script.Target);
                    script.Target = new TargetSelector { TargetID = target.ID, Parameters = Defaults(target.Parameters) };
                }
                _expanded.Add(script.Target);
                Enter(Page.Rules);
            })));
    }

    private void BuildConditionTree(ConditionNode root, ScriptTargetKind kind, Action<ConditionNode> replaceRoot) =>
        BuildCondition(new ConditionEditor { Root = root, Kind = kind, ReplaceRoot = replaceRoot }, root, null, 0);

    private void BuildCondition(ConditionEditor editor, ConditionNode node, List<ConditionNode> siblings, int depth)
    {
        bool leaf = node.Operator == "condition";
        ConditionDefinition definition = leaf ? WorldScriptRegistry.GetCondition(node.ConditionID) : null;
        bool invalid = leaf && !CanUseCondition(definition, editor.Kind);
        bool open = _expanded.Contains(node) || siblings == null && !leaf;
        string summary = leaf ? ConditionSummary(node, definition) :
            $"{GroupLabel(node.Operator)} · {node.Children.Count}";
        var row = TreeRow(depth);
        if (siblings == null && !leaf)
        {
            FieldAt(row, summary, 138, () => ChooseGroup(node));
            if (node.Operator != "not" || node.Children.Count == 0)
                ButtonAt(row, L("command.add_condition", "Add condition"), 108, () => ChooseCondition(editor, node.Children),
                    NativeIcon("iconShovelPlus"), surface: Surface.Quiet);
            foreach (ConditionNode child in node.Children)
                BuildCondition(editor, child, node.Children, depth + 1);
            return;
        }
        else
            ButtonAt(row, summary, TreeWidth(depth), () => ToggleExpanded(node), NativeIcon("iconInspect"),
                selected: open, surface: Surface.List, arrow: NativeIcon(open ? "iconArrowUP" : "iconArrowDOWN"));
        if (invalid) Message(L("warning.incompatible_target", "Incompatible with current target"), true);
        if (!open) return;

        var commands = TreeRow(depth);
        if (leaf)
        {
            if (definition != null) BuildParameters(definition.ID, node.Parameters, definition.Parameters);
            IconCommand(commands, L("editor.change", "Change"), "iconEdit", () => ChooseCondition(editor, siblings, node));
        }
        else
        {
            ButtonAt(commands, L("field.group", "Condition group"), 84, () => ChooseGroup(node), surface: Surface.Quiet);
            bool canAdd = node.Operator != "not" || node.Children.Count == 0;
            if (canAdd)
                ButtonAt(commands, L("command.add_condition", "Add condition"), 108, () => ChooseCondition(editor, node.Children),
                    NativeIcon("iconShovelPlus"), surface: Surface.Quiet);
        }
        if (siblings != null)
        {
            ButtonAt(commands, L("command.duplicate", "Duplicate"), 58, () =>
            {
                var copy = JObject.FromObject(node).ToObject<ConditionNode>();
                siblings.Insert(siblings.IndexOf(node) + 1, copy);
                _expanded.Add(copy);
                Render();
            }).interactable = !FindSingleChildGroup(editor.Root, siblings);
            ButtonAt(commands, L("command.move_up", "Move up"), 50, () => MoveNode(siblings, node, -1))
                .interactable = siblings.IndexOf(node) > 0;
            ButtonAt(commands, L("command.move_down", "Move down"), 50, () => MoveNode(siblings, node, 1))
                .interactable = siblings.IndexOf(node) < siblings.Count - 1;
            IconCommand(commands, L("command.delete", "Delete"), "iconDemolish",
                () => { siblings.Remove(node); _expanded.Remove(node); Render(); }, Surface.Destructive);
        }
        if (!leaf)
            foreach (ConditionNode child in node.Children)
                BuildCondition(editor, child, node.Children, depth + 1);
    }

    private void ChooseGroup(ConditionNode node) =>
        OpenPicker(L("field.group", "Condition group"), ConditionGroups.Select(op =>
            new Option(GroupLabel(op), () =>
            {
                if (op == "not" && node.Children.Count > 1)
                    node.Children = new List<ConditionNode> { new() { Operator = "all", Children = node.Children } };
                node.Operator = op;
                Enter(Page.Rules);
            })));

    private static bool FindSingleChildGroup(ConditionNode node, List<ConditionNode> siblings) =>
        ReferenceEquals(node.Children, siblings) ? node.Operator == "not" :
            node.Children.Any(child => FindSingleChildGroup(child, siblings));

    private bool CanUseCondition(ConditionDefinition definition, ScriptTargetKind kind) =>
        definition != null && (definition.AllowedTargets & kind) != 0 &&
        (!definition.RequiresActorEvent || WorldScriptEngine.UsesEventActor(Draft.TriggerCondition.TriggerID));

    private void ChooseCondition(ConditionEditor editor, List<ConditionNode> siblings, ConditionNode replace = null)
    {
        Action<ConditionNode> accept = node =>
        {
            if (replace == null) siblings.Add(node);
            else
            {
                if (siblings == null) editor.ReplaceRoot(node);
                else siblings[siblings.IndexOf(replace)] = node;
                _expanded.Remove(replace);
            }
            _expanded.Add(node);
            Enter(Page.Rules);
        };
        var groups = replace == null ? ConditionGroups.Select(op =>
            new Option(GroupLabel(op), () => accept(new ConditionNode { Operator = op }))) : Enumerable.Empty<Option>();
        OpenPicker(L("command.add_condition", "Add condition"), groups.Concat(WorldScriptRegistry.Conditions
            .Where(definition => CanUseCondition(definition, editor.Kind))
            .Select(definition => new Option(DefinitionName("condition", definition.ID, definition.Name), () =>
                accept(new ConditionNode { Operator = "condition", ConditionID = definition.ID,
                    Parameters = Defaults(definition.Parameters) })))));
    }

    private void BuildAction(ScriptAction action, List<ScriptAction> siblings, int depth)
    {
        bool open = _expanded.Contains(action);
        var row = TreeRow(depth);
        ButtonAt(row, ActionSummary(action), TreeWidth(depth), () => ToggleExpanded(action),
            NativeIcon(action.ActionID == "actor.health.change" ? "iconHealth" : "iconPlay"), selected: open,
            surface: Surface.List, arrow: NativeIcon(open ? "iconArrowUP" : "iconArrowDOWN"));
        if (!open) return;

        bool sequence = action.Operator == "sequence";
        ActionDefinition definition = sequence ? null : WorldScriptRegistry.GetAction(action.ActionID);
        if (!sequence && (definition == null || (definition.AllowedTargets & TargetKind) == 0))
            Message(L("warning.incompatible_target", "Incompatible with current target"), true);
        if (definition?.Dangerous == true)
            Message(L("warning.dangerous_action", "Destructive action"), true);
        if (definition != null) BuildParameters(definition.ID, action.Parameters, definition.Parameters);
        var commands = TreeRow(depth);
        SwitchAt(commands, action.Enabled, L("section.actions", "Actions"), () => { action.Enabled = !action.Enabled; Render(); });
        if (!sequence)
            IconCommand(commands, L("editor.change", "Change"), "iconEdit", () => ChooseAction(siblings, action));
        ButtonAt(commands, L("command.move_up", "Move up"), 54, () => MoveNode(siblings, action, -1))
            .interactable = siblings.IndexOf(action) > 0;
        ButtonAt(commands, L("command.move_down", "Move down"), 54, () => MoveNode(siblings, action, 1))
            .interactable = siblings.IndexOf(action) < siblings.Count - 1;
        IconCommand(commands, L("command.delete", "Delete"), "iconDemolish",
            () => { siblings.Remove(action); _expanded.Remove(action); Render(); }, Surface.Destructive);
        ButtonAt(commands, L("command.duplicate", "Duplicate"), 58, () =>
        {
            var copy = JObject.FromObject(action).ToObject<ScriptAction>();
            siblings.Insert(siblings.IndexOf(action) + 1, copy);
            _expanded.Add(copy);
            Render();
        });
        if (sequence)
        {
            foreach (ScriptAction child in action.Children)
                BuildAction(child, action.Children, depth + 1);
            ButtonAt(_body, L("command.add_child_action", "Add child action"), 130, () => ChooseAction(action.Children));
        }
    }

    private void ChooseAction(List<ScriptAction> siblings, ScriptAction replace = null)
    {
        ScriptTargetKind kind = TargetKind;
        Action<ScriptAction> accept = node =>
        {
            if (replace == null) siblings.Add(node);
            else
            {
                node.Enabled = replace.Enabled;
                siblings[siblings.IndexOf(replace)] = node;
                _expanded.Remove(replace);
            }
            _expanded.Add(node);
            Enter(Page.Rules);
        };
        var options = WorldScriptRegistry.Actions.Where(action => (action.AllowedTargets & kind) != 0)
            .Select(action => new Option(DefinitionName("action", action.ID, action.Name) +
                (action.Dangerous ? $" · {L("warning.dangerous_action", "Destructive")}" : ""), () =>
                accept(new ScriptAction { ActionID = action.ID, Parameters = Defaults(action.Parameters) }))).ToList();
        if (replace == null)
            options.Add(new Option(L("action_kind.sequence", "Action sequence"), () =>
                accept(new ScriptAction { Operator = "sequence" })));
        OpenPicker(L("command.add_action", "Add action"), options);
    }

    private static JObject Defaults(ScriptParameterDefinition[] parameters)
    {
        var values = new JObject();
        WorldScriptRegistry.ApplyDefaults(values, parameters);
        return values;
    }

    private void MoveNode<T>(List<T> siblings, T node, int delta)
    {
        int index = siblings.IndexOf(node);
        int next = index + delta;
        if (next < 0 || next >= siblings.Count) return;
        (siblings[index], siblings[next]) = (siblings[next], siblings[index]);
        Render();
    }

    private RectTransform TreeRow(int depth)
    {
        var row = Row(_body, EditorWidth, 21);
        row.GetComponent<HorizontalLayoutGroup>().padding.left = Math.Min(depth, 6) * 9;
        return row;
    }

    private static float TreeWidth(int depth) => EditorWidth - Math.Min(depth, 6) * 9;
    private void ToggleExpanded(object node)
    {
        if (!_expanded.Add(node)) _expanded.Remove(node);
        Render();
    }

    private static string GroupLabel(string kind) => L("condition_kind." + kind, kind);

    private static string ConditionSummary(ConditionNode node, ConditionDefinition definition)
    {
        string name = DefinitionName("condition", node.ConditionID, definition?.Name ?? node.ConditionID ?? "?");
        return node.ConditionID switch
        {
            "actor.health_ratio" when double.TryParse(node.Parameters["value"]?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ratio) => $"{name} {node.Parameters["operator"]} {ratio:P0}",
            "actor.asset" => $"{name} · {ActorAssetName(node.Parameters["asset_id"]?.ToString())}",
            "actor.trait" => $"{name} · {TraitName(node.Parameters["trait_id"]?.ToString())}",
            _ when WorldScriptAffiliations.Kind(node.ConditionID) != null =>
                $"{name} · {ParameterDisplayName(node.ConditionID, "id", node.Parameters["id"]?.ToString())}",
            _ => name + ParameterSummary(node.Parameters, definition?.Parameters)
        };
    }

    private static string ActionSummary(ScriptAction action)
    {
        string id = action.ActionID;
        ActionDefinition definition = WorldScriptRegistry.GetAction(id);
        string name = action.Operator == "sequence"
            ? $"{L("action_kind.sequence", "Action sequence")} · {action.Children.Count}"
            : DefinitionName("action", id, definition?.Name ?? id ?? "?");
        string summary = id switch
        {
            "actor.health.change" when int.TryParse(action.Parameters["value"]?.ToString(), out int amount) && amount > 0 =>
                string.Format(L("editor.heal_value", "Restore {0} health"), amount),
            "actor.trait.add" or "actor.trait.remove" => $"{name} · {TraitName(action.Parameters["trait_id"]?.ToString())}",
            _ when WorldScriptAffiliations.Kind(id) != null =>
                $"{name} · {ParameterDisplayName(id, "id", action.Parameters["id"]?.ToString())}",
            WorldScriptHistory.ActionID => $"{name} · {HistoryIconName(WorldScriptRegistry.GetString(action.Parameters,
                "log_icon", WorldScriptHistory.DefaultIcon))}",
            WorldScriptMessages.TipAction => WorldScriptMessages.UsesVariables(action.Parameters) ? name
                : name + ParameterSummary(action.Parameters, definition?.Parameters.Where(parameter => parameter.Key == "message").ToArray()),
            _ => name + ParameterSummary(action.Parameters, definition?.Parameters)
        };
        return action.Enabled ? summary : $"[{L("editor.paused", "Disabled")}] {summary}";
    }

    private static string ParameterSummary(JObject values, ScriptParameterDefinition[] parameters)
    {
        if (parameters == null || parameters.Length == 0) return "";
        string text = string.Join(" · ", parameters.Select(parameter => values[parameter.Key]?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return text.Length == 0 ? "" : $" · {text}";
    }

    private void BuildAdvanced()
    {
        ScriptInstance script = Draft;
        Number("field.priority", "Priority", script.Priority, value => script.Priority = value);
        if (script.Flow != null)
            Number("field.targets_per_batch", "Targets per batch (0 = off)", script.TargetsPerBatch,
                value => script.TargetsPerBatch = Math.Max(0, value));
        if (script.Flow == null)
            Number("field.max_targets", "Max targets", script.MaxTargets, value => script.MaxTargets = Math.Max(value, 0));
        var actions = script.Flow == null ? WorldScriptEngine.EnabledActions(script.Actions)
            : script.Flow.Steps.Where(step => step.Action.Enabled).SelectMany(step =>
                step.HasBranch && step.Branch.HasOtherwise ? new[] { step.Action, step.Branch.Otherwise } : new[] { step.Action });
        if (actions.Any(action => WorldScriptRegistry.GetAction(action.ActionID)?.OutputKind == ScriptTargetKind.Actor)
            || script.MaxSpawns != 20)
            Number("field.max_spawns", "Max spawns", script.MaxSpawns, value => script.MaxSpawns = Math.Max(value, 0));
        var row = FieldRow(L("field.cooldown", "Cooldown") + " (" + L("editor.world_seconds", "world seconds") + ")");
        InputAt(row, script.Cooldown.ToString(CultureInfo.InvariantCulture), 140, value =>
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number) &&
                !double.IsInfinity(number)) script.Cooldown = Math.Max(number, 0d);
        }, InputField.CharacterValidation.Decimal);
        if (script.Flow == null)
        {
            Choice("field.order_by", "Order by", "query.order.", script.SortMode, value => script.SortMode = value, "registry", "random", "actor_id");
            Choice("field.sampling", "Sampling", "query.sampling.", script.SampleMode, value => script.SampleMode = value, "all", "random", "first");
            if (script.SampleMode != "all")
                Number("field.sample_count", "Sample count", script.SampleCount, value => script.SampleCount = Math.Max(value, 0));
            Choice("field.group", "Group", "editor.group.", script.GroupMode, value => script.GroupMode = value, "none", "kind", "actor_asset");
        }
        Choice("field.empty_policy", "When empty", "query.empty.", script.EmptyTargetPolicy, value => script.EmptyTargetPolicy = value,
            script.Flow == null && TargetKind == ScriptTargetKind.World ? new[] { "skip", "error", "run_once" } : new[] { "skip", "error" });
        Toggle("field.run_once", "Run once", script.RunOnce, value => script.RunOnce = value);
        Toggle("field.dangerous", "Allow destructive actions", script.AllowDangerousActions, value => script.AllowDangerousActions = value);
    }

    private RectTransform FieldRow(string name, float height = 22f)
    {
        var row = Row(_body, EditorWidth, height);
        row.GetComponent<HorizontalLayoutGroup>().padding.left = 12;
        Label(row, name, 140);
        return row;
    }

    private void Number(string key, string fallback, int value, Action<int> set)
    {
        InputAt(FieldRow(L(key, fallback)), value.ToString(CultureInfo.InvariantCulture), 140, text =>
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) set(result);
        }, InputField.CharacterValidation.Integer);
    }

    private void Choice(string key, string fallback, string prefix, string selection, Action<string> set, params string[] choices)
    {
        string name = L(key, fallback);
        FieldAt(FieldRow(name), L(prefix + selection, selection), 206, () =>
            OpenPicker(name, choices.Select(value => new Option(L(prefix + value, value), () => { set(value); Enter(Page.Rules); }))));
    }

    private void Toggle(string key, string fallback, bool value, Action<bool> set)
    {
        SwitchAt(FieldRow(L(key, fallback)), value, L(key, fallback), () => { set(!value); Render(); });
    }
}
