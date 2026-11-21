using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private Action<WorldTile> _mapSelection;
    private MapStats _mapSelectionWorld;
    private int _mapSelectionFrame;
    private static string[] _historyIconPaths;

    private void BuildParameters(string definitionID, JObject values, ScriptParameterDefinition[] parameters,
        Dictionary<string, ScriptNumberBinding> numberBindings = null, ScriptTargetKind kind = ScriptTargetKind.None,
        Page returnTo = Page.Rules)
    {
        bool coordinates = parameters.Any(parameter => parameter.Key == "x") && parameters.Any(parameter => parameter.Key == "y");
        if (coordinates)
        {
            var row = FieldRow(L("editor.position", "Map position"));
            ButtonAt(row, L("editor.pick_map", "Pick on map"), 116, () => BeginMapSelection(tile =>
            {
                values["x"] = tile.x;
                values["y"] = tile.y;
            }), NativeIcon("iconArrowAttackTarget"), surface: Surface.Quiet);
        }

        foreach (ScriptParameterDefinition parameter in parameters)
        {
            string key = parameter.Key;
            if (WorldScriptMessages.IsMessageAction(definitionID) && key == WorldScriptMessages.EnabledKey) continue;
            string value = values[key] is JValue { Value: IFormattable scalar }
                ? scalar.ToString(null, CultureInfo.InvariantCulture)
                : values[key]?.ToString() ?? "";
            string name = L("parameter." + key, parameter.Name);
            if (key == "id" && WorldScriptAffiliations.Kind(definitionID) is string affiliation)
                name = L("affiliation." + affiliation, parameter.Name);
            if (key is "interval" or "time" or "repeat_interval" or "delay")
                name += $" ({L("editor.world_seconds", "world seconds")})";
            bool healthRatio = definitionID == "actor.health_ratio" && key == "value";
            if (healthRatio) name += " (%)";
            if (key == "message" && WorldScriptMessages.IsMessageAction(definitionID))
            {
                BuildMessageField(definitionID, values, value);
                continue;
            }
            RectTransform row = FieldRow(name);
            if (numberBindings != null && parameter.Type is ScriptParameterType.Integer or ScriptParameterType.Number &&
                BuildNumberBinding(row, name, key, numberBindings, kind)) continue;
            if (parameter.Type == ScriptParameterType.Choice || parameter.Type == ScriptParameterType.Boolean)
            {
                string[] choices = parameter.Type == ScriptParameterType.Boolean ? new[] { "true", "false" } : parameter.Choices;
                string ChoiceLabel(string choice) => parameter.Type == ScriptParameterType.Boolean
                    ? choice.Equals("true", StringComparison.OrdinalIgnoreCase) ? L("value.yes", "Yes") : L("value.no", "No")
                    : L($"choice.{key}.{choice}", choice);
                FieldAt(row, ChoiceLabel(value), 206, () => OpenPicker(name, choices.Select(choice =>
                    new Option(ChoiceLabel(choice), () =>
                    {
                        values[key] = parameter.Type == ScriptParameterType.Boolean
                            ? new JValue(choice == "true")
                            : new JValue(choice);
                        Enter(returnTo);
                    })), returnTo));
                continue;
            }
            if (IsAssetPicker(definitionID, key))
            {
                FieldAt(row, ParameterDisplayName(definitionID, key, value), 206, () =>
                    OpenPicker(name, ParameterOptions(definitionID, key, value, id =>
                    {
                        if (key == "id" && WorldScriptAffiliations.Kind(definitionID) != null)
                            WorldScriptTemplates.Bind(values, id);
                        else values[key] = id;
                        Enter(returnTo);
                    }), returnTo, layout: key == "trait_id" ? PickerLayout.Traits
                        : key == "log_icon" ? PickerLayout.Icons : PickerLayout.List),
                    icon: ParameterIcon(definitionID, key, value));
                continue;
            }
            if (key == "scenario_parameter")
            {
                var scenario = WorldScriptEngine.Scenario(Draft?.ScenarioID);
                FieldAt(row, string.IsNullOrEmpty(value) ? L("value.unset", "Unset") : ScenarioParameterName(value), 206,
                    () => OpenPicker(name, (scenario?.Parameters.Keys ?? Enumerable.Empty<string>())
                        .Select(item => new Option(ScenarioParameterName(item), () =>
                        {
                            values[key] = item;
                            Enter(returnTo);
                        })), returnTo));
                continue;
            }

            if (healthRatio && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio))
                value = (ratio * 100d).ToString("0.##", CultureInfo.InvariantCulture);
            var validation = parameter.Type switch
            {
                ScriptParameterType.Integer => InputField.CharacterValidation.Integer,
                ScriptParameterType.Number => InputField.CharacterValidation.Decimal,
                _ => InputField.CharacterValidation.None
            };
            InputAt(row, value, 206, input =>
            {
                if (parameter.Type == ScriptParameterType.Integer &&
                    int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                    values[key] = integer;
                else if (parameter.Type == ScriptParameterType.Number &&
                    double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
                    !double.IsNaN(number) && !double.IsInfinity(number))
                    values[key] = healthRatio ? number / 100d : number;
                else
                    values[key] = input;
            }, validation);
        }
    }

    private void BuildMessageField(string definition, JObject values, string value)
    {
        bool rich = definition == WorldScriptHistory.ActionID;
        RectTransform row = FieldRow(rich ? L("log.message", "Message (rich text)") : L("parameter.message", "Message"),
            rich ? 54f : 22f);
        var input = InputAt(row, value, rich ? 240f : 206f, text => values["message"] = text,
            height: rich ? 50f : 19f, multiline: rich).input;
        string title = L("message.variables", "Message variables");
        var commands = FieldRow(title);
        bool active = WorldScriptMessages.UsesVariables(values);
        SwitchAt(commands, active, title, () =>
        {
            values["message"] = input.text;
            values[WorldScriptMessages.EnabledKey] = !active;
            Render();
        });
        ButtonAt(commands, L("message.insert", "Insert variable"), 102, () =>
        {
            string text = input.text;
            int start = Math.Min(input.selectionAnchorPosition, input.selectionFocusPosition);
            int end = Math.Max(input.selectionAnchorPosition, input.selectionFocusPosition);
            if (start == 0 && end == 0) start = end = text.Length;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);
            var variables = new List<string> { WorldScriptMessages.MatchCount, WorldScriptMessages.TargetCount };
            if (Draft.Flow?.Steps.Any(step => (step.Relation != null || step.Source == WorldScriptEngine.GroupResultSource) &&
                (ReferenceEquals(step.Action.Parameters, values) || ReferenceEquals(step.Branch?.Otherwise?.Parameters, values))) == true)
                variables.Insert(0, WorldScriptMessages.GroupName);
            if (WorldScriptEngine.UsesEventActor(Draft.TriggerCondition.TriggerID))
            {
                variables.Insert(0, WorldScriptMessages.EventActor);
                variables.Insert(1, WorldScriptMessages.EventKingdom);
            }
            if (WorldScriptEngine.UsesKingdomEvent(Draft.TriggerCondition.TriggerID))
            {
                variables.Add(WorldScriptMessages.EventKingdom);
                if (WorldScriptEngine.UsesCityEvent(Draft.TriggerCondition.TriggerID))
                    variables.Add(WorldScriptMessages.EventCity);
                if (Draft.TriggerCondition.TriggerID is WorldScriptRegistry.TriggerCityKingdomChanged or WorldScriptRegistry.TriggerKingdomKingChanged)
                {
                    variables.Add(WorldScriptMessages.EventOld);
                    variables.Add(WorldScriptMessages.EventNew);
                }
            }
            if (Draft.TriggerCondition.TriggerID == WorldScriptRegistry.TriggerWarEnded)
                variables.Add(WorldScriptMessages.EventWar);
            if (Draft.TriggerCondition.TriggerID == WorldScriptRegistry.TriggerCityThreshold)
                variables.Add(WorldScriptMessages.EventValue);
            OpenPicker(title, variables.Select(key =>
            {
                string token = "{" + key + "}";
                return new Option($"{L("message.variable." + key, key)} · {token}", () =>
                {
                    values["message"] = text.Substring(0, start) + token + text.Substring(end);
                    values[WorldScriptMessages.EnabledKey] = true;
                    Enter(Page.Rules);
                });
            }));
        }, surface: Surface.Quiet);
    }

    private bool BuildNumberBinding(RectTransform row, string name, string key,
        Dictionary<string, ScriptNumberBinding> bindings, ScriptTargetKind kind)
    {
        var scenario = WorldScriptEngine.Scenario(Draft.ScenarioID);
        var sources = WorldScriptEngine.NumberSources(kind, Draft.TriggerCondition.TriggerID, Draft.ScenarioID)
            .Where(source => source != "scenario.parameter").ToArray();
        bindings.TryGetValue(key, out ScriptNumberBinding binding);
        string title = L("flow.number.source", "Value source");
        string fixedValue = L("flow.number.fixed", "Fixed number");
        void ChooseSource()
        {
            var options = sources.Select(source => new Option(L("flow.number." + source, source), () =>
            {
                bindings[key] = new ScriptNumberBinding { Source = source, Percent = binding?.Percent ?? 100d };
                Enter(Page.Rules);
            })).ToList();
            if (scenario?.Parameters != null)
                foreach (string parameter in scenario.Parameters.Keys)
                    options.Add(new Option(L("flow.number.scenario.parameter", "Scenario parameter") + " · " + ScenarioParameterName(parameter), () =>
                    {
                        bindings[key] = new ScriptNumberBinding
                        {
                            Source = "scenario.parameter", Parameter = parameter, Percent = binding?.Percent ?? 100d
                        };
                        Enter(Page.Rules);
                    }));
            options.Insert(0, new Option(fixedValue, () => { bindings.Remove(key); Enter(Page.Rules); }));
            OpenPicker(name + " · " + title, options);
        }
        if (binding == null)
        {
            if (sources.Length > 0 || scenario?.Parameters.Count > 0)
                IconCommand(row, title, "iconOptions", ChooseSource);
            return false;
        }
        string valueSource = L("flow.number." + binding.Source, binding.Source);
        if (binding.Source == "scenario.parameter") valueSource += " · " + ScenarioParameterName(binding.Parameter);
        FieldAt(row, valueSource, 206, ChooseSource);
        InputAt(FieldRow(L("flow.number.percent", "Multiplier (%)")), binding.Percent.ToString(CultureInfo.InvariantCulture), 206, value =>
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
                !double.IsNaN(number) && !double.IsInfinity(number)) binding.Percent = number;
        }, InputField.CharacterValidation.Decimal);
        return true;
    }

    private static bool IsAssetPicker(string definition, string key) =>
        key is "asset_id" or "trait_id" or "status_id" or "era_id" or "log_icon" or "resource_id" or "building_id" or "script_id" ||
        key == "id" && WorldScriptAffiliations.Kind(definition) != null;

    private static string ParameterDisplayName(string definition, string key, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return L("value.unset", "Unset");
        if (key == "asset_id") return ActorAssetName(id);
        if (key == "trait_id") return TraitName(id);
        if (key == "log_icon") return HistoryIconName(id);
        if (key == "resource_id" && AssetManager.resources.dict.TryGetValue(id, out var resource))
            return Localize(resource.getLocaleID(), id);
        if (key == "building_id") return id;
        if (key == "script_id") return WorldScripts.Scripts.Find(script => script?.ID == id)?.Name ?? id;
        if (key == "status_id" && AssetManager.status.dict.TryGetValue(id, out var status))
            return Localize(status.getLocaleID(), id);
        if (key == "era_id" && AssetManager.era_library.dict.TryGetValue(id, out var era))
            return Localize(era.getLocaleID(), id);
        if (key == "id" && WorldScriptAffiliations.Kind(definition) is string kind)
        {
            if (id == "-1") return WorldScriptAffiliations.CanClear(definition)
                ? L("affiliation.clear", "Clear affiliation") : L("value.unset", "Unset");
            if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long objectID))
                return WorldScriptAffiliations.Name(kind, objectID) ??
                    string.Format(L("affiliation.unavailable", "Unavailable · #{0}"), id);
        }
        return id;
    }

    private static Sprite ParameterIcon(string definition, string key, string value)
    {
        if (key == "log_icon" && !string.IsNullOrEmpty(value)) return SpriteTextureLoader.getSprite(value);
        if (key == "id" && WorldScriptAffiliations.Kind(definition) == "region") return NativeIcon("iconArrowAttackTarget");
        if (key == "id" && WorldScriptAffiliations.Kind(definition) is string kind &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            return AffiliationIcon(WorldScriptAffiliations.Find(kind, id));
        return null;
    }

    private static Sprite AffiliationIcon(IMetaObject item) => item switch
    {
        Kingdom kingdom when !kingdom.isNeutral() => kingdom.getElementIcon(),
        City => NativeIcon("iconCityList"),
        _ => item?.getActorAsset()?.getSpriteIcon()
    };

    private static IEnumerable<Option> ParameterOptions(string definition, string key, string current, Action<string> picked)
    {
        Option Item(string label, string id, Sprite icon) => new($"{label} · {id}", () => picked(id), icon);
        if (key == "trait_id")
            return AssetManager.traits.list.OrderByDescending(asset => asset.priority)
                .ThenBy(asset => asset.id, StringComparer.Ordinal)
                .Select(asset => new Option($"{TraitName(asset.id)} · {asset.id}", () => picked(asset.id),
                    asset.getSprite(), asset.id == current, asset.group_id));
        if (key == "status_id")
            return AssetManager.status.list.Select(asset =>
                Item(Localize(asset.getLocaleID(), asset.id), asset.id, asset.getSprite()));
        if (key == "era_id")
            return AssetManager.era_library.list.Select(asset => Item(Localize(asset.getLocaleID(), asset.id), asset.id, null));
        if (key == "resource_id")
            return AssetManager.resources.list.Select(asset => Item(Localize(asset.getLocaleID(), asset.id), asset.id, asset.getSpriteIcon()));
        if (key == "building_id")
            return AssetManager.buildings.list.Select(asset => new Option(asset.id, () => picked(asset.id)));
        if (key == "script_id")
            return WorldScripts.Scripts.Where(script => script?.TriggerCondition?.TriggerID == WorldScriptRegistry.TriggerDelayed)
                .Select(script => new Option(script.Name, () => picked(script.ID), selected: script.ID == current));
        if (key == "asset_id")
            return AssetManager.actor_library.list.Select(asset =>
                Item(ActorAssetName(asset.id), asset.id, SpriteTextureLoader.getSprite(asset.path_icon)));
        if (key == "log_icon")
            return new[] { current }.Concat(_historyIconPaths ??= GetHistoryIconPaths())
                .Where(WorldScriptHistory.HasIcon).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new Option(HistoryIconName(path), () => picked(path), SpriteTextureLoader.getSprite(path),
                    string.Equals(path, current, StringComparison.OrdinalIgnoreCase)));
        if (key == "id" && WorldScriptAffiliations.Kind(definition) is string kind)
        {
            if (kind == "region")
                return WorldScripts.Regions.OrderBy(region => region.Name)
                    .Select(region => Item(region.Name, region.ID.ToString(CultureInfo.InvariantCulture), NativeIcon("iconArrowAttackTarget")));
            var entries = WorldScriptAffiliations.List(kind).Where(item => item?.getMetaData() != null)
                .OrderBy(item => item.name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.getID())
                .Select(item =>
                {
                    string id = item.getID().ToString(CultureInfo.InvariantCulture);
                    return new Option($"{item.name} · {id}", () => picked(id), AffiliationIcon(item), id == current);
                }).ToList();
            if (WorldScriptAffiliations.CanClear(definition))
                entries.Insert(0, new Option(L("affiliation.clear", "Clear affiliation"), () => picked("-1"), selected: current == "-1"));
            return entries;
        }
        return Enumerable.Empty<Option>();
    }

    private static string[] GetHistoryIconPaths()
    {
        const string prefix = "ui/Icons/";
        var general = Resources.LoadAll<Sprite>("ui/Icons").Select(sprite => prefix + sprite.name)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        return new[] { WorldScriptHistory.DefaultIcon }
            .Concat(AssetManager.world_log_library.list.Select(asset => asset.path_icon))
            .Concat(AssetManager.traits.list.Select(asset => asset.path_icon))
            .Concat(general).Where(WorldScriptHistory.HasIcon).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string HistoryIconName(string path)
    {
        const string prefix = "ui/Icons/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : path;
    }

    private static string ActorAssetName(string id) =>
        !string.IsNullOrEmpty(id) && AssetManager.actor_library.dict.TryGetValue(id, out var asset)
            ? asset.getLocalizedName() : id ?? L("value.unset", "Unset");

    private static string TraitName(string id) =>
        !string.IsNullOrEmpty(id) && AssetManager.traits.dict.TryGetValue(id, out var asset)
            ? Localize(asset.getLocaleID(), id) : id ?? L("value.unset", "Unset");

    private void ChooseSample(bool killer = false)
    {
        if (World.world == null) return;
        string title = killer ? L("flow.sample_killer", "Choose a sample killer") : L("editor.choose_sample", "Choose a sample unit");
        var actors = World.world.units.getSimpleList().Where(actor =>
            actor?.data != null && actor.isAlive() && (!killer || actor != _sampleActor));
        void Pick(Actor actor, long id)
        {
            Actor current = actor?.data?.id == id ? actor : null;
            if (killer)
            {
                _sampleKiller = current;
                _sampleKillerID = id;
            }
            else
            {
                _sampleActor = current;
                _sampleActorID = id;
                if (_sampleKiller == current) _sampleKiller = null;
            }
            Enter(Page.Preview);
        }
        var options = actors.Select(actor =>
        {
            long id = actor.data.id;
            return new Option(ActorLabel(actor), () => Pick(actor, id), actor.asset?.getSpriteIcon());
        }).ToList();
        if (SelectedUnit.unit?.data != null && SelectedUnit.unit.isAlive() && (!killer || SelectedUnit.unit != _sampleActor))
        {
            Actor selected = SelectedUnit.unit;
            long id = selected.data.id;
            options.Insert(0, new Option($"{L("target.actor.selected", "Selected unit")} · {ActorLabel(selected)}",
                () => Pick(selected, id), selected.asset?.getSpriteIcon()));
        }
        if (killer) options.Insert(0, new Option(L("flow.no_killer", "No killer"), () => Pick(null, -1)));
        OpenPicker(title, options, Page.Preview);
    }

    private bool BuildSettlementSample(string triggerID)
    {
        bool isCity = WorldScriptEngine.UsesCityEvent(triggerID);
        if (triggerID == WorldScriptRegistry.TriggerDelayed)
        {
            string type = L("flow.sample_subject", "Sample subject");
            string KindName(string kind) => L("flow.kind." + (kind == "city" ? "City" : kind == "kingdom" ? "Kingdom" : "World"), kind);
            FieldAt(FieldRow(type), KindName(_sampleDelayedKind), 206, () =>
                OpenPicker(type, new[] { "world", "city", "kingdom" }.Select(kind =>
                    new Option(KindName(kind), () =>
                    {
                        if (_sampleDelayedKind != kind) _sampleSettlementID = null;
                        _sampleDelayedKind = kind;
                        Enter(Page.Preview);
                    })), Page.Preview));
            if (_sampleDelayedKind == "world") return true;
            isCity = _sampleDelayedKind == "city";
        }
        string label = isCity ? L("flow.sample_city", "Sample village") : L("flow.sample_kingdom", "Sample kingdom");
        string kind = isCity ? "city" : "kingdom";
        IMetaObject sample = _sampleSettlementID is long id ? WorldScriptAffiliations.Find(kind, id) : null;
        FieldAt(FieldRow(label), sample?.name ?? L("value.unset", "Unset"), 206,
            () => ChooseSettlement(label, kind, picked => _sampleSettlementID = picked));
        if (sample == null) return false;
        if (triggerID == WorldScriptRegistry.TriggerCityKingdomChanged)
        {
            string before = L("flow.sample_old_kingdom", "Previous kingdom");
            var previous = _sampleOldKingdomID is long oldID ? World.world.kingdoms.get(oldID) : null;
            FieldAt(FieldRow(before), previous?.name ?? L("value.unset", "Unset"), 206,
                () => ChooseSettlement(before, "kingdom", picked => _sampleOldKingdomID = picked));
        }
        return true;
    }

    private void ChooseSettlement(string title, string kind, Action<long> onPicked) =>
        OpenPicker(title, WorldScriptAffiliations.List(kind)
            .Where(item => item?.getMetaData() != null && item.isAlive())
            .OrderBy(item => item.name)
            .Select(item => new Option(NamedSample(item.name, item.getID()),
                () => { onPicked(item.getID()); Enter(Page.Preview); }, AffiliationIcon(item))), Page.Preview);

    private WorldScriptEvent SettlementSample(string triggerID)
    {
        if (triggerID == WorldScriptRegistry.TriggerDelayed && _sampleDelayedKind == "world")
            return new WorldScriptEvent(null, null, null, null, null, null, null);
        if (_sampleSettlementID is not long id || World.world == null) return null;
        bool citySample = triggerID == WorldScriptRegistry.TriggerDelayed ? _sampleDelayedKind == "city"
            : WorldScriptEngine.UsesCityEvent(triggerID);
        if (triggerID == WorldScriptRegistry.TriggerCityThreshold && World.world.cities.get(id) is City observed)
        {
            var values = Draft.TriggerCondition.Parameters;
            int count = WorldScriptRegistry.GetString(values, "threshold_metric", "stock") == "population"
                ? observed.getPopulationPeople() : observed.getResourcesAmount(WorldScriptRegistry.GetString(values, "resource_id", "wood"));
            return new WorldScriptEvent(observed, observed.kingdom, null, null, null, null, observed.getTile(), value: count);
        }
        return citySample
            ? World.world.cities.get(id) is City city
                ? WorldScriptEngine.VillageEvent(city, _sampleOldKingdomID is long oldID ? World.world.kingdoms.get(oldID) : null) : null
            : World.world.kingdoms.get(id) is Kingdom kingdom ? WorldScriptEngine.RulerEvent(kingdom) : null;
    }

    private void BeginMapSelection(Action<WorldTile> apply)
    {
        if (World.world == null) return;
        _mapSelection = apply;
        _mapSelectionWorld = World.world.map_stats;
        _mapSelectionFrame = Time.frameCount;
        CloseToMap();
        WorldTip.showNow(L("editor.map_instruction", "Click a tile on the map. Esc cancels."), false, "top", 5f);
    }

    internal bool HandleMapSelection(PlayerControl controls)
    {
        if (_mapSelection == null) return true;
        if (World.world?.map_stats != _mapSelectionWorld || SmoothLoader.isLoading())
        {
            _mapSelection = null;
            _mapSelectionWorld = null;
            return true;
        }
        if (Time.frameCount == _mapSelectionFrame) return false;
        bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1);
        if (!cancel && (!Input.GetMouseButtonDown(0) || controls.isOverUI(pCheckTimeout: false))) return false;
        WorldTile tile = cancel ? null : controls.getMouseTilePos();
        if (!cancel && tile == null) return false;
        Action<WorldTile> apply = _mapSelection;
        _mapSelection = null;
        _mapSelectionWorld = null;
        if (tile != null) apply(tile);
        ScrollWindow.showWindow(WindowId, pSkipAnimation: true);
        return false;
    }

    private static string ErrorLabel(string error) => error switch
    {
        "Add at least one enabled action" => L("editor.no_actions", error),
        "Enter a script name" => L("editor.name_required", error),
        "No world is loaded" => L("editor.no_world", error),
        "Target query returned no targets" => L("editor.no_targets", error),
        "Order is incompatible with the query target type" => L("flow.invalid_order", error),
        "Grouping requires actor targets" => L("flow.invalid_group", error),
        "Choose a distance origin" => L("flow.missing_origin", error),
        "The event actor is no longer a living target" => L("flow.dead_target", error),
        "Choose an actor to supply the event context" => L("editor.sample_required", error),
        "Targets changed since confirmation. Preview again before running." => L("editor.changed_targets", error),
        _ when error.EndsWith("This scope requires a matching actor event", StringComparison.Ordinal) => L("flow.scope_event_error", error),
        _ when error.EndsWith("requires an actor event", StringComparison.Ordinal) => L("flow.invalid_condition", error),
        _ when error.Contains("Choose a target source with available output") => L("flow.invalid_source", error),
        _ when error.EndsWith("Choose a grouped step before reporting its results", StringComparison.Ordinal) =>
            L("relation.report_requires_groups", "Choose a grouped step before reporting its results."),
        _ when error.EndsWith("Choose a valid step result condition", StringComparison.Ordinal) =>
            L("flow.invalid_result_condition", "Choose a valid step result condition."),
        _ when error.EndsWith("Enter a state key", StringComparison.Ordinal) =>
            L("validation.state_key", "Enter a state key."),
        _ when error.EndsWith("Choose a follow-up script", StringComparison.Ordinal) =>
            L("validation.followup", "Choose a follow-up script."),
        _ when error.EndsWith("Choose a positive delay", StringComparison.Ordinal) =>
            L("validation.delay", "Choose a positive delay."),
        _ when error.EndsWith("Choose a resource", StringComparison.Ordinal) =>
            L("validation.resource", "Choose a resource."),
        _ when error.EndsWith("Choose a building", StringComparison.Ordinal) =>
            L("validation.building", "Choose a building."),
        _ when error.EndsWith("Enter a new name", StringComparison.Ordinal) =>
            L("validation.new_name", "Enter a new name."),
        _ when error.EndsWith("Enter a phase name", StringComparison.Ordinal) =>
            L("validation.phase", "Enter a phase name."),
        "Batch jobs require independent per-target steps on the main query" =>
            L("validation.batch", "Batch jobs require independent per-target steps on the main query."),
        "Apply script changes before queueing a batch" =>
            L("validation.batch_save", "Apply script changes before queueing a batch."),
        "Follow-up is outside this package" => L("packages.external_follow_up", "Follow-up is outside this package."),
        "Set valid low and high thresholds and a sampling interval" =>
            L("validation.threshold", "Set valid low and high thresholds and a sampling interval."),
        _ when error.EndsWith("Choose a relation for the source object", StringComparison.Ordinal) =>
            L("relation.invalid", "Choose a relation for the source object."),
        _ when error.EndsWith("Only a village relation can be followed by one more relation", StringComparison.Ordinal) =>
            L("relation.invalid_chain", "Continue from a village with one more relation."),
        _ when error.EndsWith("The group limit must be nonnegative", StringComparison.Ordinal) =>
            L("relation.invalid_limit", "The group limit must be nonnegative."),
        _ when error.Contains("Invalid number source for") => L("flow.invalid_number", error),
        _ when error.EndsWith("Enter a history message", StringComparison.Ordinal) => L("log.message_required", "Enter a history message."),
        _ when error.EndsWith("Choose an available history icon", StringComparison.Ordinal) => L("log.icon_required", "Choose an available history icon."),
        _ when error.EndsWith("Choose an affiliation", StringComparison.Ordinal) => L("affiliation.choose", "Choose an affiliation."),
        _ when error.Contains("Unavailable affiliation") => L("affiliation.choose_available", "Choose an affiliation that exists in this world."),
        _ when error.EndsWith("Add a condition to the branch", StringComparison.Ordinal) => L("branch.condition_required", "Add a condition to the branch."),
        _ when error.EndsWith("Rebind the template affiliation", StringComparison.Ordinal) =>
            L("templates.rebind_required", "Choose the template affiliations for this world."),
        _ => error
    };
}
