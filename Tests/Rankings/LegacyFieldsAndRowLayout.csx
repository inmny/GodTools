using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GodTools.Features.Rankings;
using GodTools.UI;
using NeoModLoader.api;
using Newtonsoft.Json.Linq;
using UnityEngine;
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

var assembly = typeof(RankingRegistry).Assembly;
const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic;
const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
var source = typeof(RankingRegistry).GetMethod("GetSource", Internal).Invoke(null, new object[] { "godtools:actors" });
var fields = (IDictionary)source.GetType().GetField("Fields", Instance).GetValue(source);
int original = fields.Count;
bool summonFixture = AppDomain.CurrentDomain.GetAssemblies().Any(mod => mod.GetType("CustomModT002.RankingData") != null);

async System.Threading.Tasks.Task Open()
{
    ScrollWindow.hideAllEvent(pWithAnimation: false);
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
    for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
}
await Open();
Check(fields.Contains("inmny_custommodt002:summon_count") == summonFixture,
    "The summon integration is installed only when its module is loaded.");

var adapters = new Dictionary<string, (string Group, int Count)>
{
    ["INMNY_CUSTOMMODT001"] = ("inmny_custommodt001", 2),
    ["ZHETIAN"] = ("zhetian", 2), ["DOUPOCANGQIONG"] = ("doupocangqiong", 2),
    ["WITCHCRAFT_WORLDBOX"] = ("wushu", 2), ["WITCHCRAFT_KNIGHT"] = ("knight", 1),
    ["XULIE"] = ("guimi_mod", 2), ["WARRIOR"] = ("warrior_mod", 3),
    ["XIAN_TU"] = ("xian_tu_mod", 3), ["XIAN_NI_MOD"] = ("xianni", 7),
    ["THEFANTASYWORLD"] = ("fantasy_mod", 23), ["CHEVALIER"] = ("chevalier_mod", 2)
};
var install = assembly.GetType("GodTools.Features.Rankings.OptionalActorRankingFields", true).GetMethod("InstallFor", Internal);
var owners = new Dictionary<string, ModDeclare>();
foreach (var (id, (group, count)) in adapters)
{
    var owner = new ModDeclare(id + " 适配验收", "RankTest", "", "1.0", "", Application.persistentDataPath,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false);
    owners[id] = owner;
    install.Invoke(null, new object[] { id, owner });
    Check(fields.Keys.Cast<string>().Count(key => key.StartsWith(group + ":", StringComparison.Ordinal)) == count,
        "The exact number of old fields was registered for " + id);
}
int imported = fields.Count - original;
Check(imported == (summonFixture ? 51 : 49), "All old field groups are accounted for.");
RankingValue Read(string field, Actor actor) =>
    (RankingValue)((Delegate)fields[field].GetType().GetField("Read", Instance).GetValue(fields[field])).DynamicInvoke(actor);

var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
Actor Unit(string name, ActorSex sex)
{
    var unit = World.world.units.createNewUnit("human", tile, pSpawnWithItems: false, pAdultAge: true);
    unit.data.name = name;
    unit.data.custom_name = true;
    unit.data.sex = sex;
    return unit;
}
var elder = Unit("霜原剑修", ActorSex.Male);
var junior = Unit("松风修士", ActorSex.Female);
var unnamed = Unit("旅人", ActorSex.Male);
World.world.units.checkContainer();
elder.setHealth(Math.Max(1, elder.getMaxHealth() / 2));
junior.setHealth(Math.Max(1, junior.getMaxHealth() * 3 / 4));
if (!elder.isFavorite()) elder.switchFavorite();
elder.data.set("StartBoard.custommodt001.zhetian_level", 3);
junior.data.set("StartBoard.custommodt001.zhetian_level", 3);
elder.data.set("StartBoard.custommodt001.zhetian_exp", 200);
junior.data.set("StartBoard.custommodt001.zhetian_exp", 100);
elder.data.set("xn.stat.xiuwei", 9007199254740993L);
junior.data.set("xn.stat.xiuwei", 9007199254740992L);
elder.data.set("wushu.warriorNum", 0f);
elder.data.set("wushu.MagicShield", 26.5f);
elder.data.set("fixture.wolf", 3);
elder.data.set("fixture.dragon", 2);
elder.data.set("fixture.level", 4);
Check(Read("warrior_mod:warrior", elder)?.Number == 0 && Read("warrior_mod:warrior", unnamed) == null,
    "An explicit legacy zero remains a ranked value while an absent key stays missing.");
