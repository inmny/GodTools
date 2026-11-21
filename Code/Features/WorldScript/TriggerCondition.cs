using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public class TriggerCondition
{
    public string TriggerID = WorldScriptRegistry.TriggerInterval;
    public JObject Parameters = new() { ["interval"] = 60d };
    [JsonIgnore] public TriggerConditionType Type
    {
        get => WorldScriptRegistry.GetTriggerType(TriggerID);
        set => TriggerID = WorldScriptRegistry.GetTriggerID(value);
    }
    [JsonExtensionData] public System.Collections.Generic.IDictionary<string, JToken> ExtensionData { get; set; } = new System.Collections.Generic.Dictionary<string, JToken>();
}

public enum TriggerConditionType
{
    WorldLoaded, Interval, WorldTimeReached, EraChanged, ActorCreated, ActorDied, ActorStatsUpdated, Manual,
    CityKingdomChanged, KingdomKingChanged, Delayed, WarEnded, CityThreshold, RegionOwnership
}
