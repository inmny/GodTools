using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Features.Rankings;
using GodTools.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
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

var rows = new[] { (Key: "a", Name: "青玉", Grade: "heaven", Ring: (double?)4, Shape: "jade"),
    (Key: "b", Name: "白玉", Grade: "heaven", Ring: (double?)4, Shape: "jade"),
    (Key: "c", Name: "青金", Grade: "heaven", Ring: (double?)1, Shape: "metal"),
    (Key: "d", Name: "碎石", Grade: "yellow", Ring: (double?)0, Shape: "stone"),
    (Key: "e", Name: "未鉴定", Grade: (string)null, Ring: (double?)null, Shape: "stone"),
    (Key: "f", Name: "小数条目", Grade: "mystic", Ring: (double?)0.25, Shape: "stone") };
string id = "rank-snapshot-test:" + Guid.NewGuid().ToString("N")[..6];
const string Grades = "品阶验收榜", Rings = "环阶验收榜", Kinds = "种类验收榜", Spare = "未收藏榜";
const string GradePreset = "高品材料", RingPreset = "环阶速览", KindPreset = "材料种类";
const string GradeField = "品阶验收", RingField = "环阶验收", KindField = "材料种类验收";
int sourceReads = 0, ringReads = 0, unusedReads = 0;
int ringBoost = 0;
string heavenLabel = "天品初阶";

Check(RankingRegistry.RegisterSource(new RankingSource<(string Key, string Name, string Grade, double? Ring, string Shape)>
{
    ID = id, Read = () => { sourceReads++; return rows; }, Key = row => row.Key, Name = row => row.Name,
    Inspect = _ => { }
}), "The shared source is registered.");
Check(RankingRegistry.RegisterField(id, new RankingField<(string Key, string Name, string Grade, double? Ring, string Shape)>
{
    ID = "grade", Name = GradeField, Kind = RankingFieldKind.Ordinal, IconPath = "ui/icons/iconStone",
    Options = new[]
    {
        new RankingOption("heaven", "天品初阶", 10, () => heavenLabel, new("tier-heaven", "天", 0)),
        new RankingOption("earth", "地品初阶", 7, null, new("tier-earth", "地", 1)),
        new RankingOption("mystic", "玄品初阶", 4, null, new("tier-mystic", "玄", 2)),
        new RankingOption("yellow", "黄品初阶", 1, null, new("tier-yellow", "黄", 3))
    },
    Read = row => RankingValue.Choice(row.Grade, null)
}), "Ordinal options and chart bands are independent.");
Check(RankingRegistry.RegisterField(id, new RankingField<(string Key, string Name, string Grade, double? Ring, string Shape)>
{
    ID = "ring", Name = RingField, Kind = RankingFieldKind.Integer, Unit = "环", Description = "整数环阶",
    Read = row => { ringReads++; return row.Ring.HasValue ? RankingValue.Numeric(row.Ring.Value + ringBoost) : null; }
}), "The integer field accepts regular numeric values.");
Check(RankingRegistry.RegisterField(id, new RankingField<(string Key, string Name, string Grade, double? Ring, string Shape)>
{
    ID = "shape", Name = KindField, Kind = RankingFieldKind.Category,
    Operators = new[] { RankingOperator.Equal },
    Read = row => RankingValue.Choice(row.Shape, row.Shape switch { "jade" => "玉石", "metal" => "金属", _ => "矿石" }, 99)
}), "Categories may restrict their filtering operators and ignore numeric hints.");
Check(RankingRegistry.RegisterSource(new RankingSource<string>
{
    ID = id + ":unused", Read = () => { unusedReads++; return new[] { "unused" }; }, Key = row => row, Name = row => row
}), "The unfeatured source is registered.");

void Board(string key, string source, string title, params string[] columns) =>
    Check(RankingRegistry.RegisterBoard(new RankingBoard
    {
        ID = id + key, SourceID = source, Name = title, IconPath = "ui/icons/iconBooks",
        Columns = columns, DefaultSort = columns.FirstOrDefault()
    }), "A test board is registered.");
