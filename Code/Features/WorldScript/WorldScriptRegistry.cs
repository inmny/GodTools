using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

[Flags]
public enum ScriptTargetKind
{
    None = 0,
    World = 1,
    Actor = 2,
    Tile = 4,
    City = 8,
    Kingdom = 16,
    Any = World | Actor | Tile | City | Kingdom
}

public enum ScriptParameterType
{
    Text,
    Integer,
    Number,
    Boolean,
    Choice
}

public sealed class ScriptParameterDefinition
{
    public string Key;
    public string Name;
    public ScriptParameterType Type;
    public JToken DefaultValue;
    public string[] Choices;
}

public sealed class WorldScriptTarget
{
    public ScriptTargetKind Kind;
    public Actor Actor;
    public long ActorID;
    public WorldTile Tile;
    public IMetaObject Meta;
    public long MetaID;
    internal long EntityID => Kind == ScriptTargetKind.Actor ? ActorID : MetaID;

    public static WorldScriptTarget ForWorld() => new() { Kind = ScriptTargetKind.World };
    public static WorldScriptTarget ForActor(Actor actor) =>
        new() { Kind = ScriptTargetKind.Actor, Actor = actor, ActorID = actor?.data?.id ?? -1 };
    public static WorldScriptTarget ForTile(WorldTile tile) => new() { Kind = ScriptTargetKind.Tile, Tile = tile };
    public static WorldScriptTarget ForCity(City city) => new() { Kind = ScriptTargetKind.City, Meta = city, MetaID = city.getID() };
    public static WorldScriptTarget ForKingdom(Kingdom kingdom) => new()
        { Kind = ScriptTargetKind.Kingdom, Meta = kingdom, MetaID = kingdom.getID() };
}

public sealed class WorldScriptExecutionContext
{
    public string TriggerID;
    public TriggerConditionType Trigger;
    public Actor EventActor;
    public WorldScriptEvent Event;
    public double WorldTime;
    public string EraID;
    public ScriptInstance Script;
    public int SpawnCount;
    public int ActionCount;
    public int CompletedActionCount;
    public int ActionBudget = 1000;
    public int MainMatchCount;
    public int MessageTargetCount;
    public string GroupName;
    internal readonly List<ScriptStepExecution> StepDetails = new();
    internal int OmittedSteps;
    internal string EmittedMessage;
    internal string SkippedReason;
    internal long? ChangeAmount;
    internal bool HadSkippedActions;
    internal WorldScriptTarget GroupTarget;
    internal int CurrentStep;
    internal List<ScriptLimitHit> Limits;
    internal bool InspectPreview;
    internal WorldScriptPreviewResult Preview;
    internal ScriptWriteRecord WriteRecord;
    internal bool HadUnmetBranch;
    public IReadOnlyList<WorldScriptTarget> BatchTargets { get; internal set; } = Array.Empty<WorldScriptTarget>();
    internal List<WorldScriptTarget> StepOutput;

    public void RecordOutput(WorldScriptTarget target)
    {
        if (target != null) StepOutput?.Add(target);
    }

    public void RecordChange(long amount) => ChangeAmount = (ChangeAmount ?? 0) + amount;

    public void SkipAction(string reason) => SkippedReason = reason ?? "no_change";

    public bool TryTakeAction()
    {
        if (ActionCount >= ActionBudget)
        {
            NoteLimit("actions");
            return false;
        }
        ActionCount++;
        return true;
    }

    public int TakeSpawnCount(int requested)
    {
        int count = SpawnAllowance(requested);
        SpawnCount += count;
        return count;
    }

    internal int SpawnAllowance(int requested)
    {
        int count = Math.Min(Math.Max(requested, 0), Math.Max(Script.MaxSpawns - SpawnCount, 0));
        if (requested > count) NoteLimit("spawns");
        return count;
    }

    internal void NoteLimit(string kind)
    {
        Limits ??= new List<ScriptLimitHit>();
        if (Limits.Count < 40 && !Limits.Any(hit => hit.Kind == kind && hit.Step == CurrentStep))
            Limits.Add(new ScriptLimitHit { Kind = kind, Step = CurrentStep });
    }

    internal void NotePreviewDecision(WorldScriptTarget target, WorldScriptTarget group, string reason, ConditionNode rule = null)
    {
        if (Preview == null) return;
        if (Preview.Decisions.Count == 80)
        {
            Preview.OmittedDecisions++;
            return;
        }
        Preview.Decisions.Add(new ScriptPreviewDecision
        {
            Subject = ScriptTargetSample.From(target), Group = group == null ? null : ScriptTargetSample.From(group),
            Step = CurrentStep, Reason = reason, Rule = rule
        });
    }

    internal void RecordSpawn(Actor actor)
    {
        SpawnCount++;
        WorldScriptEngine.SuppressNextActorStatsUpdate(actor);
        RecordOutput(WorldScriptTarget.ForActor(actor));
    }
}

public sealed class TargetDefinition
{
    public string ID;
    public string Name;
    public ScriptTargetKind Kind;
    public ScriptParameterDefinition[] Parameters;
    public Func<WorldScriptExecutionContext, JObject, IEnumerable<WorldScriptTarget>> Resolve;
    public Func<WorldScriptExecutionContext, JObject, WorldTile> GetOrigin;
    public bool RequiresActorEvent;
    public bool RequiresDeathEvent;
    public bool RequiresCityEvent;
    public bool RequiresKingdomEvent;
    public bool RequiresPositionEvent;
}

public sealed class TriggerDefinition
{
    public string ID;
    public string Name;
    public ScriptParameterDefinition[] Parameters;
    public TriggerConditionType Type;
}

public sealed class ConditionDefinition
{
    public string ID;
    public string Name;
    public ScriptTargetKind AllowedTargets;
    public bool RequiresActorEvent;
    public ScriptParameterDefinition[] Parameters;
    public Func<WorldScriptExecutionContext, WorldScriptTarget, JObject, bool> Evaluate;
}

public sealed class ActionDefinition
{
    public string ID;
    public string Name;
    public ScriptTargetKind AllowedTargets;
    public bool Dangerous;
    public bool ChangesActorStats;
    public ScriptTargetKind OutputKind;
    public ScriptParameterDefinition[] Parameters;
    public Action<WorldScriptExecutionContext, WorldScriptTarget, JObject> Execute;
    public Func<WorldScriptTarget, JObject, int?> EstimateChange;
}

