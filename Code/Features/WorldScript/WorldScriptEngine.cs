using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public sealed class WorldScriptValidationResult
{
    public readonly List<string> Errors = new();
    public bool IsValid => Errors.Count == 0;
}

public sealed class WorldScriptPreviewResult
{
    public readonly List<WorldScriptTarget> Targets = new();
    public readonly List<string> Errors = new();
    public readonly List<WorldScriptStepPreview> Steps = new();
    public readonly List<ScriptPreviewDecision> Decisions = new();
    public int OmittedDecisions;
    public int MatchCount;
    public WorldScriptEvent Event;
    public bool GatePassed = true;
    public bool CanRun => GatePassed && (Steps.Count == 0 ? Targets.Count > 0 : Steps.Any(step => step.CanRun));
    public bool IsValid => Errors.Count == 0;
}

public sealed class ScriptPreviewDecision
{
    public ScriptTargetSample Subject;
    public ScriptTargetSample Group;
    public ConditionNode Rule;
    public string Reason;
    public int Step;
}

public sealed class WorldScriptStepPreview
{
    public int StepIndex;
    public bool Deferred;
    public readonly List<WorldScriptTarget> Targets = new();
    public bool BranchKnown;
    public bool RunsOtherwise;
    public readonly List<WorldScriptTarget> ThenTargets = new();
    public readonly List<WorldScriptTarget> OtherwiseTargets = new();
    public List<WorldScriptTargetGroup> Groups;
    public bool ReportsGroups;
    internal bool CanRun => Groups != null
        ? Groups.Any(group => group.CanRun(ReportsGroups))
        : Targets.Count > 0 && (!BranchKnown || ThenTargets.Count > 0 || RunsOtherwise && OtherwiseTargets.Count > 0);
}

public static partial class WorldScriptEngine
{
    private const int MaxQueuedEvents = 1024;
    private const int MaxEventsPerDrain = 256;
    private const int MaxActionsPerEvent = 1000;
    private const int MaxRunsPerFrame = 6;
    private const double FrameBudgetMilliseconds = 5d;

    private sealed class TriggerRequest
    {
        public string ID;
        public WorldScriptEvent Event;
        public string ScheduleOwner;
        public string ScheduleID;
        public string BatchID;
        public string TargetScriptID;
        public string[] ScriptIDs;
        public int NextScript;
        public (string Trigger, long Subject)? DeduplicationKey;
    }

    private static bool _executing;
    [ThreadStatic] private static ScriptEventCause _activeCause;
    private static string _lastEraID;
    private static readonly ConcurrentQueue<TriggerRequest> Pending = new();
    private static readonly Queue<TriggerRequest> Due = new();
    private static readonly ConcurrentDictionary<(string Trigger, long Subject), byte> PendingSignals = new();
    private static TriggerRequest _dispatch;
    private static bool _preferDue = true;
    private static int _droppedEvents;
    internal static int QueuedWork => Pending.Count + Due.Count + (_dispatch == null ? 0 : 1);
    internal static int DroppedEvents => System.Threading.Volatile.Read(ref _droppedEvents);
    private static readonly Random Random = new();
    private static readonly ConcurrentDictionary<long, byte> SuppressedActorStatsUpdates = new();

    public static void Reset()
    {
        _executing = false;
        _lastEraID = null;
        while (Pending.TryDequeue(out _)) { }
        Due.Clear();
        _dispatch = null;
        _preferDue = true;
        _droppedEvents = 0;
        PendingSignals.Clear();
        _thresholdSweep = null;
        SuppressedActorStatsUpdates.Clear();
        QueuedDelayedRuns.Clear();
        QueuedBatches.Clear();
    }

    public static void OnWorldLoaded() => OnWorldLoaded(false);

    internal static void OnWorldLoaded(bool resume)
    {
        if (World.world == null) return;
        _lastEraID = World.world.map_stats.world_age_id;
        if (resume) return;
        QueueEvent(WorldScriptRegistry.TriggerWorldLoaded, null);
        DrainQueue();
    }

    public static void Tick()
    {
        if (World.world == null || World.world.isPaused()) return;
        string eraID = World.world.map_stats.world_age_id;
        if (_lastEraID != eraID)
        {
            _lastEraID = eraID;
            QueueEvent(WorldScriptRegistry.TriggerEraChanged, null);
        }
        QueueEvent(WorldScriptRegistry.TriggerInterval, null);
        QueueEvent(WorldScriptRegistry.TriggerWorldTimeReached, null);
        CollectThresholdEvents();
        QueueDueRuns();
        QueueBatches();
        DrainQueue(frameBudget: true);
        WorldScriptScenes.Tick();
    }

    // Actor 回调可能来自游戏并行批处理的工作线程，只允许线程安全入队；
    // 排空队列和执行脚本只发生在主线程的 Tick / RunNow / OnWorldLoaded。
    public static void OnActorCreated(Actor actor) => QueueEvent(WorldScriptRegistry.TriggerActorCreated, Capture(actor));
    public static void OnActorDied(Actor actor) => OnActorDeath(Capture(actor));
    internal static void OnActorDeath(WorldScriptEvent snapshot)
    {
        if (snapshot != null) QueueEvent(WorldScriptRegistry.TriggerActorDied, snapshot);
    }

