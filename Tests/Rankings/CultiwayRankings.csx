using System;
using System.Linq;
using Cultiway;
using Cultiway.Content;
using Cultiway.Content.Artifacts;
using Cultiway.Content.Artifacts.Baibao;
using Cultiway.Content.Components;
using Cultiway.Content.Libraries;
using Cultiway.Core;
using Cultiway.Core.Components;
using Cultiway.UI.Prefab;
using Cultiway.Utils;
using Cultiway.Utils.Extension;
using GodTools.UI;
using UnityEngine;
using UnityEngine.UI;

void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

Check(Application.persistentDataPath.Contains("godtools-review") ||
    Application.persistentDataPath.Contains("cultiway-dev"), "Use an isolated WorldBox profile.");
while (World.world == null || SmoothLoader.isLoading() || World.world.transition_screen?.isActiveAndEnabled == true)
    await Game.NextFrame();
var web = MagicWebManager.Instance;
var regions = WorldboxGame.I.GeoRegions;
for (int frame = 0; frame < 360 && (web.Count == 0 || !regions.CanResolveRegionTiles()); frame++)
    await Game.NextFrame();
Check(web.Count > 0, "The magic web has live spells in this world.");
Check(regions.CanResolveRegionTiles(), "The isolated world has a live map of geographic regions.");
Config.paused = true;

var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
var actor = World.world.units.createNewUnit("human", tile);
World.world.units.checkContainer();
string suffix = Guid.NewGuid().ToString("N")[..6];
var book = new CultibookAsset
{
    id = "ranking_test_" + suffix, Name = "排行榜验收功法 " + suffix,
    Description = "用于检查功法榜注册、修炼人数和详情。", FinalStats = new BaseStats()
};
Manager.CultibookLibrary.add(book);
actor.GetExtend().Master(book, 50);

while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
for (int frame = 0; frame < 12; frame++) await Game.NextFrame();
var window = WindowRankings.Instance;
Button Button(string title)
{
    var value = window.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == title);
    Check(value != null, "Missing " + title + " in " + string.Join(" / ", window.GetComponentsInChildren<Button>().Select(item => item.name)));
    return value;
}
var side = window.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingNavigation");
var boardNames = new[] { "功法榜", "魔网法术榜", "地区榜", "法宝榜",
    "材料榜", "符箓榜" };
Check(side.GetComponentsInChildren<Button>().Select(item => item.name).Where(boardNames.Contains).SequenceEqual(boardNames),
    "Cultiway registers all six boards in order.");
Check(side.GetComponentsInChildren<Button>().All(item => item.name != "Cultiway · 境界榜"),
    "Cultiway's actor metrics use the shared actor board.");
Button("人物榜").onClick.Invoke();
Button("排序").onClick.Invoke();
Button("恢复默认排序").onClick.Invoke();
var sortSidebar = window.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingRightSidebar");
Button SortKey(string title) => sortSidebar.GetComponentsInChildren<Button>().Single(item => item.name == title);
string[] Groups() => sortSidebar.GetComponentsInChildren<Text>().Where(item => item.name == "RankingFieldGroup")
    .Select(item => item.text).ToArray();
bool HasColumn(string title) => window.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingColumns")
    .GetComponentsInChildren<Text>().Any(item => item.text == title);
var expectedGroups = new[] { "原版", Cultiway.ModClass.Instance.GetDeclaration().Name };
Check(Groups().SequenceEqual(expectedGroups),
    "Sorting groups the base game and Cultiway icons separately.");
foreach (string key in new[] { "修仙境界", "魔法境界", "骑士境界", "修仙天赋", "法术数量", "主修功法品阶", "徒弟数量" })
    Check(SortKey(key).interactable, $"Cultiway's key {key} can be selected.");