Check(Read("fantasy_mod:magicshield", elder)?.Number == 26.5 &&
    Read("xianni:xiuwei", elder).Key == "9007199254740993" && Read("xianni:combat_power", elder) != null,
    "Legacy custom data and the original Xianni formula have renderable values.");
if (summonFixture)
    Check(Read("inmny_custommodt002:summon_count", elder)?.Number == 5 &&
        Read("inmny_custommodt002:summon_level", elder)?.Label == "召唤师 4",
        "The optional adapter calls the original summon API shape and totals all types.");

var window = WindowRankings.Instance;
Button ButtonIn(Component root, string name) => root.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == name)
    ?? throw new InvalidOperationException("Missing button " + name);
RectTransform Region(string name) => window.GetComponentsInChildren<RectTransform>().Single(item => item.name == name);
void Click(string name) => ButtonIn(window, name).onClick.Invoke();
void Tab(string name) => ButtonIn(Region("RankingRightTabs"), name).onClick.Invoke();
void SelectField(string name) => ButtonIn(Region("RankingRightSidebar"), name).onClick.Invoke();
Transform ActorRow(string name) => ButtonIn(Region("RankingEntries"), name).transform.parent;
string Rank(string name) => ActorRow(name).GetComponentsInChildren<Text>().Single(item => item.name == "RankingEntryRank").text;
string[] Headers() => Region("RankingColumns").GetComponentsInChildren<Text>().Select(item => item.text).ToArray();
void SortBy(string name, string id)
{
    Tab("排序");
    Click("恢复默认排序");
    SelectField(name);
    ButtonIn(Region("RankingSortKey." + id), "↑").onClick.Invoke();
}

Check(!window.GetComponentsInChildren<Button>().Any(item => item.name == "旧版自定义人物榜"), "The legacy window has no navigation entry.");
Click("人物榜");
SortBy("遮天境界", "zhetian:level");
Check(Rank(elder.getName()) == "#1" && Rank(junior.getName()) == "#2",
    "Equal realms are ordered on the original within-realm experience.");
SortBy("修为", "xianni:xiuwei");
Check(Rank(elder.getName()) == "#1" && Rank(junior.getName()) == "#2",
    "Large integers keep their exact ordering beyond double precision.");
Tab("筛选");
SelectField("修为");
window.GetComponentsInChildren<InputField>().Single(item => item.name != "RankingRankJumpInput").text = "9007199254740993";
Click("添加条件");
Click("应用筛选");
Check(Region("RankingEntries").GetComponentsInChildren<Button>().Any(item => item.name == elder.getName()) &&
    !Region("RankingEntries").GetComponentsInChildren<Button>().Any(item => item.name == junior.getName()),
    "The integer threshold keeps its exact saved value during filtering.");
Click("清空条件");
Click("应用筛选");
Tab("排序");
ButtonIn(Region("RankingSortKey.power:godtools:standard-duel@2"), "×").onClick.Invoke();
Tab("行内容");
Click("自动补列");
SelectField("当前生命");
SelectField("收藏状态");
SelectField("性别");
Check(Headers().SequenceEqual(new[] { "名称", "修为", "当前生命", "收藏状态" }),
    "Disabling auto-fill keeps the sort key and the pins that fit.");
Click("RankingSidebarToggle");
Check(Headers().SequenceEqual(new[] { "名称", "修为", "当前生命", "收藏状态", "性别" }),
    "Extra space reveals the remaining pin without adding board defaults when auto-fill is off.");
Tab("行内容");
var elderRow = ActorRow(elder.getName());
var gauge = elderRow.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingMetricMeterFill");
Check(Mathf.Abs(gauge.anchorMax.x - elder.getHealth() / (float)elder.getMaxHealth()) < 0.001f &&
    elderRow.GetComponentsInChildren<Image>().Any(item => item.sprite == SpriteTextureLoader.getSprite("ui/icons/iconFavoriteStar")),
    "Health is rendered as a gauge and the favorite state uses the native icon.");