    private static WorldScriptEvent Capture(Actor actor, Actor killer = null) =>
        actor?.data == null ? null : new WorldScriptEvent(actor, killer);

    internal static Actor DeathKiller(Actor actor, AttackType type)
    {
        // These causes clear attackedBy in Actor.die.
        if (type is AttackType.Plague or AttackType.Infection or AttackType.Tumor or AttackType.Divine or
            AttackType.AshFever or AttackType.Metamorphosis or AttackType.Starvation or AttackType.Age or
            AttackType.None or AttackType.Poison or AttackType.Gravity or AttackType.Drowning) return null;
        Actor attacker = actor.attackedBy as Actor;
        return attacker != actor && attacker?.isAlive() == true ? attacker : null;
    }

    public static void SuppressNextActorStatsUpdate(Actor actor)
    {
        if (actor?.data != null) SuppressedActorStatsUpdates[actor.data.id] = 1;
    }

    public static void OnActorStatsUpdated(Actor actor)
    {
        if (actor?.data != null && SuppressedActorStatsUpdates.TryRemove(actor.data.id, out _)) return;
        QueueEvent(WorldScriptRegistry.TriggerActorStatsUpdated, Capture(actor));
    }

    public static WorldScriptValidationResult Validate(ScriptInstance script)
    {
        var result = new WorldScriptValidationResult();
        if (script == null)
        {
            result.Errors.Add("Script is null");
            return result;
        }

        TriggerDefinition trigger = WorldScriptRegistry.GetTrigger(script.TriggerCondition?.TriggerID);
        if (trigger == null) result.Errors.Add($"Unknown trigger '{script.TriggerCondition?.TriggerID}'");
        else
        {
            AddParameterErrors(result.Errors, "trigger", script.TriggerCondition.Parameters, trigger.Parameters, trigger.ID);
            if (result.Errors.Count == 0 && trigger.ID == WorldScriptRegistry.TriggerCityThreshold &&
                !ValidThresholdSettings(script.TriggerCondition.Parameters))
                result.Errors.Add("Set valid low and high thresholds and a sampling interval");
        }

        if (string.IsNullOrWhiteSpace(script.Name)) result.Errors.Add("Enter a script name");
        if (script.MaxTargets < 0) result.Errors.Add("MaxTargets cannot be negative");
        if (script.TargetsPerBatch < 0 || script.TargetsPerBatch > MaxActionsPerEvent)
            result.Errors.Add("Batch size must be between 0 and the action limit");
        if (script.TargetsPerBatch > 0 && !CanBatch(script))
            result.Errors.Add("Batch jobs require independent per-target steps on the main query");
        if (script.MaxSpawns < 0) result.Errors.Add("MaxSpawns cannot be negative");
        if (script.Cooldown < 0d || double.IsNaN(script.Cooldown) || double.IsInfinity(script.Cooldown))
            result.Errors.Add("Cooldown must be a finite nonnegative number");
        if (script.Flow != null)
        {
            ValidateFlow(script, result.Errors);
            return result;
        }

        TargetDefinition target = ValidateScope(script.TargetQuery, script, result.Errors, "target");
        ValidateFlowConditions(script.Conditions, target?.Kind ?? ScriptTargetKind.None, script.TriggerCondition?.TriggerID,
            result.Errors, "conditions");
        ValidateActions(script.Actions, script, target?.Kind ?? ScriptTargetKind.None, result.Errors, "actions");
        if (!EnabledActions(script.Actions).Any()) result.Errors.Add("Add at least one enabled action");
        if (script.SampleCount < 0) result.Errors.Add("SampleCount cannot be negative");
        if (!new[] { "registry", "random", "actor_id" }.Contains(script.SortMode))
            result.Errors.Add($"Unknown sort mode '{script.SortMode}'");
        if (!new[] { "none", "kind", "actor_asset" }.Contains(script.GroupMode))
            result.Errors.Add($"Unknown group mode '{script.GroupMode}'");
        if (!new[] { "all", "random", "first" }.Contains(script.SampleMode))
            result.Errors.Add($"Unknown sample mode '{script.SampleMode}'");
        if (!new[] { "skip", "error", "run_once" }.Contains(script.EmptyTargetPolicy))
            result.Errors.Add($"Unknown empty target policy '{script.EmptyTargetPolicy}'");
        return result;
    }

    public static bool UsesEventActor(string triggerID) =>
        triggerID is WorldScriptRegistry.TriggerActorCreated or WorldScriptRegistry.TriggerActorDied
            or WorldScriptRegistry.TriggerActorStatsUpdated;

    public static IEnumerable<ScriptAction> EnabledActions(IEnumerable<ScriptAction> actions)
    {
        foreach (ScriptAction action in actions ?? Enumerable.Empty<ScriptAction>())
        {
            if (action == null || !action.Enabled) continue;
            if (action.Operator == "sequence")
            {
                foreach (ScriptAction child in EnabledActions(action.Children)) yield return child;
            }
            else
                yield return action;
        }
    }

