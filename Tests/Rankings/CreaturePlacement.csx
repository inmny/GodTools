using System;
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

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(Application.persistentDataPath.Contains("godtools-review") ||
    Application.persistentDataPath.Contains("cultiway-dev"), "Use an isolated WorldBox profile.");
await Game.WaitUntil(() => World.world != null && !SmoothLoader.isLoading() &&
    World.world.transition_screen?.isActiveAndEnabled != true, 1200);
Config.paused = true;
foreach (string type in new[] { "culture", "clan", "language", "subspecies" })
    Check(!AssetManager.powers.has($"gt_{type}_spawn"), "Unfinished standalone saves no longer register placement powers.");

const string actorName = "归属验收·行者";
string baseline = Path.Combine(Application.persistentDataPath, "placement-study-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(baseline);
SaveManager.saveMapData(baseline);
WorldTile EmptyLand() => World.world.tiles_list.First(tile => !tile.main_type.liquid && tile.zone != null &&
    tile.zone.city == null && tile.zone.neighbours_all.All(zone => zone.city == null));

(Actor actor, City village, WorldTile tile) Found(string name)
{
    var tile = EmptyLand();
    var actor = World.world.units.createNewUnit("human", tile, pSpawnWithItems: false, pAdultAge: true);
    actor.data.name = name;
    actor.data.custom_name = true;
    var kingdom = World.world.kingdoms.makeNewCivKingdom(actor, null, false);
    kingdom.data.name = name + "王国";
    var village = World.world.cities.newCity(kingdom, tile.zone, actor);
    village.data.name = name + "村";
    actor.joinCity(village);
    kingdom.setCapital(village);
    World.world.units.checkContainer();
    return (actor, village, tile);
}

async Task Load(string folder)
{
    var previous = World.world.map_stats;
    SaveManager.setCurrentPathAndId(folder, -1);
    World.world.save_manager.loadWorld(folder);
    await Game.WaitUntil(() => !SmoothLoader.isLoading() && World.world.map_stats != previous, 1200);
    Config.paused = true;
}
async Task Open(string id)
{
    ScrollWindow.hideAllEvent(pWithAnimation: false);
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow(id, pSkipAnimation: true);
    for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
    Check(ScrollWindow.isCurrentWindow(id), "Could not open " + id);
}
Actor Cast(GodPower power, WorldTile tile)
{
    Check(World.world.selected_power == power, "The expected placement power is selected.");
    var ids = World.world.units.getSimpleList().Select(actor => actor.data.id).ToHashSet();
    Check(power.click_action(tile, power.id), "The placement tool created a creature.");
    World.world.units.checkContainer();
    return World.world.units.getSimpleList().Single(actor => !ids.Contains(actor.data.id) && actor.getName() == actorName);
}
bool HasOldAffiliations(Actor copy) => copy.culture != null || copy.clan != null || copy.language != null;
(int culture, int clan, int language) AffiliationCounts() =>
    (World.world.cultures.Count, World.world.clans.Count, World.world.languages.Count);
SavedActorDataCard Card() => WindowCreatureSavedList.Instance.GetComponentsInChildren<SavedActorDataCard>()
    .Single(card => card.Name.Text.text == actorName);
Button Find(Component root, string label) => root.GetComponentsInChildren<Button>().First(item => item.name == label);
RectTransform Region(string label) => WindowRankings.Instance.GetComponentsInChildren<RectTransform>().Single(item => item.name == label);
void Click(string label) => Find(WindowRankings.Instance, label).onClick.Invoke();
void Filter()
{
    Find(Region("RankingRightTabs"), "筛选").onClick.Invoke();
    Click("清空条件");
    Find(Region("RankingRightSidebar"), "名称").onClick.Invoke();
    Region("RankingRightSidebar").GetComponentInChildren<InputField>().text = actorName;
    Click("添加条件");
    Click("应用筛选");
}

var source = Found(actorName);
var sourceActor = source.actor;
var culture = World.world.cultures.newCulture(sourceActor, false);
culture.data.name = "源文化";
sourceActor.setCulture(culture);
var clan = World.world.clans.newClan(sourceActor, false);
clan.data.name = "源氏族";
var language = World.world.languages.newLanguage(sourceActor, false);
language.data.name = "源语言";
sourceActor.setLanguage(language);
var species = World.world.subspecies.newSpecies(sourceActor.asset, source.tile);
species.data.name = "源亚种";
species.addBirthTrait("strong");
sourceActor.setSubspecies(species);
CreatureSavedList.SaveActor(sourceActor);
var packet = CreatureSavedList.SavedActors.Values.Single(item => item.ActorData.name == actorName);
int age = sourceActor.getAge();
Check(packet.AgeAtCapture == age, "New records include the captured age.");
long originCountry = source.village.kingdom.id;
long originVillage = source.village.id;
long originActor = sourceActor.id;
long originSubspecies = sourceActor.subspecies.id;
var factions = AffiliationCounts();
int speciesBefore = World.world.subspecies.Count;

await Open("WindowCreatureSavedList");
Card().Button.onClick.Invoke();
var first = Cast(GodPowers.place_saved_actor, source.tile);
var second = Cast(GodPowers.place_saved_actor, source.tile);
Check(first.subspecies == species && second.subspecies == species && World.world.subspecies.Count == speciesBefore,
    "Two placements in the source map reuse the matching live subspecies.");
Check(first.city == null && second.city == null && !first.isKingdomCiv() && !second.isKingdomCiv() &&
    !first.isKing() && first.id != second.id && !CreatureSavedList.ActorSaved(first) && first.getAge() == age,
    "Default placement inside a village is still independent and keeps the captured age.");
Check(!HasOldAffiliations(first) && AffiliationCounts() == factions, "Default placement creates no cultural affiliations.");

await Open("WindowRankings");
sourceActor.stats[strings.S.health] = 8800;
sourceActor.stats[strings.S.damage] = 610;
ConsistentCreaturePowerTop.Check(sourceActor);
Click("人物榜");
Click("战力峰值记录");
Filter();
var peakRow = Find(Region("RankingEntries"), actorName).transform.parent;
Find(peakRow, "RankingPlaceInVillage").onClick.Invoke();
var peakCopy = Cast(GodPowers.place_saved_actor_in_village, source.tile);
Check(peakCopy.city == source.village && !HasOldAffiliations(peakCopy), "The new peak's village button uses the selected current village.");
await Open("WindowRankings");
Click("旧战力峰值记录");
var legacyRow = Find(Region("RankingEntries"), actorName).transform.parent;
Find(legacyRow, "+").onClick.Invoke();
var legacyCopy = Cast(GodPowers.place_saved_actor_in_top, source.tile);
Check(legacyCopy.city == null && legacyCopy.subspecies == species, "The legacy peak also places an independent copy.");

string catalog = Path.Combine(Application.persistentDataPath, "saved_actors.json");
var document = JObject.Parse(File.ReadAllText(catalog));
var oldRecord = (JObject)document[packet.ID];
oldRecord.Remove("AgeAtCapture");
oldRecord["CultureData"]["creator_id"] = originActor;
oldRecord["ClanData"]["chief_id"] = originActor;
oldRecord["LanguageData"]["creator_id"] = originActor;
oldRecord["ActorData"]["parent_id_1"] = originActor;
File.WriteAllText(catalog, document.ToString());
await Load(baseline);
new CreatureSavedList().Initialize();
var destination = Found("目标");
var different = World.world.subspecies.newSpecies(destination.actor.asset, destination.tile);
different.data.name = "目标亚种";
different.addBirthTrait("fast");
destination.actor.setSubspecies(different);
Check(destination.village.id == originVillage && destination.village.kingdom.id == originCountry &&
    destination.actor.id == originActor, "The target map contains unrelated objects reusing source IDs.");
Check(!GodPowers.place_saved_actor.click_action(destination.tile, GodPowers.place_saved_actor.id),
    "A placement selection armed in the other world cannot act after changing maps.");

factions = AffiliationCounts();
speciesBefore = World.world.subspecies.Count;
await Open("WindowCreatureSavedList");
Tooltip.hideTooltipNow();
await Game.NextFrame();
Card().Button.TriggerHover();
var tooltip = Tooltip.findActive(tip => tip.asset.id == "inmny.godtools.ranking.peak_tooltip");
var labels = tooltip?.GetComponentsInChildren<Text>().Select(text => text.text).ToArray() ?? Array.Empty<string>();
Check(labels.Contains("原归属") && new[] { "源文化", "源氏族", "源语言" }.All(name => labels.Any(text => text.Contains(name))),
    "The old record still shows all original affiliations in the catalog tooltip.");

Card().Button.onClick.Invoke();
var moved = Cast(GodPowers.place_saved_actor, destination.tile);
var repeated = Cast(GodPowers.place_saved_actor, destination.tile);
Check(moved.city == null && !moved.isKingdomCiv() && moved.data.parent_id_1 < 0 && !moved.getParents().Any(),
    "Cross-map placement does not misbind reused town, kingdom or parent IDs.");
Check(moved.subspecies != different && repeated.subspecies == moved.subspecies && World.world.subspecies.Count == speciesBefore + 1 &&
    moved.subspecies.getActorBirthTraits().getTraits().Any(trait => trait.id == "strong"),
    "A different genome is imported once, then reused for repeated placement.");
Check(AffiliationCounts() == factions && !HasOldAffiliations(moved), "Moving the character across maps does not recreate its old groups.");

await Open("WindowCreatureSavedList");
Card().VillageButton.Button.onClick.Invoke();
int beforeInvalid = World.world.units.getSimpleList().Count;
Check(!GodPowers.place_saved_actor_in_village.click_action(EmptyLand(), GodPowers.place_saved_actor_in_village.id) &&
    World.world.units.getSimpleList().Count == beforeInvalid, "Clicking outside a village spawns nothing in village mode.");
var citizen = Cast(GodPowers.place_saved_actor_in_village, destination.tile);
Check(citizen.city == destination.village && citizen.kingdom == destination.village.kingdom && !citizen.isKing() &&
    citizen.subspecies == moved.subspecies && !HasOldAffiliations(citizen),
    "The village button explicitly joins the clicked village and keeps the creature's saved subspecies.");

string restored = Path.Combine(baseline, "reload");
Directory.CreateDirectory(restored);
SaveManager.saveMapData(restored);
long importedId = moved.subspecies.id;
long villageId = destination.village.id;
await Load(restored);
await Open("WindowCreatureSavedList");
Card().Button.onClick.Invoke();
var afterReload = Cast(GodPowers.place_saved_actor, EmptyLand());
Check(afterReload.subspecies.id == importedId, "The imported subspecies can still be reused after reloading the destination.");
return new { originSubspecies, importedSubspecies = importedId, village = villageId, oldAffiliations = labels };
