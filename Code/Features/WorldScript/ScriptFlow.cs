using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public sealed class ScriptFlow
{
    public ConditionNode Gate = ConditionNode.All();
    public ScriptQuery Query = new();
    public List<ScriptStep> Steps = new();
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }
}

public sealed class ScriptQuery
{
    public TargetSelector Scope = new();
    public ConditionNode Filter = ConditionNode.All();
    public string Order = "registry";
    public TargetSelector DistanceOrigin;
    public string Group = "none";
    public int Count;
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }
}

public sealed class ScriptStep
{
    public string ID = Guid.NewGuid().ToString("N");
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Name;
    public ScriptAction Action = new();
    public string Source = "query";
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string SourceStepID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptCountRule ResultCondition;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptQuery Query;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptRelation Relation;
    public string Mode = "each";
    public Dictionary<string, ScriptNumberBinding> Numbers = new();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptBranch Branch;
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }

    internal bool HasBranch => Branch?.Enabled == true;
}

public sealed class ScriptCountRule
{
    public string Measure = "processed";
    public string Operator = ">=";
    public int Value = 3;
}

public sealed class ScriptRelation
{
    public string ID;
    public ConditionNode Filter = ConditionNode.All();
    public int Limit;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptRelation Next;
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }
}

public sealed class ScriptBranch
{
    public bool Enabled = true;
    public ConditionNode Condition = ConditionNode.All();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptAction Otherwise;
    public Dictionary<string, ScriptNumberBinding> OtherwiseNumbers = new();
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }

    internal bool HasOtherwise => Otherwise?.Enabled == true;
}

public sealed class ScriptNumberBinding
{
    public string Source = "target.max_health";
    public double Percent = 100d;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Parameter;
}

public sealed class WorldScriptEvent
{
    private readonly Actor _actor;
    private readonly Actor _killer;
    public readonly long ActorID = -1;
    public readonly long? KingdomID;
    public readonly string ActorName;
    public readonly string KingdomName;
    public readonly int? X;
    public readonly int? Y;
    public readonly double WorldTime;
    public readonly string EraID;
    public readonly int MaxHealth;
    public readonly long KillerID = -1;
    public readonly long? CityID;
    public readonly string CityName;
    public readonly long? PreviousID;
    public readonly long? NextID;
    public readonly string PreviousName;
    public readonly string NextName;
    public readonly int? Value;
    public readonly string WarName;
    public ScriptEventCause Cause { get; internal set; }

    internal WorldScriptEvent(Actor actor, Actor killer = null)
    {
        _actor = actor;
        _killer = killer;
        ActorID = actor.data.id;
        ActorName = actor.data.name;
        KillerID = killer?.data?.id ?? -1;
        KingdomID = actor.kingdom?.data?.id;
        KingdomName = actor.kingdom?.data?.name;
        WorldTile tile = actor.current_tile;
        X = tile?.x;
        Y = tile?.y;
        MaxHealth = actor.stats == null ? 0 : actor.getMaxHealth();
        WorldTime = World.world?.getCurWorldTime() ?? 0d;
        EraID = World.world?.map_stats?.world_age_id;
    }

    internal WorldScriptEvent(City city, Kingdom kingdom, long? previousID, string previousName,
        long? nextID, string nextName, WorldTile tile, int? value = null, string warName = null)
    {
        CityID = city?.data?.id;
        CityName = city?.data?.name;
        KingdomID = kingdom?.data?.id;
        KingdomName = kingdom?.data?.name;
        PreviousID = previousID;
        PreviousName = previousName;
        NextID = nextID;
        NextName = nextName;
        X = tile?.x;
        Y = tile?.y;
        WorldTime = World.world?.getCurWorldTime() ?? 0d;
        EraID = World.world?.map_stats?.world_age_id;
        Value = value;
        WarName = warName;
    }

    public Actor Actor => _actor?.data?.id == ActorID ? _actor : null;
    public Actor Killer => _killer?.data?.id == KillerID ? _killer : null;
    public City City => CityID is long id ? World.world?.cities.get(id) : null;
    public Kingdom Kingdom => KingdomID is long id ? World.world?.kingdoms.get(id) : null;
    public WorldTile Tile => X is int x && Y is int y ? World.world?.GetTile(x, y) : null;

    internal WorldScriptEvent(ScriptQueuedEvent saved)
    {
        ActorID = saved.SceneActor ?? -1;
        ActorName = saved.SceneActorName;
        CityID = saved.City;
        CityName = saved.CityName;
        KingdomID = saved.Kingdom;
        KingdomName = saved.KingdomName;
        PreviousID = saved.Previous;
        PreviousName = saved.PreviousName;
        NextID = saved.Next;
        NextName = saved.NextName;
        X = saved.X;
        Y = saved.Y;
        WarName = saved.WarName;
        Value = saved.Value;
        WorldTime = saved.Time;
        EraID = saved.Era;
        Cause = saved.Cause;
    }
}