    public static WorldScriptPreviewResult Preview(ScriptInstance script, Actor eventActor = null) => Preview(script, eventActor, null);

    public static WorldScriptPreviewResult Preview(ScriptInstance script, Actor eventActor, Actor eventKiller)
        => PreviewEvent(script, Capture(eventActor, eventKiller));

    public static WorldScriptPreviewResult PreviewEvent(ScriptInstance script, WorldScriptEvent snapshot)
    {
        var context = CreateContext(script, script?.TriggerCondition?.TriggerID, snapshot, MaxActionsPerEvent);
        context.InspectPreview = true;
        return ResolveTargets(script, context, new Random(PreviewSeed(script)));
    }

    public static bool RunNow(ScriptInstance script, Actor eventActor = null, WorldScriptPreviewResult confirmed = null) =>
        RunNow(script, eventActor, confirmed, null);

    public static bool RunNow(ScriptInstance script, Actor eventActor, WorldScriptPreviewResult confirmed, Actor eventKiller)
        => RunEventNow(script, Capture(eventActor, eventKiller), confirmed);

    public static bool RunEventNow(ScriptInstance script, WorldScriptEvent snapshot, WorldScriptPreviewResult confirmed = null)
    {
        if (World.world == null || script == null || _executing) return false;
        bool result = false;
        RunInExecutionScope(() =>
        {
            result = Execute(script,
                CreateContext(script, script.TriggerCondition?.TriggerID, snapshot, MaxActionsPerEvent), true, confirmed);
        });
        return result;
    }

    public static void Test(ScriptInstance script) => Preview(script);

    private static bool QueueEvent(string triggerID, WorldScriptEvent snapshot, string targetScriptID = null)
    {
        if (World.world == null || WorldScripts.Scripts == null || string.IsNullOrWhiteSpace(triggerID)) return false;
        if (snapshot != null) snapshot.Cause ??= _activeCause;
        (string, long)? key = triggerID switch
        {
            WorldScriptRegistry.TriggerInterval or WorldScriptRegistry.TriggerWorldTimeReached => (triggerID, -1L),
            WorldScriptRegistry.TriggerActorStatsUpdated when snapshot != null => (triggerID, snapshot.ActorID),
            _ => null
        };
        if (key.HasValue && !PendingSignals.TryAdd(key.Value, 0)) return true;
        if (Pending.Count >= MaxQueuedEvents)
        {
            if (key.HasValue) PendingSignals.TryRemove(key.Value, out _);
            System.Threading.Interlocked.Increment(ref _droppedEvents);
            return false;
        }
        Pending.Enqueue(new TriggerRequest { ID = triggerID, Event = snapshot, TargetScriptID = targetScriptID, DeduplicationKey = key });
        return true;
    }

    private static void DrainQueue(bool frameBudget = false)
    {
        if (!_executing) RunInExecutionScope(null, frameBudget);
    }

    private static void RunInExecutionScope(Action initialAction, bool frameBudget = false)
    {
        _executing = true;
        try
        {
            initialAction?.Invoke();
            var elapsed = Stopwatch.StartNew();
            int runs = 0;
            for (int checkedItems = 0; checkedItems < MaxEventsPerDrain; checkedItems++)
            {
                if (frameBudget && (runs >= MaxRunsPerFrame || runs > 0 && elapsed.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds)) break;

                if (frameBudget && _preferDue && Due.Count > 0)
                {
                    _preferDue = false;
                    var next = Due.Dequeue();
                    bool ranDue = next.BatchID == null ? RunScheduled(next.ScheduleOwner, next.ScheduleID)
                        : RunBatchSlice(next.ScheduleOwner, next.BatchID);
                    if (ranDue) runs++;
                    continue;
                }

                if (_dispatch == null)
                {
                    if (!Pending.TryDequeue(out _dispatch))
                    {
                        if (Due.Count == 0 || !frameBudget) break;
                        _preferDue = true;
                        continue;
                    }
                    if (_dispatch.DeduplicationKey.HasValue)
                        PendingSignals.TryRemove(_dispatch.DeduplicationKey.Value, out _);
                    WorldScriptScenes.Observe(_dispatch.ID, _dispatch.Event);
                    if (_dispatch.ScriptIDs == null && _dispatch.ID == WorldScriptRegistry.TriggerCityKingdomChanged &&
                        _dispatch.Event is WorldScriptEvent capture)
                        CancelOnOwnershipChange(capture);
                }

                // Alternate ordinary events and due follow-ups even when one event has many subscribers.
                _preferDue = frameBudget;
                if (ProcessNext(_dispatch, out bool ran)) _dispatch = null;
                if (ran) runs++;
            }
        }
        finally
        {
            _executing = false;
        }
    }

