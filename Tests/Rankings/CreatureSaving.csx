using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GodTools.Features;
using GodTools.Libraries;
using GodTools.UI;
using GodTools.UI.Prefabs;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using strings;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(Application.persistentDataPath.Contains("godtools-review") ||
    Application.persistentDataPath.Contains("cultiway-dev"), "Use an isolated WorldBox profile.");
while (World.world == null || SmoothLoader.isLoading() || World.world.transition_screen?.isActiveAndEnabled == true)
    await Game.NextFrame();
Config.paused = true;
const string score = "power:godtools:standard-duel@2";
const string armedName = "镜湖·守塔人";
const string unarmedName = "镜湖·巡岸人";
const string weaponName = "镜湖长刃";
var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
Actor Unit(string name)
{
    var actor = World.world.units.createNewUnit("human", tile, pSpawnWithItems: false, pAdultAge: true);
    actor.data.name = name;
    actor.data.custom_name = true;
    actor.addTrait("strong");
    return actor;
}
var armed = Unit(armedName);
var unarmed = Unit(unarmedName);
World.world.units.checkContainer();
long armedID = armed.data.id;
long unarmedID = unarmed.data.id;
var weapon = World.world.items.newItem(AssetManager.items.list.First(item =>
    item.equipment_type == EquipmentType.Weapon && !item.id.StartsWith("_")));
weapon.data.name = weaponName;
armed.equipment.setItem(weapon, armed);
long sourceWeapon = weapon.data.id;
var moduleState = new Dictionary<long, int> { [armedID] = 71, [unarmedID] = 29 };
Check(AppDomain.CurrentDomain.GetData("GodTools.CreatureSaveExtensions/v1") as Type == typeof(CreatureSaveExtensions) &&
    CreatureSaveExtensions.Register("test:creature-module", 3, actor => moduleState.TryGetValue(actor.data.id, out int data)
        ? new JObject { ["strength"] = data } : null, (actor, version, data) =>
    {
        moduleState.Add(actor.data.id, version * 100 + data.Value<int>("strength"));
        data["strength"] = -1;
    }), "A mod registers an actor extension that receives the saved version and its own payload.");

CreatureSavedList.SaveActor(armed);
CreatureSavedList.SaveActor(unarmed);
new CreatureSavedList().Initialize();
Check(CreatureSavedList.ActorSaved(armed) && CreatureSavedList.ActorSaved(unarmed), "The creature catalog reloads from disk.");

async Task Open(string id)
{
    ScrollWindow.hideAllEvent(pWithAnimation: false);
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow(id, pSkipAnimation: true);
    for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
    Check(ScrollWindow.isCurrentWindow(id), "Could not open " + id);
}
Button ButtonIn(Component root, string name) => root.GetComponentsInChildren<Button>().First(item => item.name == name);
var rankings = WindowRankings.Instance;
RectTransform Region(string name) => rankings.GetComponentsInChildren<RectTransform>().Single(item => item.name == name);
void Click(string name) => ButtonIn(rankings, name).onClick.Invoke();
void Filter()
{
    ButtonIn(Region("RankingRightTabs"), "筛选").onClick.Invoke();
    Click("清空条件");
    var pane = Region("RankingRightSidebar");
    ButtonIn(pane, "名称").onClick.Invoke();
    pane.GetComponentsInChildren<InputField>().Single().text = "镜湖";
    Click("添加条件");
    Click("应用筛选");
}
void BattleStats(Actor actor, float health, float damage)
{
    actor.stats[S.health] = health;
    actor.stats[S.damage] = damage;
    actor.stats[S.attack_speed] = 1;
    actor.stats[S.range] = 2;
    actor.stats[S.speed] = 30;
}
await Open("WindowRankings");
BattleStats(armed, 8000, 600);
BattleStats(unarmed, 4000, 120);
ConsistentCreaturePowerTop.Check(armed);
var oldPeak = ConsistentCreaturePowerTop.GetSortedCreaturePowerData().Single(item => item.ID == armedID).Power;
Click("人物榜");
Filter();
Check(Region("RankingColumns").GetComponentsInChildren<Text>()[1].text == "战力",
    "The board sorts on the single built-in power score.");
var scoreButton = ButtonIn(Region("RankingEntries"), armedName).transform.parent
    .GetComponentsInChildren<Button>().First(item => item.name == "RankingMetric");
Tooltip.hideTooltipNow();
await Game.NextFrame();
scoreButton.GetComponent<TipButton>().hoverAction.Invoke();
var metricTip = Tooltip.findActive(item => item.transform.name == "inmny.godtools.ranking.metric_tooltip");
Check(metricTip?.description.text.Contains("近战压制对手") == true, "The score tooltip shows its saved measurements.");
Tooltip.hideTooltipNow();
Click("保存本次排名");
Check(Region("RankingPanel").GetComponentsInChildren<Text>().Any(item => item.text == armedName),
    "A ranking record freezes the displayed subset.");

