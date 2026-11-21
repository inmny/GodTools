using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GodTools.Features.WorldScript;

public sealed class ScriptScenario
{
    public string ID = Guid.NewGuid().ToString("N");
    public string Name;
    public Dictionary<string, int> Parameters = new(StringComparer.Ordinal);
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, ScriptScenarioProgress> Progress;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptScenarioTransition> Timeline;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptScene> Scenes;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string PackageID;
    public int PackageRevision;
}

public sealed class ScriptScenarioProgress
{
    public ScriptTargetSample Subject;
    public string Phase;
    public double ChangedAt;
}

public sealed class ScriptScenarioTransition
{
    public ScriptTargetSample Subject;
    public string From;
    public string To;
    public string ScriptName;
    public double Time;
}