SortKey("魔法境界").onClick.Invoke();
var magicRow = sortSidebar.GetComponentsInChildren<RectTransform>().Single(row => row.name == "RankingSortKey.cultiway:magic");
Check(magicRow.GetComponentsInChildren<Text>().Any(text => text.text == "2   魔法境界"),
    "A Cultiway keyword is appended after the built-in primary.");
magicRow.GetComponentsInChildren<Button>().Single(button => button.name == "↑").onClick.Invoke();
Check(sortSidebar.GetComponentsInChildren<Text>().Any(text => text.text == "1   魔法境界"),
    "Moving the realm to the front makes it primary on the actor board.");
Canvas.ForceUpdateCanvases();
sortSidebar.verticalNormalizedPosition = 0;
float sortOffset = sortSidebar.content.anchoredPosition.y;
Check(Mathf.Abs(sortOffset - Mathf.Max(0, sortSidebar.content.rect.height - sortSidebar.viewport.rect.height)) < 1,
    "The compact grid reaches the last keyword.");
SortKey("徒弟数量").onClick.Invoke();
Check(HasColumn("徒弟数量") &&
    Mathf.Abs(sortSidebar.content.anchoredPosition.y - sortOffset) < 1,
    "Selecting a lower key updates the table and preserves the sidebar scroll position.");
window.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingRightTabs")
    .GetComponentsInChildren<Button>().Single(item => item.name == "筛选").onClick.Invoke();
window.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == "返回条件")?.onClick.Invoke();
Check(Groups().SequenceEqual(expectedGroups),
    "Built-in and Cultiway actor fields appear under their own mod names.");
SortKey("魔法境界").onClick.Invoke();
sortSidebar.GetComponentsInChildren<InputField>().Single().text = "无魔法";
SortKey("无魔法").onClick.Invoke();
Check(window.GetComponentsInChildren<Text>().Any(text => text.name == "RankingFilterReadout" && text.text == "魔法境界 等于 无魔法") &&
    HasColumn("徒弟数量"),
    "A searchable Cultiway choice becomes a condition without replacing the visible table.");
var board = side.GetComponentsInChildren<Button>().Single(item => item.name == "功法榜");
board.onClick.Invoke();
var row = Button(book.Name);
Check(row.GetComponentsInChildren<Text>().Any(text => text.name == "RankingEntryRank" && text.text == "#1") &&
    row.transform.parent.GetComponentsInChildren<Text>().Any(text => text.text == "1"),
    "The Cultiway adapter reads the real learner and the row ranks first.");
var image = Screen.Capture("rankings-cultiway.png");
await Game.NextFrame();
await Game.NextFrame();
row.onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.name != null && tip.name.text.Contains(book.Name)) != null,
    "Clicking a book opens Cultiway's own tooltip for that exact book.");
Tooltip.hideTooltipNow();

var topRing = web.Query(new MagicWebQuery { MaxResults = web.Count }).Max(entry => entry.Profile.Ring);
Button(boardNames[1]).onClick.Invoke();
var entries = window.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingEntries");
Button FirstRow() => entries.GetComponentsInChildren<Text>().First(text => text.name == "RankingEntryRank" && text.text == "#1")
    .GetComponentInParent<Button>();
string[] Metrics(Button item) => item.transform.parent.GetComponentsInChildren<Button>()
    .Where(metric => metric.name == "RankingMetric").Select(metric => metric.GetComponentInChildren<Text>().text).ToArray();
Check(HasColumn("环阶") && Metrics(FirstRow()).First() == topRing.ToString(), "The spell board ranks live magic web entries by ring.");
FirstRow().onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.GetComponent<SkillTooltip>() != null) != null, "Clicking a spell opens its skill tooltip.");
Tooltip.hideTooltipNow();