public static partial class WorldScriptRegistry
{
    public const string TriggerManual = "manual";
    public const string TriggerWorldLoaded = "world.loaded";
    public const string TriggerInterval = "world.interval";
    public const string TriggerWorldTimeReached = "world.time_reached";
    public const string TriggerEraChanged = "world.era_changed";
    public const string TriggerActorCreated = "actor.created";
    public const string TriggerActorDied = "actor.died";
    public const string TriggerActorStatsUpdated = "actor.stats_updated";
    public const string TriggerCityKingdomChanged = "city.kingdom_changed";
    public const string TriggerKingdomKingChanged = "kingdom.king_changed";
    public const string TriggerDelayed = "script.delayed";
    public const string TriggerWarEnded = "war.ended";
    public const string TriggerCityThreshold = "city.threshold";
    public const string TargetWorld = "world";
    public const string TargetActorAll = "actor.all";
    public const string TargetActorEvent = "actor.event";
    public const string TargetActorSelected = "actor.selected";
    public const string TargetActorMouseRadius = "actor.mouse_radius";
    public const string TargetActorPositionRadius = "actor.position_radius";
    public const string TargetActorKingdom = "actor.kingdom";
    public const string TargetActorCulture = "actor.culture";
    public const string TargetActorClan = "actor.clan";
    public const string TargetActorLanguage = "actor.language";
    public const string TargetActorSubspecies = "actor.subspecies";
    public const string TargetActorAsset = "actor.asset";
    public const string TargetTileMouseRadius = "tile.mouse_radius";
    public const string TargetTilePositionRadius = "tile.position_radius";
    public const string TargetActorEventRadius = "actor.event_radius";
    public const string TargetTileEventRadius = "tile.event_radius";
    public const string TargetActorKiller = "actor.killer";
    public const string TargetCityAll = "city.all";
    public const string TargetCityByID = "city.by_id";
    public const string TargetCityKingdom = "city.kingdom";
    public const string TargetKingdomAll = "kingdom.all";
    public const string TargetKingdomByID = "kingdom.by_id";
    public const string TargetEventCity = "city.event";
    public const string TargetEventKingdom = "kingdom.event";

    private static readonly object RegistrationLock = new();
    private static readonly List<TriggerDefinition> MutableTriggers = new();
    private static readonly List<TargetDefinition> MutableTargets = new();
    private static readonly List<ConditionDefinition> MutableConditions = new();
    private static readonly List<ActionDefinition> MutableActions = new();
    public static IReadOnlyList<TriggerDefinition> Triggers
    {
        get { lock (RegistrationLock) return MutableTriggers.ToArray(); }
    }
    public static IReadOnlyList<TargetDefinition> Targets
    {
        get { lock (RegistrationLock) return MutableTargets.ToArray(); }
    }
    public static IReadOnlyList<ConditionDefinition> Conditions
    {
        get { lock (RegistrationLock) return MutableConditions.ToArray(); }
    }
    public static IReadOnlyList<ActionDefinition> Actions
    {
        get { lock (RegistrationLock) return MutableActions.ToArray(); }
    }

    private static readonly Dictionary<string, TriggerDefinition> TriggerByID = new();
    private static readonly Dictionary<string, TargetDefinition> TargetByID = new();
    private static readonly Dictionary<string, ConditionDefinition> ConditionByID = new();
    private static readonly Dictionary<string, ActionDefinition> ActionByID = new();

    private static readonly ScriptParameterDefinition[] RadiusParameters =
    {
        Integer("radius", "Radius", 5)
    };

    private static readonly ScriptParameterDefinition[] PositionRadiusParameters =
    {
        Integer("x", "X", 0),
        Integer("y", "Y", 0),
        Integer("radius", "Radius", 5)
    };

    static WorldScriptRegistry()
    {
        RegisterTriggers();
        RegisterRegions();
        RegisterTargets();
        RegisterConditions();
        RegisterActions();
        RegisterSettlementActions();
        RegisterStateActions();
    }

    public static TargetDefinition GetTarget(string id) => TryGetTarget(id, out var value) ? value : null;
    public static ConditionDefinition GetCondition(string id) => TryGetCondition(id, out var value) ? value : null;
    public static ActionDefinition GetAction(string id) => TryGetAction(id, out var value) ? value : null;
    public static TriggerDefinition GetTrigger(TriggerConditionType type)
    {
        lock (RegistrationLock) return MutableTriggers.FirstOrDefault(value => value.Type == type);
    }
    public static TriggerDefinition GetTrigger(string id) => TryGetTrigger(id, out var value) ? value : null;
    public static TriggerConditionType GetTriggerType(string id) => GetTrigger(id)?.Type ?? TriggerConditionType.Interval;
    public static string GetTriggerID(TriggerConditionType type) => GetTrigger(type)?.ID ?? TriggerInterval;
    public static bool TryGetTrigger(string id, out TriggerDefinition value)
    {
        lock (RegistrationLock) return TriggerByID.TryGetValue(id ?? string.Empty, out value);
    }
    public static bool TryGetTarget(string id, out TargetDefinition value)
    {
        lock (RegistrationLock) return TargetByID.TryGetValue(id ?? string.Empty, out value);
    }
    public static bool TryGetCondition(string id, out ConditionDefinition value)
    {
        lock (RegistrationLock) return ConditionByID.TryGetValue(id ?? string.Empty, out value);
    }
    public static bool TryGetAction(string id, out ActionDefinition value)
    {
        lock (RegistrationLock) return ActionByID.TryGetValue(id ?? string.Empty, out value);
    }

    public static bool RegisterTrigger(TriggerDefinition definition) =>
        RegisterDefinition(definition?.ID, definition, TriggerByID, MutableTriggers, IsValid);
    public static bool RegisterTarget(TargetDefinition definition) =>
        RegisterDefinition(definition?.ID, definition, TargetByID, MutableTargets, IsValid);
    public static bool RegisterCondition(ConditionDefinition definition) =>
        RegisterDefinition(definition?.ID, definition, ConditionByID, MutableConditions, IsValid);
    public static bool RegisterAction(ActionDefinition definition) =>
        RegisterDefinition(definition?.ID, definition, ActionByID, MutableActions, IsValid);

    private static bool RegisterDefinition<T>(string id, T definition, IDictionary<string, T> index,
        ICollection<T> ordered, Func<T, bool> validate) where T : class
    {
        if (definition == null || string.IsNullOrWhiteSpace(id) || id != id.Trim() || !validate(definition)) return false;
        lock (RegistrationLock)
        {
            if (index.ContainsKey(id)) return false;
            index.Add(id, definition);
            ordered.Add(definition);
            return true;
        }
    }

