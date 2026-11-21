using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GodTools.Features;
using GodTools.Features.Rankings;
using GodTools.UI;
using NeoModLoader.api;
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
while (World.world == null || SmoothLoader.isLoading() || World.world.transition_screen?.isActiveAndEnabled == true)
    await Game.NextFrame();
Config.paused = true;
string suffix = Guid.NewGuid().ToString("N")[..6];
const string groupName = "北境试炼";
string fighterName = groupName + "·霜刃", scholarName = groupName + "·星语", excludedName = groupName + "·陪练";
string weaponName = "榜单验收之刃" + suffix, cultureName = "榜单验收文化" + suffix;
string spellA = "注册验收火花" + suffix, spellB = "注册验收护盾" + suffix, spellC = "注册验收闪光" + suffix;
string spellSource = "ranktest:spells-" + suffix, spellBoard = "测试模组 · 法术榜 " + suffix;
string profile = "ranktest:combat-" + suffix, factor = "ranktest:training-" + suffix, powerName = "测试战力 " + suffix;
string presetName = "北境试炼候选";
string Version(int version) => $"{powerName} · v{version}";
var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
Actor Unit(string name, int kills, int level)
{
    var actor = World.world.units.createNewUnit("human", tile);
    actor.data.name = name;
    actor.data.kills = kills;
    actor.data.level = level;
    return actor;
}
var fighter = Unit(fighterName, 120, 1);
var scholar = Unit(scholarName, 1, 25);
var excluded = Unit(excludedName, 160, 25);
var culture = World.world.cultures.newCulture(fighter, false);
culture.data.name = cultureName;
fighter.setCulture(culture);
scholar.setCulture(culture);
culture.listUnit(fighter);
culture.listUnit(scholar);
World.world.units.checkContainer();

var weapon = World.world.items.newItem(AssetManager.items.list.First(item =>
    item.equipment_type == EquipmentType.Weapon && !item.id.StartsWith("_")));
weapon.data.name = weaponName;
weapon.data.kills = 95000;
fighter.equipment.setItem(weapon, fighter);
fighter.stats[strings.S.health] = 1e8f;
ConsistentCreaturePowerTop.Check(fighter);
long rankedActorID = fighter.data.id;
long weaponID = weapon.id;

var scores = new Dictionary<long, double> { [fighter.data.id] = 42d, [scholar.data.id] = 41d };
var fieldOwner = new ModDeclare("验收扩展", "Rankings", "", "1.0", "", Application.persistentDataPath,
    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false);
Check(RankingPowerProfiles.RegisterFactor(factor, "测试修炼因子",
    actor => scores.TryGetValue(actor.data.id, out var value) ? value : null, fieldOwner),
    "A module can register a power factor.");
bool RegisterPower(int version) => RankingPowerProfiles.Register(new RankingPowerProfile
{
    ID = profile, Version = version, Name = powerName, RecordPeaks = true, Owner = fieldOwner,
    Calculate = actor => RankingPowerProfiles.ReadFactor(factor, actor) is double value
        ? new RankingPowerScore { Total = value * version, Parts = new Dictionary<string, double> { ["修炼因子"] = value } }
        : null
});
Check(RegisterPower(1) && RegisterPower(2) && !RegisterPower(1), "Power profiles have independent versions.");
Check(RankingRegistry.RegisterField("godtools:actors", new RankingField<Actor>
{
    ID = "plain-" + suffix, Name = "测试注册来源 " + suffix, Kind = RankingFieldKind.Number,
    Read = actor => RankingValue.Numeric(actor.data.level)
}), "A field without a module declaration still registers.");

var spells = new[] { (ID: "spark", Name: spellA, Uses: 7), (ID: "ward", Name: spellB, Uses: 10),
    (ID: "flash", Name: spellC, Uses: 10) };
string inspected = null;
int reads = 0;
Check(RankingRegistry.RegisterSource(new RankingSource<(string ID, string Name, int Uses)>
{
    ID = spellSource, Read = () => spells, Key = row => row.ID, Name = row => row.Name, Inspect = key => inspected = key
}), "A foreign assembly can register a source with its own row type.");
Check(RankingRegistry.RegisterField(spellSource, new RankingField<(string ID, string Name, int Uses)>
{
    ID = "uses", Name = "施放次数", Kind = RankingFieldKind.Number, Read = row =>
    {
        reads++;
        return RankingValue.Numeric(row.Uses);
    }
}), "A foreign source can provide its own metric.");
Check(RankingRegistry.RegisterBoard(new RankingBoard
{
    ID = ("ranktest:popular-" + suffix), SourceID = spellSource, Name = spellBoard, Category = "测试扩展分组",
    Columns = new[] { "uses" }, DefaultSort = "uses"
}), "A foreign module can add a complete board.");

async System.Threading.Tasks.Task Open()
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
        ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
        for (int i = 0; i < 8; i++) await Game.NextFrame();
        if (ScrollWindow.isCurrentWindow("WindowRankings")) return;
    }
    throw new InvalidOperationException("The rankings window did not open.");
}
await Open();
var ui = WindowRankings.Instance;
Button[] Buttons() => ui.GetComponentsInChildren<Button>();
Text[] Texts() => ui.GetComponentsInChildren<Text>();
Button Find(string name)
{
    var button = Buttons().FirstOrDefault(item => item.name == name);
    Check(button != null, $"Missing {name}. Buttons: " + string.Join(" / ", Buttons().Select(item => item.name).Take(35)));
    return button;
}
void Click(string name) => Find(name).onClick.Invoke();
RectTransform Toolbar() => ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingToolbar");
RectTransform Columns() => ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingColumns");
string[] ColumnNames() => Columns().GetComponentsInChildren<Text>().Select(item => item.text).ToArray();
RectTransform RightTabs() => ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingRightTabs");
void RightTab(string name) => RightTabs().GetComponentsInChildren<Button>().Single(item => item.name == name).onClick.Invoke();
void FilterAction(string name) => ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingFilterActions")
    .GetComponentsInChildren<Button>().Single(item => item.name == name).onClick.Invoke();
void FilterSymbol(string name) => RightSidebar().GetComponentsInChildren<RectTransform>()
    .Single(item => item.name == "RankingFilterOperators").GetComponentsInChildren<Button>()
    .Single(item => item.name == name).onClick.Invoke();
void FilterField(string name) => RightSidebar().GetComponentsInChildren<Button>().Single(item => item.name == name).onClick.Invoke();
string FilterReadout() => Texts().Single(item => item.name == "RankingFilterReadout").text;
ScrollRect Navigation() => ui.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingNavigation");
RectTransform NavigationHeader() => ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingNavigationHeader");
Button[] BoardButtons() => NavigationHeader().GetComponentsInChildren<Button>()
    .Concat(Navigation().GetComponentsInChildren<Button>()).Where(item => item.name != "RankingNavigationToggle").ToArray();
void Board(string next)
{
    var button = BoardButtons().SingleOrDefault(item => item.name == next);
    Check(button != null, $"Missing sidebar board {next}");
    button.onClick.Invoke();
}
ScrollRect RightSidebar() => ui.GetComponentsInChildren<ScrollRect>(true).Single(item => item.name == "RankingRightSidebar");
RectTransform Distribution() => ui.GetComponentsInChildren<RectTransform>(true)
    .Single(item => item.name == "RankingDistribution");