Board(":grades", id, Grades, "grade", "ring");
Board(":rings", id, Rings, "ring");
Board(":kinds", id, Kinds, "shape");
Board(":spare", id + ":unused", Spare);

async System.Threading.Tasks.Task Open()
{
    ScrollWindow.hideAllEvent(pWithAnimation: false);
    while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
    ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
    for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
}
await Open();
var window = WindowRankings.Instance;
Button ButtonIn(Component root, string name) => root.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == name)
    ?? throw new InvalidOperationException("Missing button " + name);
void Click(string name) => ButtonIn(window, name).onClick.Invoke();
RectTransform Region(string name) => window.GetComponentsInChildren<RectTransform>().Single(item => item.name == name);
void Tab(string name) => ButtonIn(Region("RankingRightTabs"), name).onClick.Invoke();
string Rank(string unit) => ButtonIn(Region("RankingEntries"), unit)
    .GetComponentsInChildren<Text>().Single(item => item.name == "RankingEntryRank").text;
Button CardButton(string title) => ButtonIn(Region("RankingPanel"), title);
string[] Card(string title) => CardButton(title).GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
int Leaders(string title) => CardButton(title).GetComponentsInChildren<RectTransform>().Count(item => item.name == "RankingFeaturedLeader");
InputField CountInput(string title) => CardButton(title).GetComponentsInChildren<InputField>().Single(item => item.name == "RankingFeaturedCount");
bool Below(string lower, string upper)
{
    var below = (RectTransform)CardButton(lower).transform;
    var above = (RectTransform)CardButton(upper).transform;
    return Mathf.Approximately(below.anchoredPosition.x, above.anchoredPosition.x) &&
        Mathf.Approximately(above.anchoredPosition.y - below.anchoredPosition.y, above.rect.height + 6);
}
Tooltip Details(string title)
{
    Tooltip.hideTooltipNow();
    ButtonIn(CardButton(title), "RankingFeaturedDetails").onClick.Invoke();
    var tip = Tooltip.findActive(item => item.transform.name == "inmny.godtools.ranking.preset_tooltip");
    Check(tip != null && tip.name.text == title, "The card opens the same named preset tooltip.");
    return tip;
}
string[] Chart() => Region("RankingDistribution").GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
void Favorite(string preset)
{
    Tab("预设");
    window.GetComponentsInChildren<InputField>().Single(item => item.name == "RankingPresetName").text = preset;
    Click("保存预设");
    Check(!window.GetComponentsInChildren<Button>().Any(item => item.name == "加入总览"),
        "The preset sidebar no longer has a separate Add to overview button.");
    Button Star() => ButtonIn(ButtonIn(Region("RankingRightSidebar"), preset).transform.parent, "RankingPresetFavorite");
    var icon = SpriteTextureLoader.getSprite("ui/icons/iconFavoriteStar");
    Check(Star().image.sprite == icon && Star().image.color.a < 0.9f, "A saved preset starts with the muted native star.");
    Star().onClick.Invoke();
    Check(Star().image.sprite == icon && Star().image.color.a == 1f, "Favoriting lights the native star in place.");
    Star().onClick.Invoke();
    Check(Star().image.color.a < 0.9f, "The same star unfavorites the preset.");
    Star().onClick.Invoke();
}

Check(sourceReads == 0 && unusedReads == 0, "An empty overview does not scan any source.");
Click(Kinds);
Check(Chart().Where(label => new[] { "玉石", "金属", "矿石" }.Contains(label))
    .SequenceEqual(new[] { "矿石", "玉石", "金属" }), "Category bars sort by frequency and ignore numeric hints.");
Tab("筛选");
ButtonIn(Region("RankingRightSidebar"), KindField).onClick.Invoke();
Check(!Region("RankingRightSidebar").GetComponentsInChildren<Button>()
    .Any(item => item.name is "至少" or "至多" or "有值" or "无值"),
    "A category exposes only its declared operators.");
Click(Grades);
Tab("排序");
ButtonIn(Region("RankingRightSidebar"), RingField).onClick.Invoke();
Check(Rank("青玉") == "#1" && Rank("白玉") == "#1" && Rank("青金") == "#3" && Rank("未鉴定") == "未排名",
    "Both keys must agree for a competition tie; missing primary has no rank.");
