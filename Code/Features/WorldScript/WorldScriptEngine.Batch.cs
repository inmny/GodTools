using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    private static readonly HashSet<string> QueuedBatches = new();

    private static bool CanBatch(ScriptInstance script) =>
        (script.TriggerCondition?.TriggerID is WorldScriptRegistry.TriggerManual or WorldScriptRegistry.TriggerInterval
            or WorldScriptRegistry.TriggerWorldTimeReached or WorldScriptRegistry.TriggerWorldLoaded) &&
        script.Flow?.Steps.Where(step => step?.Action?.Enabled == true)
            .All(step => step.Source == "query" && step.Mode == "each" && step.Relation == null && !step.HasBranch) == true;

    private static string BatchDefinition(ScriptInstance script)
    {
        var settings = new JObject
        {
            ["flow"] = JObject.FromObject(script.Flow),
            ["trigger"] = script.TriggerCondition?.TriggerID ?? "",
            ["batchSize"] = script.TargetsPerBatch,
            ["maxSpawns"] = script.MaxSpawns,
            ["allowDangerous"] = script.AllowDangerousActions,
            ["scenario"] = Scenario(script.ScenarioID)?.Parameters is { } parameters ? JObject.FromObject(parameters) : null
        };
        using var integrity = SHA256.Create();
        return Convert.ToBase64String(integrity.ComputeHash(Encoding.UTF8.GetBytes(settings.ToString(Formatting.None))));
    }

    private static void StartBatch(ScriptInstance script, WorldScriptExecutionContext context, WorldScriptPreviewResult preview, bool manual)
    {
        var saved = WorldScripts.Scripts.Find(item => item?.ID == script.ID);
        string definition = BatchDefinition(script);
        if (saved?.Flow == null || BatchDefinition(saved) != definition || saved.Runtime.Batch != null)
            throw new InvalidOperationException("Apply script changes before queueing a batch");
        script.Runtime = saved.Runtime;
        script.Runtime.Batch = new ScriptBatchJob
        {
            Definition = definition, TriggerID = context.TriggerID, Manual = manual,
            Targets = preview.Targets.Select(ScriptTargetSample.From).ToList(),
            Steps = preview.Steps.Select(step => step.StepIndex).ToList(), MatchCount = preview.MatchCount
        };
    }

    private static void QueueBatches()
    {
        foreach (var script in WorldScripts.Scripts)
        {
            var job = script.Runtime?.Batch;
            if (job == null || !job.Manual && !script.Enabled || Due.Count >= MaxQueuedEvents) continue;
            if (!QueuedBatches.Add(job.ID)) continue;
            Due.Enqueue(new TriggerRequest { ID = job.TriggerID, BatchID = job.ID, ScheduleOwner = script.ID });
        }
    }

    private static bool RunBatchSlice(string scriptID, string jobID)
    {
        QueuedBatches.Remove(jobID);
        var script = WorldScripts.Scripts.Find(item => item?.ID == scriptID);
        var job = script?.Runtime?.Batch;
        if (job?.ID != jobID || !job.Manual && !script.Enabled) return false;

        var context = CreateContext(script, job.TriggerID, null, MaxActionsPerEvent);
        var plan = new WorldScriptPreviewResult { MatchCount = job.MatchCount };
        int initialSpawns = job.Spawned;
        var timer = Stopwatch.StartNew();
        string outcome = "batch_stopped";
        try
        {
            var validation = Validate(script);
            if (script.TargetsPerBatch <= 0 || !validation.IsValid || BatchDefinition(script) != job.Definition)
                throw new InvalidOperationException("Batch stopped because its rules or settings changed");
            if (job.Steps?.Count is not > 0 || job.Targets?.Count is not > 0 ||
                job.Step < 0 || job.Step >= job.Steps.Count || job.Offset < 0 || job.Offset >= job.Targets.Count)
                throw new InvalidOperationException("Batch cursor is invalid");

            int length = Math.Min(script.TargetsPerBatch, job.Targets.Count - job.Offset);
            var slice = new WorldScriptStepPreview { StepIndex = job.Steps[job.Step] };
            for (int i = job.Offset; i < job.Offset + length; i++)
            {
                var target = RestoreBatchTarget(job.Targets[i]);
                if (target != null && IsUsable(target, context)) slice.Targets.Add(target);
            }
            plan.Targets.AddRange(slice.Targets);
            plan.Steps.Add(slice);
            context.MainMatchCount = job.MatchCount;
            context.SpawnCount = job.Spawned;
            script.Runtime.LastTargetCount = 0;
            ExecuteFlow(script, context, plan);
            job.Spawned = context.SpawnCount;
            job.Applied += script.Runtime.LastTargetCount;
            job.Offset += length;
            if (job.Offset == job.Targets.Count)
            {
                job.Offset = 0;
                job.Step++;
            }
            bool finished = job.Step == job.Steps.Count;
            outcome = finished ? "batch_completed" : "batch_slice";
            script.Runtime.LastSpawnCount = job.Spawned;
            script.Runtime.LastError = null;
            if (finished)
            {
                script.Runtime.Batch = null;
                if (!job.Manual)
                {
                    script.Runtime.LastRunWorldTime = context.WorldTime;
                    script.Runtime.RunCount++;
                }
            }
        }
        catch (Exception exception)
        {
            script.Runtime.Batch = null;
            script.Runtime.LastError = exception.Message;
            script.Runtime.LastRunWorldTime = context.WorldTime;
        }
        RecordExecution(script.Runtime, context, plan, outcome, job.Manual, timer.Elapsed.TotalMilliseconds,
            job.ID, Math.Max(0, context.SpawnCount - initialSpawns));
        WorldScripts.NotifyRuntimeChanged(script);
        return true;
    }

    internal static void CancelBatch(ScriptInstance script)
    {
        if (script?.Runtime?.Batch is not { } job) return;
        script.Runtime.Batch = null;
        script.Runtime.LastAttemptWorldTime = World.world.getCurWorldTime();
        var context = CreateContext(script, job.TriggerID, null, MaxActionsPerEvent);
        RecordExecution(script.Runtime, context, null, "batch_cancelled", true, 0d, job.ID);
        WorldScripts.NotifyRuntimeChanged(script);
    }

    private static WorldScriptTarget RestoreBatchTarget(ScriptTargetSample subject) => subject?.Kind switch
    {
        ScriptTargetKind.Actor => World.world.units.get(subject.ActorID) is Actor actor ? WorldScriptTarget.ForActor(actor) : null,
        ScriptTargetKind.City => World.world.cities.get(subject.MetaID) is City city ? WorldScriptTarget.ForCity(city) : null,
        ScriptTargetKind.Kingdom => World.world.kingdoms.get(subject.MetaID) is Kingdom kingdom ? WorldScriptTarget.ForKingdom(kingdom) : null,
        ScriptTargetKind.Tile => World.world.GetTile(subject.X, subject.Y) is WorldTile tile ? WorldScriptTarget.ForTile(tile) : null,
        ScriptTargetKind.World => WorldScriptTarget.ForWorld(),
        _ => null
    };
}