bool ChartHas(string text) => Distribution().GetComponentsInChildren<Text>().Any(item => item.text == text);
void SortKey(string key)
{
    var button = RightSidebar().GetComponentsInChildren<Button>().SingleOrDefault(item => item.name == key);
    Check(button?.interactable == true, $"Unavailable sort key {key}.");
    button.onClick.Invoke();
}
void SortBy(string key)
{
    if (!RightSidebar().GetComponentsInChildren<Text>().Any(item => item.text == "排序关键字")) RightTab("排序");
    while (SortRows().FirstOrDefault() is RectTransform row)
        row.GetComponentsInChildren<Button>().Single(button => button.name == "×").onClick.Invoke();
    SortKey(key);
}
RectTransform[] SortRows() => RightSidebar().GetComponentsInChildren<RectTransform>()
    .Where(item => item.name.StartsWith("RankingSortKey.", StringComparison.Ordinal)).ToArray();
Button SortControl(string field, Func<Button, bool> matches) => SortRows().Single(item => item.name == "RankingSortKey." + field)
    .GetComponentsInChildren<Button>().Single(matches);
Button SortDirection(string field) => SortControl(field, item => item.name is "升" or "降");
string Rank(string name) => Find(name).GetComponentsInChildren<Text>().Single(text => text.name == "RankingEntryRank").text.TrimStart('#');
void Number(string field, string value)
{
    FilterField(field);
    RightSidebar().GetComponentsInChildren<InputField>().Single().text = value;
    Click("添加条件");
}

Board("榜单总览");
Check(ui.GetComponentsInChildren<Text>().Any(text => text.text == "榜单总览"), "The hub offers a board overview.");
Check(!Distribution().gameObject.activeSelf, "The overview does not open a chart.");
var navigation = BoardButtons().Select(button => button.name).ToArray();
Check(new[] { "人物榜", "装备榜", "王国榜", "城市榜", "联盟榜", "军队榜", "文化榜", "氏族榜", "家庭榜", "语言榜",
        "亚种榜", "宗教榜", "战争榜", "谋划榜" }.All(name => navigation.Count(button => button == name) == 1) &&
    new[] { "人物击杀榜", "人物年龄榜", "装备击杀榜", "文化人口榜", Version(1) }.All(name => !navigation.Contains(name)),
    "Live boards have a single entry per built-in source. Formula versions are sort keys.");
Board("人物榜");
RightTab("排序");
Check(RightSidebar().isActiveAndEnabled, "The table opens with its sort sidebar.");
var toolbarButtons = Toolbar().GetComponentsInChildren<Button>();
Check(toolbarButtons.All(item => item.name is not ("排序" or "筛选")) &&
    RightTabs().GetComponentsInChildren<Button>().Select(item => item.name)
        .SequenceEqual(new[] { "排序", "筛选", "行内容", "预设", "RankingSidebarToggle" }) &&
    RightSidebar().GetComponentsInChildren<Button>().Any(item => item.name == "恢复默认排序"),
    "The right pane puts pinned columns between filtering and presets.");
var actions = Toolbar().GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingToolbarActions");
Check(actions.GetComponentsInChildren<Button>().Select(item => item.name).SequenceEqual(new[] { "分布", "刷新", "保存本次排名" }) &&
    actions.GetComponentsInChildren<Button>().All(item => ((RectTransform)item.transform).rect.width == 22 &&
        item.GetComponentsInChildren<Text>().All(caption => string.IsNullOrEmpty(caption.text)) &&
        item.GetComponentsInChildren<Image>().Any(icon => icon.name == "Icon" && icon.sprite != null) &&
        item.GetComponent<TipButton>() != null),
    "The distribution, refresh and archive controls use equal icon buttons with hover hints.");
Check(SortDirection("power:godtools:standard-duel@2").GetComponentsInChildren<Image>().Single(item => item.name == "RankingSortCorner").sprite ==
    SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"), "The sort direction has a single native corner badge.");
Check(Distribution().gameObject.activeSelf &&
    ui.GetComponentsInChildren<ScrollRect>(true).Where(pane => pane.vertical).All(pane =>
        pane.verticalScrollbar.colors.normalColor == (Color)new Color32(231, 83, 64, 255)),
    "The chart opens with the table and each scrollbar has the original red tint.");
var sortGrid = RightSidebar().GetComponentsInChildren<RectTransform>().Where(item => item.name == "RankingFieldIcons").ToArray();
var sortIcons = sortGrid.SelectMany(row => row.GetComponentsInChildren<Button>()).ToArray();
Check(sortGrid[0].childCount == 7 &&
    sortIcons.All(button => button.GetComponentsInChildren<Text>().Length == 0 &&
        button.GetComponent<Image>().color.a == 0 && button.image.sprite != null) &&
    sortIcons.All(button => button.name != "特质") &&
    !sortIcons.Single(button => button.name == "战力").interactable,
    "Sorting uses the shared icon grid and disables the current primary key.");
Check(RightSidebar().GetComponentsInChildren<Text>().Where(item => item.name == "RankingFieldGroup")
    .Select(item => item.text).Take(2).SequenceEqual(new[] { "原版", "验收扩展" }),
    "Sorting shows the localized built-in group and the registering mod's group.");
Check(Buttons().All(item => item.name is not ("设为主排序" or "添加次排序")), "The sort list has no mode buttons.");
SortKey("年龄");
Check(SortRows().Select(item => item.name).SequenceEqual(new[] { "RankingSortKey.power:godtools:standard-duel@2", "RankingSortKey.age" }) &&
    ChartHas("战力") && !Find("年龄").interactable, "Choosing a keyword appends it once and keeps the first key primary.");
Click("年龄");
Check(SortRows().Length == 2, "A duplicate keyword cannot be appended.");
SortRows()[1].GetComponentsInChildren<Button>().Single(button => button.name == "↑").onClick.Invoke();
Check(SortRows()[0].name == "RankingSortKey.age" && ChartHas("年龄"), "Moving a keyword to the top makes it primary.");
SortDirection("age").onClick.Invoke();
Check(SortDirection("age").name == "升" && ColumnNames()[1] == "年龄",
    "The right sidebar can reverse a primary metric without adding any arrows to the column header.");
Click("恢复默认排序");
Check(SortRows().Select(item => item.name).SequenceEqual(new[] { "RankingSortKey.power:godtools:standard-duel@2" }) &&
    ChartHas("战力") && RightSidebar().isActiveAndEnabled, "Reset restores the default sort without collapsing the right sidebar.");
SortBy("击杀");
Check(Columns().GetComponentsInChildren<Button>().Length == 0 &&
    Columns().GetComponentsInChildren<Text>().All(text => !text.raycastTarget ||
        text.text == "战力" && text.GetComponent<TipButton>() != null) &&
    ColumnNames()[1] == "击杀" && ColumnNames().Contains("战力"), "Column headings are plain, noninteractive labels.");
string originalChartSamples = Distribution().GetComponentsInChildren<Text>().Single(item => item.text.StartsWith("样本 ")).text;
RightTab("筛选");
Check(RightSidebar().isActiveAndEnabled && ColumnNames()[1] == "击杀" && ChartHas(originalChartSamples),
    "Opening filters keeps the table and its chart on screen.");
for (int frame = 0; frame < 2; frame++) await Game.NextFrame();
Canvas.ForceUpdateCanvases();
var fieldRows = RightSidebar().GetComponentsInChildren<RectTransform>().Where(item => item.name == "RankingFieldIcons").ToArray();
var fieldButtons = fieldRows.SelectMany(row => row.GetComponentsInChildren<Button>()).ToArray();
Check(fieldRows[0].childCount == 7 && fieldButtons.All(button => button.GetComponentsInChildren<Text>().Length == 0 &&
    button.GetComponent<Image>().color.a == 0 && button.image.sprite != null && button.GetComponent<TipButton>() != null),
    "The field grid displays seven icons per row with transparent click areas and hover hints.");
var fieldGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
List<string> currentGroup = null;
foreach (Transform row in RightSidebar().content)
{
    if (!row.gameObject.activeSelf) continue;
    var heading = row.GetComponentsInChildren<Text>().FirstOrDefault(item => item.name == "RankingFieldGroup");
    if (heading != null) fieldGroups[heading.text] = currentGroup = new List<string>();
    if (row.name == "RankingFieldIcons") currentGroup.AddRange(row.GetComponentsInChildren<Button>().Select(item => item.name));
}
Check(fieldGroups["原版"].Contains("击杀") && fieldGroups["原版"].Contains("特质") &&
    fieldGroups["验收扩展"].SequenceEqual(new[] { "测试修炼因子", Version(1), Version(2) }) &&
    fieldGroups[System.Reflection.Assembly.GetExecutingAssembly().GetName().Name].Contains("测试注册来源 " + suffix),
    "Fields on the same ranking source are grouped by explicit ownership or the registering assembly.");
var killsIcon = fieldButtons.Single(button => button.name == "击杀");
var raycaster = ui.GetComponentInParent<GraphicRaycaster>();
var eventData = new PointerEventData(EventSystem.current)
{
    position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, ((RectTransform)killsIcon.transform)
        .TransformPoint(((RectTransform)killsIcon.transform).rect.center))
};
var hits = new List<RaycastResult>();
raycaster.Raycast(eventData, hits);
Check(hits.Any(hit => hit.gameObject == killsIcon.gameObject), "The transparent icon slot receives pointer input.");
FilterAction("清空条件");
FilterSymbol("（");
Click("RankingSidebarToggle");
Check(!RightSidebar().isActiveAndEnabled &&
    Navigation().GetComponentsInChildren<Text>().Any(text => text.text == "人物与装备"),
    "The right arrow also collapses the filter pane without collapsing left navigation.");