string folder = Path.Combine(Application.persistentDataPath, "creature-saving-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
SaveManager.saveMapData(folder);
var stored = JObject.Parse(File.ReadAllText(Path.Combine(folder, "godtools_rankings.json")));
var historic = stored["Archives"].Single(item => item["View"]["Sort"][0].Value<string>("Field") == score);
var sample = historic["Rows"].Single(row => row.Value<string>("Name") == armedName)["Values"][score];
var peak = stored["Peaks"].Single(item => item.Value<string>("ProfileID") == "godtools:standard-duel")["Entries"]
    .Single(row => row.Value<string>("ActorID") == armedID.ToString());
Check(historic["Rows"].Count() == 2 && sample["Parts"].HasValues &&
    peak.Value<double>("Score") == sample.Value<double>("Number") &&
    peak["Creature"]["ModData"]["test:creature-module"].Value<int>("Version") == 3,
    "The ranking record and the current peak use the same score, and the peak includes a saved actor.");
Check(peak["Portrait"]?.Value<string>("Png")?.StartsWith("iVBOR", StringComparison.Ordinal) == true,
    "The peak saves the character's original pixel portrait.");

var previousMap = World.world.map_stats;
SaveManager.setCurrentPathAndId(folder, -1);
World.world.save_manager.loadWorld(folder);
for (int frame = 0; frame < 1200 && (SmoothLoader.isLoading() || previousMap == World.world.map_stats); frame++)
    await Game.NextFrame();
Check(!SmoothLoader.isLoading() && previousMap != World.world.map_stats, "The saved world reloaded.");
Config.paused = true;
var savedLegacy = ConsistentCreaturePowerTop.GetSortedCreaturePowerData().Single(item => item.ID == armedID);
Check(savedLegacy.Power == oldPeak && savedLegacy.Sprite != null, "Legacy peak data and its portrait survive loading.");

Actor Cast(GodPower expected, string name)
{
    Check(World.world.selected_power == expected, "The window selected the actual placement tool.");
    var existing = new HashSet<long>(World.world.units.getSimpleList().Select(actor => actor.data.id));
    var target = World.world.tiles_list.Last(item => !item.main_type.liquid && item.zone != null);
    Check(expected.click_action(target, expected.id), "The saved actor was placed.");
    World.world.units.checkContainer();
    return World.world.units.getSimpleList().Single(actor => !existing.Contains(actor.data.id) && actor.getName() == name);
}
void Verify(Actor actor, int payload, bool armedCopy)
{
    var items = actor.equipment.getItems().ToArray();
    Check(actor.hasTrait("strong") && moduleState.GetValueOrDefault(actor.data.id) == payload,
        "Placement restores the original trait and the module's versioned data.");
    Check(armedCopy ? items.Single().data.name == weaponName && items[0].data.id != sourceWeapon : items.Length == 0,
        "Placement restores the saved equipment with fresh IDs and accepts an empty inventory.");
}

await Open("WindowRankings");
Click("战力峰值记录");
Filter();
var peakRow = ButtonIn(Region("RankingEntries"), armedName).transform.parent;
Check(peakRow.GetComponentsInChildren<Image>().Any(image => image.enabled && image.sprite?.name == "RankingPeakPortrait"),
    "The reloaded peak shows the portrait captured at the peak.");
peakRow.GetComponentsInChildren<Button>().Single(button => button.name == "+").onClick.Invoke();
Verify(Cast(GodPowers.place_saved_actor, armedName), 371, armedCopy: true);

string vaultID = "WindowCreatureSavedList";
await Open(vaultID);
var vault = WindowCreatureSavedList.Instance;
void PlaceCard(string name) => vault.GetComponentsInChildren<SavedActorDataCard>()
    .Single(card => card.Name.Text.text == name).Button.onClick.Invoke();
PlaceCard(armedName);
Verify(Cast(GodPowers.place_saved_actor, armedName), 371, armedCopy: true);
await Open(vaultID);
PlaceCard(unarmedName);
Verify(Cast(GodPowers.place_saved_actor, unarmedName), 329, armedCopy: false);
await Open(vaultID);
PlaceCard(armedName);
Verify(Cast(GodPowers.place_saved_actor, armedName), 371, armedCopy: true);

return new { legacyPeak = savedLegacy.Power, currentPeak = peak.Value<double>("Score"), recorded = historic["Rows"].Count() };
