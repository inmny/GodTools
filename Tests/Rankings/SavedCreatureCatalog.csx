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
using UnityEngine.EventSystems;
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
const string windowId = "WindowCreatureSavedList";
const string oldName = "北境·旧档向导";
const string favoriteName = "霜原·持灯人";
const string targetName = "苍河·归林猎手";
var names = new[] { oldName, "云丘·学徒", "松林·医师", "映月·采药客", "青岩·铸师", "北境·铁卫",
    "赤沙·迁徙者", "苍河·抄书人", "映月·渡船人", "银城·巡夜人", "幽谷·守塔客", "风谷·采药人",
    "寒川·白袍客", "星泉·见习学者", "明溪·哨兵", "暮谷·长弓手", "远峰·铁匠", "石门·守钟人",
    "绿野·歌者", "青丘·木匠", "北境·银盾卫", "云岚·寻路者", "映月·织梦师", favoriteName, targetName };
var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null && item.zone.city == null);
CreatureSavedList.SavedActorPacket Packet(string name) =>
    CreatureSavedList.SavedActors.Values.Single(packet => packet.ActorData.name == name);
Culture[] cultures = new Culture[3];
foreach (var (name, index) in names.Select((name, index) => (name, index)))
{
    string species = index % 3 == 0 ? "human" : index % 3 == 1 ? "elf" : "dwarf";
    var actor = World.world.units.createNewUnit(species, tile, pSpawnWithItems: false, pAdultAge: true);
    actor.data.name = name;
    actor.data.custom_name = true;
    actor.data.level = index + 1;
    actor.data.sex = index % 2 == 0 ? ActorSex.Male : ActorSex.Female;
    actor.clearSprites();
    int cultureIndex = index % cultures.Length;
    if (cultures[cultureIndex] == null)
    {
        cultures[cultureIndex] = World.world.cultures.newCulture(actor, false);
        cultures[cultureIndex].data.name = new[] { "祈风文化", "湖岸工艺", "石岭传统" }[cultureIndex];
    }
    actor.setCulture(cultures[cultureIndex]);
    CreatureSavedList.SaveActor(actor);
}
World.world.units.checkContainer();

string savedPath = Path.Combine(Application.persistentDataPath, "saved_actors.json");
var disk = JObject.Parse(File.ReadAllText(savedPath));
var old = (JObject)disk[Packet(oldName).ID];
old.Remove("SavedAtUtc");
old.Remove("Portrait");
File.WriteAllText(savedPath, disk.ToString());
new CreatureSavedList().Initialize();
Check(Packet(targetName).SavedAtUtc != null && Packet(oldName).SavedAtUtc == null, "Old and new records load together.");

var window = WindowCreatureSavedList.Instance;
async Task Open()
{
    ScrollWindow.hideAllEvent(pWithAnimation: false);
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow(windowId, pSkipAnimation: true);
    for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
    Check(ScrollWindow.isCurrentWindow(windowId), "The saved creatures window opened.");
}
Button Control(string name) => window.GetComponentsInChildren<Button>().Single(button => button.name == name);
SavedActorDataCard[] Visible() => window.GetComponentsInChildren<SavedActorDataCard>()
    .OrderByDescending(card => ((RectTransform)card.transform).anchoredPosition.y).ToArray();
SavedActorDataCard Card(string name) => Visible().Single(card => card.Name.Text.text == name);
string CountText() => window.GetComponentsInChildren<Text>().Single(text => text.name == "SavedCreatureCount").text;

await Open();
var search = window.GetComponentsInChildren<InputField>().Single();
var scroll = window.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "SavedCreatureScroll");
Check(Visible()[0].Name.Text.text == targetName && Visible().Length < names.Length &&
    CountText() == "共 25 项" && scroll.verticalScrollbar.gameObject.activeSelf,
    "The first page defaults to recent saves and uses a scrollable subset of the catalog.");
Check(Card(targetName).Avatar.image.sprite.name == "RankingPeakPortrait", "A new record shows its captured portrait after disk load.");
var sortLabel = Control("SortRecent").GetComponentsInChildren<Text>().Single(text => text.name == "SortLabel");
var sortDirection = Control("SortDirection").GetComponentsInChildren<Text>().Single(text => text.name == "DirectionLabel");
Check(sortLabel.fontSize == 8 && !sortLabel.resizeTextForBestFit && sortDirection.text == "↓",
    "Sort uses fixed-size labels and a full downward arrow for descending order.");
float top = scroll.content.anchoredPosition.y;
var wheel = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -3) };
ExecuteEvents.ExecuteHierarchy(Card(targetName).FavoriteButton.gameObject, wheel, ExecuteEvents.scrollHandler);
await Game.NextFrame();
Check(scroll.content.anchoredPosition.y > top, "The mouse wheel scrolls the list when the pointer is above a row action.");

