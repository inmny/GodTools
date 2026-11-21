using System;
using System.Globalization;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using GodTools.UI.Prefabs;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private ScriptScene _scene;
    private string _sceneName = "";
    private string _markName = "";
    private string _checkpointName = "";
    private string _checkpointScenarioID;
    private RawText _sceneClock;
    private int _displayedSceneYear;

    private void RefreshScenes()
    {
        if (gameObject.activeInHierarchy && _page is Page.Scenario or Page.Scene) _refresh = true;
    }

    private void RefreshSceneClock()
    {
        if (_page is not (Page.Scenario or Page.Scene) || _sceneClock == null || World.world?.map_stats == null) return;
        int year = Date.getYear(World.world.getCurWorldTime());
        if (year == _displayedSceneYear) return;
        _displayedSceneYear = year;
        _sceneClock.Text.text = SceneYear(year);
    }

    private static string SceneYear(int year) => string.Format(L("scene.year", "World year {0}"), year);
    private static string SceneMode(ScriptSceneMode mode) => L("scene.mode." + mode.ToString().ToLowerInvariant(), mode.ToString());
    private static string SceneState(ScriptScene scene) => scene.StartedAt.HasValue
        ? L("scene.started", "Started")
        : scene.AnnouncedAt.HasValue ? L("scene.awaiting", "Ready to start")
        : scene.Mode != ScriptSceneMode.Manual && scene.Year.HasValue ? SceneYear(scene.Year.Value) : SceneMode(ScriptSceneMode.Manual);

    private void BuildSceneClock()
    {
        _displayedSceneYear = Date.getYear(World.world?.getCurWorldTime() ?? 0d);
        _sceneClock = Label(_body, SceneYear(_displayedSceneYear), EditorWidth, 16, TitleColor);
    }

    private void BuildSceneList(ScriptScenario scenario)
    {
        BuildSceneClock();
        var current = WorldScriptScenes.Current(scenario);
        var next = WorldScriptScenes.Next(scenario);
        if (current != null) Message(string.Format(L("scene.current", "Current · {0}"), current.Name), false);
        if (next != null) Message(string.Format(L("scene.next", "Next · {0} · {1}"), next.Name, SceneState(next)), false);
        foreach (var scene in WorldScriptScenes.List(scenario).OrderBy(scene => scene.Year ?? int.MaxValue))
            ButtonAt(_body, $"{scene.Name} · {SceneState(scene)}", EditorWidth, () =>
            {
                _scene = scene;
                _checkpointName = _markName = "";
                Enter(Page.Scene);
            }, NativeIcon(scene == current ? "iconPlay" : "iconClock"), surface: Surface.List, selected: scene == current);
        var controls = Row(_body, EditorWidth, 24);
        ButtonAt(controls, L("scene.add", "New scene"), 128, () =>
        {
            _scene = new ScriptScene { Name = L("scene.untitled", "Untitled scene") };
            (scenario.Scenes ??= new()).Add(_scene);
            _markName = _checkpointName = "";
            Enter(Page.Scene);
        }, NativeIcon("iconShovelPlus"), surface: Surface.Primary);
        ButtonAt(controls, L("scene.checkpoints", "Checkpoints"), 136, () => OpenCheckpoints(scenario),
            NativeIcon("iconSaveLocal"), surface: Surface.Quiet);
    }

    private void BuildScene()
    {
        var scene = _scene;
        var scenario = _scenario;
        if (!WorldScriptScenes.List(scenario).Contains(scene)) { BuildScenario(); return; }
        ButtonAt(_body, scenario.Name, 160, () => Enter(Page.Scenario), NativeIcon("iconArrowLeft"), surface: Surface.Quiet);
        Heading(scene.Name);
        BuildSceneClock();
        Message(SceneState(scene), false);
        var cast = new[] { scene.Actor?.Name, scene.Location == null ? null : SampleLabel(scene.Location) }
            .Where(label => !string.IsNullOrWhiteSpace(label)).ToArray();
        if (cast.Length > 0) Label(_body, string.Join(" · ", cast), EditorWidth, color: SecondaryText);
        if (WorldScriptScenes.Current(scenario) == scene)
        {
            var watching = new[]
            {
                Pending(scene.WatchDeath, scene.Actor, "actor.died", "Featured unit dies"),
                Pending(scene.WatchOwnership, scene.Village, "city.kingdom_changed", "Village changes hands"),
                Pending(scene.WatchRuler, scene.Kingdom, "kingdom.king_changed", "New ruler takes the throne")
            }.Where(label => label != null).ToArray();
            if (watching.Length > 0)
                Message(string.Format(L("scene.watching", "Waiting for · {0}"), string.Join(" · ", watching)), false);
        }

        var controls = Row(_body, EditorWidth, 24);
        ButtonAt(controls, L("scene.start", "Start scene"), 115, () =>
        {
            WorldScriptScenes.Start(scenario, scene);
            Render();
        }, NativeIcon("iconPlay"), surface: Surface.Primary).interactable = !scene.StartedAt.HasValue;
        BuildSceneCamera(controls, scene);

        if (_notice != null) Message(_notice, true);
        foreach (var mark in scene.Marks.AsEnumerable().Reverse())
            BuildSceneMark(scenario, scene, mark);

        if (scene.StartedAt.HasValue)
        {
            var marks = FieldRow(L("scene.mark_name", "Take note"));
            InputAt(marks, _markName, 142, value => _markName = value.Trim());
            ButtonAt(marks, L("scene.add_mark", "Add mark"), 68, () =>
            {
                WorldScriptScenes.AddMark(scene, "manual", string.IsNullOrEmpty(_markName) ? L("scene.manual_mark", "Take note") : _markName);
                _markName = "";
                Render();
            }, surface: Surface.Quiet);
        }
        var checkpoint = FieldRow(L("scene.checkpoint_name", "Checkpoint name"));
        InputAt(checkpoint, _checkpointName, 142, value => _checkpointName = value.Trim());
        ButtonAt(checkpoint, L("scene.save", "Save"), 68, () => SaveCheckpoint(scenario, scene, null),
            NativeIcon("iconSaveLocal"), surface: Surface.Quiet);

        if (!FlowFold((scene, "settings"), L("scene.settings", "Scene settings"), "iconOptions")) return;
        Message(L("scene.clean_help", "Use the game's Toggle UI hotkey (H by default) to restore the interface."), false);
        InputAt(FieldRow(L("scene.name", "Scene name")), scene.Name, 206, value =>
        {
            if (!string.IsNullOrWhiteSpace(value)) scene.Name = value.Trim();
        });
        if (!scene.StartedAt.HasValue) BuildSceneSchedule(scenario, scene);
        FieldAt(FieldRow(L("scene.actor", "Featured unit")), scene.Actor == null ? L("value.unset", "Unset") :
            SampleLabel(scene.Actor), 206, () => ChooseSceneActor(scene));
        ButtonAt(_body, scene.Location == null ? L("scene.pick_site", "Pick filming location") :
            $"{L("scene.pick_site", "Pick filming location")} · {SampleLabel(scene.Location)}", EditorWidth, () =>
            BeginMapSelection(tile =>
            {
                scene.Location = new ScriptTargetSample { Kind = ScriptTargetKind.Tile, X = tile.x, Y = tile.y };
                var city = tile.zone_city;
                scene.Village = city?.data == null ? null :
                    new ScriptTargetSample { Kind = ScriptTargetKind.City, MetaID = city.id, Name = city.name };
                var kingdom = city?.kingdom;
                scene.Kingdom = kingdom?.data == null ? null :
                    new ScriptTargetSample { Kind = ScriptTargetKind.Kingdom, MetaID = kingdom.id, Name = kingdom.name };
            }), NativeIcon("iconArrowAttackTarget"), surface: Surface.List);
        BuildSceneWatch(scene);
        ButtonAt(_body, L("scene.delete", "Remove scene"), 138, () =>
            Confirm(L("scene.delete_confirm", "Remove this scene and its marks?"), () =>
            {
                scenario.Scenes.Remove(scene);
                _scene = null;
                Enter(Page.Scenario);
            }, () => Enter(Page.Scene)), NativeIcon("iconDemolish"), surface: Surface.Destructive);

        string Pending(bool enabled, ScriptTargetSample subject, string kind, string label) =>
            enabled && subject != null && !scene.Marks.Any(mark => mark.Kind == kind) ? L("scene." + kind, label) : null;
    }

    private void BuildSceneSchedule(ScriptScenario scenario, ScriptScene scene)
    {
        string mode = L("scene.start_mode", "Start cue");
        FieldAt(FieldRow(mode), SceneMode(scene.Mode), 206, () => OpenPicker(mode,
            Enum.GetValues(typeof(ScriptSceneMode)).Cast<ScriptSceneMode>().Select(value =>
                new Option(SceneMode(value), () =>
                {
                    if (value != scene.Mode) scene.AnnouncedAt = null;
                    scene.Mode = value;
                    Enter(Page.Scene);
                }, null, value == scene.Mode)), Page.Scene));
        if (scene.Mode != ScriptSceneMode.Manual)
            InputAt(FieldRow(L("scene.planned_year", "Planned world year")), scene.Year?.ToString(CultureInfo.InvariantCulture) ?? "",
                206, value =>
                {
                    int? year = int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
                    if (year != scene.Year) scene.AnnouncedAt = null;
                    scene.Year = year;
                }, InputField.CharacterValidation.Integer);
        string scriptTitle = L("scene.start_script", "Opening script");
        var scripts = WorldScriptScenes.StartScripts(scenario).ToArray();
        string scriptName = scripts.FirstOrDefault(script => script.ID == scene.StartScriptID)?.Name ??
            (scene.StartScriptID == null ? L("value.unset", "Unset") : L("scene.missing_script", "Opening script unavailable"));
        FieldAt(FieldRow(scriptTitle), scriptName, 206, () => OpenPicker(scriptTitle,
            new[] { new Option(L("value.unset", "Unset"), () => Pick(null)) }.Concat(scripts.Select(script =>
                new Option(script.Name, () => Pick(script.ID), NativeIcon("iconPlay"), scene.StartScriptID == script.ID))), Page.Scene));

        void Pick(string id) { scene.StartScriptID = id; Enter(Page.Scene); }
    }

    private void BuildSceneWatch(ScriptScene scene)
    {
        if (!FlowFold((scene, "watch"), L("scene.watch", "Watch for first event in scene"), "iconWorldLog")) return;
        Watch(L("scene.actor.died", "Featured unit dies"), scene.Actor, scene.WatchDeath, value => scene.WatchDeath = value);
        Watch(L("scene.city.kingdom_changed", "Village changes hands"), scene.Village, scene.WatchOwnership, value => scene.WatchOwnership = value);
        Watch(L("scene.kingdom.king_changed", "New ruler takes the throne"), scene.Kingdom, scene.WatchRuler, value => scene.WatchRuler = value);

        void Watch(string title, ScriptTargetSample subject, bool enabled, Action<bool> set)
        {
            var row = Row(_body, EditorWidth, 22);
            ButtonAt(row, title, 185, () => { set(!enabled); Render(); },
                NativeIcon(enabled ? "iconCheckmark" : "iconWorldLog"), surface: Surface.List, selected: enabled)
                .interactable = subject != null;
            Label(row, subject == null ? L("value.unset", "Unset") : SampleLabel(subject), 200, color: SecondaryText);
        }
    }

    private void ChooseSceneActor(ScriptScene scene)
    {
        void Pick(Actor actor)
        {
            scene.Actor = actor == null ? null : new ScriptTargetSample
                { Kind = ScriptTargetKind.Actor, ActorID = actor.data.id, Name = actor.data.name };
            Enter(Page.Scene);
        }
        var options = World.world.units.getSimpleList().Where(actor => actor?.data != null && actor.isAlive())
            .Select(actor =>
            {
                long id = actor.data.id;
                return new Option(ActorLabel(actor), () =>
                {
                    if (World.world?.units.get(id) is Actor current && current.data?.id == id && current.isAlive())
                        Pick(current);
                }, actor.asset?.getSpriteIcon(), id == scene.Actor?.ActorID);
            }).ToList();
        options.Insert(0, new Option(L("value.unset", "Unset"), () => Pick(null)));
        OpenPicker(L("scene.actor", "Featured unit"), options, Page.Scene);
    }

    private void BuildSceneCamera(UnityEngine.Transform row, ScriptScene scene)
    {
        ButtonAt(row, L("scene.follow", "Follow unit"), 88, () => FollowSceneActor(scene.Actor),
            NativeIcon("iconInspect"), surface: Surface.Quiet).interactable = WorldScriptScenes.LiveActor(scene.Actor) != null;
        ButtonAt(row, L("scene.wide", "Wide shot"), 82, () =>
        {
            var tile = WorldScriptScenes.LiveActor(scene.Actor)?.current_tile;
            WorldScriptScenes.Locate(scene.Location?.X ?? tile?.x, scene.Location?.Y ?? tile?.y, 55f);
        }, NativeIcon("iconArrowAttackTarget"), surface: Surface.Quiet)
            .interactable = scene.Location != null || WorldScriptScenes.LiveActor(scene.Actor)?.current_tile != null;
        ButtonAt(row, L("scene.clean", "Clean view"), 92, () =>
        {
            CloseToMap();
            WorldScriptRecording.Start();
        }, NativeIcon("iconOptions"), surface: Surface.Quiet);
    }

    private void BuildSceneMark(ScriptScenario scenario, ScriptScene scene, ScriptSceneMark mark)
    {
        string type = L("scene." + mark.Kind, mark.Kind);
        string caption = $"{SceneYear(Date.getYear(mark.Time))} · {type} · {mark.Name}";
        var (x, y) = (mark.X, mark.Y);
        if (!FlowFold(mark, caption, "iconWorldLog")) return;
        if (mark.Subject != null) Message(SampleLabel(mark.Subject), false);
        if (mark.Previous != null || mark.Next != null) Message($"{mark.Previous ?? "—"} → {mark.Next ?? "—"}", false);
        if (mark.Cause != null) Message(mark.Cause.ScriptName, false);
        if (mark.Outcome != null) Message(L("scene." + mark.Outcome, RunOutcome(mark.Outcome)), mark.Outcome is "failed" or "partial" or "missing_script");
        var controls = Row(_body, EditorWidth, 24);
        ButtonAt(controls, L("scenario.locate", "Locate on map"), 102, () => WorldScriptScenes.Locate(x, y), surface: Surface.Quiet)
            .interactable = x.HasValue && y.HasValue;
        ButtonAt(controls, L("scene.follow", "Follow unit"), 102, () => FollowSceneActor(mark.Subject), surface: Surface.Quiet)
            .interactable = WorldScriptScenes.LiveActor(mark.Subject) != null;
        ButtonAt(controls, L("scene.save_here", "Save current world"), 110, () => SaveCheckpoint(scenario, scene, mark),
            NativeIcon("iconSaveLocal"), surface: Surface.Quiet);
    }

    private void FollowSceneActor(ScriptTargetSample subject)
    {
        if (WorldScriptScenes.LiveActor(subject) is not Actor actor) return;
        CloseToMap();
        World.world.followUnit(actor);
    }

    private static void CloseToMap() => ScrollWindow.hideAllEvent(pWithAnimation: false);
}
