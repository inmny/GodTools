using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cultiway.Content;
using Cultiway.Content.Components;
using Cultiway.Core.Components;
using Cultiway.Core.SkillLibV3;
using Cultiway.Core.SkillLibV3.Components;
using Cultiway.Core.SkillLibV3.Impacts;
using Cultiway.Core.SkillLibV3.Utils;
using Cultiway.Utils.Extension;
using Friflo.Engine.ECS;
using GodTools.Features;
using GodTools.Features.Rankings;
using GodTools.UI;
using strings;
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
var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
string prefix = "inmny.godtools.ranking.duel.";
var estimate = typeof(RankingPowerProfiles).Assembly.GetType("GodTools.Features.Rankings.StandardDuelPower", true);
var assess = estimate.GetMethod("Assess", BindingFlags.Static | BindingFlags.NonPublic);
RankingPowerScore Score(Actor actor) => (RankingPowerScore)assess.Invoke(null, new object[] { actor });
var units = new List<Actor>();
var suffix = Guid.NewGuid().ToString("N")[..4];

Actor Human(string name)
{
    var actor = World.world.units.createNewUnit("human", tile, pAdultAge: true);
    actor.data.name = name + " " + suffix;
    actor.data.custom_name = true;
    var extend = actor.GetExtend();
    extend.SetPowerLevel(0);
    foreach (var skill in extend.all_skills.ToArray()) extend.ForgetSkillV3(skill);
    units.Add(actor);
    return actor;
}

void Stats(Actor actor, float damage = 11, float health = 120)
{
    actor.stats[S.damage] = damage;
    actor.stats[S.health] = health;
    actor.stats[S.armor] = 0;
    actor.stats[S.range] = 2;
    actor.stats[S.speed] = 28;
    actor.stats[S.attack_speed] = 1;
    actor.stats[S.damage_range] = 1;
    actor.stats[S.critical_chance] = 0;
}

var plain = Human("原版基准");
var cultured = Human("境界护体");
var littleMana = Human("低灵力术士");
var ampleMana = Human("高灵力术士");
var parry = Human("止水招架");
var stock = Human("一张符箓");
World.world.units.checkContainer();
foreach (var unit in units) Stats(unit);
var beforeExtreme = Score(plain);
Check(beforeExtreme != null, "The original fighter is evaluated.");
var inflated = Human("极高数值");
var hugeAttack = Human("极端伤害");
var overflow = Human("普攻输入溢出");
World.world.units.checkContainer();
Stats(inflated, 1e8f, 1e8f);
Stats(hugeAttack, 1e9f, 1e8f);
Stats(overflow, 1e32f, 1e8f);
var large = Score(inflated);
var extreme = Score(hugeAttack);
Check(large.Total > beforeExtreme.Total && extreme.Total > large.Total && extreme.Total < 5400,
    "Legitimate inflation changes the score and the estimate stays finite.");
Check(Score(overflow) == null, "An out-of-range ordinary hit cannot produce a fictitious combat score.");
Check(Score(plain).Total == beforeExtreme.Total, "An unrelated extreme unit does not rescale an existing score.");

var realm = cultured.GetExtend();
realm.SetPowerLevel(2);
Stats(cultured);
var tiered = Score(cultured);
Check(tiered.Total > beforeExtreme.Total && tiered.Details.Contains("境界减伤"),
    "Cultiway's actual power layer affects incoming damage.");
Check(ConsistentCreaturePowerTop.CalculatePower(plain) == ConsistentCreaturePowerTop.CalculatePower(cultured),
    "Changing the layer alone does not change the old panel formula.");

var projectile = Cultiway.ModClass.I.SkillV3.SkillLib.list.First(asset => asset.CanBeLearned &&
    asset.Animations.Count > 0 && asset.DealsBaseDamage && asset.ImpactProfile.Kind == SkillImpactKind.Projectile &&
    asset.UseProfile.TargetRelation.ToString() == "Hostile" && asset.ImpactProfile.DamageMultiplier > 0.5f);
var spell = new SkillContainerBuilder(projectile).UseAnimation(0)
    .UseCastResources(SkillCastResourceRequirement.Single(SkillCastResources.Wakan)).Build(SkillContainerBuildMode.RuleOnly);
float WakanPerCast(Actor actor) => SkillCastCost.CalculateStepDemand(spell) *
    Mathf.Sqrt(SkillEffectRadius.ResolveCasterGrowth(actor.GetExtend(), spell));
foreach (var (actor, factor) in new[] { (littleMana, 1.05f), (ampleMana, 20f) })
{
    var extend = actor.GetExtend();
    extend.AddComponent(new Xian { level = 0, wakan = 0 });
    extend.LearnSkillV3(spell);
    Stats(actor);
    actor.stats[BaseStatses.MaxWakan.id] = WakanPerCast(actor) * factor;
}
var low = Score(littleMana);
var high = Score(ampleMana);
Check(low.Parts.TryGetValue(prefix + "casts", out double lowCasts) && lowCasts <= 1 &&
      high.Parts.TryGetValue(prefix + "casts", out double highCasts) && highCasts > 1 && high.Total > low.Total,
    $"The same spell and different full pools yield different spell uptime and rankings. Low {low.Total}, high {high.Total}.");