    private static bool ProcessNext(TriggerRequest request, out bool ran)
    {
        ran = false;
        request.ScriptIDs ??= request.TargetScriptID != null ? new[] { request.TargetScriptID } : WorldScripts.Scripts
            .Where(value => value != null && value.Enabled && value.TriggerCondition?.TriggerID == request.ID)
            .OrderByDescending(value => value.Priority).Select(value => value.ID).ToArray();
        if (request.NextScript >= request.ScriptIDs.Length) return true;

        string id = request.ScriptIDs[request.NextScript++];
        var script = WorldScripts.Scripts.Find(value => value?.ID == id);
        if (script?.Enabled == true && script.TriggerCondition?.TriggerID == request.ID)
        {
            try
            {
                ran = ExecuteTriggered(script, request.ID, request.Event);
            }
            catch (Exception exception)
            {
                script.Runtime ??= new ScriptRuntimeState();
                script.Runtime.LastError = exception.Message;
                Main.LogWarning($"World script '{script.Name}' scheduling failed: {exception}");
                WorldScripts.NotifyRuntimeChanged(script);
            }
        }
        return request.NextScript >= request.ScriptIDs.Length;
    }

    private static bool ExecuteTriggered(ScriptInstance script, string triggerID, WorldScriptEvent snapshot)
    {
        if (triggerID == WorldScriptRegistry.TriggerRegionOwnership && !InTriggerRegion(script, snapshot)) return false;
        double time = World.world.getCurWorldTime();
        NoteTrigger(script, time);
        string eventKey = EventCooldownKey(script, snapshot);
        if (!ShouldRun(script, time, eventKey)) return false;
        script.Runtime.LastAttemptWorldTime = time;
        int runsBefore = script.Runtime.RunCount;
        Execute(script, CreateContext(script, triggerID, snapshot, MaxActionsPerEvent), false);
        if (eventKey != null && script.Runtime.RunCount != runsBefore)
            RememberEventCooldown(script, eventKey, time);
        return true;
    }

    private static bool ShouldRun(ScriptInstance script, double worldTime, string eventKey)
    {
        script.Runtime ??= new ScriptRuntimeState();
        if (script.Runtime.Batch != null) return false;
        if (script.RunOnce && script.Runtime.RunCount > 0) return false;
        double last = eventKey == null ? script.Runtime.LastRunWorldTime
            : script.Runtime.EventCooldowns?.GetValueOrDefault(eventKey, -1d) ?? -1d;
        if (last >= 0d && last <= worldTime && worldTime - last < script.Cooldown) return false;
        switch (script.TriggerCondition.TriggerID)
        {
            case WorldScriptRegistry.TriggerInterval:
                double interval = Math.Max(WorldScriptRegistry.GetNumber(script.TriggerCondition.Parameters, "interval", 60d), 0.01d);
                double previous = Math.Max(script.Runtime.LastRunWorldTime, script.Runtime.LastAttemptWorldTime);
                return previous < 0d || previous > worldTime || worldTime - previous >= interval;
            case WorldScriptRegistry.TriggerWorldTimeReached:
                double start = WorldScriptRegistry.GetNumber(script.TriggerCondition.Parameters, "time");
                double repeat = WorldScriptRegistry.GetNumber(script.TriggerCondition.Parameters, "repeat_interval");
                return worldTime >= start && (script.Runtime.RunCount == 0 || repeat > 0d &&
                    (script.Runtime.LastRunWorldTime > worldTime || worldTime - script.Runtime.LastRunWorldTime >= repeat));
            default:
                return true;
        }
    }

    private static void NoteTrigger(ScriptInstance script, double time)
    {
        var state = script.Runtime ??= new ScriptRuntimeState();
        state.TriggerCount++;
        state.FirstTriggerWorldTime ??= time;
        state.LastTriggerWorldTime = time;
    }

    private static WorldScriptExecutionContext CreateContext(ScriptInstance script, string triggerID, WorldScriptEvent snapshot,
        int actionBudget) => new()
    {
        TriggerID = triggerID,
        Trigger = WorldScriptRegistry.GetTriggerType(triggerID),
        EventActor = snapshot?.Actor,
        Event = snapshot,
        WorldTime = World.world?.getCurWorldTime() ?? 0d,
        EraID = script?.Flow != null && snapshot != null ? snapshot.EraID : World.world?.map_stats.world_age_id,
        Script = script,
        ActionBudget = Math.Max(actionBudget, 0)
    };