for (int i = 0; i < 2; i++) await Game.NextFrame();
string actorShot = Screen.Capture("ranking-custom-actor-row.png");
await Game.NextFrame();
await Game.NextFrame();
Click("头像");
Click("名次");
Check(!ActorRow(elder.getName()).GetComponentsInChildren<RectTransform>().Any(item => item.name == "RankingPortrait") &&
    !ActorRow(elder.getName()).GetComponentsInChildren<Text>().Any(item => item.name == "RankingEntryRank"),
    "Portraits and ranks can be hidden by the viewer.");
Tab("预设");
window.GetComponentsInChildren<InputField>().Single(item => item.name == "RankingPresetName").text = "人物行布局验收";
Click("保存预设");
Tab("行内容");
Click("头像");
Click("名次");
Click("自动补列");
Tab("预设");
Click("人物行布局验收");
Check(Headers().SequenceEqual(new[] { "名称", "修为", "当前生命", "收藏状态" }) &&
    !ActorRow(elder.getName()).GetComponentsInChildren<RectTransform>().Any(item => item.name == "RankingPortrait"),
    "Applying a row preset restores its display options along with its filters and fields.");

const string external = "row-layout-test:caravans";
Check(RankingRegistry.RegisterSource(new RankingSource<int>
{
    ID = external, Read = () => new[] { 90, 40 }, Key = row => row.ToString(), Name = row => $"商队 {row}"
}), "A second row type can be registered.");
Check(RankingRegistry.RegisterField(external, new RankingField<int>
{
    ID = "progress", Name = "行程", Kind = RankingFieldKind.Number,
    Read = row => RankingValue.Progress(row, 100)
}) && RankingRegistry.RegisterField(external, new RankingField<int>
{
    ID = "state", Name = "状态", Kind = RankingFieldKind.Category,
    Read = row => RankingValue.WithIcon("moving", "行进中", "ui/icons/iconCityList")
}), "External fields can return meter and icon values.");
Check(RankingRegistry.RegisterBoard(new RankingBoard
{
    ID = external, SourceID = external, Name = "商队榜", Columns = new[] { "progress" },
    DefaultSort = "state", RowLayout = new RankingRowLayout { ShowPortrait = false, ShowRank = false, FillColumns = false }
}), "An external board can declare its own default row settings.");
Click("榜单总览");
Click("商队榜");
Check(Headers().SequenceEqual(new[] { "名称", "状态", "行程" }) &&
    Region("RankingEntries").GetComponentsInChildren<RectTransform>().Count(item => item.name == "RankingMetricMeterFill") == 2 &&
    Region("RankingEntries").GetComponentsInChildren<Image>().Any(item => item.sprite == SpriteTextureLoader.getSprite("ui/icons/iconCityList")) &&
    !Region("RankingEntries").GetComponentsInChildren<Text>().Any(item => item.name == "RankingEntryRank"),
    "A third-party board displays its sort key even when it is not a pinned column and auto-fill is off.");
for (int i = 0; i < 2; i++) await Game.NextFrame();
string externalShot = Screen.Capture("ranking-custom-caravan-row.png");
await Game.NextFrame();
await Game.NextFrame();

string folder = Path.Combine(Application.persistentDataPath, "ranking-row-layout-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
SaveManager.saveMapData(folder);
var data = JObject.Parse(File.ReadAllText(Path.Combine(folder, "godtools_rankings.json")));
var layout = data["Presets"].Single(item => item.Value<string>("Name") == "人物行布局验收")["RowLayout"];
Check(!layout.Value<bool>("ShowPortrait") && !layout.Value<bool>("ShowRank") && !layout.Value<bool>("FillColumns"),
    "The row settings are persisted in the world preset.");
Check(RankingRegistry.RegisterField("godtools:actors", new RankingField<Actor>
{
    ID = "warrior_mod:warrior", Name = "主动提供气血", Kind = RankingFieldKind.Number, Owner = owners["WARRIOR"],
    Read = _ => RankingValue.Numeric(99)
}) && !fields.Contains("warrior_mod:true_gang") && Read("warrior_mod:warrior", elder)?.Number == 99,
    "A participating mod replaces its legacy fallback group.");
return new { imported, summonFixture, actorShot, externalShot };