RightTab("筛选");
Check(FilterReadout() == "（", "Opening the filter icon restores its draft after folding.");
RightTab("排序");
RightTab("筛选");
Check(FilterReadout() == "（", "A draft persists when switching tabs.");
Board("装备榜");
Check(Texts().Any(item => item.text == "当前没有筛选条件"), "A different board opens its own draft.");
Board("人物榜");
Check(FilterReadout() == "（", "Switching back restores the actor draft.");
Number("击杀", "100");
FilterSymbol("或");
Number("等级", "20");
FilterSymbol("）");
FilterSymbol("与");
FilterSymbol("非");
FilterField("名称");
Check(RightSidebar().GetComponentsInChildren<InputField>().Length == 1 && ColumnNames()[1] == "击杀",
    "The value form opens within the sidebar and leaves the table accessible.");
RightSidebar().GetComponentsInChildren<InputField>().Single().text = "陪练";
Click("添加条件");
FilterSymbol("与");
FilterField("名称");
RightSidebar().GetComponentsInChildren<InputField>().Single().text = groupName;
Click("添加条件");
var line = ui.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingConditionViewport");
Check(line.horizontal && !line.vertical && line.content.anchoredPosition.x < 0f,
    "A long condition line scrolls horizontally to its insertion caret.");
Click("RankingFilterToken.1");
Check(FilterReadout() == "击杀 至少 100", "Selecting a condition reveals the full value below the line.");
Click("RankingFilterToken.9");
Click("预览命中");
Check(FilterReadout() == "预览 · 共 2 项" &&
    ChartHas(originalChartSamples), "Preview reports the count without applying the draft to the chart.");
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
string filterShot = Screen.Capture("rankings-filter-sidebar.png");
await Game.NextFrame();
FilterSymbol("与");
Click("应用筛选");
Check(Texts().Any(text => text.text.Contains("条件未完成")), "An incomplete expression is rejected.");
FilterAction("退格");
Click("应用筛选");
Check(ChartHas("击杀") && ChartHas("样本 2"), "The chart counts the filtered rows.");
Canvas.ForceUpdateCanvases();
var boardTitle = Toolbar().GetComponentsInChildren<Text>().Single(item => item.text == "人物榜");
var count = Toolbar().GetComponentsInChildren<Text>().Single(item => item.name == "RankingResultCount");
Check(count.text == "共 2 项" && Mathf.Approximately(count.transform.position.y, boardTitle.transform.position.y) &&
    count.transform.position.x > boardTitle.transform.position.x, "The result count appears beside the board name.");
SortBy("名称");
Check(ChartHas(fighterName) && ChartHas(scholarName) && !ChartHas(excludedName),
    "Text keys use a category frequency chart with the same filter.");
Check(ColumnNames().Count(name => name == "名称") == 1,
    "Sorting by name uses the existing identity column.");
SortBy("击杀");
var avatar = Find(fighterName).GetComponentInChildren<UnitAvatarLoader>();
Check(avatar != null && avatar._actor_image?.sprite != null, "Live rankings show the native rendered actor.");
Check(Buttons().Any(item => item.name == fighterName) && Buttons().Any(item => item.name == scholarName) &&
      Buttons().All(item => item.name != excludedName), "An OR group and a negated name filter are both applied.");
SortKey("等级");
var secondary = Texts().Single(text => text.text == "2   等级");
secondary.transform.parent.GetComponentsInChildren<Button>().Single(button => button.name == "↑").onClick.Invoke();
Check(RightSidebar().isActiveAndEnabled && Rank(scholarName) == "1" && Rank(fighterName) == "2",
    "Moving the secondary metric up updates the visible table and keeps the filter.");
SortDirection("level").onClick.Invoke();
Check(Rank(fighterName) == "1", "The new primary order can be flipped without changing the secondary key.");
SortControl("kills", button => button.name == "↑").onClick.Invoke();
Check(ColumnNames().Skip(1).SequenceEqual(new[] { "击杀", "等级", "战力" }),
    "The three table headings follow the configured sort fields without controls.");
for (int i = 0; i < 4; i++) await Game.NextFrame();
Check(RightSidebar().transform.position.x > Find(fighterName).transform.position.x,
    "The sort sidebar is to the right of the visible entries.");