    private static bool Execute(ScriptInstance script, WorldScriptExecutionContext context, bool manual,
        WorldScriptPreviewResult confirmed = null)
    {
        script.Runtime ??= new ScriptRuntimeState();
        script.Runtime.LastTargetCount = 0;
        script.Runtime.LastSpawnCount = 0;
        var stopwatch = Stopwatch.StartNew();
        string outcome = "failed";
        WorldScriptPreviewResult preview = null;
        try
        {
            preview = ResolveTargets(script, context, manual ? new Random(PreviewSeed(script)) : Random);
            if (!preview.IsValid) throw new InvalidOperationException(string.Join("; ", preview.Errors));
            if (confirmed != null && !SamePreview(confirmed, preview))
                throw new InvalidOperationException("Targets changed since confirmation. Preview again before running.");
            context.MainMatchCount = preview.MatchCount;
            context.MessageTargetCount = preview.Targets.Count;
            if (script.TargetsPerBatch > 0 && preview.CanRun)
            {
                if (script.Runtime.Batch != null) throw new InvalidOperationException("A batch is already active");
                StartBatch(script, context, preview, manual);
                outcome = "batch_queued";
                script.Runtime.LastError = null;
                return true;
            }
            if (script.Flow != null)
                ExecuteFlow(script, context, preview);
            else
            {
                var details = new Dictionary<ScriptAction, ScriptStepExecution>();
                foreach (WorldScriptTarget target in preview.Targets)
                {
                    ExecuteActions(script.Actions, context, target, details);
                    script.Runtime.LastTargetCount++;
                }
            }
            script.Runtime.LastError = null;
            bool skipped = script.Flow != null ? context.CompletedActionCount == 0 : preview.Targets.Count == 0;
            outcome = !preview.GatePassed ? "gated" : skipped
                ? context.HadSkippedActions ? "no_change" : context.HadUnmetBranch ? "condition_unmet" : "no_targets"
                : "completed";
            if (!manual && !skipped)
            {
                script.Runtime.LastRunWorldTime = context.WorldTime;
                script.Runtime.RunCount++;
            }
            return true;
        }
        catch (Exception exception)
        {
            script.Runtime.LastError = exception.Message;
            if (context.CompletedActionCount > 0 || context.SpawnCount > 0) outcome = "partial";
            if (!manual)
            {
                script.Runtime.LastRunWorldTime = context.WorldTime;
                script.Runtime.RunCount++;
            }
            Main.LogWarning($"World script '{script.Name}' failed: {exception}");
            return false;
        }
        finally
        {
            script.Runtime.LastSpawnCount = context.SpawnCount;
            RecordExecution(script.Runtime, context, preview, outcome, manual, stopwatch.Elapsed.TotalMilliseconds);
            WorldScripts.NotifyRuntimeChanged(script);
        }
    }

    private static bool SameTargets(List<WorldScriptTarget> first, List<WorldScriptTarget> second) =>
        first.Count == second.Count && first.Zip(second, SameTarget).All(same => same);

    private static bool SameTarget(WorldScriptTarget left, WorldScriptTarget right) =>
        left.Kind == right.Kind && left.Actor == right.Actor && left.ActorID == right.ActorID &&
        left.Tile == right.Tile && ReferenceEquals(left.Meta, right.Meta) && left.MetaID == right.MetaID;

    private static bool SameGroups(List<WorldScriptTargetGroup> first, List<WorldScriptTargetGroup> second) =>
        first == null || second == null ? first == second : first.Count == second.Count && first.Zip(second, (left, right) =>
            SameTarget(left.Parent, right.Parent) && left.Name == right.Name && SameTargets(left.Targets, right.Targets) &&
            SameGroups(left.Villages, right.Villages)).All(same => same);

    private static bool SamePreview(WorldScriptPreviewResult first, WorldScriptPreviewResult second) =>
        first.GatePassed == second.GatePassed && SameTargets(first.Targets, second.Targets) &&
        first.Steps.Count == second.Steps.Count && first.Steps.Zip(second.Steps, (left, right) =>
            left.StepIndex == right.StepIndex && left.Deferred == right.Deferred && SameTargets(left.Targets, right.Targets) &&
            SameGroups(left.Groups, right.Groups) &&
            left.BranchKnown == right.BranchKnown && left.RunsOtherwise == right.RunsOtherwise &&
            (!left.BranchKnown || SameTargets(left.ThenTargets, right.ThenTargets) &&
                SameTargets(left.OtherwiseTargets, right.OtherwiseTargets))).All(same => same);

    private static void RecordExecution(ScriptRuntimeState runtime, WorldScriptExecutionContext context,
        WorldScriptPreviewResult preview, string outcome, bool manual, double elapsedMilliseconds,
        string batchID = null, int? batchSpawns = null)
    {
        runtime.History ??= new List<ScriptExecutionRecord>();
        ScriptExecutionRecord previous = runtime.History.LastOrDefault();
        int? matches = preview?.IsValid == true && preview.GatePassed ? preview.MatchCount : null;
        int? selected = matches.HasValue ? preview.Targets.Count : null;
        string message = ScriptStepExecution.Limit(runtime.LastError, 1024);
        var subject = EventSubject(context.Event);
        if (batchID == null && !manual && context.ActionCount == 0 && previous != null && !previous.Manual &&
            previous.Outcome == outcome && previous.TriggerID == context.TriggerID &&
            previous.Message == message && previous.WorldTime <= context.WorldTime &&
            previous.MatchCount == matches && previous.SelectedCount == selected &&
            previous.Subject?.Kind == subject?.Kind && previous.Subject?.MetaID == subject?.MetaID &&
            previous.Subject?.ActorID == subject?.ActorID &&
            (previous.Steps ?? Enumerable.Empty<ScriptStepExecution>()).Select(StepResult)
                .SequenceEqual(context.StepDetails.Select(StepResult)) &&
            context.WorldTime - previous.WorldTime < 1d) return;
        runtime.History.Add(new ScriptExecutionRecord
        {
            WorldTime = context.WorldTime,
            TriggerID = context.TriggerID,
            Manual = manual,
            Outcome = outcome,
            TargetCount = runtime.LastTargetCount,
            ActionCount = context.CompletedActionCount,
            SpawnCount = batchSpawns ?? context.SpawnCount,
            Message = message,
            MatchCount = matches,
            SelectedCount = selected,
            Steps = context.StepDetails,
            OmittedSteps = context.OmittedSteps,
            Subject = subject, Cause = context.Event?.Cause, BatchID = batchID,
            DurationMilliseconds = elapsedMilliseconds, Limits = context.Limits
        });
        if (runtime.History.Count > 20) runtime.History.RemoveRange(0, runtime.History.Count - 20);

        static (int, string, int, int?, int?) StepResult(ScriptStepExecution step) =>
            step == null ? default : (step.Step, step.Outcome, step.InputCount, step.ThenCount, step.OtherwiseCount);
    }

