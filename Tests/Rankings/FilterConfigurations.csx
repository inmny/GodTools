using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features;
using GodTools.UI;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(Application.persistentDataPath.Contains("godtools-review") ||
    Application.persistentDataPath.Contains("cultiway-dev"), "Run in an isolated WorldBox profile.");
while (World.world == null || SmoothLoader.isLoading() || World.world.transition_screen?.isActiveAndEnabled == true)
    await Game.NextFrame();
Config.paused = true;

Actor Settlement(string resident, string country, string town, string culture, string clan, string language, string species, string religion)
{
    var tile = World.world.tiles_list.First(t => !t.main_type.liquid && t.zone != null &&
        t.zone.city == null && t.zone.neighbours_all.All(zone => zone.city == null));
    var actor = World.world.units.createNewUnit("human", tile, pAdultAge: true);
    actor.data.name = resident;
    var kingdom = World.world.kingdoms.makeNewCivKingdom(actor, null, false);
    kingdom.data.name = country;
    var city = World.world.cities.newCity(kingdom, tile.zone, actor);
    city.data.name = town;
    actor.joinCity(city);
    kingdom.setCapital(city);

    var localCulture = World.world.cultures.newCulture(actor, false);
    localCulture.data.name = culture;
    actor.setCulture(localCulture);
    localCulture.listUnit(actor);
    var familyClan = World.world.clans.newClan(actor, false);
    familyClan.data.name = clan;
    actor.setClan(familyClan);
    var tongue = World.world.languages.newLanguage(actor, false);
    tongue.data.name = language;
    actor.setLanguage(tongue);
    var people = World.world.subspecies.newSpecies(actor.asset, tile);
    people.data.name = species;
    actor.setSubspecies(people);
    var faith = World.world.religions.newReligion(actor, false);
    faith.data.name = religion;
    actor.setReligion(faith);
    return actor;
}

var north = Settlement("北境守卫", "北境王国", "霜石城", "霜原文化", "铁杉氏族", "北境语", "霜原人", "群星教");
var coast = Settlement("海湾铁匠", "海湾王国", "白港城", "海湾文化", "银帆氏族", "海湾语", "白港人", "潮汐教");
var hill = Settlement("山岭学者", "山岭王国", "青峰城", "山岭文化", "青岩氏族", "山岭语", "青峰人", "山灵教");
var wolf = World.world.units.createNewUnit("wolf", north.current_tile, pAdultAge: true, pSpawnWithItems: false);
wolf.data.name = "灰狼样本";
north.addTrait("strong");
north.addTrait("giant");
coast.removeTrait("strong");
hill.removeTrait("strong");
north.data.kills = 120;
coast.data.level = 8;
north.stats[strings.S.health] = 1e8f;
ConsistentCreaturePowerTop.Check(north);

var sword = World.world.items.newItem(AssetManager.items.list.First(item =>
    item.equipment_type == EquipmentType.Weapon && !item.id.StartsWith("_")));
sword.data.name = "霜石长剑";
sword.data.kills = 12;
sword.data.modifiers.Add(AssetManager.items_modifiers.list.First().id);
north.equipment.setItem(sword, north);
World.world.units.checkContainer();

for (int attempt = 0; attempt < 4 && !ScrollWindow.isCurrentWindow("WindowRankings"); attempt++)
{
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
    for (int frame = 0; frame < 8; frame++) await Game.NextFrame();
}
Check(ScrollWindow.isCurrentWindow("WindowRankings"), "The ranking window opened.");
var window = WindowRankings.Instance;
RectTransform Rect(string name) => window.GetComponentsInChildren<RectTransform>().Single(item => item.name == name);
Button ButtonIn(Component area, string name) => area.GetComponentsInChildren<Button>().First(item => item.name == name);
void Click(string name) => ButtonIn(window, name).onClick.Invoke();
ScrollRect Sidebar() => window.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingRightSidebar");
Button[] Fields() => Sidebar().GetComponentsInChildren<RectTransform>().Where(item => item.name == "RankingFieldIcons")
    .SelectMany(row => row.GetComponentsInChildren<Button>()).ToArray();
void Field(string name) => Fields().Single(item => item.name == name).onClick.Invoke();
Button[] Choices() => Rect("RankingFilterChoices").GetComponentsInChildren<Button>()
    .Where(button => button.isActiveAndEnabled).ToArray();
InputField Search() => Sidebar().GetComponentsInChildren<InputField>().Single(item => item.name == "RankingFilterSearch");
bool HasActor(Actor actor) => Rect("RankingEntries").GetComponentsInChildren<Button>().Any(item => item.name == actor.name);
void ClearFilters()
{
    var clear = ButtonIn(Rect("RankingFilterActions"), "清空条件");
    if (clear.interactable) clear.onClick.Invoke();
    Click("应用筛选");
}

var portfolio = new List<object>();
async System.Threading.Tasks.Task Capture(string board, string field, string file)
{
    Canvas.ForceUpdateCanvases();
    for (int frame = 0; frame < 3; frame++) await Game.NextFrame();
    string path = Screen.Capture(file + ".png");
    await Game.NextFrame();
    portfolio.Add(new { board, field, path });
}

