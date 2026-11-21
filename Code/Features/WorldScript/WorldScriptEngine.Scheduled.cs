using System;
using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    private static readonly HashSet<string> QueuedDelayedRuns = new();

    internal static void Schedule(WorldScriptExecutionContext context, WorldScriptTarget target, string scriptID,
        double delay, bool replacePending, bool cancelOnOwnershipChange, string cancelPhase)
    {
        var followUp = WorldScripts.Scripts.Find(script => script?.ID == scriptID);
        if (followUp?.TriggerCondition?.TriggerID != WorldScriptRegistry.TriggerDelayed)
        {
            context.SkipAction("follow_up_unavailable");
            return;
        }
        if (!(delay >= 0.01d) || double.IsInfinity(delay))
        {
            context.SkipAction("delay_invalid");
            return;
        }

        IMetaObject subject = target.Meta ?? context.GroupTarget?.Meta ?? (IMetaObject)context.Event?.City ?? context.Event?.Kingdom;
        long? city = (subject as City)?.id;
        long? kingdom = (subject as Kingdom)?.id;
        var entries = context.Script.Runtime.DelayedRuns ??= new List<ScriptDelayedRun>();
        var entry = replacePending
            ? entries.Find(item => item.ScriptID == scriptID && item.CityID == city && item.KingdomID == kingdom) : null;
        if (entry == null)
        {
            if (entries.Count >= 256)
            {
                context.SkipAction("follow_up_limit");
                return;
            }
            entry = new ScriptDelayedRun { ScriptID = scriptID, CityID = city, KingdomID = kingdom };
            entries.Add(entry);
        }
        entry.SubjectName = subject?.name;
        entry.Cause = _activeCause;
        entry.CancelPhase = string.IsNullOrWhiteSpace(cancelPhase) ? null : cancelPhase.Trim();
        entry.RequiredKingdomID = cancelOnOwnershipChange ? (subject as City)?.kingdom?.id : null;
        entry.Due = context.WorldTime + Math.Max(0.01d, delay);
        WorldScripts.NotifyRuntimeChanged(context.Script);
    }

    private static void QueueDueRuns()
    {
        if (!WorldScripts.Scripts.Any(script => script?.Runtime?.DelayedRuns?.Count > 0)) return;
        double time = World.world.getCurWorldTime();
        var disabled = new HashSet<string>(WorldScripts.Scripts.Where(script => script?.Enabled == false).Select(script => script.ID));
        var due = WorldScripts.Scripts.Where(script => script?.Runtime?.DelayedRuns != null)
            .SelectMany(script => script.Runtime.DelayedRuns.Where(run => run != null && run.Due <= time &&
                !disabled.Contains(run.ScriptID) && !QueuedDelayedRuns.Contains(run.ID)).Select(run => (Owner: script.ID, Run: run)))
            .OrderBy(item => item.Run.Due).Take(32);
        foreach (var item in due)
        {
            if (Due.Count >= MaxQueuedEvents) break;
            if (!QueuedDelayedRuns.Add(item.Run.ID)) continue;
            Due.Enqueue(new TriggerRequest
            {
                ID = WorldScriptRegistry.TriggerDelayed, ScheduleOwner = item.Owner, ScheduleID = item.Run.ID
            });
        }
    }

    private static bool RunScheduled(string ownerID, string scheduleID)
    {
        QueuedDelayedRuns.Remove(scheduleID);
        var owner = WorldScripts.Scripts.Find(script => script?.ID == ownerID);
        var entries = owner?.Runtime?.DelayedRuns;
        var entry = entries?.Find(item => item?.ID == scheduleID);
        double time = World.world.getCurWorldTime();
        if (entry == null || entry.Due > time) return false;

        var script = WorldScripts.Scripts.Find(candidate => candidate?.ID == entry.ScriptID);
        if (script?.Enabled == false) return false;
        if (script?.TriggerCondition?.TriggerID != WorldScriptRegistry.TriggerDelayed)
        {
            FinishFollowUp(owner, entry, "missing_script");
            return false;
        }

        var city = entry.CityID is long cityID ? World.world.cities.get(cityID) : null;
        var kingdom = entry.KingdomID is long kingdomID ? World.world.kingdoms.get(kingdomID) : city?.kingdom;
        var snapshot = new WorldScriptEvent(city, kingdom, null, null, null, null, city?.getTile() ?? kingdom?.capital?.getTile())
        {
            Cause = entry.Cause
        };
        if (entry.CityID.HasValue && city?.isAlive() != true || entry.KingdomID.HasValue && kingdom?.isAlive() != true)
        {
            FinishFollowUp(owner, entry, "missing_subject");
            return false;
        }
        if (entry.RequiredKingdomID.HasValue && city?.kingdom?.id != entry.RequiredKingdomID)
        {
            FinishFollowUp(owner, entry, "ownership_changed");
            return false;
        }
        // Consume before running so the follow-up can create a new task for its own next check.
        entries.Remove(entry);
        bool ran = ExecuteTriggered(script, WorldScriptRegistry.TriggerDelayed, snapshot);
        FinishFollowUp(owner, entry, ran ? script.Runtime.History.LastOrDefault()?.Outcome ?? "completed" : "cooldown");
        return ran;
    }

    private static void CancelOnOwnershipChange(WorldScriptEvent change)
    {
        foreach (var owner in WorldScripts.Scripts.Where(script => script.Runtime?.DelayedRuns?.Count > 0))
        {
            foreach (var run in owner.Runtime.DelayedRuns.Where(run => run.CityID == change.CityID &&
                run.RequiredKingdomID.HasValue && run.RequiredKingdomID != change.NextID).ToArray())
                FinishFollowUp(owner, run, "ownership_changed");
        }
    }

    private static void FinishFollowUp(ScriptInstance owner, ScriptDelayedRun run, string outcome)
    {
        owner.Runtime.DelayedRuns?.Remove(run);
        var history = owner.Runtime.DelayedOutcomes ??= new List<ScriptDelayedOutcome>();
        double time = World.world.getCurWorldTime();
        history.Add(new ScriptDelayedOutcome { Run = run, Outcome = outcome, Time = time });
        if (history.Count > 32) history.RemoveAt(0);
        if (outcome is "missing_subject" or "ownership_changed" or "manual_cancel" &&
            !string.IsNullOrEmpty(run.CancelPhase) && Scenario(owner.ScenarioID) is ScriptScenario scenario)
        {
            var subject = ScheduledSubject(run);
            string address = run.CityID.HasValue ? $"city/{run.CityID}" : run.KingdomID.HasValue ? $"kingdom/{run.KingdomID}" : "world";
            SetScenarioPhase(scenario, address, subject, run.CancelPhase, owner.Name, time);
        }
        WorldScripts.NotifyRuntimeChanged(owner);
    }

    internal static void CancelFollowUp(ScriptInstance owner, ScriptDelayedRun run)
    {
        if (owner.Runtime.DelayedRuns?.Contains(run) == true) FinishFollowUp(owner, run, "manual_cancel");
    }

    internal static ScriptTargetSample ScheduledSubject(ScriptDelayedRun run)
    {
        if (run.CityID is long city)
            return new ScriptTargetSample { Kind = ScriptTargetKind.City, MetaID = city, Name = World.world.cities.get(city)?.name ?? run.SubjectName };
        if (run.KingdomID is long kingdom)
            return new ScriptTargetSample { Kind = ScriptTargetKind.Kingdom, MetaID = kingdom, Name = World.world.kingdoms.get(kingdom)?.name ?? run.SubjectName };
        return new ScriptTargetSample { Kind = ScriptTargetKind.World };
    }
}