var chartCorners = new Vector3[4];
var backgroundCorners = new Vector3[4];
Distribution().GetWorldCorners(chartCorners);
((RectTransform)Distribution().parent).GetWorldCorners(backgroundCorners);
Check(chartCorners[0].x > backgroundCorners[3].x, "The chart sits outside the right edge of the main window.");
Tooltip.hideTooltipNow();
string sortShot = Screen.Capture("rankings-sort.png");
await Game.NextFrame();
var tablePane = ui.GetComponent<ScrollWindow>().scrollRect;
float expandedTableWidth = tablePane.viewport.rect.width;
RightTab("排序");
Check(RightSidebar().isActiveAndEnabled, "Clicking the current tab keeps the right sidebar open.");
Click("RankingSidebarToggle");
var compactTabs = RightTabs().GetComponentsInChildren<Button>();
Check(!RightSidebar().isActiveAndEnabled && Rank(fighterName) == "1" &&
    compactTabs.Select(item => item.name).SequenceEqual(new[] { "排序", "筛选", "行内容", "预设" }) &&
    compactTabs.All(item => item.GetComponentsInChildren<Text>().Length == 0 &&
        item.GetComponent<TipButton>() != null &&
        item.GetComponentsInChildren<Image>().Any(image => image.name == "Icon" && image.sprite != null)) &&
    tablePane.viewport.rect.width > expandedTableWidth,
    "The right arrow collapses the sidebar to four icons and frees room for the table.");
float rightFoldedWidth = tablePane.viewport.rect.width;
Click("RankingNavigationToggle");
Check(!RightSidebar().isActiveAndEnabled && tablePane.viewport.rect.width > rightFoldedWidth &&
    Navigation().GetComponentsInChildren<Text>().All(text => string.IsNullOrEmpty(text.text)) &&
    Navigation().GetComponentsInChildren<RectTransform>().Count(item => item.name == "RankingGroupDivider") >= 2 &&
    BoardButtons().All(button => button.GetComponentsInChildren<Image>().Any(image => image.name == "Icon" && image.sprite != null)),
    "The left pane collapses independently to icons with divider lines and supplies fallback icons to extensions.");
var leftRail = ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingNavigationBackground");
var rightRail = ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingRightBackground");
var selectedBoard = BoardButtons().Single(item => item.name == "人物榜");
float navigationGutter = Navigation().verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHide &&
    Navigation().content.rect.height > Navigation().viewport.rect.height ? 9 : 0;
Check(leftRail.rect.width == rightRail.rect.width + navigationGutter &&
    Mathf.Approximately(leftRail.anchoredPosition.y + leftRail.rect.height * 0.5f, rightRail.anchoredPosition.y + rightRail.rect.height * 0.5f) &&
    leftRail.GetComponent<Image>().sprite == rightRail.GetComponent<Image>().sprite &&
    selectedBoard.colors.normalColor.a > 0 &&
    selectedBoard.GetComponentsInChildren<Image>().Length == 2,
    "Collapsed rails use the same frame, with an extra scrollbar gutter on overflowing navigation.");
Board("装备榜");
Check(Buttons().Any(item => item.name == weaponName) && !RightSidebar().isActiveAndEnabled,
    "The icon-only navigation still opens a board while the right pane stays collapsed.");
Board("人物榜");
for (int i = 0; i < 4; i++) await Game.NextFrame();
string foldedShot = Screen.Capture("rankings-folded-sidebars.png");
await Game.NextFrame();
RightTab("预设");
Check(RightSidebar().isActiveAndEnabled && Navigation().GetComponentsInChildren<Text>().All(text => string.IsNullOrEmpty(text.text)) &&
    Texts().Any(text => text.text == "已保存的预设"), "The preset icon expands its own tab while navigation stays collapsed.");
Click("RankingSidebarToggle");
RightTab("筛选");
Check(RightSidebar().isActiveAndEnabled && Find("应用筛选").interactable && Rank(fighterName) == "1",
    "The filter icon expands its editor beside the same ranked rows.");
Check(Navigation().GetComponentsInChildren<Text>().All(text => string.IsNullOrEmpty(text.text)),
    "Expanding the right pane leaves the navigation folded.");
Click("RankingNavigationToggle");
Check(Navigation().GetComponentsInChildren<Text>().Any(text => text.text == "人物与装备"),
    "Reopening navigation restores its named groups.");
RightTab("排序");
Check(RightSidebar().isActiveAndEnabled && SortDirection("kills").name == "降",
    "Reopening the sidebar restores the same live table and sort state.");
RightTab("预设");
InputField PresetName() => ui.GetComponentsInChildren<InputField>().Single(item => item.name == "RankingPresetName");
RectTransform PresetCard(string title) => RightSidebar().GetComponentsInChildren<RectTransform>()
    .Single(card => card.name == "RankingPreset" && card.GetComponentsInChildren<Button>().Any(item => item.name == title));
Tooltip PresetTip(string title)
{
    Tooltip.hideTooltipNow();
    Find(title).GetComponent<TipButton>().hoverAction.Invoke();
    var tip = Tooltip.findActive(item => item.transform.name == "inmny.godtools.ranking.preset_tooltip");
    Check(tip != null && tip.name.text == title, $"The hovered preset shows the native tooltip for {title}.");
    return tip;
}
Image[] PresetSortIcons(Tooltip tip) => tip.GetComponentsInChildren<Image>()
    .Where(image => image.name == "RankingPresetSortIcon").ToArray();
bool ShowsTrialSort(Tooltip tip)
{
    var icons = PresetSortIcons(tip);
    return icons.Select(image => image.sprite).SequenceEqual(new[]
        {
            SpriteTextureLoader.getSprite("ui/icons/iconSkulls"), SpriteTextureLoader.getSprite("ui/icons/iconLevels")
        }) &&
        icons.Select(image => image.transform.Find("RankingSortCorner").GetComponent<Image>().sprite).SequenceEqual(new[]
        {
            SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"), SpriteTextureLoader.getSprite("ui/icons/iconArrowDOWN")
        });
}

PresetName().text = presetName;
Click("保存预设");
Check(Rank(fighterName) == "1" && Distribution().gameObject.activeSelf &&
    PresetCard(presetName).rect.height == 22 &&
    PresetCard(presetName).GetComponentsInChildren<Text>().All(text => string.IsNullOrEmpty(text.text) || text.text == presetName) &&
    PresetCard(presetName).GetComponentsInChildren<Button>().Select(item => item.name)
        .SequenceEqual(new[] { presetName, "RankingPresetFavorite", "RankingPresetRename", "RankingPresetDelete" }),
    "Each saved preset keeps the table visible and uses one line for its name and two icon actions.");
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
Tooltip.hideTooltipNow();
string presetShot = Screen.Capture("rankings-presets.png");
await Game.NextFrame();
var presetTip = PresetTip(presetName);
float trialTipWidth = ((RectTransform)presetTip.transform).rect.width;
Check(presetTip.description.fontSize == 10 && presetTip.description_2.fontSize == 9 && trialTipWidth < 188 &&
    ShowsTrialSort(presetTip) &&
    presetTip.description_2.text.Contains("（\n　击杀 至少 100\n　或者 等级 至少 20\n）") &&
    presetTip.description_2.text.Contains("\n并且 名称不包含「陪练」\n并且 名称包含「北境试炼」"),
    "The tooltip uses ordered native icons with directions, indents the whole group and phrases negated text directly.");
string presetTooltipShot = Screen.Capture("rankings-preset-tooltip.png");
await Game.NextFrame();
Tooltip.hideTooltipNow();
SortBy("年龄");
RightTab("筛选");
FilterAction("清空条件");
Click("应用筛选");
RightTab("预设");
Check(ColumnNames()[1] == "年龄" && !Texts().Any(text => text.name == "RankingResultCount" && text.text == "共 2 项"),
    "Live sorting and filters can be changed without modifying the saved preset.");
