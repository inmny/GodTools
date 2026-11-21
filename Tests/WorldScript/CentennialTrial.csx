using GodTools;
using GodTools.Features;
using GodTools.Features.WorldScript;
using GodTools.UI;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

void Check(bool value, string reason)
{
    if (!value) throw new InvalidOperationException(reason);
}

Check(Application.persistentDataPath.Contains("godtools-review") || Application.persistentDataPath.Contains("cultiway-dev"),
    "Run the fixture in the isolated WorldBox profile.");
while (World.world == null || SmoothLoader.isLoading()) await Game.NextFrame();
Config.paused = true;
WorldScripts.Scripts.Clear();
WorldScripts.Scenarios.Clear();
WorldScripts.NotifyChanged();

var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null &&
    item.zone.city == null && item.zone.neighbours_all.All(zone => zone.city == null));
var guard = World.world.units.createNewUnit("human", tile);
var realm = World.world.kingdoms.makeNewCivKingdom(guard, null, false);
var city = World.world.cities.newCity(realm, tile.zone, guard);
guard.data.name = "试炼守关人";
guard.joinCity(city);
realm.setCapital(city);
realm.setKing(guard);
var heir = World.world.units.createNewUnit("human", tile);
heir.data.name = "试炼继承人";
heir.joinKingdom(realm);
heir.joinCity(city);
World.world.units.checkContainer();
WorldScriptEngine.Reset();

long guardID = guard.data.id, heirID = heir.data.id, realmID = realm.id;
int x = tile.x, y = tile.y;
const string trialName = "百年试炼后的王位交替";
const string openingName = "守关人的试炼";
const string nextName = "试炼后的第一年";
var window = WindowWorldScript.Instance;
async System.Threading.Tasks.Task ShowEditor()
{
    ScrollWindow.showWindow("WindowWorldScript", pSkipAnimation: true);
    for (int frame = 0; frame < 10; frame++) await Game.NextFrame();
}
Button[] Buttons() => window.GetComponentsInChildren<Button>();
Button Find(string name, bool prefix = false)
{
    var button = Buttons().FirstOrDefault(item => prefix ? item.name.StartsWith(name, StringComparison.Ordinal) : item.name == name);
    Check(button != null, "Missing " + name + " in " + string.Join(" / ", Buttons().Select(item => item.name)));
    return button;
}
void Click(string name, bool prefix = false)
{
    var button = Find(name, prefix);
    Check(button.interactable, name + " should be available");
    button.onClick.Invoke();
}
async System.Threading.Tasks.Task CheckBackToMap(string action)
{
    for (int frame = 0; frame < 60 && (ScrollWindow.isAnimationActive() || window.gameObject.activeInHierarchy); frame++)
        await Game.NextFrame();
    Check(!ScrollWindow.isWindowActive() && !WindowHistory.hasHistory() &&
        !HoveringBgIconManager._instance._icons.gameObject.activeSelf && !WindowToolbar._instance._last_state,
        action + " clears native window history, hovering decorations and the window toolbar.");
}
void SetRowInput(string action, string value)
{
    var input = Find(action).transform.parent.GetComponentInChildren<InputField>();
    input.text = value;
    input.onEndEdit.Invoke(value);
}