Check(Chart().Where(label => new[] { "天", "地", "玄", "黄" }.Contains(label))
    .SequenceEqual(new[] { "天", "地", "玄", "黄" }) && Chart().Contains("有值 5 / 6"),
    "Bands follow declared order, include empty bins, and omit missing values from coverage.");

Tab("筛选");
ButtonIn(Region("RankingRightSidebar"), GradeField).onClick.Invoke();
ButtonIn(Region("RankingRightSidebar"), "至少").onClick.Invoke();
ButtonIn(Region("RankingRightSidebar"), heavenLabel).onClick.Invoke();
Click("应用筛选");
Check(Rank("青玉") == "#1" && Rank("青金") == "#3" && Chart().Contains("样本 3"),
    "A named ordinal cutoff uses the same rows as the live chart.");
Favorite(GradePreset);
await Game.NextFrame();
string presetShot = Screen.Capture("rankings-preset-star.png");
await Game.NextFrame();
await Game.NextFrame();
int previousReads = sourceReads, previousRingReads = ringReads;
Click("保存本次排名");
Check(sourceReads == previousReads && ringReads == previousRingReads, "Capturing the shown result performs no reads.");

Click(Rings);
Check(Rank("碎石") == "#4" && Rank("未鉴定") == "未排名" && Rank("小数条目") == "未排名" &&
    Chart().Contains("0") && Chart().Contains("有值 4 / 6"), "A real zero ranks, a missing or fractional integer does not.");
Favorite(RingPreset);
Click(Kinds);
Favorite(KindPreset);
Click("榜单总览");
Check(Card(GradePreset).Any(text => text.Contains("#3  青金")) &&
    Card(GradePreset).All(text => !text.Contains("参排范围") && !text.Contains(GradeField)) &&
    Card(RingPreset).Any(text => text.Contains("刷新于")) &&
    Region("RankingPanel").GetComponentsInChildren<Text>().Any(text => text.text == "最近记录"),
    "The card keeps the entries and time while moving the field and filters out of the card body.");
var detail = Details(GradePreset);
Check(detail.description_2.text.Contains("品阶验收 至少") && detail.description_2.text.Contains(heavenLabel) &&
    detail.GetComponentsInChildren<Image>().Any(item => item.name == "RankingPresetSortIcon" &&
        item.sprite == SpriteTextureLoader.getSprite("ui/icons/iconStone")),
    "The details use the preset's filter description and custom sort icon.");
Tooltip.hideTooltipNow();
Check(Leaders(GradePreset) == 3 && Leaders(RingPreset) == 3, "Each preset starts with three visible items.");
previousReads = sourceReads;
previousRingReads = ringReads;
ButtonIn(CardButton(RingPreset), "RankingFeaturedMore").onClick.Invoke();
Check(Leaders(RingPreset) == 4 && Leaders(GradePreset) == 3 && CountInput(RingPreset).text == "4",
    "The up arrow increases only its own card.");
Check(Below(KindPreset, GradePreset), "The third card follows the shorter left column.");
ButtonIn(CardButton(RingPreset), "RankingFeaturedFewer").onClick.Invoke();
Check(Leaders(RingPreset) == 3 && CountInput(RingPreset).text == "3", "The down arrow decreases the item count.");
CountInput(RingPreset).onEndEdit.Invoke("999");
Check(CountInput(RingPreset).text == "50" && Leaders(RingPreset) == 4, "Manual input clamps to the maximum.");
CountInput(RingPreset).onEndEdit.Invoke("0");
Check(CountInput(RingPreset).text == "1" && Leaders(RingPreset) == 1, "Manual input clamps to the minimum.");
Check(Below(KindPreset, RingPreset), "Changing a card height moves the next card below the shorter right column.");
CountInput(RingPreset).onEndEdit.Invoke("4");
Check(CountInput(RingPreset).text == "4" && Leaders(RingPreset) == 4 &&
    sourceReads == previousReads && ringReads == previousRingReads, "The change only redraws already sampled data.");