string artifactName = "排行榜验收飞剑 " + suffix;
var design = ArtifactComposer.ComposeDesign(new ArtifactDesignRequest
{
    Shape = ItemShapes.Sword, Atoms = new[] { ArtifactAtoms.SwordEdge, ArtifactAtoms.Iron },
    Level = new ItemLevel { Stage = 1, Level = 3 }, Name = artifactName
});
var artifact = ArtifactBlueprintCodec.Materialize(ArtifactBlueprintCodec.FromComposeResult(design), actor.getName());
actor.GetExtend().AddSpecialItem(artifact);
Button(boardNames[3]).onClick.Invoke();
var artifactRow = Button(artifactName);
Check(HasColumn("品阶") && Metrics(artifactRow).Contains(actor.getName()), "The artifact board shows the live artifact and its holder.");
artifactRow.onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.name != null && tip.name.text.Contains(artifactName)) != null,
    "Clicking the artifact opens its special item tooltip.");
Tooltip.hideTooltipNow();

string materialName = "排行榜验收灵材 " + suffix;
var material = SpecialItemUtils.StartBuild(ItemShapes.Herb, World.world.getCurWorldTime(), actor.getName())
    .AddComponent(new ItemLevel { Stage = 1, Level = 2 }).AddTag<TagIngredient>()
    .AddComponent(new Friflo.Engine.ECS.EntityName(materialName)).Build();
actor.GetExtend().AddSpecialItem(material);
Button(boardNames[4]).onClick.Invoke();
var materialRow = Button(materialName);
Check(HasColumn("材料种类") && Metrics(materialRow).Contains(actor.getName()),
    "The material board shows a real ingredient and its holder.");
var stages = new[] { "天", "地", "玄", "黄" };
var distribution = window.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingDistribution");
Check(distribution.GetComponentsInChildren<Text>().Select(item => item.text).Where(stages.Contains).SequenceEqual(stages),
    "Cultiway groups its finer material grades into the four ordinal display bands.");
materialRow.onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.name != null && tip.name.text.Contains(materialName)) != null,
    "The material opens its original item tooltip.");
Tooltip.hideTooltipNow();

string talismanName = "排行榜验收符箓 " + suffix;
var spell = web.Query(new MagicWebQuery { MaxResults = 1 }).Single().Container;
var talisman = SpecialItemUtils.StartBuild(ItemShapes.Talisman, World.world.getCurWorldTime(), actor.getName())
    .AddComponent(new ItemLevel { Stage = 2, Level = 1 }).AddTag<TagIngredient>()
    .AddComponent(new Talisman { Strength = 17, PowerLevel = 6, SkillContainer = spell })
    .AddComponent(new Friflo.Engine.ECS.EntityName(talismanName)).Build();
actor.GetExtend().AddSpecialItem(talisman);
Button(boardNames[4]).onClick.Invoke();
Check(entries.GetComponentsInChildren<Button>().All(item => item.name != talismanName),
    "Talismans stay out of the material board even with the ingredient tag.");
Button(boardNames[5]).onClick.Invoke();
var talismanRow = Button(talismanName);
Check(HasColumn("封存法术") && Metrics(talismanRow).Contains("17"), "The talisman shows its spell and real stored strength.");
talismanRow.onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.name != null && tip.name.text.Contains(talismanName)) != null,
    "The talisman opens its original item tooltip.");
Tooltip.hideTooltipNow();

Button(boardNames[2]).onClick.Invoke();
var regionRow = FirstRow();
var banner = regionRow.GetComponentInChildren<GeoRegionBanner>();
Check(banner != null, "The region board draws Cultiway's region banner.");
var region = (GeoRegion)banner.GetNanoObject();
Check(HasColumn("地块数") && Metrics(regionRow).Contains(regions.GetTileCount(region).ToString()),
    "The region board shows the real area of the ranked region.");
regionRow.onClick.Invoke();
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
Check(ScrollWindow.isCurrentWindow(WorldboxGame.MetaTypes.GeoRegion.window_name) && WorldboxGame.I.SelectedGeoRegion == region,
    "Clicking a region opens that region's detail window.");
return new { book = book.id, learners = 1, spellCount = web.Count, topRing, regionCount = regions.Count, artifact = artifactName,
    material = materialName, talisman = talismanName, image };