await ShowEditor();
Click("添加脚本");
Click(trialName);
var scenarioID = WorldScripts.Scenarios.Single().ID;
var loadProbe = WorldScripts.AddScript(new ScriptInstance
{
    Name = "补拍加载验收", Enabled = true, ScenarioID = scenarioID,
    TriggerCondition = new TriggerCondition { TriggerID = WorldScriptRegistry.TriggerWorldLoaded },
    Flow = new ScriptFlow
    {
        Query = new ScriptQuery { Scope = new TargetSelector { TargetID = WorldScriptRegistry.TargetWorld } },
        Steps = { new ScriptStep { Action = new ScriptAction
            { ActionID = "world.log", Parameters = new JObject { ["message"] = "载入触发验收" } } } }
    }
});
string loadProbeID = loadProbe.ID;
ScriptScenario Scenario() => WorldScripts.Scenarios.Single(item => item.ID == scenarioID);
ScriptScene Scene(string name = openingName) => Scenario().Scenes.Single(item => item.Name == name);
ScriptInstance Opening() => WorldScripts.Scripts.Single(item => item.ID == Scene().StartScriptID);
void OpenScene(string name = openingName)
{
    Click("剧本");
    Click(trialName + " · ", prefix: true);
    Click(name + " · ", prefix: true);
}
Click(openingName + " · ", prefix: true);
Click("场次设置");
Click("未设置");
string guardLabel = $"试炼守关人 · #{guardID}";
Find("取消").transform.parent.GetComponentInChildren<InputField>().text = guardLabel;
Click(guardLabel);
Check(Scene().Actor?.ActorID == guardID, "The featured unit is picked from the native menu.");
Scene().Location = new ScriptTargetSample { Kind = ScriptTargetKind.Tile, X = x, Y = y };
Scene().Village = new ScriptTargetSample { Kind = ScriptTargetKind.City, MetaID = city.id, Name = city.name };
Scene().Kingdom = new ScriptTargetSample { Kind = ScriptTargetKind.Kingdom, MetaID = realmID, Name = realm.name };
int year = Scene().Year.Value;
Check(year - Date.getYear(World.world.getCurWorldTime()) == 100, "The example schedules the trial 100 years from now.");

void SimulateTick()
{
    World.world._is_paused = false;
    WorldScriptEngine.Tick();
    World.world._is_paused = Config.paused;
}
World.world.map_stats.world_time = (year - 1) * 60d;
Config.paused = false;
SimulateTick();
Check(Config.paused && Scene().AnnouncedAt.HasValue && !Scene().StartedAt.HasValue && Opening().Runtime.History.Count == 0,
    "The due-year cue pauses before the opening script changes the world.");
await Game.NextFrame();

string saves = Path.Combine(Application.persistentDataPath, "world_script_checkpoints", "saved");
string SaveAs(string name)
{
    SetRowInput("保存", name);
    Click("保存");
    return Directory.GetDirectories(saves).Single(folder =>
        JObject.Parse(File.ReadAllText(Path.Combine(folder, "checkpoint.json"))).Value<string>("Name") == name &&
        JObject.Parse(File.ReadAllText(Path.Combine(folder, "checkpoint.json"))).Value<string>("ScenarioID") == scenarioID);
}
string baseline = SaveAs("试炼前共同起点");
string seed = Path.Combine(baseline, "map", "world_scripts.json");
string baselineState = File.ReadAllText(seed);
string baselineMap = Path.Combine(baseline, "map", "map.wbox");
byte[] baselineBytes = File.ReadAllBytes(baselineMap);

async System.Threading.Tasks.Task OpenCopy(string name, bool chosen = false)
{
    await ShowEditor();
    Click("补拍检查点");
    Click((chosen ? "正片结尾 · " : "") + name + " · ", prefix: true);
    var stats = World.world.map_stats;
    Click("打开工作副本");
    Click("确认");
    await CheckBackToMap("Opening a checkpoint");
    for (int frame = 0; frame < 900 && (SmoothLoader.isLoading() || stats == World.world.map_stats); frame++)
        await Game.NextFrame();
    Check(stats != World.world.map_stats && !SmoothLoader.isLoading() && Config.paused, "A fresh checkpoint copy loaded paused.");
    Check(WorldScripts.Scripts.Single(item => item.ID == loadProbeID).Runtime.History.Count == 0,
        "Opening a checkpoint does not rerun world-loaded rules.");
    await ShowEditor();
    OpenScene();
}

await OpenCopy("试炼前共同起点");
Click("开始场次");
Check(Scene().Marks.Count(mark => mark.Kind == "start") == 1 &&
    Opening().Runtime.History.Single().Outcome == "completed", "The opening script runs once in the survival copy.");
