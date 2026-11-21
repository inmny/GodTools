using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public enum FilterGroup { MustTrue, AnyTrue, MustFalse, AnyFalse }

public class TargetFilter
{
    public string FilterID;
    public FilterGroup Group;
    public JObject Parameters = new();
    [JsonExtensionData] public System.Collections.Generic.IDictionary<string, JToken> ExtensionData { get; set; } = new System.Collections.Generic.Dictionary<string, JToken>();
}

public class TargetSelector
{
    public string TargetID = WorldScriptRegistry.TargetActorAll;
    public JObject Parameters = new();
    [JsonExtensionData] public System.Collections.Generic.IDictionary<string, JToken> ExtensionData { get; set; } = new System.Collections.Generic.Dictionary<string, JToken>();
}
