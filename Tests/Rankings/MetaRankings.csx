using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(Application.persistentDataPath.Contains("godtools-review") ||
    Application.persistentDataPath.Contains("cultiway-dev"), "Use an isolated WorldBox profile.");
while (World.world == null || SmoothLoader.isLoading() || World.world.transition_screen?.isActiveAndEnabled == true)
    await Game.NextFrame();
Config.paused = true;

var factions = new[]
{
    (Land: "霜原", Town: "霜石城", Leader: "霜刃", People: 13, Years: 132),
    (Land: "海湾", Town: "白港城", Leader: "银帆", People: 11, Years: 118),
    (Land: "青岩", Town: "青峰城", Leader: "石魁", People: 9, Years: 96),
    (Land: "赤砂", Town: "日蚀城", Leader: "赤牙", People: 7, Years: 75),
    (Land: "苍林", Town: "松风城", Leader: "松影", People: 5, Years: 62),
    (Land: "雷谷", Town: "风暴城", Leader: "雷角", People: 3, Years: 29)
};
var zones = World.world.tiles_list.Where(tile => !tile.main_type.liquid && tile.zone != null)
    .GroupBy(tile => tile.zone).Select(group => group.First()).ToArray();
var examples = new Dictionary<string, List<NanoObject>>();
void Add(string board, NanoObject item)
{
    if (!examples.TryGetValue(board, out var list)) examples.Add(board, list = new List<NanoObject>());
    list.Add(item);
}

var leaders = new List<Actor>();
for (int i = 0; i < factions.Length; i++)
{
    var faction = factions[i];
    var tile = zones.First(item => item.zone.city == null && item.zone.neighbours_all.All(zone => zone.city == null));
    Actor Unit(string name, bool adult)
    {
        var actor = World.world.units.createNewUnit("human", tile, pSpawnWithItems: false, pAdultAge: adult);
        actor.data.name = name;
        return actor;
    }

    var leader = Unit(faction.Leader, true);
    var kingdom = World.world.kingdoms.makeNewCivKingdom(leader, null, false);
    kingdom.data.name = faction.Land + "王国";
    var city = World.world.cities.newCity(kingdom, tile.zone, leader);
    city.data.name = faction.Town;
    leader.joinCity(city);
    kingdom.setCapital(city);
    var culture = World.world.cultures.newCulture(leader, false);
    culture.data.name = faction.Land + "文化";
    var clan = World.world.clans.newClan(leader, false);
    clan.data.name = faction.Land + "氏族";
    clan.addRenown(210 - 29 * i);
    var language = World.world.languages.newLanguage(leader, false);
    language.data.name = faction.Land + "语";
    language.data.books_written = 33 - 5 * i;
    var religion = World.world.religions.newReligion(leader, false);
    religion.data.name = faction.Land + "信仰";
    var species = World.world.subspecies.newSpecies(leader.asset, tile);
    species.data.name = faction.Land + "亚种";
    var partner = Unit(faction.Land + "行者", true);
    partner.joinCity(city);
    var family = World.world.families.newFamily(leader, tile, partner);
    family.data.name = faction.Land + "家族";
    var army = World.world.armies.newArmy(leader, city);
    army.data.name = faction.Land + "军团";
    city.setCulture(culture);
    city.setLanguage(language);
    city.setReligion(religion);
    culture.listCity(city);
    language.listCity(city);
    religion.listCity(city);
    for (int j = 0; j < faction.People; j++)
    {
        var unit = j == 0 ? leader : j == 1 ? partner : Unit(faction.Land + "居民" + j, j % 3 != 0);
        unit.joinCity(city);
        unit.setCulture(culture);
        unit.setClan(clan);
        unit.setLanguage(language);
        unit.setReligion(religion);
        unit.setSubspecies(species);
        unit.setFamily(family);
        if (j < faction.People / 2)
        {
            unit.setArmy(army);
            army.listUnit(unit);
        }
        city.listUnit(unit);
        culture.listUnit(unit);
        clan.listUnit(unit);
        language.listUnit(unit);
        religion.listUnit(unit);
        species.listUnit(unit);
        family.listUnit(unit);
    }
    var age = World.world.getCurWorldTime() - 60d * faction.Years;
    foreach (var item in new IMetaObject[] { kingdom, city, clan, culture, language, religion, species, family, army })
        item.getMetaData().created_time = age;
    leaders.Add(leader);
    Add("王国榜", kingdom);
    Add("城市榜", city);
    Add("军队榜", army);
    Add("文化榜", culture);
    Add("氏族榜", clan);
    Add("家庭榜", family);
    Add("语言榜", language);
    Add("亚种榜", species);
    Add("宗教榜", religion);
}
World.world.units.checkContainer();
for (int i = 0; i < leaders.Count; i++)
{
    var leader = leaders[i];
    var neighbour = leaders[(i + 1) % leaders.Count];
    var plot = World.world.plots.newPlot(leader, AssetManager.plots_library.get("new_war"), true);
    plot.target_kingdom = neighbour.kingdom;
    plot.data.name = leader.kingdom.name + "的谋划";
    plot.data.progress_current = plot.getProgressMax() * (0.8f - i * 0.1f);
    Add("谋划榜", plot);
    if (i % 2 != 0) continue;
    var alliance = World.world.alliances.newAlliance(leader.kingdom, neighbour.kingdom);
    alliance.data.name = factions[i].Land + "同盟";
    Add("联盟榜", alliance);
    var war = World.world.wars.newWar(leader.kingdom, leaders[(i + 2) % leaders.Count].kingdom,
        AssetManager.war_types_library.get("normal"));
    war.data.name = factions[i].Land + "战事";
    war.data.dead_attackers = 50 - i * 8;
    war.data.dead_defenders = 25 - i * 4;
    Add("战争榜", war);
}