string updateName = "更新测试 " + suffix;
PresetName().text = updateName;
Click("保存预设");
await Game.NextFrame();
var compactTip = PresetTip(updateName);
Check(PresetSortIcons(compactTip).Select(image => image.sprite)
    .SequenceEqual(new[] { SpriteTextureLoader.getSprite("ui/icons/iconAge") }) &&
    ((RectTransform)compactTip.transform).rect.width < trialTipWidth,
    "A shorter preset shrinks the tooltip and hides the previous icons.");
Click(presetName);
Check(Rank(fighterName) == "1" && !Buttons().Any(item => item.name == excludedName) &&
    ColumnNames().Skip(1).SequenceEqual(new[] { "击杀", "等级", "战力" }) &&
    Texts().Single(text => text.name == "RankingResultCount").text == "共 2 项" && PresetName().text == presetName,
    "Loading a preset restores sorting and filtering together and selects its name for updates.");
PresetName().text = updateName;
Click("更新预设");
await Game.NextFrame();
var updatedTip = PresetTip(updateName);
Check(ShowsTrialSort(updatedTip) && updatedTip.description_2.text.Contains("击杀 至少 100"),
    "Updating an existing preset replaces both sorting and filter conditions.");
PresetCard(updateName).GetComponentsInChildren<Button>().Single(item => item.name == "RankingPresetRename").onClick.Invoke();
var rename = RightSidebar().GetComponentsInChildren<InputField>().Single(item => item.name == "RankingPresetRenameInput");
var confirmRename = Find("RankingPresetConfirmRename");
rename.text = presetName;
Check(!confirmRename.interactable, "Renaming refuses a duplicate name on this board.");
string renamedPreset = "重命名验收 " + suffix;
rename.text = "  " + renamedPreset + " ";
Check(confirmRename.interactable, "A new name can be confirmed.");
confirmRename.onClick.Invoke();
Check(PresetName().text == renamedPreset && !Buttons().Any(item => item.name == updateName),
    "Renaming replaces the saved name and updates the selected name.");
await Game.NextFrame();
var renamedTip = PresetTip(renamedPreset);
Check(ShowsTrialSort(renamedTip) && renamedTip.description_2.text.Contains("击杀 至少 100"),
    "Renaming keeps sorting and filter conditions.");
PresetName().text = "删除验收 " + suffix;
string deletedPreset = PresetName().text;
Click("保存预设");
PresetCard(deletedPreset).GetComponentsInChildren<Button>().Single(item => item.name == "RankingPresetDelete").onClick.Invoke();
Click("RankingPresetCancel");
PresetCard(deletedPreset).GetComponentsInChildren<Button>().Single(item => item.name == "RankingPresetDelete").onClick.Invoke();
Click("RankingPresetConfirmDelete");
Check(!Buttons().Any(item => item.name == deletedPreset) && PresetName().text == "" && Rank(fighterName) == "1",
    "Deleting removes the saved preset while keeping the current ranking.");
RightTab("排序");
await Game.NextFrame();
string actorShot = Screen.Capture("rankings-actor.png");
await Game.NextFrame();
await Game.NextFrame();

Board("装备榜");
SortBy("耐久");
Check(SortDirection("durability").name == "降" && ColumnNames()[1] == "耐久", "Equipment can sort by its own metrics.");
Click(weaponName);
Check(SelectedMetas.selected_item?.id == weaponID && ScrollWindow.isCurrentWindow("item"),
    "The equipment row opens its real item instance.");
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
Check(!Distribution().gameObject.activeInHierarchy, "The chart closes through the ranking window lifecycle.");
await Open();
Board("文化榜");
SortBy("年龄");
Check(SortDirection("age").name == "降" && ColumnNames()[1] == "年龄", "Culture can also switch its own sort.");
Click(cultureName);
Check(ScrollWindow.isCurrentWindow("culture"), "A culture opens the original culture window.");
await Open();
Board(spellBoard);
Check(Texts().Any(text => text.text == "测试扩展分组"), "A module can name its group in the sidebar.");
Check(Rank(spellC) == "1" && Rank(spellB) == "1" && Rank(spellA) == "3" && reads == 3,
    "Each field is captured once and equal values share rank.");
Check(ChartHas("施放次数") && ChartHas("样本 3") && ChartHas("7") && ChartHas("10"),
    "The attached chart counts the current primary numeric key.");
Distribution().GetComponentsInChildren<Button>().Single().onClick.Invoke();
Check(!Distribution().gameObject.activeSelf && reads == 3, "The chart can close without refreshing the table.");
Click("分布");
Check(Distribution().gameObject.activeSelf && reads == 3, "Reopening the chart reuses the ranking snapshot.");
Click("RankingSidebarToggle");
RightTab("排序");
Check(reads == 3, "Folding and expanding the table reuses the ranking snapshot.");
Click(spellB);
Check(inspected == "ward", "The external board's own inspect callback is used.");
Board("旧战力峰值记录");
var archived = Find(fighterName);
archived.transform.parent.GetComponentsInChildren<Button>().First(item => item.name == "+").onClick.Invoke();
Check(World.world.selected_power?.id == GodTools.Libraries.GodPowers.place_saved_actor_in_top.id,
    "The old peak board still arms its original placement power.");
await Open();
Board("人物榜");
SortBy(Version(1));
var scoreButton = Find(fighterName).transform.parent.GetComponentsInChildren<Button>()
    .Single(item => item.GetComponentsInChildren<Text>().Any(text => text.text == "42"));
Tooltip.hideTooltipNow();
await Game.NextFrame();
scoreButton.GetComponent<TipButton>().hoverAction.Invoke();
var scoreTip = Tooltip.findActive(tip => tip.transform.name == "inmny.godtools.ranking.metric_tooltip");
Check(scoreTip?.description.text.Contains("修炼因子") == true && scoreTip.description.text.Contains("42"),
    "Hovering the score shows its captured breakdown without opening a page.");
Tooltip.hideTooltipNow();

RightTab("预设");
Click(presetName);
RightTab("行内容");
Button PinField(string name) => RightSidebar().GetComponentsInChildren<RectTransform>()
    .Where(row => row.name == "RankingFieldIcons").SelectMany(row => row.GetComponentsInChildren<Button>())
    .Single(button => button.name == name);
Button PinControl(string field, string control) => RightSidebar().GetComponentsInChildren<RectTransform>()
    .Single(row => row.name == "RankingPinnedColumn." + field).GetComponentsInChildren<Button>()
    .Single(button => button.name == control);
PinField("王国").onClick.Invoke();
PinField("年龄").onClick.Invoke();
PinControl("age", "↑").onClick.Invoke();
PinField("击杀").onClick.Invoke();
Check(ColumnNames().SequenceEqual(new[] { "名称", "击杀", "等级", "年龄" }) &&
    !PinField("等级").interactable && Rank(fighterName) == "1" && ChartHas("击杀"),
    "Narrow tables keep the primary and secondary sort keys before the highest-priority pin.");
