using System;
using System.Linq;
using Cultiway.Content.Libraries;
using Cultiway.Core.Components;
using Cultiway.Utils.Extension;
using GodTools.Features.Rankings;
using GodTools.UI;
using GodTools.UI.CreatureDataEditorGrids;
using GodTools.UI.Prefabs;
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
var actor = World.world.units.createNewUnit("human", World.world.tiles_list.First(tile => !tile.main_type.liquid && tile.zone != null));
World.world.units.checkContainer();
var suffix = Guid.NewGuid().ToString("N")[..6];
var book = new CultibookAsset
{
    id = "cultiway_fallback_probe_" + suffix, Name = "兼容验收功法 " + suffix, Description = "用于验证内置功法榜。",
    FinalStats = new BaseStats()
};
Manager.CultibookLibrary.add(book);
actor.GetExtend().Master(book, 40);
actor.GetExtend().AddComponent(new ElementRoot(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }));
SelectedUnit.select(actor);
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
ScrollWindow.showWindow("WindowCreatureDataEditor", pSkipAnimation: true);
for (int frame = 0; frame < 8; frame++) await Game.NextFrame();
var editor = WindowCreatureDataEditor.Instance;
editor.GetComponentsInChildren<TextButton>().Single(item => item.name == "cultiway").Button.onClick.Invoke();
editor.GetComponentInChildren<PowerLevelEditor>().GetComponentInChildren<InputField>().onEndEdit.Invoke("12");
editor.GetComponentInChildren<ElementRootEditor>().GetComponentsInChildren<InputField>().First().onEndEdit.Invoke("9");
Check(actor.GetExtend().GetPowerLevel() == 12 && actor.GetExtend().GetElementRoot()[0] == 9,
    "The Cultiway creature editor still writes the power level and element root without a compile-time dependency.");
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
for (int frame = 0; frame < 12; frame++) await Game.NextFrame();

Button Button(string name) => WindowRankings.Instance.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == name);
Button("人物榜").onClick.Invoke();
Button("排序").onClick.Invoke();
Check(Button("修仙境界") != null && Button("旧版自定义人物榜") == null,
    "The optional actor fields appear on the unified actor board.");
Button("功法榜")?.onClick.Invoke();
Check(Button(book.Name) != null, "Cultiway without a provider uses the built-in book board.");
Check(Button("魔网法术榜") == null, "Native-only boards stay in Cultiway.");
Button(book.Name).onClick.Invoke();
await Game.NextFrame();
Check(Tooltip.findActive(tip => tip.name != null && tip.name.text.Contains(book.Name)) != null,
    "The fallback opens the real Cultiway book tooltip.");
Tooltip.hideTooltipNow();

const string source = "cultiway:cultibooks";
var owner = Cultiway.ModClass.Instance.GetDeclaration();
Check(!RankingRegistry.RegisterGroup(new RankingGroup { ID = RankingGroupIds.Archives, Name = "Conflicting", Owner = owner }),
    "A conflicting definition is rejected.");
Check(Button(book.Name) != null, "Rejecting a registration keeps the working fallback.");
Check(RankingRegistry.RegisterSource(new RankingSource<string>
{
    ID = source, Owner = owner, Read = () => new[] { "主动注册功法" }, Key = name => name, Name = name => name
}), "The owner can replace its built-in data source.");
Check(RankingRegistry.RegisterBoard(new RankingBoard
{
    ID = source, SourceID = source, Owner = owner, Name = "主动注册榜"
}), "The owner can reuse the same board ID.");
for (int frame = 0; frame < 8; frame++) await Game.NextFrame();
Check(Button("主动注册功法") != null && Button("功法榜") == null && Button(book.Name) == null,
    "The active window replaces the fallback board and renders the native provider.");
return new { fallback = book.Name, replacement = "主动注册功法" };
