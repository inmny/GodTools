using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public class ScriptInstance
{
    public string ID = Guid.NewGuid().ToString("N");
    public string Name = "New Script";
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string ScenarioID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string PackageScriptID;
    public bool Enabled = true;
    public bool IsDraft;
    public int Priority;
    public TriggerCondition TriggerCondition = new();
    public TargetSelector TargetQuery = new();
    [JsonIgnore] public TargetSelector Target { get => TargetQuery; set => TargetQuery = value; }
    public ConditionNode Conditions = ConditionNode.All();
    public List<ScriptAction> Actions = new();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptFlow Flow;
    public string SortMode = "registry";
    public string GroupMode = "none";
    public string SampleMode = "all";
    public int SampleCount;
    public string EmptyTargetPolicy = "skip";
    public double Cooldown;
    public int MaxTargets = 20;
    public int TargetsPerBatch;
    public int MaxSpawns = 20;
    public bool RunOnce;
    public bool AllowDangerousActions;
    public ScriptRuntimeState Runtime = new();
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
}

public class ScriptRuntimeState
{
    public double LastRunWorldTime = -1d;
    public double LastAttemptWorldTime = -1d;
    public int RunCount;
    public long TriggerCount;
    public double? FirstTriggerWorldTime;
    public double? LastTriggerWorldTime;
    public int LastTargetCount;
    public int LastSpawnCount;
    public string LastError;
    public List<ScriptExecutionRecord> History = new();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, double> EventCooldowns;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, int> State;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptDelayedRun> DelayedRuns;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptDelayedOutcome> DelayedOutcomes;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<long, bool> ThresholdLatches;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string ThresholdSettings;
    public double NextThresholdSample;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptBatchJob Batch;
}

public sealed class ScriptDelayedRun
{
    public string ID = Guid.NewGuid().ToString("N");
    public string ScriptID;
    public double Due;
    public long? CityID;
    public long? KingdomID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public long? RequiredKingdomID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string SubjectName;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string CancelPhase;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptEventCause Cause;
}

public sealed class ScriptDelayedOutcome
{
    public ScriptDelayedRun Run;
    public string Outcome;
    public double Time;
}
public class ScriptExecutionRecord
{
    public double WorldTime;
    public string TriggerID;
    public bool Manual;
    public string Outcome;
    public int TargetCount;
    public int ActionCount;
    public int SpawnCount;
    public string Message;
    public int? MatchCount;
    public int? SelectedCount;
    public int OmittedSteps;
    public List<ScriptStepExecution> Steps;
    public double DurationMilliseconds;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string BatchID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptEventCause Cause;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ScriptTargetSample Subject;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptLimitHit> Limits;
}

public sealed class ScriptLimitHit
{
    public string Kind;
    public int Step;
}

public sealed class ScriptStepExecution
{
    public int Step;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string StepID;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Name;
    public string ActionID;
    public string Outcome;
    public string Error;
    public int InputCount;
    public int? ThenCount;
    public int? OtherwiseCount;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? ResultValue;
    public ScriptTargetSample Parent;
    public int ProcessedCount;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptStepExecution> Groups;
    public int OmittedGroups;
    public List<ScriptTargetSample> Targets = new();
    public List<ScriptActionExecution> Actions = new();

    internal void Sample(WorldScriptTarget target)
    {
        if (Targets.Count >= 8) return;
        Targets.Add(ScriptTargetSample.From(target));
    }

    internal static string Limit(string text, int limit) => text?.Length > limit ? text.Substring(0, limit) + "…" : text;
}

public sealed class ScriptTargetSample
{
    public ScriptTargetKind Kind;
    public long ActorID;
    public long MetaID;
    public string Name;
    public string Branch;
    public int X;
    public int Y;

    internal static ScriptTargetSample From(WorldScriptTarget target) => new()
    {
        Kind = target.Kind, ActorID = target.ActorID, MetaID = target.MetaID,
        Name = ScriptStepExecution.Limit(target.Meta?.name ?? target.Actor?.data?.name, 100),
        X = target.Tile?.x ?? 0, Y = target.Tile?.y ?? 0
    };
}

public sealed class ScriptActionExecution
{
    public string ActionID;
    public string Branch;
    public int TargetCount;
    public int ActionCount;
    public int SpawnCount;
    public int SkippedCount;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptWriteRecord> Writes;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string SkippedReason;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public long? ChangeAmount;
    public string Message;
    public bool MessageTruncated;
}

public class WorldScriptDocument
{
    public const int CurrentVersion = 2;
    public int schemaVersion = CurrentVersion;
    [JsonIgnore] public int Version { get => schemaVersion; set => schemaVersion = value; }
    public List<ScriptInstance> Scripts = new();
    public List<ScriptScenario> Scenarios = new();
    public List<ScriptRegion> Regions = new();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ScriptQueuedEvent> QueuedEvents;
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
}

public class ConditionNode
{
    public string Operator = "all";
    public string ConditionID;
    public JObject Parameters = new();
    public List<ConditionNode> Children = new();
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    public static ConditionNode All() => new() { Operator = "all", Children = new List<ConditionNode>() };

    public static implicit operator ConditionNode(TargetFilter filter) => filter == null
        ? null
        : new ConditionNode
        {
            Operator = "condition",
            ConditionID = filter.FilterID,
            Parameters = filter.Parameters ?? new JObject(),
            ExtensionData = filter.ExtensionData ?? new Dictionary<string, JToken>()
        };
}