async System.Threading.Tasks.Task Open()
{
    for (int attempt = 0; attempt < 4 && !ScrollWindow.isCurrentWindow("WindowRankings"); attempt++)
    {
        while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
        ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
        for (int frame = 0; frame < 10; frame++) await Game.NextFrame();
    }
    Check(ScrollWindow.isCurrentWindow("WindowRankings"), "The ranking hub opened.");
}

await Open();
var window = WindowRankings.Instance;
RectTransform Rect(string name) => window.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == name);
Button ButtonIn(Component component, string name) => component.GetComponentsInChildren<Button>().Single(button => button.name == name);
var navigation = Rect("RankingNavigation").GetComponent<ScrollRect>();
Canvas.ForceUpdateCanvases();
var firstBoard = ButtonIn(navigation, "人物榜");
firstBoard.GetComponent<ScrollableButton>().OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) });
Check(navigation.content.anchoredPosition.y > 0, "The longer navigation scrolls with the pointer over an entry.");
navigation.verticalNormalizedPosition = 1;
void Board(string name)
{
    var button = ButtonIn(navigation, name);
    Canvas.ForceUpdateCanvases();
    float max = navigation.content.rect.height - navigation.viewport.rect.height;
    if (max > 0)
    {
        float offset = -((RectTransform)button.transform.parent).anchoredPosition.y - navigation.viewport.rect.height / 2;
        navigation.verticalNormalizedPosition = 1 - Mathf.Clamp01(offset / max);
    }
    button.onClick.Invoke();
}
var shots = new Dictionary<string, string>();
int index = 0;
foreach (var entry in examples)
{
    Board(entry.Key);
    var toggle = Rect("RankingRightTabs").GetComponentsInChildren<Button>().FirstOrDefault(button => button.name == "RankingSidebarToggle");
    if (toggle != null) toggle.onClick.Invoke();
    Canvas.ForceUpdateCanvases();
    for (int frame = 0; frame < 3; frame++) await Game.NextFrame();
    var entries = Rect("RankingEntries");
    var matching = entry.Value.FirstOrDefault(item => entries.GetComponentsInChildren<Button>().Any(button => button.name == item.name));
    Check(matching != null, entry.Key + " shows the seeded native objects.");
    var row = ButtonIn(entries, matching.name);
    Check(row.GetComponentsInChildren<BannerBase>().Any(banner => banner.GetNanoObject() == matching) &&
        row.transform.parent.GetComponentsInChildren<Text>().Any(text => text.name == "RankingEntryRank"),
        entry.Key + " shows a native flag and its rank.");
    Check(Rect("RankingColumns").GetComponentsInChildren<Text>().Length == 4, entry.Key + " has three default metrics.");
    shots.Add(entry.Key, Screen.Capture($"meta-ranking-{++index:00}.png"));
    await Game.NextFrame();
    row.onClick.Invoke();
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    var asset = matching.getMetaTypeAsset();
    Check(ScrollWindow.isCurrentWindow(asset.window_name) && asset.get_selected() == matching,
        entry.Key + " opens the corresponding native detail window.");
    await Open();
}

Board("城市榜");
var tabs = Rect("RankingRightTabs");
ButtonIn(tabs, "筛选").onClick.Invoke();
var sidebar = Rect("RankingRightSidebar");
ButtonIn(sidebar, "王国").onClick.Invoke();
var search = sidebar.GetComponentsInChildren<InputField>().Single(field => field.name == "RankingFilterSearch");
search.text = factions[0].Land;
var flag = ButtonIn(sidebar, factions[0].Land + "王国");
Check(flag.GetComponentInChildren<KingdomBanner>()?.GetNanoObject() == leaders[0].kingdom, "The city filter uses searchable native flags.");
flag.onClick.Invoke();
ButtonIn(window, "应用筛选").onClick.Invoke();
var filtered = Rect("RankingEntries");
Check(filtered.GetComponentsInChildren<Button>().Any(button => button.name == factions[0].Town) &&
    filtered.GetComponentsInChildren<Button>().All(button => button.name != factions[1].Town),
    "The affiliation filter narrows the rows by the native kingdom ID.");
ButtonIn(Rect("RankingRightTabs"), "行内容").onClick.Invoke();
ButtonIn(sidebar, "年龄").onClick.Invoke();
Check(Rect("RankingColumns").GetComponentsInChildren<Text>().Skip(1).Take(2).Select(item => item.text)
    .SequenceEqual(new[] { "人口", "年龄" }), "The city board displays its sort key before the pinned column.");
return new { boards = shots };
