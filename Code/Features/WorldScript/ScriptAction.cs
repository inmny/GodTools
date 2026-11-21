using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public class ScriptAction
{
    public string Operator = "action";
    public string ActionID;
    public bool Enabled = true;
    public JObject Parameters = new();
    public List<ScriptAction> Children = new();
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
}

public class ActionNode : ScriptAction
{
}