var ageCell = Find(fighterName).transform.parent.GetComponentsInChildren<Button>()
    .Where(button => button.name == "RankingMetric").ElementAt(2).GetComponentInChildren<Text>();
Check(ageCell.text == fighter.data.getAge().ToString(), "A pinned field is read even when it is neither a sort key nor a default column.");
Click("RankingSidebarToggle");
Check(ColumnNames().Take(5).SequenceEqual(new[] { "名称", "击杀", "等级", "年龄", "王国" }) &&
    ColumnNames().Count(column => column == "击杀") == 1,
    "Folding supplies room for more pins and shows a pinned sort key only once.");
RightTab("行内容");
PinControl("kills", "×").onClick.Invoke();
Check(PinField("等级").interactable && ColumnNames().SequenceEqual(new[] { "名称", "击杀", "等级", "年龄" }),
    "Unpinning a sort key reopens a pin slot while keeping the sort key visible.");
Board("装备榜");
Check(Texts().Any(text => text.text == "已固定 0/3") && !ColumnNames().Contains("王国"),
    "Fixed columns are kept per board.");
Board("人物榜");
string pinnedPresetName = "人物固定列 " + suffix;
RightTab("预设");
PresetName().text = pinnedPresetName;
Click("保存预设");
RightTab("行内容");
Click("清空");
Check(ColumnNames()[1] == "击杀", "Clearing restores automatic columns.");
RightTab("预设");
Click(pinnedPresetName);
Check(ColumnNames().SequenceEqual(new[] { "名称", "击杀", "等级", "年龄" }) &&
    !Buttons().Any(item => item.name == excludedName),
    "Applying a preset restores sort priority, pin order and filtering.");
RightTab("行内容");
PinControl("kingdom", "↑").onClick.Invoke();
RightTab("预设");
Click("更新预设");
await Game.NextFrame();
Check(PresetTip(pinnedPresetName).description_2.text.Contains("固定列\n王国 · 年龄"),
    "The preset tooltip shows the updated column order.");
Tooltip.hideTooltipNow();
Click(presetName);
Check(ColumnNames()[1] == "击杀", "A preset with no pinned columns restores the automatic layout.");
SortBy(Version(2));

string folder = Path.Combine(Application.persistentDataPath, "rankings-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
SaveManager.saveMapData(folder);
string sidecar = Path.Combine(folder, "godtools_rankings.json");
var data = JObject.Parse(File.ReadAllText(sidecar));
var presets = (JArray)data["Presets"];
Check(presets.Any(item => item.Value<string>("Name") == renamedPreset) &&
    !presets.Any(item => item.Value<string>("Name") == deletedPreset || item.Value<string>("Name") == updateName),
    "The world save records renamed presets and omits deleted ones.");
var savedPreset = (JObject)presets.Single(item => item.Value<string>("Name") == presetName);
Check(presets.Single(item => item.Value<string>("Name") == pinnedPresetName)["PinnedColumns"].Values<string>()
    .SequenceEqual(new[] { "kingdom", "age" }), "The updated fixed columns are written in display order.");
Check(savedPreset.Value<string>("BoardID") == "godtools:actors" && savedPreset["Sort"].Count() == 2,
    "The preset keeps all sort keys and is written under the unified board with the world.");
var versions = data["Peaks"].Where(item => item.Value<string>("ProfileID") == profile).OrderBy(item => item.Value<int>("Version")).ToArray();
Check(versions.Length == 2 && versions[0]["Entries"].First().Value<double>("Score") == 42d &&
    versions[1]["Entries"].First().Value<double>("Score") == 84d, "Peaks from two formula versions are stored independently.");
savedPreset["BoardID"] = "godtools:actor-kills";
savedPreset.Remove("PinnedColumns");
var oldAge = (JObject)savedPreset.DeepClone();
oldAge["BoardID"] = "godtools:actor-age";
oldAge["Sort"] = new JArray(new JObject { ["Field"] = "age", ["Ascending"] = true });
presets.Add(oldAge);
var oldPower = (JObject)savedPreset.DeepClone();
oldPower["Name"] = Version(1);
oldPower["BoardID"] = $"godtools:power:{profile}@1";
oldPower["Sort"] = new JArray(new JObject { ["Field"] = $"power:{profile}@1" });
presets.Add(oldPower);
File.WriteAllText(sidecar, data.ToString());
var oldMap = World.world.map_stats;
SaveManager.setCurrentPathAndId(folder, -1);
World.world.save_manager.loadWorld(folder);
for (int frame = 0; frame < 900 && (SmoothLoader.isLoading() || oldMap == World.world.map_stats); frame++) await Game.NextFrame();
Check(!SmoothLoader.isLoading() && oldMap != World.world.map_stats, "The test world reloaded.");
await Open();
Board((Version(1) + " · 采样峰值"));
Check(Buttons().Any(item => item.name == fighterName), "Versioned peaks are readable after reload.");
Board("人物榜");
RightTab("预设");
Click(pinnedPresetName);
Check(ColumnNames().SequenceEqual(new[] { "名称", "击杀", "等级", "王国" }),
    "The highest-priority pin still follows the sort keys after a world reload.");
Click(presetName + " (2)");
RightTab("排序");
Check(ColumnNames()[1] == "年龄" && SortDirection("age").name == "升",
    "A same-named preset from the old age board is preserved with its original order.");
RightTab("预设");
Click(Version(1));
Check(ColumnNames()[1] == Version(1), "An old power profile preset migrates to the unified actor board.");
RightTab("预设");
Click(presetName);
Check(Buttons().Any(item => item.name == fighterName) && Buttons().All(item => item.name != excludedName),
    "The restored preset applies the same expression.");

RightTab("排序");
var metricOrder = new[] { "击杀", "等级", "年龄", "战力", "攻击", "最大生命", "子女数" };
foreach (string key in metricOrder.Skip(2)) SortKey(key);

void CheckColumns(int count)
{
    Canvas.ForceUpdateCanvases();
    var labels = Columns().GetComponentsInChildren<Text>();
    var identity = Find(fighterName);
    var row = identity.transform.parent;
    var cells = row.GetComponentsInChildren<Button>().Where(item => item.name == "RankingMetric").ToArray();
    Check(labels.Select(label => label.text).SequenceEqual(new[] { "名称" }.Concat(metricOrder.Take(count))) &&
        row.GetChild(0) == identity.transform && identity.GetComponentInChildren<UnitAvatarLoader>() != null &&
        Rank(fighterName) == "1" && cells.Length == count,
        $"The identity leads the row followed by {count} visible metrics in priority order.");
    Check(cells.Zip(labels.Skip(1), (cell, label) => Mathf.Abs(CenterX(cell.transform) - CenterX(label.transform)) < 1)
        .All(aligned => aligned) && cells.All(cell => cell.GetComponentInChildren<Text>().text != "—"),
        "Every displayed metric is aligned with its header and populated.");

    float CenterX(Transform item)
    {
        var rect = (RectTransform)item;
        return rect.TransformPoint(rect.rect.center).x;
    }
}

CheckColumns(3);
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
Tooltip.hideTooltipNow();
string narrowColumnsShot = Screen.Capture("rankings-columns-narrow.png");
await Game.NextFrame();
Click("RankingNavigationToggle");
CheckColumns(5);
Click("RankingSidebarToggle");
CheckColumns(7);
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
Tooltip.hideTooltipNow();
string wideColumnsShot = Screen.Capture("rankings-columns-wide.png");
await Game.NextFrame();
Click("RankingNavigationToggle");
CheckColumns(6);
RightTab("排序");
CheckColumns(3);

string longSource = "ranktest:long-" + suffix;
Check(RankingRegistry.RegisterSource(new RankingSource<int>
{
    ID = longSource, Read = () => Enumerable.Range(1, 56), Key = position => position.ToString(),
    Name = position => $"测试条目 {position:00}"
}) && RankingRegistry.RegisterField(longSource, new RankingField<int>
{
    ID = "score", Name = "测试分数", Kind = RankingFieldKind.Number,
    Read = position => RankingValue.Numeric(position == 12 ? 11 : position)
}) && RankingRegistry.RegisterBoard(new RankingBoard
{
    ID = longSource, SourceID = longSource, Name = "跳转验收榜", Category = "测试扩展分组",
    Columns = new[] { "score" }, DefaultSort = "score", Ascending = true
}), "A long board with a tied rank can be registered.");
Click("刷新");
Board("跳转验收榜");
var toolbarNavigation = Toolbar().GetComponentsInChildren<RectTransform>()
    .Single(item => item.name == "RankingToolbarNavigation");
var navigationButtons = toolbarNavigation.GetComponentsInChildren<Button>().Where(item => item.transform.parent == toolbarNavigation).ToArray();
var jumpInput = toolbarNavigation.GetComponentsInChildren<InputField>().Single(item => item.name == "RankingRankJumpInput");
Canvas.ForceUpdateCanvases();
Check(navigationButtons.Select(item => item.name).SequenceEqual(new[] { "至顶", "至底" }) &&
    navigationButtons.All(item => item.interactable &&
        item.GetComponentsInChildren<Text>().All(text => string.IsNullOrEmpty(text.text)) &&
        item.GetComponentsInChildren<Image>().Any(icon => icon.name == "Icon" && icon.sprite != null)) &&
    navigationButtons[0].transform.position.x < jumpInput.transform.position.x &&
    jumpInput.transform.position.x < navigationButtons[1].transform.position.x,
    "The central toolbar places the inline rank field between two icon-only buttons.");

async System.Threading.Tasks.Task CompleteRank(string value)
{
    jumpInput.ActivateInputField();
    for (int frame = 0; frame < 5 && !jumpInput.isFocused; frame++) await Game.NextFrame();
    Check(jumpInput.isFocused, "The inline rank control can be edited.");
    jumpInput.text = value;
    jumpInput.DeactivateInputField();
    await Game.NextFrame();
}

await CompleteRank("57");
Check(Mathf.Approximately(tablePane.content.anchoredPosition.y, 0), "A rank outside the result range does not scroll.");
await CompleteRank("11");
Check(Rank("测试条目 11") == "11" && Mathf.Approximately(tablePane.content.anchoredPosition.y, 390) &&
    string.IsNullOrEmpty(jumpInput.text), "Completing the field jumps to the first entry in a tie and clears the field.");
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
Tooltip.hideTooltipNow();
string jumpShot = Screen.Capture("rankings-jump.png");
await Game.NextFrame();
await CompleteRank("12");
Check(Mathf.Approximately(tablePane.content.anchoredPosition.y, 468) && Rank("测试条目 13") == "13",
    "A rank skipped by a tie goes to the next available rank.");
Click("至底");
Check(Mathf.Approximately(tablePane.content.anchoredPosition.y, tablePane.content.rect.height - tablePane.viewport.rect.height) &&
    Rank("测试条目 56") == "56", "Bottom scrolls to and renders the final entry.");
Click("至顶");
Check(Mathf.Approximately(tablePane.content.anchoredPosition.y, 0) && Rank("测试条目 01") == "1",
    "Top scrolls back to and renders the first entry.");

var reigningActor = World.world.units.get(rankedActorID);
var homeland = World.world.kingdoms.makeNewCivKingdom(reigningActor, null, false);
homeland.data.name = "北境王国";
var homeZone = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null &&
    item.zone.city == null && item.zone.neighbours_all.All(zone => zone.city == null)).zone;
