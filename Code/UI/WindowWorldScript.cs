using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using NeoModLoader.api;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript : AbstractWideWindow<WindowWorldScript>
{
    private const float ListWidth = 130f;
    private const float EditorWidth = 404f;
    private enum Page { Rules, Preview, History, Picker, Confirm, Package, ScenarioList, Scenario, Regions, Rehearsal, Scene, Checkpoints }

    private RectTransform _list;
    private RectTransform _body;
    private RectTransform _toolbar;
    private ScrollRect _bodyScroll;
    private readonly Dictionary<ScriptInstance, ScriptInstance> _drafts = new();
    private readonly HashSet<object> _expanded = new();
    private ScriptInstance _selected;
    private ScriptInstance Draft => _selected != null && _drafts.TryGetValue(_selected, out var draft) ? draft : null;
    private bool _advanced;
    private bool _refresh;
    private Page _page;
    private Actor _sampleActor;
    private long _sampleActorID;
    private Actor _sampleKiller;
    private long _sampleKillerID;
    private long? _sampleSettlementID;
    private long? _sampleOldKingdomID;
    private string _sampleDelayedKind = "world";
    private string _notice;
    private bool _noticeIsError;
    private string _pickerTitle;
    private PickerLayout _pickerLayout;
    private IReadOnlyList<Option> _options;
    private Page _pickerReturn;
    private float _pickerScroll = 1f;
    private Action _confirm;
    private Action _cancelConfirm;
    private string _confirmText;

    protected override void Init()
    {
        var listScroll = ContentTransform.GetComponentInParent<ScrollRect>(true);
        _bodyScroll = Instantiate(listScroll, BackgroundTransform);
        _bodyScroll.name = "WorldScriptEditor";
        _list = ConfigureScroll(listScroll, new Vector2(-216, -20), new Vector2(144, 224));
        _body = ConfigureScroll(_bodyScroll, new Vector2(73, -20), new Vector2(420, 224));
        var listPanel = new GameObject("ScriptListPanel", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        listPanel.transform.SetParent(BackgroundTransform, false);
        listPanel.transform.SetSiblingIndex(listScroll.transform.GetSiblingIndex());
        listPanel.rectTransform.anchoredPosition = new Vector2(-218, -20);
        listPanel.rectTransform.sizeDelta = new Vector2(146, 229);
        PaintSurface(listPanel, Surface.List, true);
        listPanel.color = new Color(0.86f, 0.87f, 0.8f, 0.65f);
        listPanel.raycastTarget = false;
        _toolbar = Row(BackgroundTransform, 566, 29, "WorldScriptToolbar");
        _toolbar.anchoredPosition = new Vector2(0, 107);
        WorldScripts.ScriptsChanged += RefreshScripts;
        WorldScripts.ScriptRuntimeChanged += RefreshRuntime;
        WorldScriptScenes.Changed += RefreshScenes;
    }

    public override void OnNormalEnable()
    {
        _notice = null;
        RefreshScripts();
    }

    private void OnDestroy()
    {
        WorldScripts.ScriptsChanged -= RefreshScripts;
        WorldScripts.ScriptRuntimeChanged -= RefreshRuntime;
        WorldScriptScenes.Changed -= RefreshScenes;
    }

    private void LateUpdate()
    {
        RefreshSceneClock();
        if (!_refresh) return;
        _refresh = false;
        if (_page is Page.History or Page.Rules or Page.Scenario or Page.Scene) Render();
    }

    private void RefreshScripts()
    {
        if (_scenario != null && !WorldScripts.Scenarios.Contains(_scenario)) _scenario = null;
        foreach (ScriptInstance key in _drafts.Keys.Where(key => !WorldScripts.Scripts.Contains(key)).ToArray())
            _drafts.Remove(key);
        if (_selected != null && !WorldScripts.Scripts.Contains(_selected))
            SelectScript(WorldScripts.Scripts.FirstOrDefault());
        else if (_selected == null && WorldScripts.Scripts.Count > 0)
            SelectScript(WorldScripts.Scripts[0]);
        else if (gameObject.activeInHierarchy)
            Render();
    }

    private void RefreshRuntime(ScriptInstance script)
    {
        if (gameObject.activeInHierarchy &&
            (_page == Page.History && ReferenceEquals(_selected, script) || _page == Page.Scenario && script.ScenarioID == _scenario?.ID))
            _refresh = true;
    }

    private void SelectScript(ScriptInstance script)
    {
        _showValidation = false;
        _selected = script;
        if (script != null && !_drafts.ContainsKey(script)) _drafts.Add(script, WorldScripts.Clone(script));
        _expanded.Clear();
        _advanced = false;
        _sampleActor = _sampleKiller = null;
        _sampleSettlementID = _sampleOldKingdomID = null;
        _notice = null;
        _page = Page.Rules;
        if (gameObject.activeInHierarchy) Render(true);
    }

    private void Enter(Page page)
    {
        bool returningFromPicker = _page == Page.Picker && page == _pickerReturn;
        _page = page;
        _notice = null;
        Render(true);
        if (returningFromPicker) _bodyScroll.verticalNormalizedPosition = _pickerScroll;
    }

    private void Render(bool resetScroll = false)
    {
        if (_body == null) return;
        float scroll = _bodyScroll.verticalNormalizedPosition;
        Clear(_toolbar);
        Clear(_list);
        Clear(_body);
        BuildList();
        BuildToolbar();
        if (Draft == null && _page is Page.Rules or Page.Preview or Page.History)
            Label(_body, L("editor.empty", "Create a script or start from a template."), EditorWidth, 25);
        else
        {
            switch (_page)
            {
                case Page.Rules: BuildRules(); break;
                case Page.Preview: BuildPreview(); break;
                case Page.History: BuildHistory(); break;
                case Page.Picker: BuildPicker(); break;
                case Page.Confirm: BuildConfirmation(); break;
                case Page.Package: BuildPackage(); break;
                case Page.ScenarioList: BuildScenarioList(); break;
                case Page.Scenario: BuildScenario(); break;
                case Page.Regions: BuildRegions(); break;
                case Page.Rehearsal: BuildRehearsal(); break;
                case Page.Scene: BuildScene(); break;
                case Page.Checkpoints: BuildCheckpoints(); break;
            }
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
        _bodyScroll.verticalNormalizedPosition = resetScroll ? 1f : scroll;
    }

    private void BuildList()
    {
        ButtonAt(_list, L("scenario.list", "Scenarios"), ListWidth, () => Enter(Page.ScenarioList),
            NativeIcon("iconWorldLaws"), surface: Surface.List, selected: _page is Page.ScenarioList or Page.Scenario or Page.Scene);
        ButtonAt(_list, L("region.list", "Map regions"), ListWidth, () => Enter(Page.Regions),
            NativeIcon("iconArrowAttackTarget"), surface: Surface.List, selected: _page == Page.Regions);
        ButtonAt(_list, L("rehearsal.list", "Rehearsals"), ListWidth, () => OpenRehearsal(null),
            NativeIcon("iconInspect"), surface: Surface.List, selected: _page == Page.Rehearsal);
        ButtonAt(_list, L("scene.checkpoints", "Checkpoints"), ListWidth, () => OpenCheckpoints(null),
            NativeIcon("iconSaveLocal"), surface: Surface.List, selected: _page == Page.Checkpoints);
        if (WorldScripts.CanImportLegacy)
            ButtonAt(_list, L("editor.import", "Import old scripts"), ListWidth, ImportLegacy,
                NativeIcon("iconArrowDOWN"), height: 21, surface: Surface.Quiet);
        if (WorldScripts.Scripts.Count == 0)
            Label(_list, L("editor.no_scripts", "No scripts in this world"), ListWidth, 22, SecondaryText, 8);
        foreach (ScriptInstance script in WorldScripts.Scripts)
        {
            string name = _drafts.TryGetValue(script, out var draft) ? draft.Name : script.Name;
            string state = script.Enabled ? L("editor.active", "Enabled") : script.IsDraft ?
                L("editor.draft", "Draft") : L("editor.paused", "Disabled");
            ScriptRow(_list, name, state, ListWidth, script.Enabled, script.IsDraft,
                ReferenceEquals(_selected, script), () => SelectScript(script));
        }
    }

    private void BuildToolbar()
    {
        ButtonAt(_toolbar, L("add_script", "Add script"), 130, OpenTemplates, NativeIcon("iconShovelPlus"), height: 23);
        if (Draft == null || _page is Page.ScenarioList or Page.Scenario or Page.Regions or Page.Rehearsal or Page.Scene or Page.Checkpoints) return;
        Row(_toolbar, 9, 16);
        TabAt(_toolbar, L("editor.rules", "Rules"), 60, "iconWorldLaws", _page == Page.Rules, () => Enter(Page.Rules));
        TabAt(_toolbar, L("command.preview", "Preview"), 65, "iconInspect", _page == Page.Preview, () => Enter(Page.Preview));
        TabAt(_toolbar, L("editor.history", "Run history"), 78, "iconWorldLog", _page == Page.History, () => Enter(Page.History));
        ButtonAt(_toolbar, L("editor.save_draft", "Save changes"), 77, () => Apply(_selected.Enabled),
            NativeIcon("iconSaveLocal"), height: 22, surface: Surface.Quiet);
        ButtonAt(_toolbar, _selected.Enabled ? L("editor.pause", "Disable") : L("editor.enable", "Save and enable"),
            92, () =>
            {
                if (!_selected.Enabled) Apply(true);
                else
                {
                    _selected.Enabled = false;
                    Draft.Enabled = false;
                    WorldScripts.NotifyChanged();
                }
            }, NativeIcon(_selected.Enabled ? "iconPause" : "iconPlay"), height: 22,
            surface: _selected.Enabled ? Surface.Command : Surface.Primary);
        IconCommand(_toolbar, L("editor.more", "More"), "iconOptions", OpenScriptCommands);
    }

    private void Apply(bool enabled)
    {
        ScriptInstance draft = Draft;
        if (enabled)
        {
            WorldScriptValidationResult validation = WorldScriptEngine.Validate(draft);
            if (!validation.IsValid)
            {
                _page = Page.Rules;
                _notice = null;
                _showValidation = true;
                _noticeIsError = true;
                Render(true);
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(draft.Name))
        {
            _page = Page.Rules;
            _notice = L("editor.name_required", "Enter a script name.");
            _noticeIsError = true;
            Render(true);
            return;
        }
        ScriptInstance original = _selected;
        _showValidation = false;
        _selected = draft;
        _drafts.Remove(original);
        _expanded.Clear();
        _page = Page.Rules;
        _notice = L("editor.applied", "Changes applied. Save the world to keep them.");
        _noticeIsError = false;
        _drafts.Add(draft, WorldScripts.Clone(draft));
        WorldScripts.Apply(original, draft, enabled);
    }

    private void ImportLegacy()
    {
        try
        {
            int count = WorldScripts.ImportLegacy();
            if (count > 0) SelectScript(WorldScripts.Scripts[WorldScripts.Scripts.Count - count]);
            _notice = string.Format(L("editor.imported", "Imported {0} disabled scripts. Review them before enabling."), count);
            _noticeIsError = false;
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not import legacy WorldScript file: {exception}");
            _notice = L("editor.import_failed", "Could not read the old script file.");
            _noticeIsError = true;
        }
        WorldTip.showNow(_notice, false, "top");
        Render();
    }

    private void OpenTemplates()
    {
        OpenPicker(L("add_script", "Add script"), new[]
        {
            new Option(L("editor.blank", "Blank script"), () => AddFlowTemplate("blank")),
            new Option(L("templates.library", "My templates"), OpenTemplateLibrary, NativeIcon("iconSaveLocal")),
            new Option(L("packages.import", "Import script package"), OpenPackages, NativeIcon("iconSaveLocal")),
            new Option(L("flow.template.rescue", "Rescue after an ally dies"), () => AddFlowTemplate("rescue")),
            new Option(L("flow.template.city_rescue", "Treat residents of small villages"), () => AddFlowTemplate("city_rescue"), NativeIcon("iconCityList")),
            new Option(L("scenario.reconstruction", "Postwar reconstruction"), CreateReconstructionScenario, NativeIcon("iconCityList")),
            new Option(L("scenario.relief", "Famine relief"), CreateReliefScenario, NativeIcon("iconCityList")),
            new Option(L("scenario.dynasty", "Royal succession"), CreateDynastyScenario, NativeIcon("iconWorldLaws")),
            new Option(L("scene.trial", "The centennial trial"), CreateTrialScenario, NativeIcon("iconClock")),
            new Option(L("flow.template.spawn", "Spawn units and give them a trait"), () => AddFlowTemplate("spawn")),
            new Option(L("editor.template.heal", "Treat wounded units"), () => AddTemplate(false)),
            new Option(L("editor.template.birth", "Give new units a trait"), () => AddTemplate(true))
        }, Page.Rules);
    }

    private void AddFlowTemplate(string template)
    {
        var script = new ScriptInstance
        {
            Name = L("default_name", "New script"),
            Enabled = false,
            IsDraft = true,
            Flow = new ScriptFlow(),
            TriggerCondition = new TriggerCondition { TriggerID = WorldScriptRegistry.TriggerManual }
        };
        ScriptQuery query = script.Flow.Query;
        if (template == "rescue")
        {
            script.Name = L("flow.template.rescue", "Rescue after an ally dies");
            script.TriggerCondition.TriggerID = WorldScriptRegistry.TriggerActorDied;
            query.Scope = NewScope(WorldScriptRegistry.TargetActorEventRadius);
            query.Scope.Parameters["radius"] = 8;
            query.Filter.Children.Add(new ConditionNode { Operator = "condition", ConditionID = "actor.event_kingdom" });
            query.Filter.Children.Add(new ConditionNode
            {
                Operator = "condition", ConditionID = "actor.health_ratio", Parameters = new JObject { ["operator"] = "<", ["value"] = 0.5 }
            });
            query.Order = "health_asc";
            query.Count = 3;
            script.Flow.Steps.Add(new ScriptStep
            {
                Action = new ScriptAction { ActionID = "actor.health.change" },
                Numbers = { ["value"] = new ScriptNumberBinding { Percent = 20d } }
            });
        }
        else if (template == "city_rescue")
        {
            script.Name = L("flow.template.city_rescue", "Treat residents of small villages");
            script.TriggerCondition.TriggerID = WorldScriptRegistry.TriggerInterval;
            script.TriggerCondition.Parameters["interval"] = 60;
            query.Scope = NewScope(WorldScriptRegistry.TargetCityAll);
            query.Filter.Children.Add(new ConditionNode
            {
                Operator = "condition", ConditionID = "city.population",
                Parameters = new JObject { ["operator"] = "<", ["value"] = 20 }
            });
            var relation = new ScriptRelation { ID = WorldScriptEngine.CityResidents };
            relation.Filter.Children.Add(new ConditionNode
            {
                Operator = "condition", ConditionID = "actor.health_ratio",
                Parameters = new JObject { ["operator"] = "<", ["value"] = 0.5 }
            });
            script.Flow.Steps.Add(new ScriptStep
            {
                Action = new ScriptAction { ActionID = "actor.health.change" },
                Relation = relation,
                Numbers = { ["value"] = new ScriptNumberBinding { Percent = 20d } }
            });
            script.Flow.Steps.Add(new ScriptStep
            {
                Source = WorldScriptEngine.GroupResultSource,
                Mode = "once",
                Action = new ScriptAction
                {
                    ActionID = WorldScriptHistory.ActionID,
                    Parameters = new JObject
                    {
                        ["message"] = L("relation.city_rescue_message", "{group_name}: treated {target_count} residents."),
                        ["log_icon"] = "ui/Icons/iconCityList",
                        [WorldScriptMessages.EnabledKey] = true
                    }
                }
            });
        }
        else if (template == "spawn")
        {
            script.Name = L("flow.template.spawn", "Spawn units and give them a trait");
            script.MaxTargets = 1;
            script.AllowDangerousActions = true;
            query.Scope = NewScope(WorldScriptRegistry.TargetTilePositionRadius);
            WorldTile tile = World.world?.getMouseTilePosCachedFrame() ?? World.world?.GetTile(MapBox.width / 2, MapBox.height / 2);
            if (tile != null)
            {
                query.Scope.Parameters["x"] = tile.x;
                query.Scope.Parameters["y"] = tile.y;
            }
            script.Flow.Steps.Add(new ScriptStep
            {
                Action = new ScriptAction
                {
                    ActionID = "tile.actor.spawn", Parameters = new JObject { ["asset_id"] = "human", ["count"] = 3 }
                }
            });
            script.Flow.Steps.Add(new ScriptStep
            {
                Source = "previous",
                Action = new ScriptAction { ActionID = "actor.trait.add", Parameters = new JObject { ["trait_id"] = "strong" } }
            });
        }
        SelectScript(WorldScripts.AddScript(script));
        if (template == "spawn")
        {
            _expanded.Add(Draft.Flow.Query.Scope);
            Render();
        }
    }

    private void AddTemplate(bool birth)
    {
        var template = new ScriptInstance
        {
            Name = birth ? L("editor.template.birth", "Give new units a trait") : L("editor.template.heal", "Treat wounded units"),
            Enabled = false,
            IsDraft = true,
            MaxTargets = birth ? 1 : 2,
            TriggerCondition = new TriggerCondition
            {
                TriggerID = birth ? WorldScriptRegistry.TriggerActorCreated : WorldScriptRegistry.TriggerInterval
            },
            Target = new TargetSelector
            {
                TargetID = birth ? WorldScriptRegistry.TargetActorEvent : WorldScriptRegistry.TargetActorAll
            }
        };
        template.Conditions.Children.Add(new ConditionNode
        {
            Operator = "condition",
            ConditionID = birth ? "actor.asset" : "actor.health_ratio",
            Parameters = birth ? new JObject { ["asset_id"] = "human" }
                : new JObject { ["operator"] = "<", ["value"] = 0.5 }
        });
        template.Actions.Add(new ScriptAction
        {
            ActionID = birth ? "actor.trait.add" : "actor.health.change",
            Parameters = birth ? new JObject { ["trait_id"] = "strong" } : new JObject { ["value"] = 10 }
        });
        SelectScript(WorldScripts.AddScript(template));
    }

    private void OpenScriptCommands()
    {
        OpenPicker(L("editor.more", "More"), new[]
        {
            new Option(L("command.duplicate", "Duplicate"),
                () => SelectScript(WorldScripts.Duplicate(Draft, L("copy_suffix", " Copy")))),
            new Option(L("templates.save", "Save as template"), SaveTemplate, NativeIcon("iconSaveLocal")),
            new Option(L("packages.export", "Export script package"), OpenPackageExport, NativeIcon("iconSaveLocal")),
            new Option(L("rehearsal.create", "Create rehearsal copies"), () =>
                CreateRehearsal(Draft.Name, new[] { Draft }), NativeIcon("iconInspect")),
            new Option(L("scenario.assign", "Move to scenario"), ChooseScenario, NativeIcon("iconWorldLaws")),
            new Option(L("editor.discard", "Discard draft edits"), () =>
                Confirm(L("editor.discard_question", "Discard unapplied edits?"), () =>
                {
                    _drafts.Remove(_selected);
                    SelectScript(_selected);
                }, () => Enter(Page.Rules))),
            new Option(L("command.delete", "Delete"), () =>
                Confirm(string.Format(L("confirm.delete_body", "Delete '{0}' permanently?"), _selected.Name),
                    () => WorldScripts.Delete(_selected), () => Enter(Page.Rules)))
        }, Page.Rules);
    }

    private void BuildPreview()
    {
        ScriptInstance draft = Draft;
        Heading(L("command.preview", "Preview"));
        if (_notice != null) Message(_notice, true);
        bool actorEvent = WorldScriptEngine.UsesEventActor(draft.TriggerCondition.TriggerID);
        bool deathEvent = draft.TriggerCondition.TriggerID == WorldScriptRegistry.TriggerActorDied;
        Actor actor = _sampleActor?.data?.id == _sampleActorID && _sampleActor?.isAlive() == true ? _sampleActor : null;
        Actor killer = _sampleKiller?.data?.id == _sampleKillerID && _sampleKiller?.isAlive() == true ? _sampleKiller : null;
        if (actorEvent)
        {
            string who = deathEvent && draft.Flow != null ? L("flow.sample_victim", "Sample victim") : L("editor.choose_sample", "Choose a sample unit");
            ButtonAt(_body, actor == null ? who : $"{who} · {ActorLabel(actor)}", EditorWidth, () => ChooseSample());
            if (actor == null)
            {
                Message(L("editor.sample_required", "Select a unit to supply the event context."), false);
                return;
            }
            if (deathEvent && draft.Flow != null)
                ButtonAt(_body, killer == null ? L("flow.sample_killer", "Choose a sample killer")
                    : $"{L("flow.killer", "Killer")} · {ActorLabel(killer)}", EditorWidth, () => ChooseSample(true));
            if (draft.Flow != null) Message(L("flow.simulated_event", "The sample units supply event data for this preview."), false);
        }
        string triggerID = draft.TriggerCondition.TriggerID;
        bool settlementEvent = WorldScriptEngine.UsesKingdomEvent(triggerID);
        if (settlementEvent && !BuildSettlementSample(triggerID)) return;
        WorldScriptPreviewResult preview = settlementEvent
            ? WorldScriptEngine.PreviewEvent(draft, SettlementSample(triggerID)) : WorldScriptEngine.Preview(draft, actor, killer);
        if (!preview.IsValid)
        {
            BuildValidationErrors(preview.Errors);
            BuildPreviewDecisions(preview);
            return;
        }
        if (!preview.GatePassed)
        {
            Message(L("flow.gate_failed", "The run conditions are not met."), false);
            BuildPreviewDecisions(preview);
            ButtonAt(_body, L("editor.refresh_preview", "Refresh preview"), 126, () => Enter(Page.Preview));
            return;
        }
        var counts = Row(_body, EditorWidth, 29);
        Readout(counts, L("editor.matches", "Matched"), preview.MatchCount, "iconInspect", 122);
        Readout(counts, L("field.max_targets", "Max targets"), draft.MaxTargets, "iconArrowAttackTarget", 122);
        Readout(counts, draft.Flow == null ? L("editor.processing", "Will process") : L("flow.main_selected", "Main query"),
            preview.Targets.Count, "iconPlay", 122);
        if (draft.Flow == null)
        {
            foreach (ScriptAction action in WorldScriptEngine.EnabledActions(draft.Actions))
            {
                Label(_body, ActionSummary(action), EditorWidth);
                BuildActionPreview(action, preview, preview.Targets.Count);
            }
        }
        else
        {
            foreach (WorldScriptStepPreview stepPreview in preview.Steps)
            {
                if (stepPreview.Groups != null) continue;
                ScriptStep step = draft.Flow.Steps[stepPreview.StepIndex];
                string amount = stepPreview.Deferred
                    ? WorldScriptEngine.ResultSources.Contains(step.Source)
                        ? L("flow.preview_result_deferred", "Uses the selected step’s actual results at runtime")
                        : L("flow.preview_deferred", "Uses the preceding step's output when it runs")
                    : string.Format(L("flow.preview_inputs", "{0} inputs"), stepPreview.Targets.Count);
                if (step.Mode == "once") amount += " · " + L("flow.mode.once", "Once per batch");
                if (step.HasBranch)
                {
                    Message($"{stepPreview.StepIndex + 1}. {L("branch.title", "Conditional")} · {amount}", false);
                    BuildBranchPreview(step, stepPreview, preview);
                }
                else
                {
                    Message($"{stepPreview.StepIndex + 1}. {NamedStepSummary(step)} · {amount}", false);
                    BuildActionPreview(step.Action, preview, stepPreview.Deferred ? null : stepPreview.Targets.Count);
                    BuildEstimatedChange(step, stepPreview.Targets, preview.Event);
                }
                if (step.Source == "scope")
                    foreach (WorldScriptTarget target in stepPreview.Targets.Take(3))
                        Label(_body, TargetLabel(target), EditorWidth);
            }
            BuildGroupedPreview(draft.Flow, preview);
        }
        if (!preview.CanRun)
            Message(L("editor.no_targets", "No matching targets."), false);
        var grouped = new HashSet<(ScriptTargetKind, long)>(preview.Steps.Where(step => step.Groups != null)
            .SelectMany(step => step.Groups).Select(group => (group.Parent.Kind, group.Parent.MetaID)));
        var otherTargets = preview.Targets.Where(target => !grouped.Contains((target.Kind, target.MetaID))).ToList();
        foreach (WorldScriptTarget target in otherTargets.Take(40))
            Label(_body, TargetLabel(target), EditorWidth);
        if (otherTargets.Count > 40)
            Label(_body, string.Format(L("editor.extra_targets", "+ {0} more targets"), otherTargets.Count - 40), EditorWidth);
        BuildPreviewDecisions(preview);
        var commands = Row(_body, EditorWidth, 25);
        ButtonAt(commands, L("editor.refresh_preview", "Refresh preview"), 126,
            () => Enter(Page.Preview), NativeIcon("iconInspect"), surface: Surface.Quiet);
        if (preview.CanRun)
        {
            ButtonAt(commands, L("command.run_now", "Run now"), 126, () =>
                Confirm(draft.Flow == null
                        ? string.Format(L("editor.run_question", "Apply these actions to {0} target(s) now?"), preview.Targets.Count)
                        : L("flow.confirm", "Run the enabled steps with these inputs?"),
                    () =>
                    {
                        draft.Runtime = _selected.Runtime;
                        bool ran = settlementEvent ? WorldScriptEngine.RunEventNow(draft, SettlementSample(triggerID), preview)
                            : WorldScriptEngine.RunNow(draft, actor, preview, killer);
                        _page = ran ? Page.History : Page.Preview;
                        _notice = ran ? null : ErrorLabel(draft.Runtime.LastError ?? L("runtime.run_failed", "Run failed."));
                        Render(true);
                    }, () => Enter(Page.Preview)), NativeIcon("iconPlay"), surface: Surface.Primary);
        }
    }

    private void BuildGroupedPreview(ScriptFlow flow, WorldScriptPreviewResult preview)
    {
        var groups = preview.Steps.Where(step => step.Groups != null)
            .SelectMany(step => step.Groups.Select(group => (Step: step, Group: group)))
            .GroupBy(item => (item.Group.Parent.Kind, item.Group.Parent.MetaID)).ToList();
        if (groups.Count == 0) return;
        FlowHeading("relation.group_results", "Groups", "iconCityList");
        foreach (var group in groups.Take(40))
        {
            var first = group.First().Group;
            var villages = group.Where(item => item.Group.Villages != null)
                .SelectMany(item => item.Group.Villages.Select(village => (item.Step, Group: village)))
                .GroupBy(item => item.Group.Parent.MetaID).ToList();
            string amount = group.Any(item => item.Group.Villages != null)
                ? VillageCount(villages.Count) : RelatedCount(first.Targets.Count);
            string title = TargetLabel(first.Parent) + " · " + amount;
            if (!FlowFold(("preview_group", group.Key), title, GroupIcon(group.Key.Kind))) continue;
            foreach (var item in group.Where(item => item.Group.Villages == null))
                BuildDetails(item.Step, item.Group, 0);
            foreach (var village in villages.Take(40))
            {
                var firstVillage = village.First().Group;
                string label = TargetLabel(firstVillage.Parent) + " · " + RelatedCount(firstVillage.Targets.Count);
                if (!FlowFold(("preview_village", group.Key, village.Key), label, GroupIcon(ScriptTargetKind.City), VillageRowIndent)) continue;
                foreach (var item in village) BuildDetails(item.Step, item.Group, VillageDetailIndent);
            }
            if (villages.Count > 40)
                Message(string.Format(L("history.omitted_groups", "{0} additional groups"), villages.Count - 40), false, indent: VillageRowIndent);
        }
        if (groups.Count > 40)
            Message(string.Format(L("history.omitted_groups", "{0} additional groups"), groups.Count - 40), false);

        void BuildDetails(WorldScriptStepPreview stepPreview, WorldScriptTargetGroup group, int indent)
        {
            var step = flow.Steps[stepPreview.StepIndex];
            int count = group.Targets.Count;
            string amount = stepPreview.ReportsGroups ? L("relation.preview_report", "Uses this group's actual results")
                : string.Format(L("flow.preview_inputs", "{0} inputs"), count);
            Message($"{stepPreview.StepIndex + 1}. {NamedStepSummary(step)} · {amount}", false, indent: indent);
            if (step.ResultCondition is ScriptCountRule rule)
                Message($"{L("flow.result_condition", "Result count condition")} · {L("flow.result." + rule.Measure, rule.Measure)} " +
                    $"{rule.Operator} {rule.Value}", false, indent: indent);
            if (step.HasBranch)
                BuildBranchPreview(step, stepPreview, preview, group.Name, indent);
            else
                BuildActionPreview(step.Action, preview, stepPreview.ReportsGroups ? null : count, group.Name, indent);
            if (!stepPreview.ReportsGroups) BuildEstimatedChange(step, group.Targets, preview.Event, indent);
            if (stepPreview.ReportsGroups || count == 0) return;
            string samples = string.Join(" / ", group.Targets.Take(8).Select(TargetLabel));
            if (count > 8) samples += " · " + string.Format(L("editor.extra_targets", "+ {0} more targets"), count - 8);
            Message(samples, false, indent: indent);
        }
    }

    private void BuildEstimatedChange(ScriptStep step, List<WorldScriptTarget> targets, WorldScriptEvent snapshot, int indent = 0)
    {
        var estimate = WorldScriptRegistry.GetAction(step.Action.ActionID)?.EstimateChange;
        if (estimate == null || step.HasBranch || step.Mode == "once") return;
        foreach (var target in targets.Take(8))
            if (WorldScriptEngine.EstimateChange(Draft, step, target, snapshot) is int change)
                Message(TargetLabel(target) + " · " + string.Format(L("flow.estimated_change", "Estimated change {0:+0;-0;0}"), change),
                    false, indent: indent);
    }

    private const int VillageRowIndent = 18;
    private const int VillageDetailIndent = VillageRowIndent + 24;

    private static string VillageCount(int count) => string.Format(L("relation.village_count", "{0} villages"), count);

    private static string RelatedCount(int count) => string.Format(L("relation.group_inputs", "{0} related targets"), count);

    private void BuildBranchPreview(ScriptStep step, WorldScriptStepPreview branch, WorldScriptPreviewResult preview,
        string groupName = null, int indent = 0)
    {
        if (!branch.BranchKnown)
            Message(L("branch.deferred", "Conditions are evaluated when this step starts."), false, indent: indent);
        Outcome(L("branch.then", "Then"), step.Action, branch.ThenTargets.Count);
        Outcome(L("branch.otherwise", "Otherwise"), step.Branch.HasOtherwise ? step.Branch.Otherwise : null,
            branch.OtherwiseTargets.Count);

        void Outcome(string title, ScriptAction action, int count)
        {
            string number = branch.BranchKnown ? $" · {string.Format(L("flow.preview_inputs", "{0} inputs"), count)}" : "";
            Message($"{title} · {(action != null ? ActionSummary(action) : L("branch.skip", "Skip"))}{number}", false, indent: indent);
            if (action != null) BuildActionPreview(action, preview, branch.BranchKnown ? count : null, groupName, indent);
        }
    }

    private void BuildActionPreview(ScriptAction action, WorldScriptPreviewResult preview, int? targetCount,
        string groupName = null, int indent = 0)
    {
        bool richText = action.ActionID == WorldScriptHistory.ActionID;
        if (richText || action.ActionID == WorldScriptMessages.TipAction && WorldScriptMessages.UsesVariables(action.Parameters))
            Message(WorldScriptMessages.Render(action.Parameters, preview.Event, preview.MatchCount, targetCount, groupName),
                false, richText, indent);
    }

    private void Confirm(string text, Action confirm, Action cancel)
    {
        _confirmText = text;
        _confirm = confirm;
        _cancelConfirm = cancel;
        Enter(Page.Confirm);
    }

    private void BuildConfirmation()
    {
        Message(_confirmText, false);
        RectTransform row = Row(_body, EditorWidth, 26);
        ButtonAt(row, L("command.cancel", "Cancel"), 95, _cancelConfirm);
        ButtonAt(row, L("editor.confirm", "Confirm"), 95, _confirm, surface: Surface.Primary);
    }

    private static string TargetLabel(WorldScriptTarget target) => target.Kind switch
    {
        ScriptTargetKind.Actor => ActorLabel(target.Actor),
        ScriptTargetKind.Tile => $"({target.Tile.x}, {target.Tile.y})",
        ScriptTargetKind.City or ScriptTargetKind.Kingdom => $"{target.Meta.name} · #{target.MetaID}",
        _ => L("target.world", "Current world")
    };

    private static string GroupIcon(ScriptTargetKind kind) => kind == ScriptTargetKind.Kingdom ? "iconKingdomList" : "iconCityList";

    private static string ActorLabel(Actor actor) => $"{actor.getName()} · #{actor.data.id}";
}