    private static void ExecuteActions(IEnumerable<ScriptAction> actions, WorldScriptExecutionContext context,
        WorldScriptTarget target, Dictionary<ScriptAction, ScriptStepExecution> details)
    {
        foreach (ScriptAction action in actions ?? Enumerable.Empty<ScriptAction>())
        {
            if (action == null || !action.Enabled) continue;
            if (action.Operator == "sequence")
            {
                ExecuteActions(action.Children, context, target, details);
                continue;
            }
            ActionDefinition definition = WorldScriptRegistry.GetAction(action.ActionID) ??
                                          throw new InvalidOperationException($"Unknown action '{action.ActionID}'");
            if (!details.TryGetValue(action, out ScriptStepExecution step))
                details.Add(action, step = BeginStep(context, details.Count + 1, action.ActionID));
            context.CurrentStep = step?.Step ?? 0;
            if (step != null)
            {
                step.InputCount++;
                step.Sample(target);
            }
            try
            {
                bool applied = ExecuteTracedAction(definition, context, target, action.Parameters, step, null, 1);
                if (step != null) step.Outcome = applied || step.Outcome == "completed" ? "completed" : "no_change";
            }
            catch (Exception exception)
            {
                FailStep(step, exception);
                throw;
            }
        }
    }

    private static ScriptStepExecution BeginStep(WorldScriptExecutionContext context, int index, string actionID)
    {
        if (context.StepDetails.Count >= 40)
        {
            context.OmittedSteps++;
            return null;
        }
        var step = new ScriptStepExecution { Step = index, ActionID = actionID };
        context.StepDetails.Add(step);
        return step;
    }

    private static void FailStep(ScriptStepExecution step, Exception exception)
    {
        if (step == null) return;
        step.Outcome = step.Actions.Any(action => action.ActionCount > 0 || action.SpawnCount > 0) ? "partial" : "failed";
        step.Error = ScriptStepExecution.Limit(exception.Message, 1024);
    }

    private static bool ExecuteTracedAction(ActionDefinition definition, WorldScriptExecutionContext context,
        WorldScriptTarget target, JObject values, ScriptStepExecution step, string branch, int targetCount)
    {
        ScriptActionExecution detail = null;
        if (step != null)
        {
            detail = step.Actions.FirstOrDefault(item => item.Branch == branch);
            if (detail == null)
            {
                detail = new ScriptActionExecution { ActionID = definition.ID, Branch = branch };
                step.Actions.Add(detail);
            }
        }
        int originalSpawns = context.SpawnCount;
        context.EmittedMessage = null;
        context.SkippedReason = null;
        context.ChangeAmount = null;
        context.WriteRecord = null;
        try
        {
            bool applied = ExecuteAction(definition, context, target, values);
            if (detail == null) return applied;
            if (context.WriteRecord != null && detail.Writes?.Count != 32)
            {
                context.WriteRecord.Reason = context.SkippedReason;
                (detail.Writes ??= new List<ScriptWriteRecord>()).Add(context.WriteRecord);
            }
            if (!applied)
            {
                detail.SkippedCount++;
                detail.SkippedReason = ScriptStepExecution.Limit(context.SkippedReason, 200);
                return false;
            }
            detail.ActionCount++;
            detail.TargetCount += targetCount;
            if (context.ChangeAmount.HasValue) detail.ChangeAmount = (detail.ChangeAmount ?? 0) + context.ChangeAmount.Value;
            if (context.EmittedMessage != null)
            {
                detail.MessageTruncated = context.EmittedMessage.Length > 1024;
                detail.Message = ScriptStepExecution.Limit(context.EmittedMessage, 1024);
            }
            return true;
        }
        finally
        {
            if (detail != null) detail.SpawnCount += context.SpawnCount - originalSpawns;
            context.EmittedMessage = null;
        }
    }

    private static bool ExecuteAction(ActionDefinition definition, WorldScriptExecutionContext context,
        WorldScriptTarget target, JObject parameters)
    {
        if (!context.TryTakeAction()) throw new InvalidOperationException("Action budget exhausted");
        if (definition.ChangesActorStats && target.Actor != null) SuppressNextActorStatsUpdate(target.Actor);
        var previous = _activeCause;
        _activeCause = new ScriptEventCause
        {
            ScriptID = context.Script.ID, ScriptName = context.Script.Name, Step = context.CurrentStep
        };
        try { definition.Execute(context, target, parameters); }
        finally { _activeCause = previous; }
        if (context.SkippedReason != null)
        {
            context.HadSkippedActions = true;
            return false;
        }
        context.CompletedActionCount++;
        return true;
    }

