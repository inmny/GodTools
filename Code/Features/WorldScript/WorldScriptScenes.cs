using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features;
using UnityEngine;

namespace GodTools.Features.WorldScript;

public enum ScriptSceneMode { Manual, Automatic, Remind, Pause }

public sealed class ScriptScene
{
    public string ID = Guid.NewGuid().ToString("N");
    public string Name;
    public int? Year;
    public ScriptSceneMode Mode;
    public string StartScriptID;
    public ScriptTargetSample Actor;
    public ScriptTargetSample Location;
    public ScriptTargetSample Village;
    public ScriptTargetSample Kingdom;
    public bool WatchDeath;
    public bool WatchOwnership;
    public bool WatchRuler;
    public double? AnnouncedAt;
    public double? StartedAt;
    public List<ScriptSceneMark> Marks = new();
}

public sealed class ScriptSceneMark
{
    public string Kind;
    public string Name;
    public double Time;
    public ScriptTargetSample Subject;
    public int? X;
    public int? Y;
    public string Previous;
    public string Next;
    public string Outcome;
    public ScriptEventCause Cause;
}

internal static class WorldScriptScenes
{
    internal static IEnumerable<ScriptScene> List(ScriptScenario scenario) =>
        scenario?.Scenes ?? Enumerable.Empty<ScriptScene>();

    internal static ScriptScene Current(ScriptScenario scenario) =>
        scenario?.Scenes?.FindLast(scene => scene.StartedAt.HasValue);

    internal static ScriptScene Next(ScriptScenario scenario) =>
        List(scenario).Where(scene => !scene.StartedAt.HasValue)
            .OrderBy(scene => scene.Year ?? int.MaxValue).FirstOrDefault();

    internal static event Action Changed;
    internal static void NotifyChanged() => Changed?.Invoke();