search.text = "北境 HUMAN 祈风";
Check(CountText() == "1 / 25 项" && Card(oldName).Avatar.image.sprite.name != "RankingPeakPortrait",
    "Case-insensitive multi-word search combines name, species and origin and shows the older fallback icon.");
search.text = "苍河";
Check(CountText() == "2 / 25 项" && Visible().Any(card => card.Name.Text.text == targetName),
    "Searching by name updates the list and matching count immediately.");
search.text = "不存在的样本";
Check(Visible().Length == 0 && CountText() == "0 / 25 项" &&
    window.GetComponentsInChildren<Text>().Any(text => text.text == "没有符合条件的生物"),
    "A no-results search shows the proper empty state.");
Control("ClearSearch").onClick.Invoke();
Check(search.text == "" && Visible()[0].Name.Text.text == targetName, "Clearing the search restores all records.");

Control("SortLevel").onClick.Invoke();
Check(Visible()[0].Name.Text.text == targetName, "Level sorting starts with highest level.");
Control("SortDirection").onClick.Invoke();
Check(Visible()[0].Name.Text.text == oldName && sortDirection.text == "↑", "Reversing the sort puts the lowest level first.");
Control("SortName").onClick.Invoke();
var firstNames = Visible().Select(card => card.Name.Text.text).ToArray();
Check(firstNames.SequenceEqual(firstNames.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)),
    "Name sort orders the visible entries alphabetically.");
Control("SortRecent").onClick.Invoke();

Card(favoriteName).FavoriteButton.Button.onClick.Invoke();
Control("OnlyFavorites").onClick.Invoke();
Check(CountText() == "收藏 · 1 / 25 项" && Visible().Single().Name.Text.text == favoriteName,
    "The favorites filter uses the saved-record star.");
ScrollWindow.hideAllEvent(pWithAnimation: false);
new CreatureSavedList().Initialize();
Check(Packet(favoriteName).Favorite, "The favorite survives disk reload.");
await Open();
Check(Visible().Single().Name.Text.text == favoriteName, "Reopening preserves the favorites filter.");
Control("OnlyFavorites").onClick.Invoke();

scroll.verticalNormalizedPosition = 0;
await Game.NextFrame();
Check(Visible().Any(card => card.Name.Text.text == oldName) && Visible().Length < names.Length,
    "Scrolling to the bottom rebinds the small card pool to the oldest records.");
search.text = "云丘";
Card("云丘·学徒").DeleteButton.Button.onClick.Invoke();
Check(CreatureSavedList.SavedActors.Values.Any(packet => packet.ActorData.name == "云丘·学徒"),
    "One click arms deletion and leaves the saved record available.");
search.text = "松林";
Card("松林·医师").DeleteButton.Button.onClick.Invoke();
Check(Card("松林·医师") != null && CreatureSavedList.SavedActors.Count == 25,
    "Rebinding the row through search does not carry deletion confirmation to another record.");
Card("松林·医师").DeleteButton.Button.onClick.Invoke();
Check(CreatureSavedList.SavedActors.Count == 24 && Visible().Length == 0 &&
    CreatureSavedList.SavedActors.Values.Any(packet => packet.ActorData.name == "云丘·学徒"),
    "A second click removes only the confirmed entry and updates results.");
Control("ClearSearch").onClick.Invoke();

Tooltip.hideTooltipNow();
await Game.NextFrame();
Card(targetName).Button.TriggerHover();
var tip = Tooltip.findActive(item => item.asset.id == "inmny.godtools.ranking.peak_tooltip");
Check(tip?.GetComponentsInChildren<Text>().Any(text => text.name == "PeakName" && text.text == targetName) == true,
    "A recycled row shows the selected saved creature tooltip.");
Tooltip.hideTooltipNow();
WorldTip.hideNow();
await Game.WaitUntil(() => WorldTip.instance == null || WorldTip.instance.canvasGroup.alpha <= 0, 120);
string screenshot = Screen.Capture("saved-creatures-catalog.png");
await Game.NextFrame();

search.text = "北境";
string searchShot = Screen.Capture("saved-creatures-search.png");
await Game.NextFrame();
search.text = "归林猎手";
Card(targetName).Button.onClick.Invoke();
var before = World.world.units.getSimpleList().Select(actor => actor.id).ToHashSet();
Check(World.world.selected_power == GodPowers.place_saved_actor &&
    GodPowers.place_saved_actor.click_action(tile, GodPowers.place_saved_actor.id), "Filtered rows still arm normal placement.");
World.world.units.checkContainer();
var placed = World.world.units.getSimpleList().Single(actor => !before.Contains(actor.id));
Check(placed.getName() == targetName && placed.city == null, "Placement uses the record bound to the filtered row.");
await Open();
Check(search.text == "归林猎手" && Visible().Single().Name.Text.text == targetName,
    "The search term is preserved after placing and reopening the catalog.");
Control("ClearSearch").onClick.Invoke();
return new { screenshot, searchShot, records = CreatureSavedList.SavedActors.Count };