Check(littleMana.GetExtend().GetCultisys<Xian>().wakan == 0 && ampleMana.GetExtend().GetCultisys<Xian>().wakan == 0,
    "Previewing the benchmark never spends real resources.");

var inventory = stock.GetExtend();
inventory.AddComponent(new Xian { level = 0, wakan = 0 });
var consumable = Cultiway.Utils.SpecialItemUtils
    .StartBuild(ItemShapes.Talisman, World.world.getCurWorldTime(), stock.getName())
    .AddComponent(new EntityName("战力验收符")).AddComponent(new Talisman { Strength = 150, PowerLevel = 0, SkillContainer = spell })
    .Build();
consumable.AddRelation(new SkillMasterRelation { SkillContainer = spell });
inventory.AddSpecialItem(consumable);
var oneUse = Score(stock);
Check(oneUse.Parts.TryGetValue(prefix + "casts", out double talismanCasts) && talismanCasts == 1 &&
    !consumable.IsNull && inventory.GetItems().Any(item => item == consumable),
    "A carried talisman contributes one cast and stays in inventory.");

var combat = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetType("Sakuga.Fighters") != null);
var trait = AssetManager.traits.get("sakuga_still_water");
Check(trait != null, "Combat Gods provides the actual Still Water technique.");
parry.addTrait(trait.id);
combat.GetType("Sakuga.Fighters").GetMethod("Get", new[] { typeof(Actor) }).Invoke(null, new object[] { parry });
Stats(parry);
var protectedScore = Score(parry);
Check(protectedScore.Parts.TryGetValue(prefix + "parries", out double blocks) && blocks > 0 &&
    protectedScore.Parts[prefix + "melee"] > beforeExtreme.Parts[prefix + "melee"] &&
    protectedScore.Parts[prefix + "ranged"] == beforeExtreme.Parts[prefix + "ranged"],
    "Still Water changes the applicable melee test and leaves the ranged test at the same score.");
Check(ConsistentCreaturePowerTop.CalculatePower(plain) == ConsistentCreaturePowerTop.CalculatePower(parry),
    "The special move changes the benchmark even with identical panel stats.");

var samples = units.Select(actor =>
{
    var score = Score(actor);
    return new { name = actor.getName(), previous = ConsistentCreaturePowerTop.CalculatePower(actor),
        revised = score?.Total, parts = score?.Parts, coverage = score?.Details };
}).ToArray();

while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
for (int frame = 0; frame < 8; frame++) await Game.NextFrame();
// The game's start-of-world update may rebuild panel stats while the window is animating.
foreach (var unit in units) Stats(unit);
Stats(inflated, 1e8f, 1e8f);
Stats(hugeAttack, 1e9f, 1e8f);
Stats(overflow, 1e32f, 1e8f);
littleMana.stats[BaseStatses.MaxWakan.id] = WakanPerCast(littleMana) * 1.05f;
ampleMana.stats[BaseStatses.MaxWakan.id] = WakanPerCast(ampleMana) * 20f;
var window = WindowRankings.Instance;
Button ButtonIn(Component root, string name) => root.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == name)
    ?? throw new InvalidOperationException("Missing button " + name);
ButtonIn(window, "人物榜").onClick.Invoke();
var sidebar = window.GetComponentsInChildren<ScrollRect>().Single(item => item.name == "RankingRightSidebar");
ButtonIn(sidebar, "恢复默认排序").onClick.Invoke();
Check(sidebar.GetComponentsInChildren<Text>().Any(item => item.text == "1   战力") &&
    !sidebar.GetComponentsInChildren<Button>().Any(item => item.name.Contains("标准单体战力")),
    "The unified power is the default and the only built-in combat score.");
for (int frame = 0; frame < 4; frame++) await Game.NextFrame();
var ranking = Screen.Capture("standard-duel-ranking.png");
await Game.NextFrame();
await Game.NextFrame();
Check(ButtonIn(window, hugeAttack.getName()).GetComponentsInChildren<Text>().Any(text => text.name == "RankingEntryRank"),
    "The extreme sample appears in the ranked list.");
var rows = (IEnumerable)typeof(WindowRankings).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
object parryRow = rows.Cast<object>().First(item =>
    (string)item.GetType().GetField("Name", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item) == parry.getName());
int parryRank = (int)parryRow.GetType().GetField("Rank", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(parryRow);
window.GetComponentsInChildren<InputField>().Single(item => item.name == "RankingRankJumpInput").onEndEdit.Invoke(parryRank.ToString());
var row = ButtonIn(window, parry.getName());
var value = row.transform.parent.GetComponentsInChildren<Button>().First(item => item.name == "RankingMetric");
Tooltip.hideTooltipNow();
await Game.NextFrame();
value.GetComponent<TipButton>().hoverAction.Invoke();
var tip = Tooltip.findActive(item => item.transform.name == "inmny.godtools.ranking.metric_tooltip");
Check(tip?.description.text.Contains("近战压制对手") == true &&
    tip.description_2.text.Contains("止水的近战普攻招架") && ButtonIn(window, parry.getName()) != null,
    "The score tooltip shows the benchmark matchups and modeled parry without leaving the board.");
await Game.NextFrame();
var detail = Screen.Capture("standard-duel-tooltip.png");
await Game.NextFrame();
await Game.NextFrame();

return new { samples, ranking, detail, spell = projectile.id };