    private static bool ValidParameters(ScriptParameterDefinition[] definitions)
    {
        if (definitions == null) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return definitions.All(value => value != null && !string.IsNullOrWhiteSpace(value.Key) &&
                                        value.Key == value.Key.Trim() && keys.Add(value.Key) &&
                                        (value.Type != ScriptParameterType.Choice ||
                                         value.Choices != null && value.Choices.Length > 0));
    }

    private static bool IsValid(TriggerDefinition value) =>
        !string.IsNullOrWhiteSpace(value.Name) && ValidParameters(value.Parameters);
    private static bool IsValid(TargetDefinition value) =>
        !string.IsNullOrWhiteSpace(value.Name) && value.Kind != ScriptTargetKind.None &&
        ValidParameters(value.Parameters) && value.Resolve != null;
    private static bool IsValid(ConditionDefinition value) =>
        !string.IsNullOrWhiteSpace(value.Name) && value.AllowedTargets != ScriptTargetKind.None &&
        ValidParameters(value.Parameters) && value.Evaluate != null;
    private static bool IsValid(ActionDefinition value) =>
        !string.IsNullOrWhiteSpace(value.Name) && value.AllowedTargets != ScriptTargetKind.None &&
        ValidParameters(value.Parameters) && value.Execute != null;

    public static IReadOnlyList<string> ValidateParameters(JObject values, ScriptParameterDefinition[] definitions)
    {
        var errors = new List<string>();
        values ??= new JObject();
        foreach (var definition in definitions ?? EmptyParameters)
        {
            JToken token = values[definition.Key];
            if (token == null || token.Type == JTokenType.Null) continue;
            bool valid = definition.Type switch
            {
                ScriptParameterType.Integer => int.TryParse(ValueText(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                ScriptParameterType.Number => double.TryParse(ValueText(token), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                                              && !double.IsNaN(number) && !double.IsInfinity(number),
                ScriptParameterType.Boolean => token.Type == JTokenType.Boolean || bool.TryParse(token.ToString(), out _),
                ScriptParameterType.Choice => definition.Choices != null && definition.Choices.Contains(token.ToString()),
                _ => token.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Boolean
            };
            if (!valid) errors.Add($"Invalid parameter '{definition.Key}'");
        }
        return errors;
    }

    private static string ValueText(JToken token) => token is JValue { Value: IFormattable scalar }
        ? scalar.ToString(null, CultureInfo.InvariantCulture) : token.ToString();

    internal static IEnumerable<string> ValidateReferences(string definitionID, JObject values)
    {
        string assetID = GetString(values, "asset_id");
        string traitID = GetString(values, "trait_id");
        string statusID = GetString(values, "status_id");
        if (definitionID is "actor.asset" or "tile.actor.spawn" &&
            !AssetManager.actor_library.dict.ContainsKey(assetID))
            yield return $"Unknown unit type '{assetID}'";
        if (definitionID is "actor.trait" or "actor.trait.add" or "actor.trait.remove" &&
            !AssetManager.traits.dict.ContainsKey(traitID))
            yield return $"Unknown trait '{traitID}'";
        if (definitionID is "actor.status" or "actor.status.add" or "actor.status.remove" &&
            !AssetManager.status.dict.ContainsKey(statusID))
            yield return $"Unknown status '{statusID}'";
        if (definitionID is "city.resources" or "city.resources.change" or "city.resources.scenario" &&
            !AssetManager.resources.dict.ContainsKey(GetString(values, "resource_id", "wood")))
            yield return "Choose a resource";
        if (definitionID == "tile.building.place" && !AssetManager.buildings.dict.ContainsKey(GetString(values, "building_id")))
            yield return "Choose a building";
        if (definitionID == "settlement.rename" && string.IsNullOrWhiteSpace(GetString(values, "new_name")))
            yield return "Enter a new name";
        if (definitionID == "scenario.phase.set" && string.IsNullOrWhiteSpace(GetString(values, "phase")))
            yield return "Enter a phase name";
        if (definitionID is "state.number" or "state.set" or "state.add" &&
            string.IsNullOrWhiteSpace(GetString(values, "state_key")))
            yield return "Enter a state key";
        if (definitionID == "script.schedule")
        {
            string id = GetString(values, "script_id");
            if (WorldScriptEngine.FindScriptForValidation(id)?.TriggerCondition?.TriggerID != TriggerDelayed)
                yield return "Choose a follow-up script";
            if (GetNumber(values, "delay") < 0.01d) yield return "Choose a positive delay";
        }
        string affiliation = WorldScriptAffiliations.Kind(definitionID);
        if (affiliation != null)
        {
            if (WorldScriptTemplates.HasBinding(values))
            {
                yield return "Rebind the template affiliation";
                yield break;
            }
            string id = GetString(values, "id");
            if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long objectID))
                yield return "Choose an affiliation";
            else if (World.world != null && (affiliation is "kingdom" or "city" or "region" || WorldScriptAffiliations.IsSetter(definitionID)) &&
                !(objectID == -1 && WorldScriptAffiliations.CanClear(definitionID)) &&
                !WorldScriptAffiliations.Exists(affiliation, objectID))
                yield return $"Unavailable affiliation '{id}'";
        }
        if (definitionID == WorldScriptHistory.ActionID)
        {
            if (string.IsNullOrWhiteSpace(GetString(values, "message")))
                yield return "Enter a history message";
            if (!WorldScriptHistory.HasIcon(GetString(values, "log_icon", WorldScriptHistory.DefaultIcon)))
                yield return "Choose an available history icon";
        }
    }

    public static void ApplyDefaults(JObject values, ScriptParameterDefinition[] definitions)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        foreach (var definition in definitions ?? EmptyParameters)
        {
            if (values[definition.Key] == null && definition.DefaultValue != null)
            {
                values[definition.Key] = definition.DefaultValue.DeepClone();
            }
        }
    }

    public static string GetString(JObject values, string key, string defaultValue = "")
    {
        return values?[key]?.ToString() ?? defaultValue;
    }

    public static int GetInt(JObject values, string key, int defaultValue = 0)
    {
        JToken token = values?[key];
        if (token == null) return defaultValue;
        return token.Type == JTokenType.Integer ? token.Value<int>() : int.Parse(token.ToString(), CultureInfo.InvariantCulture);
    }

    public static long GetLong(JObject values, string key, long defaultValue = 0L)
    {
        JToken token = values?[key];
        if (token == null) return defaultValue;
        return token.Type == JTokenType.Integer
            ? token.Value<long>()
            : long.Parse(token.ToString(), CultureInfo.InvariantCulture);
    }