ringBoost = 1;
Click("刷新常看视图");
Check(sourceReads == previousReads + 1 && ringReads == previousRingReads + rows.Length && unusedReads == 0,
    "Refreshing three featured views reads their shared source and field once, with no reads of the unused board.");
previousReads = sourceReads;
previousRingReads = ringReads;
heavenLabel = "Heaven, first grade";
Click(RingPreset);
Check(sourceReads == previousReads && ringReads == previousRingReads && Chart().Contains("5"),
    "A card opens the same captured values in its table and chart.");
Click("榜单总览");
Check(Details(GradePreset).description_2.text.Contains(heavenLabel),
    "A cached preset label can change without rereading the source.");
Tooltip.hideTooltipNow();
Click(Grades + " · " + GradePreset);
Check(sourceReads == previousReads && ringReads == previousRingReads &&
    Region("RankingPanel").GetComponentsInChildren<Text>().Any(text => text.text == heavenLabel),
    "Recent archives open frozen samples directly, and stable option IDs resolve in the current locale.");

heavenLabel = "天品初阶";
Click("榜单总览");
await Game.NextFrame();
string shot = Screen.Capture("featured-rankings-overview.png");
await Game.NextFrame();
await Game.NextFrame();
Details(GradePreset);
await Game.NextFrame();
string tooltipShot = Screen.Capture("featured-rankings-tooltip.png");
await Game.NextFrame();
await Game.NextFrame();
Tooltip.hideTooltipNow();
string folder = Path.Combine(Application.persistentDataPath, "rank-overview-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
SaveManager.saveMapData(folder);
string sidecar = Path.Combine(folder, "godtools_rankings.json");
var data = JObject.Parse(File.ReadAllText(sidecar));
var preset = data["Presets"].Single(item => item.Value<string>("Name") == GradePreset);
Check(preset["Conditions"][0]["Condition"].Value<string>("Value") == "heaven" &&
    preset["Conditions"][0]["Condition"].Value<bool>("ChoiceKey"),
    "Rank cutoffs are persisted with a stable option key.");
var archive = data["Archives"].Single(item => item["View"].Value<string>("Name") == GradePreset);
var frozen = archive["Rows"].First();
Check(frozen["Values"]["grade"].Value<string>("Key") == "heaven" && frozen["Values"]["ring"].Value<double>("Number") == 4,
    "History stores the original numeric and option values even after refreshing the overview.");
var ringPreset = data["Presets"].Single(item => item.Value<string>("Name") == RingPreset);
Check(ringPreset.Value<int>("PreviewCount") == 4 &&
    data["Previews"][ringPreset.Value<string>("ID")]["Leaders"].Count() == 4,
    "The chosen display count and cached results are stored in the world save.");
((JObject)preset).Remove("PreviewCount");
((JObject)data["Previews"][preset.Value<string>("ID")]).Remove("Complete");
File.WriteAllText(sidecar, data.ToString());

var oldMap = World.world.map_stats;
SaveManager.setCurrentPathAndId(folder, -1);
World.world.save_manager.loadWorld(folder);
for (int frame = 0; frame < 900 && (SmoothLoader.isLoading() || oldMap == World.world.map_stats); frame++) await Game.NextFrame();
Check(!SmoothLoader.isLoading() && oldMap != World.world.map_stats, "The isolated world reloaded.");
await Open();
Check(sourceReads == previousReads && ringReads == previousRingReads && Card(GradePreset).Any(text => text.Contains(heavenLabel)),
    "Opening the overview after reloading a world uses saved summaries and does not query the sources.");
Check(CountInput(GradePreset).text == "3" && CountInput(RingPreset).text == "4",
    "The old preset defaults to three and the new setting survives reload.");
ButtonIn(CardButton(GradePreset), "RankingFeaturedMore").onClick.Invoke();
Check(sourceReads == previousReads + 1 && CountInput(GradePreset).text == "4" && Leaders(GradePreset) == 3,
    "Expanding a legacy three-entry preview fills the cache only for that card.");
Click(GradePreset);
Check(sourceReads == previousReads + 1 && Chart().Contains("样本 3"), "Opening that card reuses the rebuilt result.");
return new { sourceReads, ringReads, unusedReads, shot, presetShot, tooltipShot, saved = folder };
