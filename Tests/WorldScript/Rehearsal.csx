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
WorldScriptEngine.Reset();
WorldScripts.Scripts.Clear();
WorldScripts.Scenarios.Clear();
WorldScripts.Regions.Clear();
WorldScripts.NotifyChanged();

var warehouseAsset = AssetManager.buildings.get("stockpile");
(City City, Kingdom Realm, WorldTile Site) Village(string name)
{
    var tile = World.world.tiles_list.First(t => t != null && !t.main_type.liquid && t.zone != null &&
        t.zone.city == null && t.zone.neighbours_all.All(zone => zone.city == null) &&
        World.world.buildings.canBuildFrom(t, warehouseAsset, null, BuildPlacingType.Load));
    var actor = World.world.units.createNewUnit("human", tile);
    var kingdom = World.world.kingdoms.makeNewCivKingdom(actor, null, false);
    var city = World.world.cities.newCity(kingdom, tile.zone, actor);
    city.data.name = name;
    actor.joinCity(city);
    kingdom.setCapital(city);
    return (city, kingdom, tile);
}

const string villageName = "重建救济验收村";
var before = Village(villageName);
var receiver = Village("验收接管国");
World.world.units.checkContainer();
var warehouse = World.world.buildings.addBuilding(warehouseAsset, before.Site);
World.world.buildings.checkContainer();
World.world.cities.beginChecksBuildings();
if (!before.City.storages.Contains(warehouse)) before.City.storages.Add(warehouse);
if (!before.City.stockpiles.Contains(warehouse)) before.City.stockpiles.Add(warehouse);
warehouse.addResources("wood", 5);
Check(before.City.getResourcesAmount("wood") == 5, "Baseline starts with five units of wood.");
long cityID = before.City.id;
long receiverID = receiver.Realm.id;
var territory = before.City.zones.SelectMany(zone => zone.tiles).ToArray();
var region = new ScriptRegion
{
    ID = WorldScripts.Regions.Select(item => item.ID).DefaultIfEmpty(0).Max() + 1,
    Name = "验收边境", X1 = territory.Min(tile => tile.x), X2 = territory.Max(tile => tile.x),
    Y1 = territory.Min(tile => tile.y), Y2 = territory.Max(tile => tile.y)
};
WorldScripts.Regions.Add(region);

var reconstruction = new ScriptScenario { Name = "重建验收" };
var relief = new ScriptScenario { Name = "救济验收" };
WorldScripts.Scenarios.Add(reconstruction);
WorldScripts.Scenarios.Add(relief);
ScriptInstance Supply(ScriptScenario scenario, string name, int priority)
{
    var script = new ScriptInstance { Name = name, ScenarioID = scenario.ID, Enabled = true, Priority = priority,
        TriggerCondition = new TriggerCondition { TriggerID = "region.ownership",
            Parameters = new JObject { ["id"] = region.ID.ToString() } }, Flow = new ScriptFlow() };
    script.Flow.Query.Scope.TargetID = "city.event";
    script.Flow.Steps.Add(new ScriptStep { Action = new ScriptAction { ActionID = "city.resources.change",
        Parameters = new JObject { ["resource_id"] = "wood", ["value"] = 10, ["stock_write"] = "delta" } } });
    return WorldScripts.AddScript(script);
}
const string rebuildName = "重建批次验收";
const string reliefName = "救济批次验收";
var rebuild = Supply(reconstruction, rebuildName, 10);
var giveRelief = Supply(relief, reliefName, 0);
var review = new ScriptInstance { Name = "验收复查", ScenarioID = relief.ID, Enabled = true,
    TriggerCondition = new TriggerCondition { TriggerID = "script.delayed" }, Flow = new ScriptFlow() };
review.Flow.Query.Scope.TargetID = "city.event";
review.Flow.Steps.Add(new ScriptStep { Mode = "once", Action = new ScriptAction
    { ActionID = "world.log", Parameters = new JObject { ["message"] = "验收复查" } } });
WorldScripts.AddScript(review);
giveRelief.Flow.Steps.Add(new ScriptStep { Action = new ScriptAction { ActionID = "script.schedule",
    Parameters = new JObject { ["delay"] = 60, ["script_id"] = review.ID } } });
string reliefID = giveRelief.ID;