    internal static void Normalize(ScriptScenario scenario)
    {
        if (scenario.Scenes == null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        scenario.Scenes.RemoveAll(scene => string.IsNullOrWhiteSpace(scene?.ID) || !ids.Add(scene.ID));
        foreach (var scene in scenario.Scenes)
        {
            scene.Marks ??= new List<ScriptSceneMark>();
            scene.Marks.RemoveAll(mark => mark == null);
        }
    }

    internal static void Tick()
    {
        double time = World.world.getCurWorldTime();
        int year = Date.getYear(time);
        foreach (var scenario in WorldScripts.Scenarios)
        {
            var due = List(scenario).Where(scene => !scene.StartedAt.HasValue && !scene.AnnouncedAt.HasValue &&
                scene.Mode != ScriptSceneMode.Manual && scene.Year is int planned && planned <= year)
                .OrderBy(scene => scene.Year).FirstOrDefault();
            if (due == null) continue;
            due.AnnouncedAt = time;
            if (due.Mode == ScriptSceneMode.Automatic)
                Start(scenario, due);
            else
            {
                if (due.Mode == ScriptSceneMode.Pause) Config.paused = true;
                WorldTip.showNow(due.Name, false, "top", 5f);
                NotifyChanged();
            }
            if (Config.paused) break;
        }
    }

    internal static IEnumerable<ScriptInstance> StartScripts(ScriptScenario scenario) =>
        WorldScripts.Scripts.Where(script => script.ScenarioID == scenario.ID &&
            script.TriggerCondition?.TriggerID == WorldScriptRegistry.TriggerManual);

    internal static void Start(ScriptScenario scenario, ScriptScene scene)
    {
        if (scene.StartedAt.HasValue || World.world == null || SmoothLoader.isLoading()) return;
        scene.StartedAt = World.world.getCurWorldTime();
        // Keep the last scene started as the active one, including while the world is paused.
        scenario.Scenes.Remove(scene);
        scenario.Scenes.Add(scene);
        var mark = AddMark(scene, "start", scene.Name);
        if (!string.IsNullOrEmpty(scene.StartScriptID))
        {
            var script = StartScripts(scenario).FirstOrDefault(item => item.ID == scene.StartScriptID);
            if (script == null) mark.Outcome = "missing_script";
            else
            {
                mark.Cause = new ScriptEventCause { ScriptID = script.ID, ScriptName = script.Name };
                WorldScriptEngine.RunNow(script);
                mark.Outcome = script.Runtime.History.LastOrDefault()?.Outcome ?? "failed";
            }
        }
        NotifyChanged();
    }

    internal static ScriptSceneMark AddMark(ScriptScene scene, string kind, string name)
    {
        Actor actor = LiveActor(scene.Actor);
        var mark = new ScriptSceneMark
        {
            Kind = kind, Name = name, Time = World.world.getCurWorldTime(), Subject = scene.Actor,
            X = scene.Location?.X ?? actor?.current_tile?.x, Y = scene.Location?.Y ?? actor?.current_tile?.y
        };
        scene.Marks.Add(mark);
        return mark;
    }

    private static bool Watches(ScriptScene scene, string trigger, WorldScriptEvent snapshot) =>
        scene?.StartedAt is double started && started <= snapshot.WorldTime &&
        !scene.Marks.Any(mark => mark.Kind == trigger) &&
        (trigger switch
        {
            WorldScriptRegistry.TriggerActorDied => scene.WatchDeath && scene.Actor?.ActorID == snapshot.ActorID,
            WorldScriptRegistry.TriggerCityKingdomChanged => scene.WatchOwnership && scene.Village != null &&
                scene.Village.MetaID == snapshot.CityID,
            WorldScriptRegistry.TriggerKingdomKingChanged => scene.WatchRuler && scene.Kingdom != null &&
                scene.Kingdom.MetaID == snapshot.KingdomID && snapshot.NextID.HasValue,
            _ => false
        });

    internal static bool WatchesDeath(WorldScriptEvent snapshot) =>
        WorldScripts.Scenarios.Any(scenario => Watches(Current(scenario), WorldScriptRegistry.TriggerActorDied, snapshot));

    internal static void Observe(string trigger, WorldScriptEvent snapshot)
    {
        if (snapshot == null || trigger is not (WorldScriptRegistry.TriggerActorDied or
            WorldScriptRegistry.TriggerCityKingdomChanged or WorldScriptRegistry.TriggerKingdomKingChanged)) return;
        bool changed = false;
        foreach (var scenario in WorldScripts.Scenarios)
        {
            var scene = Current(scenario);
            if (!Watches(scene, trigger, snapshot)) continue;
            var subject = trigger switch
            {
                WorldScriptRegistry.TriggerActorDied => new ScriptTargetSample
                    { Kind = ScriptTargetKind.Actor, ActorID = snapshot.ActorID, Name = snapshot.ActorName },
                WorldScriptRegistry.TriggerCityKingdomChanged => new ScriptTargetSample
                    { Kind = ScriptTargetKind.City, MetaID = snapshot.CityID.Value, Name = snapshot.CityName },
                _ => new ScriptTargetSample
                    { Kind = ScriptTargetKind.Actor, ActorID = snapshot.NextID.Value, Name = snapshot.NextName }
            };
            scene.Marks.Add(new ScriptSceneMark
            {
                Kind = trigger, Subject = subject, Name = snapshot.KingdomName,
                Time = snapshot.WorldTime, X = snapshot.X, Y = snapshot.Y,
                Previous = snapshot.PreviousName, Next = snapshot.NextName, Cause = snapshot.Cause
            });
            changed = true;
        }
        if (changed) NotifyChanged();
    }

    internal static Actor LiveActor(ScriptTargetSample subject) =>
        subject?.Kind == ScriptTargetKind.Actor && World.world?.units.get(subject.ActorID) is Actor actor &&
        actor.data?.id == subject.ActorID && actor.isAlive() ? actor : null;

    internal static void Locate(int? x, int? y, float zoom = 15f)
    {
        if (x is not int tileX || y is not int tileY || World.world?.GetTile(tileX, tileY) is not WorldTile tile)
            return;
        World.world.locatePosition(new Vector3(tile.x, tile.y, 0));
        World.world.move_camera.forceZoom(zoom);
    }
}