    private static WorldScriptPreviewResult ResolveTargets(ScriptInstance script, WorldScriptExecutionContext context,
        Random random)
    {
        var result = new WorldScriptPreviewResult { Event = context.Event };
        if (context.InspectPreview) context.Preview = result;
        WorldScriptValidationResult validation = Validate(script);
        result.Errors.AddRange(validation.Errors);
        if (!validation.IsValid) return result;
        if (World.world == null)
        {
            result.Errors.Add("No world is loaded");
            return result;
        }
        if (UsesEventActor(context.TriggerID) && (script.Flow == null ? context.EventActor?.data == null : context.Event == null))
        {
            result.Errors.Add("Choose an actor to supply the event context");
            return result;
        }
        if (UsesKingdomEvent(context.TriggerID) && context.Event == null)
        {
            result.Errors.Add("Choose a village or kingdom to supply the event context");
            return result;
        }
        if (context.TriggerID == WorldScriptRegistry.TriggerRegionOwnership && !InTriggerRegion(script, context.Event))
        {
            result.GatePassed = false;
            return result;
        }
        if (script.Flow != null) return ResolveFlow(script, context, random, result);

        TargetDefinition definition = WorldScriptRegistry.GetTarget(script.TargetQuery.TargetID);
        IEnumerable<WorldScriptTarget> query;
        try
        {
            query = definition.Resolve(context, script.TargetQuery.Parameters) ?? Enumerable.Empty<WorldScriptTarget>();
            query = query.Where(value => value != null && value.Kind != ScriptTargetKind.None);
            query = query.Where(target => PassesPreviewFilter(script.Conditions, context, target));
            var matches = Distinct(query).ToList();
            result.MatchCount = matches.Count;
            query = GroupAndOrder(matches, script.GroupMode, script.SortMode, random);
            query = Sample(query, script.SampleMode, script.SampleCount, random);
            result.Targets.AddRange(query.Take(Math.Max(script.MaxTargets, 0)));
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
            return result;
        }

        if (result.Targets.Count == 0 && script.EmptyTargetPolicy == "error")
            result.Errors.Add("Target query returned no targets");
        else if (result.Targets.Count == 0 && script.EmptyTargetPolicy == "run_once" &&
                 definition.Kind == ScriptTargetKind.World && script.MaxTargets > 0)
            result.Targets.Add(WorldScriptTarget.ForWorld());
        return result;
    }

    private static IEnumerable<WorldScriptTarget> Distinct(IEnumerable<WorldScriptTarget> targets)
    {
        var seenActors = new HashSet<long>();
        var seenTiles = new HashSet<WorldTile>();
        var seenSettlements = new HashSet<(ScriptTargetKind, long)>();
        bool seenWorld = false;
        foreach (WorldScriptTarget target in targets)
        {
            bool unique = target.Kind switch
            {
                ScriptTargetKind.World => !seenWorld,
                ScriptTargetKind.Actor => target.Actor?.data != null && seenActors.Add(target.Actor.data.id),
                ScriptTargetKind.Tile => target.Tile != null && seenTiles.Add(target.Tile),
                ScriptTargetKind.City or ScriptTargetKind.Kingdom => target.Meta != null && seenSettlements.Add((target.Kind, target.MetaID)),
                _ => false
            };
            if (!unique) continue;
            if (target.Kind == ScriptTargetKind.World) seenWorld = true;
            yield return target;
        }
    }

    private static IEnumerable<WorldScriptTarget> GroupAndOrder(IEnumerable<WorldScriptTarget> targets,
        string groupMode, string sortMode, Random random)
    {
        Func<WorldScriptTarget, string> groupKey = groupMode switch
        {
            "kind" => target => target.Kind.ToString(),
            "actor_asset" => target => target.Actor?.asset?.id ?? string.Empty,
            _ => _ => string.Empty
        };
        IEnumerable<IGrouping<string, WorldScriptTarget>> groups = targets.GroupBy(groupKey);
        foreach (IGrouping<string, WorldScriptTarget> group in groups.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            IEnumerable<WorldScriptTarget> ordered = sortMode switch
            {
                "actor_id" => group.OrderBy(value => value.Actor?.data?.id ?? long.MinValue),
                "random" => group.OrderBy(_ => random.Next()),
                _ => group
            };
            foreach (WorldScriptTarget target in ordered) yield return target;
        }
    }

    private static IEnumerable<WorldScriptTarget> Sample(IEnumerable<WorldScriptTarget> targets, string mode,
        int requested, Random random)
    {
        if (mode == "all" || requested <= 0) return targets;
        return mode == "random"
            ? targets.OrderBy(_ => random.Next()).Take(requested)
            : targets.Take(requested);
    }