async System.Threading.Tasks.Task ShowEditor()
{
    ScrollWindow.showWindow("WindowWorldScript", pSkipAnimation: true);
    for (int frame = 0; frame < 14; frame++) await Game.NextFrame();
}
await ShowEditor();
var window = WindowWorldScript.Instance;
Button[] Buttons() => window.GetComponentsInChildren<Button>();
void Click(string name)
{
    var button = Buttons().FirstOrDefault(item => item.name == name);
    Check(button != null, "Missing " + name + " in " + string.Join(" / ", Buttons().Select(item => item.name)));
    button.onClick.Invoke();
}
void Card(string prefix)
{
    var button = Buttons().FirstOrDefault(item => item.name.StartsWith(prefix, StringComparison.Ordinal));
    Check(button != null, "Missing " + prefix);
    button.onClick.Invoke();
}
void SelectRehearsal()
{
    Click("试运行");
    Card(reliefName + " · ");
}

Card(reliefName + "\n");
Card("1.");
Click("直接增减");
Click("补到目标下限");
Click("更多");
Click("创建试运行副本");
Check(giveRelief.Flow.Steps[0].Action.Parameters.Value<string>("stock_write") == "delta",
    "Creating the two copies leaves the published script in the current world intact.");

var root = Path.Combine(Application.persistentDataPath, "world_script_rehearsals");
string caseFolder = Directory.GetDirectories(root).OrderByDescending(Path.GetFileName)
    .First(path => File.ReadAllText(Path.Combine(path, "case.json")).Contains(reliefName));
var baseline = Path.Combine(caseFolder, "saved", "world_scripts.json");
var variant = Path.Combine(caseFolder, "draft", "world_scripts.json");
string frozen = File.ReadAllText(baseline);
Check(frozen.Contains("\"stock_write\": \"delta\"") && File.ReadAllText(variant).Contains("\"stock_write\": \"at_least\""),
    "The two frozen copies have the expected rule definitions.");

async System.Threading.Tasks.Task RunCopy(string button, int expectedWood, int expectedReliefBefore, bool skipped)
{
    var current = World.world.map_stats;
    Click(button);
    Click("确认");
    while (SmoothLoader.isLoading() || current == World.world.map_stats) await Game.NextFrame();
    Config.paused = true;
    World.world.cities.beginChecksBuildings();
    var fixture = World.world.cities.get(cityID);
    Check(fixture.getResourcesAmount("wood") == 5, "Frozen map reloads the warehouse and five units of starting stock.");
    Check(WorldScripts.FindRegion(region.ID).Contains(fixture.getTile().x, fixture.getTile().y), "Frozen map reloads the region.");
    fixture.setKingdom(World.world.kingdoms.get(receiverID));
    World.world.map_stats.world_time = 10d;
    for (int i = 0; i < 12; i++)
    {
        World.world._is_paused = false;
        WorldScriptEngine.Tick();
        World.world._is_paused = true;
        await Game.NextFrame();
    }
    var city = World.world.cities.get(cityID);
    Check(city.getResourcesAmount("wood") == expectedWood, "Real stock meets the expected result after overlapping rules. Actual: " + city.getResourcesAmount("wood"));
    var rebuild = WorldScripts.Scripts.Single(script => script.Name == rebuildName);
    var first = rebuild.Runtime.History.Single().Steps[0].Actions[0].Writes.Single();
    Check(first.Before == 5 && first.After == 15, "Reconstruction records the first contribution.");
    var relief = WorldScripts.Scripts.Single(script => script.ID == reliefID);
    var detail = relief.Runtime.History.Single().Steps[0].Actions[0].Writes.Single();
    Check(detail.Before == expectedReliefBefore && detail.After == expectedWood &&
        (detail.Reason == "inventory_satisfied") == skipped, "Relief records the second attempt or its policy skip.");
    Check(relief.Runtime.DelayedRuns.Count == 1 && relief.Runtime.DelayedRuns[0].CityID == cityID,
        "Follow-up is recorded for the same subject.");
    await ShowEditor();
    SelectRehearsal();
    Click("记录结果");
}

await RunCopy("打开已保存规则", 25, 15, false);
await RunCopy("打开草稿规则", 15, 15, true);
Card("库存差异 1 项");
var labels = window.GetComponentsInChildren<Text>();
Check(labels.Any(label => label.text.Contains(villageName) && label.text.Contains("25 → 15")),
    "The native comparison shows the real inventory difference.");
Check(File.ReadAllText(baseline) == frozen, "Rehearsals used fresh working copies of the frozen inputs.");
return new { savedWood = 25, draftWood = 15, pendingPerCopy = 1 };