Click("跟拍人物");
Check(MoveCamera.isCameraFollowingUnit(World.world.units.get(guardID)), "Close shot uses native unit following.");
await CheckBackToMap("Following a unit");
await ShowEditor();
Click("地图全景");
Check(Mathf.Abs(World.world.move_camera.getTargetZoom() - 55f) < 0.1f, "Wide shot has its own camera zoom.");
await CheckBackToMap("Taking a wide shot");
await ShowEditor();
Click("干净取景");
Check(Config.ui_main_hidden && CustomGodToolsConfig.HideDrop && CustomGodToolsConfig.HideCursor &&
    CustomGodToolsConfig.HideUnitHover && CustomGodToolsConfig.DisableCameraShake, "Clean view enables the capture preset.");
await CheckBackToMap("Entering clean view");
Config.ui_main_hidden = false;
await Game.NextFrame();
await Game.NextFrame();
Check(!Config.ui_main_hidden, "The native visibility toggle exits clean view.");
await ShowEditor();
SetRowInput("加标记", "守关人生还");
Click("加标记");
string survival = SaveAs("生还版");
Check(World.world.units.get(guardID).isAlive(), "The guard survives in the first work copy.");

await OpenCopy("试炼前共同起点");
Check(!Scene().StartedAt.HasValue && Opening().Runtime.History.Count == 0, "The other copy starts with the same pending cue.");
Click("开始场次");
World.world.units.get(guardID).die(false, AttackType.Divine, false, false);
World.world.kingdoms.get(realmID).setKing(World.world.units.get(heirID));
SaveAs("事件尚待记录");
await OpenCopy("事件尚待记录");
SimulateTick();
await Game.NextFrame();
var death = Scene().Marks.Single(mark => mark.Kind == "actor.died");
var coronation = Scene().Marks.Single(mark => mark.Kind == "kingdom.king_changed");
Check(death.Subject.ActorID == guardID && death.X == x && death.Y == y && coronation.Subject.ActorID == heirID,
    "The scene records the dead unit's location and names the successor.");
Click("世界第 " + year + " 年 · 新王登基", prefix: true);
await Game.NextFrame();
var sceneShot = Screen.Capture("worldscript-centennial-trial.png");
await Game.NextFrame();
await Game.NextFrame();
string succession = SaveAs("继位版");
Click("继位版 · ", prefix: true);
Click("选为正片结尾");
Click("正片结尾 · 继位版 · ", prefix: true);
await OpenCopy("继位版", chosen: true);
Check(World.world.kingdoms.get(realmID).king?.data?.id == heirID &&
    Scene().Marks.Any(mark => mark.Kind == "actor.died"), "The selected ending resumes with the heir and its saved event marks.");
OpenScene(nextName);
Click("开始场次");
Check(Scene(nextName).StartedAt.HasValue && Opening().Runtime.History.Count == 1,
    "The next scene starts without repeating the trial opening.");
SaveManager.saveMapData(SaveManager.currentSavePath);
var reminder = new ScriptScene { Name = "到期提醒验收", Mode = ScriptSceneMode.Remind, Year = year };
var automatic = new ScriptScene { Name = "自动开场验收", Mode = ScriptSceneMode.Automatic, Year = year,
    StartScriptID = Opening().ID };
Scenario().Scenes.Add(reminder);
Scenario().Scenes.Add(automatic);
Config.paused = false;
SimulateTick();
Check(reminder.AnnouncedAt.HasValue && !reminder.StartedAt.HasValue && !Config.paused, "Reminder mode leaves the world running.");
SimulateTick();
SimulateTick();
Check(automatic.StartedAt.HasValue && Opening().Runtime.History.Count == 2, "Automatic mode opens exactly once.");
Config.paused = true;
Check(File.ReadAllText(seed) == baselineState && File.ReadAllBytes(baselineMap).SequenceEqual(baselineBytes) &&
    Directory.Exists(survival) && Directory.Exists(succession), "Both results and the shared starting checkpoint remain intact.");
OpenScene(nextName);
Click("场次设置");
Click("选择取景地点");
await CheckBackToMap("Picking a filming location");
return new { year, routes = 2, nativeEvents = 2, continued = true, sceneShot };