var home = World.world.cities.newCity(homeland, homeZone, reigningActor);
reigningActor.joinCity(home);
homeland.setCapital(home);
string silverName = groupName + "·霁雪", bronzeName = groupName + "·铜羽";
foreach (var contender in new[] { (Name: silverName, Kills: 101), (Name: bronzeName, Kills: 100) })
{
    var actor = World.world.units.createNewUnit("human", reigningActor.current_tile);
    actor.data.name = contender.Name;
    actor.data.kills = contender.Kills;
}
World.world.units.checkContainer();
Board("人物榜");
while (ScrollWindow.isAnimationActive() || CanvasMain.tooltip_show_timeout > 0f) await Game.NextFrame();
await Game.NextFrame();
Canvas.ForceUpdateCanvases();
var identityButton = Find(fighterName);
var nativeAvatar = identityButton.GetComponentInChildren<UiUnitAvatarElement>();
var flag = nativeAvatar.kingdomBanner;
float actorAvatarScale = nativeAvatar.transform.localScale.x;
Check(nativeAvatar.getActor() == reigningActor && flag.isActiveAndEnabled && flag.GetNanoObject() == homeland &&
    identityButton.GetComponentInChildren<Text>().color == homeland.getColor().getColorText(),
    "The native portrait shows the actor's kingdom flag, and the name uses its text color.");
Check(!Find(scholarName).GetComponentInChildren<UiUnitAvatarElement>().kingdomBanner.gameObject.activeSelf,
    "An actor outside a civilization has no kingdom flag.");

Sprite MedalFrame(string metal) => SpriteTextureLoader.getSprite("inmny/godtools/ui/rankings/portrait_" + metal);
var ordinaryFrame = nativeAvatar.GetComponent<Image>().sprite;
void CheckPortraitFrame(string name, string rank, Sprite expected)
{
    var avatar = Find(name).GetComponentInChildren<UiUnitAvatarElement>();
    var frame = avatar.GetComponent<Image>();
    Check(Rank(name) == rank && frame.isActiveAndEnabled && frame.overrideSprite == expected &&
        !avatar.unit_type_bg.isActiveAndEnabled && !avatar.transform.parent.GetComponent<Image>().enabled,
        $"{name} at rank {rank} displays one portrait frame with the expected sprite.");
}

var goldFrame = MedalFrame("gold");
var silverFrame = MedalFrame("silver");
var bronzeFrame = MedalFrame("bronze");
Check(new[] { goldFrame, silverFrame, bronzeFrame, ordinaryFrame }.Distinct().Count() == 4 &&
    goldFrame != null && silverFrame != null && bronzeFrame != null, "All three medal frames loaded as distinct assets.");
CheckPortraitFrame(fighterName, "1", goldFrame);
CheckPortraitFrame(silverName, "2", silverFrame);
CheckPortraitFrame(bronzeName, "3", bronzeFrame);
CheckPortraitFrame(scholarName, "4", ordinaryFrame);

