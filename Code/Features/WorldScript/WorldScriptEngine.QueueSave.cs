using System.Collections.Generic;

namespace GodTools.Features.WorldScript;

public sealed class ScriptQueuedEvent
{
    public string Trigger;
    public string TargetScript;
    public string[] RemainingScripts;
    public long? City;
    public string CityName;
    public long? Kingdom;
    public string KingdomName;
    public long? Previous;
    public string PreviousName;
    public long? Next;
    public string NextName;
    public int? X;
    public int? Y;
    public string WarName;
    public int? Value;
    public double Time;
    public string Era;
    public ScriptEventCause Cause;
    public long? SceneActor;
    public string SceneActorName;
}

public static partial class WorldScriptEngine
{
    private static bool SavesEvent(string trigger) => trigger is WorldScriptRegistry.TriggerCityKingdomChanged
        or WorldScriptRegistry.TriggerKingdomKingChanged or WorldScriptRegistry.TriggerWarEnded
        or WorldScriptRegistry.TriggerCityThreshold or WorldScriptRegistry.TriggerRegionOwnership;

    internal static List<ScriptQueuedEvent> SaveQueuedEvents()
    {
        var result = new List<ScriptQueuedEvent>();
        Add(_dispatch);
        foreach (var request in Pending.ToArray()) Add(request);
        return result.Count == 0 ? null : result;

        void Add(TriggerRequest request)
        {
            if (request?.Event is not WorldScriptEvent snapshot) return;
            if (!SavesEvent(request.ID))
            {
                if (request.ID == WorldScriptRegistry.TriggerActorDied &&
                    WorldScriptScenes.WatchesDeath(snapshot))
                    result.Add(new ScriptQueuedEvent
                    {
                        Trigger = request.ID, RemainingScripts = System.Array.Empty<string>(), SceneActor = snapshot.ActorID,
                        SceneActorName = snapshot.ActorName, Kingdom = snapshot.KingdomID, KingdomName = snapshot.KingdomName,
                        X = snapshot.X, Y = snapshot.Y, Time = snapshot.WorldTime, Era = snapshot.EraID, Cause = snapshot.Cause
                    });
                return;
            }
            string[] remaining = request.ScriptIDs == null ? null : request.ScriptIDs[request.NextScript..];
            if (remaining?.Length == 0) return;
            result.Add(new ScriptQueuedEvent
            {
                Trigger = request.ID, TargetScript = request.TargetScriptID, RemainingScripts = remaining,
                City = snapshot.CityID, CityName = snapshot.CityName, Kingdom = snapshot.KingdomID, KingdomName = snapshot.KingdomName,
                Previous = snapshot.PreviousID, PreviousName = snapshot.PreviousName, Next = snapshot.NextID, NextName = snapshot.NextName,
                X = snapshot.X, Y = snapshot.Y, WarName = snapshot.WarName, Value = snapshot.Value, Time = snapshot.WorldTime,
                Era = snapshot.EraID, Cause = snapshot.Cause
            });
        }
    }

    internal static void RestoreQueuedEvents(List<ScriptQueuedEvent> events)
    {
        if (events == null) return;
        foreach (var saved in events)
        {
            if (Pending.Count >= MaxQueuedEvents) break;
            if (saved == null || !SavesEvent(saved.Trigger) &&
                (saved.Trigger != WorldScriptRegistry.TriggerActorDied || !saved.SceneActor.HasValue)) continue;
            Pending.Enqueue(new TriggerRequest
            {
                ID = saved.Trigger, TargetScriptID = saved.TargetScript,
                ScriptIDs = saved.SceneActor.HasValue ? System.Array.Empty<string>() : saved.RemainingScripts,
                Event = new WorldScriptEvent(saved)
            });
        }
    }
}