    public static double GetNumber(JObject values, string key, double defaultValue = 0d)
    {
        JToken token = values?[key];
        if (token == null) return defaultValue;
        return token.Type is JTokenType.Float or JTokenType.Integer
            ? token.Value<double>()
            : double.Parse(token.ToString(), CultureInfo.InvariantCulture);
    }

    public static bool GetBool(JObject values, string key, bool defaultValue = false)
    {
        JToken token = values?[key];
        if (token == null) return defaultValue;
        return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.Parse(token.ToString());
    }

    private static void RegisterTriggers()
    {
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerManual, Type = TriggerConditionType.Manual, Name = "Manual only",
            Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerWorldLoaded, Type = TriggerConditionType.WorldLoaded,
            Name = "World loaded",
            Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerInterval, Type = TriggerConditionType.Interval,
            Name = "Interval",
            Parameters = new[] { Number("interval", "Interval", 60d) }
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerWorldTimeReached, Type = TriggerConditionType.WorldTimeReached,
            Name = "World time reached",
            Parameters = new[]
            {
                Number("time", "World time", 0d),
                Number("repeat_interval", "Repeat interval", 0d)
            }
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerEraChanged, Type = TriggerConditionType.EraChanged,
            Name = "Era changed",
            Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerActorCreated, Type = TriggerConditionType.ActorCreated,
            Name = "Actor created",
            Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerActorDied, Type = TriggerConditionType.ActorDied,
            Name = "Actor died",
            Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerActorStatsUpdated, Type = TriggerConditionType.ActorStatsUpdated,
            Name = "Actor stats updated",
            Parameters = EmptyParameters
        });
        var perObjectCooldown = new[] { new ScriptParameterDefinition { Key = "per_object_cooldown",
            Name = "Cooldown per event object", Type = ScriptParameterType.Boolean, DefaultValue = new JValue(false) } };
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerCityKingdomChanged, Type = TriggerConditionType.CityKingdomChanged,
            Name = "Village changes kingdom", Parameters = perObjectCooldown
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerKingdomKingChanged, Type = TriggerConditionType.KingdomKingChanged,
            Name = "Kingdom changes ruler", Parameters = perObjectCooldown
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerDelayed, Type = TriggerConditionType.Delayed,
            Name = "Scheduled follow-up", Parameters = EmptyParameters
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerWarEnded, Type = TriggerConditionType.WarEnded,
            Name = "War ends", Parameters = perObjectCooldown
        });
        RegisterTrigger(new TriggerDefinition
        {
            ID = TriggerCityThreshold, Type = TriggerConditionType.CityThreshold,
            Name = "Village crosses threshold", Parameters = new[]
            {
                Choice("threshold_metric", "Metric", "stock", "stock", "population"),
                Text("resource_id", "Resource", "wood"),
                Integer("low_threshold", "Low threshold", 20),
                Integer("high_threshold", "Recovery threshold", 40),
                Choice("threshold_direction", "When to trigger", "low", "low", "recovered"),
                Number("sample_interval", "Sampling interval", 5d),
                perObjectCooldown[0]
            }
        });
    }

    private static void RegisterTargets()
    {
        RegisterSettlementTargets();
        AddTarget(TargetWorld, "World", ScriptTargetKind.World, EmptyParameters,
            (_, _) => new[] { WorldScriptTarget.ForWorld() });
        AddTarget(TargetActorAll, "All actors", ScriptTargetKind.Actor, EmptyParameters,
            (_, _) => Actors().Where(actor => actor.isAlive()).Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorEvent, "Event actor", ScriptTargetKind.Actor, EmptyParameters,
            (context, _) => context.EventActor == null
                ? Enumerable.Empty<WorldScriptTarget>()
                : new[] { WorldScriptTarget.ForActor(context.EventActor) }, requiresActorEvent: true);
        AddTarget(TargetActorKiller, "Killer", ScriptTargetKind.Actor, EmptyParameters,
            (context, _) => context.Event?.Killer is Actor killer
                ? new[] { WorldScriptTarget.ForActor(killer) }
                : Enumerable.Empty<WorldScriptTarget>(), requiresDeathEvent: true);
        AddTarget(TargetActorSelected, "Selected actor", ScriptTargetKind.Actor, EmptyParameters,
            (_, _) => SelectedUnit.unit == null
                ? Enumerable.Empty<WorldScriptTarget>()
                : new[] { WorldScriptTarget.ForActor(SelectedUnit.unit) });
        AddTarget(TargetActorMouseRadius, "Actors near cursor", ScriptTargetKind.Actor, RadiusParameters,
            (_, values) => ActorsNear(World.world.getMouseTilePosCachedFrame(), Math.Max(GetInt(values, "radius", 5), 0)),
            (_, _) => World.world.getMouseTilePosCachedFrame());
        AddTarget(TargetActorPositionRadius, "Actors near position", ScriptTargetKind.Actor, PositionRadiusParameters,
            (_, values) => ActorsNear(GetTile(values), Math.Max(GetInt(values, "radius", 5), 0)), (_, values) => GetTile(values));
        AddTarget(TargetActorEventRadius, "Actors near event position", ScriptTargetKind.Actor, RadiusParameters,
            (context, values) => ActorsNear(context.Event?.Tile, Math.Max(GetInt(values, "radius", 5), 0)),
            (context, _) => context.Event?.Tile, requiresActorEvent: true);
        AddTarget(TargetActorKingdom, "Actors in kingdom", ScriptTargetKind.Actor,
            new[] { Text("id", "Kingdom ID", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.kingdom?.data.id.ToString() == GetString(values, "id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorCulture, "Actors in culture", ScriptTargetKind.Actor,
            new[] { Text("id", "Culture ID", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.culture?.data.id.ToString() == GetString(values, "id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorClan, "Actors in clan", ScriptTargetKind.Actor,
            new[] { Text("id", "Clan ID", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.clan?.data.id.ToString() == GetString(values, "id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorLanguage, "Actors using language", ScriptTargetKind.Actor,
            new[] { Text("id", "Language ID", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.language?.data.id.ToString() == GetString(values, "id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorSubspecies, "Actors in subspecies", ScriptTargetKind.Actor,
            new[] { Text("id", "Subspecies ID", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.subspecies?.data.id.ToString() == GetString(values, "id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetActorAsset, "Actors by asset", ScriptTargetKind.Actor,
            new[] { Text("asset_id", "Actor asset", "") },
            (_, values) => Actors().Where(actor => actor.isAlive() && actor.asset?.id == GetString(values, "asset_id"))
                .Select(WorldScriptTarget.ForActor));
        AddTarget(TargetTileMouseRadius, "Tiles near cursor", ScriptTargetKind.Tile, RadiusParameters,
            (_, values) => TilesNear(World.world.getMouseTilePosCachedFrame(), Math.Max(GetInt(values, "radius", 5), 0)),
            (_, _) => World.world.getMouseTilePosCachedFrame());
        AddTarget(TargetTilePositionRadius, "Tiles near position", ScriptTargetKind.Tile, PositionRadiusParameters,
            (_, values) => TilesNear(GetTile(values), Math.Max(GetInt(values, "radius", 5), 0)), (_, values) => GetTile(values));
        AddTarget(TargetTileEventRadius, "Tiles near event position", ScriptTargetKind.Tile, RadiusParameters,
            (context, values) => TilesNear(context.Event?.Tile, Math.Max(GetInt(values, "radius", 5), 0)),
            (context, _) => context.Event?.Tile, requiresPositionEvent: true);
    }

    private static void RegisterSettlementTargets()
    {
        IEnumerable<City> Cities() => World.world.cities.Where(city => city?.data != null && city.isAlive());
        AddTarget(TargetCityAll, "All villages", ScriptTargetKind.City, EmptyParameters,
            (_, _) => Cities().Select(WorldScriptTarget.ForCity));
        AddTarget(TargetCityByID, "Specific village", ScriptTargetKind.City, new[] { Text("id", "Village ID", "") },
            (_, values) => World.world.cities.get(GetLong(values, "id", -1)) is City city && city.isAlive()
                ? new[] { WorldScriptTarget.ForCity(city) } : Enumerable.Empty<WorldScriptTarget>());
        AddTarget(TargetCityKingdom, "Villages in kingdom", ScriptTargetKind.City, new[] { Text("id", "Kingdom ID", "") },
            (_, values) => Cities().Where(city => city.kingdom?.data?.id == GetLong(values, "id", -1))
                .Select(WorldScriptTarget.ForCity));
        AddTarget(TargetKingdomAll, "All kingdoms", ScriptTargetKind.Kingdom, EmptyParameters,
            (_, _) => World.world.kingdoms.Where(kingdom => kingdom?.data != null && kingdom.isAlive())
                .Select(WorldScriptTarget.ForKingdom));
        AddTarget(TargetKingdomByID, "Specific kingdom", ScriptTargetKind.Kingdom, new[] { Text("id", "Kingdom ID", "") },
            (_, values) => World.world.kingdoms.get(GetLong(values, "id", -1)) is Kingdom kingdom && kingdom.isAlive()
                ? new[] { WorldScriptTarget.ForKingdom(kingdom) } : Enumerable.Empty<WorldScriptTarget>());
        RegisterTarget(new TargetDefinition
        {
            ID = TargetEventCity, Name = "Event village", Kind = ScriptTargetKind.City, RequiresCityEvent = true,
            Parameters = EmptyParameters, GetOrigin = (context, _) => context.Event?.Tile,
            Resolve = (context, _) => context.Event?.City is City city
                ? new[] { WorldScriptTarget.ForCity(city) } : Enumerable.Empty<WorldScriptTarget>()
        });
        RegisterTarget(new TargetDefinition
        {
            ID = TargetEventKingdom, Name = "Event kingdom", Kind = ScriptTargetKind.Kingdom, RequiresKingdomEvent = true,
            Parameters = EmptyParameters, GetOrigin = (context, _) => context.Event?.Tile,
            Resolve = (context, _) => context.Event?.Kingdom is Kingdom kingdom
                ? new[] { WorldScriptTarget.ForKingdom(kingdom) } : Enumerable.Empty<WorldScriptTarget>()
        });
    }

    private static void RegisterConditions()
    {
        var numeric = new[]
        {
            Choice("operator", "Operator", ">=", ">=", ">", "=", "!=", "<", "<="),
            Number("value", "Value", 0d)
        };
        AddCondition("city.population", "Village population", ScriptTargetKind.City, numeric,
            (_, target, values) => Compare(target.Meta.getPopulationPeople(), values));
        AddCondition("city.capital", "Village is a capital", ScriptTargetKind.City, EmptyParameters,
            (_, target, _) => target.Meta is City city && city.kingdom?.capital == city);
        AddCondition("city.kingdom", "Village kingdom", ScriptTargetKind.City,
            new[] { Text("id", "Kingdom ID", "") },
            (_, target, values) => target.Meta is City city && city.kingdom?.data?.id == GetLong(values, "id", -1));
        AddCondition("kingdom.population", "Kingdom population", ScriptTargetKind.Kingdom, numeric,
            (_, target, values) => Compare(target.Meta.getPopulationPeople(), values));
        AddCondition("kingdom.villages", "Kingdom village count", ScriptTargetKind.Kingdom, numeric,
            (_, target, values) => Compare(target.Meta.getCities().Count(), values));
        AddCondition("kingdom.has_king", "Kingdom has a living king", ScriptTargetKind.Kingdom, EmptyParameters,
            (_, target, _) => target.Meta is Kingdom kingdom && kingdom.king?.isAlive() == true);
        AddCondition("actor.alive", "Actor is alive", ScriptTargetKind.Actor, EmptyParameters,
            (_, target, _) => target.Actor.isAlive());
        AddCondition("actor.asset", "Actor asset", ScriptTargetKind.Actor,
            new[] { Text("asset_id", "Actor asset", "") },
            (_, target, values) => target.Actor.asset?.id == GetString(values, "asset_id"));
        AddCondition("actor.trait", "Actor has trait", ScriptTargetKind.Actor,
            new[] { Text("trait_id", "Trait ID", "") },
            (_, target, values) => target.Actor.hasTrait(GetString(values, "trait_id")));
        AddCondition("actor.status", "Actor has status", ScriptTargetKind.Actor,
            new[] { Text("status_id", "Status ID", "") },
            (_, target, values) => target.Actor.hasStatus(GetString(values, "status_id")));
        AddCondition("actor.age", "Actor age", ScriptTargetKind.Actor, numeric,
            (_, target, values) => Compare(target.Actor.getAge(), values));
        AddCondition("actor.health", "Actor health", ScriptTargetKind.Actor, numeric,
            (_, target, values) => Compare(target.Actor.getHealth(), values));
        AddCondition("actor.health_ratio", "Actor health ratio", ScriptTargetKind.Actor, numeric,
            (_, target, values) => Compare(target.Actor.getHealthRatio(), values));
        AddCondition("actor.level", "Actor level", ScriptTargetKind.Actor, numeric,
            (_, target, values) => Compare(target.Actor.data.level, values));
        AddCondition("actor.power", "Actor power", ScriptTargetKind.Actor, numeric,
            (_, target, values) => Compare(ConsistentCreaturePowerTop.CalculatePower(target.Actor), values));
        AddCondition("actor.kingdom", "Actor kingdom", ScriptTargetKind.Actor,
            new[] { Text("id", "Kingdom ID", "") },
            (_, target, values) => target.Actor.kingdom?.data.id.ToString() == GetString(values, "id"));
        AddCondition("actor.event_kingdom", "Same kingdom as event actor", ScriptTargetKind.Actor, EmptyParameters,
            (context, target, _) => context.Event?.KingdomID is long id && target.Actor.kingdom?.data?.id == id,
            requiresActorEvent: true);
        AddCondition("actor.culture", "Actor culture", ScriptTargetKind.Actor,
            new[] { Text("id", "Culture ID", "") },
            (_, target, values) => target.Actor.culture?.data.id.ToString() == GetString(values, "id"));
        AddCondition("actor.clan", "Actor clan", ScriptTargetKind.Actor,
            new[] { Text("id", "Clan ID", "") },
            (_, target, values) => target.Actor.clan?.data.id.ToString() == GetString(values, "id"));
        AddCondition("actor.language", "Actor language", ScriptTargetKind.Actor,
            new[] { Text("id", "Language ID", "") },
            (_, target, values) => target.Actor.language?.data.id.ToString() == GetString(values, "id"));
        AddCondition("actor.subspecies", "Actor subspecies", ScriptTargetKind.Actor,
            new[] { Text("id", "Subspecies ID", "") },
            (_, target, values) => target.Actor.subspecies?.data.id.ToString() == GetString(values, "id"));
        AddCondition("tile.main_type", "Main tile type", ScriptTargetKind.Tile,
            new[] { Text("tile_id", "Main tile ID", "") },
            (_, target, values) => target.Tile.main_type?.id == GetString(values, "tile_id"));
        AddCondition("tile.top_type", "Top tile type", ScriptTargetKind.Tile,
            new[] { Text("tile_id", "Top tile ID", "") },
            (_, target, values) => target.Tile.top_type?.id == GetString(values, "tile_id"));
        AddCondition("world.time", "World time", ScriptTargetKind.Any, numeric,
            (context, _, values) => Compare(context.Script?.Flow != null ? context.Event?.WorldTime ?? context.WorldTime
                : context.WorldTime, values));
        AddCondition("world.population", "World population", ScriptTargetKind.Any, numeric,
            (_, _, values) => Compare(World.world.getCivWorldPopulation(), values));
        AddCondition("world.era", "World era", ScriptTargetKind.Any,
            new[] { Text("era_id", "Era ID", "") },
            (context, _, values) => (context.Script?.Flow == null ? World.world.map_stats.world_age_id : context.EraID)
                == GetString(values, "era_id"));
    }

    private static void RegisterActions()
    {
        AddAction("actor.health.set", "Set health", ScriptTargetKind.Actor, false,
            new[] { Integer("value", "Health", 100) },
            (_, target, values) => target.Actor.setHealth(GetInt(values, "value", 100), true));
        AddAction("actor.health.change", "Change health", ScriptTargetKind.Actor, false,
            new[] { Integer("value", "Amount", 10) },
            (context, target, values) =>
            {
                int before = target.Actor.getHealth();
                target.Actor.changeHealth(GetInt(values, "value", 10));
                int change = target.Actor.getHealth() - before;
                if (change == 0) context.SkipAction("no_change");
                else context.RecordChange(change);
            });
        AddAction("actor.data.set", "Set actor value", ScriptTargetKind.Actor, false,
            new[]
            {
                Choice("field", "Field", "level", "age", "level", "nutrition", "happiness", "stamina", "mana", "kills"),
                Integer("value", "Value", 1)
            },
            (_, target, values) => SetActorData(target.Actor, GetString(values, "field", "level"), GetInt(values, "value", 1), false), true);
        AddAction("actor.data.add", "Add actor value", ScriptTargetKind.Actor, false,
            new[]
            {
                Choice("field", "Field", "level", "age", "level", "nutrition", "happiness", "stamina", "mana", "kills"),
                Integer("value", "Amount", 1)
            },
            (_, target, values) => SetActorData(target.Actor, GetString(values, "field", "level"), GetInt(values, "value", 1), true), true);
        AddAction("actor.trait.add", "Add trait", ScriptTargetKind.Actor, false,
            new[] { Text("trait_id", "Trait ID", "") },
            (_, target, values) => target.Actor.addTrait(GetString(values, "trait_id"), true), true);
        AddAction("actor.trait.remove", "Remove trait", ScriptTargetKind.Actor, false,
            new[] { Text("trait_id", "Trait ID", "") },
            (_, target, values) => target.Actor.removeTrait(GetString(values, "trait_id")), true);
        AddAction("actor.status.add", "Add status", ScriptTargetKind.Actor, false,
            new[] { Text("status_id", "Status ID", ""), Number("duration", "Duration", 60d) },
            (_, target, values) => target.Actor.addStatusEffect(GetString(values, "status_id"), (float)GetNumber(values, "duration", 60d), true), true);
        AddAction("actor.status.remove", "Remove status", ScriptTargetKind.Actor, false,
            new[] { Text("status_id", "Status ID", "") },
            (_, target, values) => target.Actor.finishStatusEffect(GetString(values, "status_id")), true);
        AddAction("actor.kingdom.set", "Set kingdom", ScriptTargetKind.Actor, false,
            new[] { Text("id", "Kingdom ID", "-1"),
                Choice("kingdom_write", "Kingdom write policy", "always", "always", "event_kingdom") },
            (context, target, values) =>
            {
                long current = target.Actor.kingdom?.id ?? -1;
                long desired = GetLong(values, "id", -1);
                if (GetString(values, "kingdom_write", "always") == "event_kingdom" && current != context.Event?.KingdomID)
                    context.SkipAction("kingdom_conflict");
                else if (current == desired) context.SkipAction("no_change");
                else target.Actor.setKingdom(World.world.kingdoms.get(desired));
                context.WriteRecord = new ScriptWriteRecord
                {
                    Target = ScriptTargetSample.From(target), Key = "kingdom",
                    Policy = GetString(values, "kingdom_write", "always"), Before = current, After = target.Actor.kingdom?.id ?? -1
                };
            }, true);
        AddAction("actor.culture.set", "Set culture", ScriptTargetKind.Actor, false,
            new[] { Text("id", "Culture ID", "-1") },
            (_, target, values) => target.Actor.setCulture(World.world.cultures.get(GetLong(values, "id", -1))), true);
        AddAction("actor.clan.set", "Set clan", ScriptTargetKind.Actor, false,
            new[] { Text("id", "Clan ID", "-1") },
            (_, target, values) => target.Actor.setClan(World.world.clans.get(GetLong(values, "id", -1))), true);
        AddAction("actor.language.set", "Set language", ScriptTargetKind.Actor, false,
            new[] { Text("id", "Language ID", "-1") },
            (_, target, values) => target.Actor.setLanguage(World.world.languages.get(GetLong(values, "id", -1))), true);
        AddAction("actor.subspecies.set", "Set subspecies", ScriptTargetKind.Actor, false,
            new[] { Text("id", "Subspecies ID", "-1") },
            (_, target, values) => target.Actor.setSubspecies(World.world.subspecies.get(GetLong(values, "id", -1))), true);
        AddAction("actor.teleport", "Teleport actor", ScriptTargetKind.Actor, true,
            new[] { Integer("x", "X", 0), Integer("y", "Y", 0) },
            (_, target, values) => target.Actor.setCurrentTilePosition(GetTile(values)));
        AddAction("actor.kill", "Kill actor", ScriptTargetKind.Actor, true, EmptyParameters,
            (_, target, _) => target.Actor.dieAndDestroy(AttackType.Divine));
        AddAction("actor.duplicate", "Duplicate actor", ScriptTargetKind.Actor, true,
            new[] { Integer("count", "Count", 1) },
            (context, target, values) => DuplicateActors(context, target.Actor, GetInt(values, "count", 1)),
            outputKind: ScriptTargetKind.Actor);
        AddAction("tile.actor.spawn", "Spawn actors", ScriptTargetKind.Tile, true,
            new[] { Text("asset_id", "Actor asset", "human"), Integer("count", "Count", 1) },
            (context, target, values) => SpawnActors(context, GetString(values, "asset_id", "human"), target.Tile, GetInt(values, "count", 1)),
            outputKind: ScriptTargetKind.Actor);
        AddAction("world.time.set", "Set world time", ScriptTargetKind.World, true,
            new[] { Number("value", "World time", 0d) },
            (_, _, values) => World.world.map_stats.world_time = GetNumber(values, "value"));
        AddAction("world.time.add", "Add world time", ScriptTargetKind.World, true,
            new[] { Number("value", "Amount", 1d) },
            (_, _, values) => World.world.map_stats.world_time += GetNumber(values, "value", 1d));
        AddAction("world.era.set", "Set era", ScriptTargetKind.World, true,
            new[] { Text("era_id", "Era ID", "age_hope") },
            (_, _, values) => World.world.era_manager.setCurrentAge(AssetManager.era_library.get(GetString(values, "era_id")), true));
        AddAction(WorldScriptMessages.TipAction, "Show world tip", ScriptTargetKind.World, false,
            new[] { Text("message", "Message", ""), MessageVariables() },
            (context, _, values) =>
            {
                string message = WorldScriptMessages.Render(values, context.Event, context.MainMatchCount, context.MessageTargetCount, context.GroupName);
                WorldTip.showNowTop(message, false);
                context.EmittedMessage = message;
            });
        AddAction(WorldScriptHistory.ActionID, "Add history message", ScriptTargetKind.World, false,
            new[]
            {
                Text("log_icon", "History icon", WorldScriptHistory.DefaultIcon), Text("message", "Message", ""), MessageVariables(),
                Choice("log_location", "Map location", "none", "none", "event", "group")
            },
            (context, _, values) =>
            {
                string message = WorldScriptMessages.Render(values, context.Event, context.MainMatchCount, context.MessageTargetCount, context.GroupName);
                WorldTile tile = GetString(values, "log_location", "none") switch
                {
                    "event" => context.Event?.Tile,
                    "group" => context.GroupTarget?.Meta switch
                    {
                        City city => city.getTile(),
                        Kingdom kingdom => kingdom.capital?.getTile(),
                        _ => null
                    },
                    _ => null
                };
                WorldScriptHistory.Add(message, GetString(values, "log_icon", WorldScriptHistory.DefaultIcon), tile);
                context.EmittedMessage = message;
            });
        AddAction("tile.terraform", "Terraform tiles", ScriptTargetKind.Tile, true,
            new[] { Text("main_tile_id", "Main tile", "soil_low"), Text("top_tile_id", "Top tile", "") },
            (_, target, values) => Terraform(target.Tile, values));
        AddAction("tile.cloud.spawn", "Spawn cloud", ScriptTargetKind.Tile, true,
            new[] { Text("cloud_id", "Cloud ID", "cloud_rain") },
            (_, target, values) => AssetManager.powers.spawnCloud(target.Tile, GetString(values, "cloud_id", "cloud_rain")));
    }

    private static readonly ScriptParameterDefinition[] EmptyParameters = Array.Empty<ScriptParameterDefinition>();

    private static ScriptParameterDefinition MessageVariables() => new()
    {
        Key = WorldScriptMessages.EnabledKey, Name = "Message variables", Type = ScriptParameterType.Boolean, DefaultValue = new JValue(false)
    };

    private static ScriptParameterDefinition Text(string key, string name, string defaultValue) =>
        new() { Key = key, Name = name, Type = ScriptParameterType.Text, DefaultValue = defaultValue };

    private static ScriptParameterDefinition Integer(string key, string name, int defaultValue) =>
        new() { Key = key, Name = name, Type = ScriptParameterType.Integer, DefaultValue = defaultValue };

    private static ScriptParameterDefinition Number(string key, string name, double defaultValue) =>
        new() { Key = key, Name = name, Type = ScriptParameterType.Number, DefaultValue = defaultValue };

    private static ScriptParameterDefinition Choice(string key, string name, string defaultValue, params string[] choices) =>
        new() { Key = key, Name = name, Type = ScriptParameterType.Choice, DefaultValue = defaultValue, Choices = choices };

    private static void AddTarget(string id, string name, ScriptTargetKind kind,
        ScriptParameterDefinition[] parameters,
        Func<WorldScriptExecutionContext, JObject, IEnumerable<WorldScriptTarget>> resolve,
        Func<WorldScriptExecutionContext, JObject, WorldTile> getOrigin = null,
        bool requiresActorEvent = false, bool requiresDeathEvent = false, bool requiresPositionEvent = false)
    {
        var definition = new TargetDefinition
        {
            ID = id, Name = name, Kind = kind, Parameters = parameters, Resolve = resolve, GetOrigin = getOrigin,
            RequiresActorEvent = requiresActorEvent, RequiresDeathEvent = requiresDeathEvent, RequiresPositionEvent = requiresPositionEvent
        };
        RegisterTarget(definition);
    }

    private static void AddCondition(string id, string name, ScriptTargetKind allowedTargets,
        ScriptParameterDefinition[] parameters,
        Func<WorldScriptExecutionContext, WorldScriptTarget, JObject, bool> evaluate, bool requiresActorEvent = false)
    {
        var definition = new ConditionDefinition
        {
            ID = id,
            Name = name,
            AllowedTargets = allowedTargets,
            RequiresActorEvent = requiresActorEvent,
            Parameters = parameters,
            Evaluate = evaluate
        };
        RegisterCondition(definition);
    }

    private static void AddAction(string id, string name, ScriptTargetKind allowedTargets, bool dangerous,
        ScriptParameterDefinition[] parameters,
        Action<WorldScriptExecutionContext, WorldScriptTarget, JObject> execute, bool changesActorStats = false,
        ScriptTargetKind outputKind = ScriptTargetKind.None)
    {
        var definition = new ActionDefinition
        {
            ID = id,
            Name = name,
            AllowedTargets = allowedTargets,
            Dangerous = dangerous,
            ChangesActorStats = changesActorStats,
            OutputKind = outputKind,
            Parameters = parameters,
            Execute = execute
        };
        RegisterAction(definition);
    }

    private static List<Actor> Actors() => World.world.units.getSimpleList()
        .Where(actor => actor != null && actor.data != null).ToList();

    private static IEnumerable<WorldScriptTarget> ActorsNear(WorldTile center, int radius)
    {
        if (center == null) return Enumerable.Empty<WorldScriptTarget>();
        long radiusSquared = (long)radius * radius;
        return Actors().Where(actor => actor.isAlive() && actor.current_tile != null &&
                                      DistanceSquared(actor.current_tile, center) <= radiusSquared)
            .Select(WorldScriptTarget.ForActor);
    }

    private static IEnumerable<WorldScriptTarget> TilesNear(WorldTile center, int radius)
    {
        if (center == null) yield break;
        long radiusSquared = (long)radius * radius;
        int minX = Math.Max(0, center.x - radius);
        int minY = Math.Max(0, center.y - radius);
        int maxX = (int)Math.Min(MapBox.width - 1, (long)center.x + radius);
        int maxY = (int)Math.Min(MapBox.height - 1, (long)center.y + radius);
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            WorldTile tile = World.world.GetTileSimple(x, y);
            if (tile != null && DistanceSquared(tile, center) <= radiusSquared) yield return WorldScriptTarget.ForTile(tile);
        }
    }

    internal static long DistanceSquared(WorldTile first, WorldTile second)
    {
        long x = first.x - second.x;
        long y = first.y - second.y;
        return x * x + y * y;
    }

    private static WorldTile GetTile(JObject values)
    {
        int x = GetInt(values, "x");
        int y = GetInt(values, "y");
        return World.world.GetTile(x, y);
    }

    private static bool Compare(double actual, JObject values) =>
        Compare(actual, GetString(values, "operator", ">="), GetNumber(values, "value"));

    internal static bool Compare(double actual, string op, double expected) => op switch
        {
            ">" => actual > expected,
            "=" => actual == expected,
            "!=" => actual != expected,
            "<" => actual < expected,
            "<=" => actual <= expected,
            _ => actual >= expected
        };

    private static void SetActorData(Actor actor, string field, int value, bool add)
    {
        int Resolve(int current) => add ? current + value : value;
        switch (field)
        {
            case "age":
                actor.data.created_time = World.world.getCurWorldTime() - Math.Max(Resolve(actor.getAge()), 0) * 60d;
                actor.calcAgeStates();
                break;
            case "nutrition": actor.data.nutrition = Resolve(actor.data.nutrition); break;
            case "happiness": actor.data.happiness = Resolve(actor.data.happiness); break;
            case "stamina": actor.data.stamina = Resolve(actor.data.stamina); break;
            case "mana": actor.data.mana = Resolve(actor.data.mana); break;
            case "kills": actor.data.kills = Resolve(actor.data.kills); break;
            default: actor.data.level = Resolve(actor.data.level); break;
        }
        actor.setStatsDirty();
    }

    private static void SpawnActors(WorldScriptExecutionContext context, string assetID, WorldTile tile, int requested)
    {
        if (AssetManager.actor_library.get(assetID) == null)
            throw new InvalidOperationException($"Unknown actor asset '{assetID}'");
        int count = context.SpawnAllowance(requested);
        for (int i = 0; i < count; i++)
        {
            Actor actor = World.world.units.spawnNewUnit(assetID, tile, false, true, 0f, null, false, true);
            if (actor != null) context.RecordSpawn(actor);
        }
    }

    private static void DuplicateActors(WorldScriptExecutionContext context, Actor source, int requested)
    {
        int count = context.SpawnAllowance(requested);
        for (int i = 0; i < count; i++)
        {
            Actor clone = World.world.units.spawnNewUnit(source.asset.id, source.current_tile, false, true, 0f,
                source.subspecies, false, true);
            if (clone == null) continue;
            context.RecordSpawn(clone);
            clone.data.name = source.data.name;
            clone.data.created_time = source.data.created_time;
            clone.data.level = source.data.level;
            clone.data.nutrition = source.data.nutrition;
            clone.data.happiness = source.data.happiness;
            clone.data.stamina = source.data.stamina;
            clone.data.mana = source.data.mana;
            if (source.kingdom != null) clone.setKingdom(source.kingdom);
            if (source.culture != null) clone.setCulture(source.culture);
            if (source.clan != null) clone.setClan(source.clan);
            if (source.language != null) clone.setLanguage(source.language);
            foreach (ActorTrait trait in source.getTraits()) clone.addTrait(trait, false);
            clone.calcAgeStates();
            clone.updateStats();
            clone.setHealth((int)(clone.getMaxHealth() * source.getHealthRatio()), true);
        }
    }

    private static void Terraform(WorldTile tile, JObject values)
    {
        string mainID = GetString(values, "main_tile_id");
        string topID = GetString(values, "top_tile_id");
        if (!string.IsNullOrEmpty(mainID))
        {
            TileType main = AssetManager.tiles.get(mainID);
            if (main == null) throw new InvalidOperationException($"Unknown main tile '{mainID}'");
            MapAction.terraformMain(tile, main, false);
        }
        if (!string.IsNullOrEmpty(topID))
        {
            TopTileType top = AssetManager.top_tiles.get(topID);
            if (top == null) throw new InvalidOperationException($"Unknown top tile '{topID}'");
            MapAction.terraformTop(tile, top, false);
        }
        World.world.setTileDirty(tile);
    }
}