Click("人物榜");
ButtonIn(Rect("RankingRightTabs"), "筛选").onClick.Invoke();
await Capture("人物榜", "筛选字段总览", "filter-actors-overview");
var strong = AssetManager.traits.get("strong");
string strongName = LM.Get(strong.getLocaleID());
var affiliations = new Dictionary<string, NanoObject>
{
    ["王国"] = north.kingdom,
    ["所属文化"] = north.culture,
    ["所属氏族"] = north.clan,
    ["所属语言"] = north.language,
    ["所属亚种"] = north.subspecies,
    ["所属城市"] = north.city,
    ["所属宗教"] = north.religion
};
foreach (var pair in affiliations)
{
    Field(pair.Key);
    Check(Choices().Any(item => item.name == pair.Value.name), "The affiliation shows live choices for " + pair.Key);
    Search().text = pair.Value.name;
    var flags = Choices();
    Check(flags.Length == 1 && flags[0].name == pair.Value.name, "Search updates live for " + pair.Key);
    var flag = flags[0];
    var banner = flag.GetComponentInChildren<BannerBase>();
    Check(flag.transform.parent.name == "RankingAffiliationFlags" && flag.GetComponent<TipButton>() != null &&
        banner?.GetNanoObject() == pair.Value &&
        banner.GetComponentsInChildren<Image>().Count(image => image.enabled && image.sprite != null) >= 2,
        "The picker uses the selected object's native banner for " + pair.Key);
    await Game.NextFrame();
    Canvas.ForceUpdateCanvases();
    var raycaster = window.GetComponentInParent<GraphicRaycaster>();
    var rect = (RectTransform)flag.transform;
    var pointer = new PointerEventData(EventSystem.current)
    {
        position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center))
    };
    var hits = new List<RaycastResult>();
    raycaster.Raycast(pointer, hits);
    Check(hits.Any(hit => hit.gameObject == flag.gameObject) &&
        hits.All(hit => !hit.gameObject.transform.IsChildOf(banner.transform)),
        "The banner routes pointer input to the filter button for " + pair.Key);
    flag.onClick.Invoke();
    Click("应用筛选");
    Check(HasActor(north) && !HasActor(coast) && !HasActor(hill), "Affiliation matches the selected ID for " + pair.Key);
    ClearFilters();
}

Field("特质");
Check(Rect("RankingFilterChoices").GetComponentsInChildren<RectTransform>()
    .Count(item => item.name.StartsWith("RankingTraitGroup.", StringComparison.Ordinal)) >= 2 &&
    Choices().All(item => item.GetComponentsInChildren<Text>().Length == 0 && item.image.sprite != null),
    "Traits use native groups and bare icon cells.");
Search().text = "__no_trait_matches__";
Check(Choices().Length == 0, "Searching hides unmatched trait groups.");
Search().text = strongName;
Check(Choices().Any(item => item.name == strongName), "Traits can be searched by localized name.");
await Capture("人物榜", "特质搜索示例", "filter-actors-trait-search");
Choices().First(item => item.name == strongName).onClick.Invoke();
Click("应用筛选");
Check(HasActor(north) && !HasActor(coast) && !HasActor(hill), "A trait condition checks membership in the actor's tag set.");
await Capture("人物榜", "应用特质筛选后", "filter-actors-applied");
ClearFilters();

Field("生物类型");
var speciesOptions = Choices();
Check(speciesOptions.Length >= 2 && speciesOptions.All(button =>
    button.transform.parent.name == "RankingSpeciesIcons" && button.GetComponentsInChildren<Text>().Length == 0 &&
    button.GetComponent<TipButton>() != null && button.image?.sprite != null),
    "Species choices use an icon grid with hover hints.");
Search().text = "__no_species_matches__";
Check(Choices().Length == 0, "Searching hides species that do not match.");
string humanName = LM.Get(north.asset.getLocaleID());
Search().text = humanName;
var humanOption = Choices().Single(button => button.name == humanName);
Check(humanOption.image.sprite == north.asset.getSpriteIcon(), "Localised search exposes the human's native icon.");
humanOption.onClick.Invoke();
Click("应用筛选");
int humanCount = World.world.units.getSimpleList().Count(actor =>
    actor?.data != null && actor.isAlive() && actor.asset?.can_be_inspected == true && actor.asset.id == north.asset.id);
Check(HasActor(north) && !HasActor(wolf) &&
    window.GetComponentsInChildren<Text>().Single(text => text.name == "RankingResultCount").text ==
        string.Format(LM.Get("inmny.godtools.ranking.result_count"), humanCount),
    "Clicking the human icon filters using the matching actor asset ID.");
ClearFilters();

var boards = new List<(string Name, string ID)>
{
    ("人物榜", "actors"),
    ("装备榜", "equipment"),
    ("文化榜", "cultures"),
    ("战力峰值记录", "archive")
};
var navigation = Rect("RankingNavigation").GetComponentsInChildren<Button>();
if (navigation.Any(item => item.name == "功法榜")) boards.Add(("功法榜", "cultibooks"));

var samples = new Dictionary<string, string>
{
    ["名称"] = "霜", ["战力"] = "100", ["击杀"] = "10", ["年龄"] = "20", ["等级"] = "5",
    ["攻击"] = "15", ["最大生命"] = "100", ["子女数"] = "2", ["词条数"] = "1",
    ["耐久"] = "30", ["持有者"] = "北境", ["人口"] = "10", ["覆盖村庄"] = "1"
};

foreach (var board in boards)
{
    Click(board.Name);
    string[] fields = Fields().Select(item => item.name).ToArray();
    for (int index = 0; index < fields.Length; index++)
    {
        string field = fields[index];
        Field(field);
        var input = Sidebar().GetComponentsInChildren<InputField>().Single();
        if (input.name != "RankingFilterSearch" && samples.TryGetValue(field, out string sample))
            input.text = sample;
        await Capture(board.Name, field, $"filter-{board.ID}-{index + 1:00}");
        Click("返回条件");
    }
}
return new { screenshots = portfolio };