PointerEventData PointAtPortrait(RectTransform target)
{
    var pointer = new PointerEventData(EventSystem.current)
    {
        position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, target.TransformPoint(target.rect.center)),
        button = PointerEventData.InputButton.Left
    };
    var targets = new List<RaycastResult>();
    raycaster.Raycast(pointer, targets);
    Check(targets.Count > 0 && targets[0].gameObject == identityButton.gameObject,
        "The portrait and kingdom flag both pass pointer events to the actor button.");
    return pointer;
}

Tooltip.hideTooltipNow();
Config.tooltips_active = true;
string actorIdentityShot = Screen.Capture("rankings-actor-identity.png");
await Game.NextFrame();
var portraitPointer = PointAtPortrait(nativeAvatar.avatarLoader._actor_image.rectTransform);
PointAtPortrait((RectTransform)flag.transform);
ExecuteEvents.Execute(identityButton.gameObject, portraitPointer, ExecuteEvents.pointerEnterHandler);
Check(Tooltip.findActive(tip => tip.data?.actor == reigningActor) != null,
    "Hovering the portrait opens the original actor tooltip for the correct unit.");
await Game.NextFrame();
string actorTooltipShot = Screen.Capture("rankings-actor-tooltip.png");
await Game.NextFrame();
ExecuteEvents.Execute(identityButton.gameObject, portraitPointer, ExecuteEvents.pointerClickHandler);
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
Check(ScrollWindow.isCurrentWindow("unit") && SelectedUnit.unit == reigningActor,
    "Clicking the portrait opens the original unit inspector for the correct actor.");
string unitWindowShot = Screen.Capture("rankings-actor-inspector.png");
await Game.NextFrame();
await Open();
Board("跳转验收榜");
Check(!nativeAvatar.gameObject.activeSelf && nativeAvatar.transform.parent.GetComponent<Image>().enabled &&
    Find("测试条目 01").GetComponentInChildren<Text>().color == Color.white,
    "A recycled non-actor entry drops the native avatar and resets the name color.");
Board("人物榜");
Check(nativeAvatar.getActor() == reigningActor && flag.GetNanoObject() == homeland &&
    Mathf.Approximately(nativeAvatar.transform.localScale.x, actorAvatarScale),
    "Rebinding the same slot restores the actor and flag at the original scale.");
RightTab("排序");
SortDirection("kills").onClick.Invoke();
CheckPortraitFrame(scholarName, "1", goldFrame);
CheckPortraitFrame(bronzeName, "2", silverFrame);
CheckPortraitFrame(silverName, "3", bronzeFrame);
CheckPortraitFrame(fighterName, "4", ordinaryFrame);

string customGroup = "ranktest:research-" + suffix;
string sameTitleGroup = "ranktest:research-other-" + suffix;
string futureGroup = "ranktest:future-" + suffix;
const string research = "研习榜单";
const string future = "跨模组联动";
Check(RankingRegistry.RegisterGroup(new RankingGroup { ID = customGroup, Name = research, Order = 150 }) &&
    RankingRegistry.RegisterGroup(new RankingGroup { ID = sameTitleGroup, Name = research, Order = 250 }) &&
    !RankingRegistry.RegisterGroup(new RankingGroup { ID = customGroup, Name = "重复组" }) &&
    !RankingRegistry.RegisterGroup(new RankingGroup { ID = RankingGroupIds.CharactersAndEquipment, Name = "覆盖组" }),
    "External groups register by ID, and neither a mod nor a built-in group can be replaced by a duplicate ID.");

bool RegisterPlacement(string key, string name, string group, int order = int.MaxValue, string category = null,
    Func<bool> available = null) =>
    RankingRegistry.RegisterBoard(new RankingBoard
    {
        ID = $"ranktest:{key}-{suffix}", SourceID = spellSource, Name = name, GroupID = group, Order = order,
        Category = category, Columns = new[] { "uses" }, DefaultSort = "uses", Available = available
    });
const string insertedName = "施法记录";
const string earlyName = "研习新榜";
const string laterName = "研习总榜";
const string separateName = "研习外榜";
const string pendingName = "联动榜";
const string legacyName = "旧分组扩展榜";
Check(RegisterPlacement("inserted", insertedName, RankingGroupIds.CharactersAndEquipment, 150) &&
    RegisterPlacement("research-main", laterName, customGroup, 300) &&
    RegisterPlacement("research-new", earlyName, customGroup, 100) &&
    RegisterPlacement("research-other", separateName, sameTitleGroup) &&
    RegisterPlacement("pending", pendingName, futureGroup) &&
    RegisterPlacement("legacy", legacyName, null, category: "世界榜单") &&
    RankingRegistry.RegisterGroup(new RankingGroup { ID = "ranktest:empty-" + suffix, Name = "空分组验收" }) &&
    RegisterPlacement("hidden", "隐藏榜单验收", "ranktest:empty-" + suffix, available: () => false),
    "An extension can assign an existing group, a new group, a late group, or a legacy category.");

List<(string Name, List<string> Boards)> ReadGroups(Transform area)
{
    var groups = new List<(string Name, List<string> Boards)>();
    foreach (Transform row in area)
    {
        if (!row.gameObject.activeInHierarchy) continue;
        var buttons = row.GetComponentsInChildren<Button>();
        if (buttons.Length == 0)
            groups.Add((row.GetComponentInChildren<Text>().text, new List<string>()));
        else
            groups[^1].Boards.AddRange(buttons.Select(button => button.name));
    }
    return groups;
}

Board("榜单总览");
Check(ReadGroups(Navigation().content).Single(group => group.Name == "模组榜单").Boards.Contains(pendingName),
    "A late or optional group does not hide its board.");
Check(RankingRegistry.RegisterGroup(new RankingGroup { ID = futureGroup, Name = future }), "A group can register after its board.");
Board("榜单总览");
var overviewPanel = ui.GetComponentsInChildren<RectTransform>().Single(item => item.name == "RankingPanel");
var navigationGroups = ReadGroups(Navigation().content);
Check(navigationGroups[0].Name == "人物与装备" &&
    navigationGroups[0].Boards.SequenceEqual(new[] { "人物榜", insertedName, "装备榜", legacyName }),
    "The renamed first group supports explicit insertion and keeps old category references working.");
Check(navigationGroups[1].Name == research && navigationGroups[1].Boards.SequenceEqual(new[] { earlyName, laterName }) &&
    navigationGroups[2].Name == "势力与聚落" &&
    navigationGroups[3].Name == research && navigationGroups[3].Boards.SequenceEqual(new[] { separateName }),
    "Groups and boards use their configured order and keep identical display names distinct.");
Check(navigationGroups.Single(group => group.Name == future).Boards.Contains(pendingName) &&
    navigationGroups.All(group => group.Name is not ("模组榜单" or "空分组验收")),
    "The late registration relocates its board and removes empty headings.");
Check(overviewPanel.GetComponentsInChildren<Button>().All(button => button.name != insertedName && button.name != pendingName),
    "The overview leaves board navigation in the left sidebar.");
Board(insertedName);
Check(Rank(spellB) == "1", "An inserted board still opens its external data source.");

return new
{
    actorShot, sortShot, filterShot, foldedShot, presetShot, presetTooltipShot, narrowColumnsShot, wideColumnsShot,
    jumpShot, actorIdentityShot, actorTooltipShot, unitWindowShot, rankedActorID, weaponID, versions = versions.Length
};