    private static int PreviewSeed(ScriptInstance script)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in script?.ID ?? string.Empty)
            {
                hash ^= value;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private static bool PassesPreviewFilter(ConditionNode rule, WorldScriptExecutionContext context, WorldScriptTarget target,
        WorldScriptTarget group = null)
    {
        bool passed = EvaluateNode(rule, context, target, out var rejecting);
        if (!passed) context.NotePreviewDecision(target, group, "filter", rejecting);
        return passed;
    }

    private static bool EvaluateNode(ConditionNode node, WorldScriptExecutionContext context, WorldScriptTarget target) =>
        EvaluateNode(node, context, target, out _);

    private static bool EvaluateNode(ConditionNode node, WorldScriptExecutionContext context, WorldScriptTarget target,
        out ConditionNode rejecting)
    {
        rejecting = null;
        if (node == null) return true;
        string op = node.Operator ?? "all";
        if (op == "condition")
        {
            if (EvaluateLeaf(node.ConditionID, node.Parameters, context, target)) return true;
            rejecting = node;
            return false;
        }

        var children = node.Children ?? Enumerable.Empty<ConditionNode>();
        if (op == "not" || op == "none")
        {
            var candidates = op == "not" ? children.Take(1) : children;
            if (candidates.All(child => !EvaluateNode(child, context, target))) return true;
            rejecting = node;
            return false;
        }
        if (op == "any")
        {
            if (children.Any(child => EvaluateNode(child, context, target))) return true;
            rejecting = node;
            return false;
        }
        foreach (var child in children)
            if (!EvaluateNode(child, context, target, out rejecting)) return false;
        return true;
    }

    private static bool EvaluateLeaf(string id, JObject parameters, WorldScriptExecutionContext context,
        WorldScriptTarget target)
    {
        ConditionDefinition definition = WorldScriptRegistry.GetCondition(id) ??
                                         throw new InvalidOperationException($"Unknown condition '{id}'");
        return (definition.AllowedTargets & target.Kind) != 0 && definition.Evaluate(context, target, parameters);
    }

    private static void ValidateCondition(ConditionNode node, ScriptTargetKind kind, List<string> errors, string path)
    {
        if (node == null) return;
        if (node.Operator == "condition")
        {
            ValidateConditionLeaf(node.ConditionID, node.Parameters, kind, errors, path);
            return;
        }
        if (!new[] { "all", "any", "none", "not" }.Contains(node.Operator))
            errors.Add($"Unknown condition operator '{node.Operator}' at {path}");
        if (node.Operator == "not" && (node.Children?.Count ?? 0) != 1)
            errors.Add($"Operator 'not' requires one child at {path}");
        for (int i = 0; i < (node.Children?.Count ?? 0); i++)
            ValidateCondition(node.Children[i], kind, errors, $"{path}[{i}]");
    }

    private static void ValidateConditionLeaf(string id, JObject parameters, ScriptTargetKind kind,
        List<string> errors, string path)
    {
        ConditionDefinition definition = WorldScriptRegistry.GetCondition(id);
        if (definition == null)
        {
            errors.Add($"Unknown condition '{id}' at {path}");
            return;
        }
        if ((definition.AllowedTargets & kind) == 0)
            errors.Add($"Condition '{id}' cannot run on {kind}");
        AddParameterErrors(errors, $"condition '{id}'", parameters, definition.Parameters, definition.ID);
    }

    private static void ValidateActions(IEnumerable<ScriptAction> actions, ScriptInstance script,
        ScriptTargetKind kind, List<string> errors, string path)
    {
        int index = 0;
        foreach (ScriptAction action in actions ?? Enumerable.Empty<ScriptAction>())
        {
            string itemPath = $"{path}[{index++}]";
            if (action == null || !action.Enabled) continue;
            if (action.Operator == "sequence")
            {
                ValidateActions(action.Children, script, kind, errors, itemPath + ".children");
                continue;
            }
            if (action.Operator != "action")
            {
                errors.Add($"Unknown action operator '{action.Operator}' at {itemPath}");
                continue;
            }
            ActionDefinition definition = WorldScriptRegistry.GetAction(action.ActionID);
            if (definition == null)
            {
                errors.Add($"Unknown action '{action.ActionID}' at {itemPath}");
                continue;
            }
            if ((definition.AllowedTargets & kind) == 0)
                errors.Add($"Action '{action.ActionID}' cannot run on {kind}");
            if (definition.Dangerous && !script.AllowDangerousActions)
                errors.Add($"Dangerous action '{action.ActionID}' is not enabled");
            AddParameterErrors(errors, $"action '{action.ActionID}'", action.Parameters, definition.Parameters, definition.ID);
        }
    }

    private static void AddParameterErrors(List<string> errors, string owner, JObject values,
        ScriptParameterDefinition[] definitions, string definitionID = null)
    {
        foreach (string error in WorldScriptRegistry.ValidateParameters(values, definitions))
            errors.Add($"{owner}: {error}");
        if (definitionID != null)
            foreach (string error in WorldScriptRegistry.ValidateReferences(definitionID, values))
                errors.Add($"{owner}: {error}");
    }
}
