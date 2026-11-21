using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GodTools.Features.Rankings;
using GodTools.UI;
using NeoModLoader;
using UnityEngine;
using UnityEngine.UI;
using strings;

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
const string Prefix = "inmny.godtools.ranking.duel.";
const string Previous = "power:godtools:standard-duel@1";
const string Current = "power:godtools:standard-duel@2";
var assess = assembly.GetType("GodTools.Features.Rankings.StandardDuelPower", true)
    .GetMethod("Assess", BindingFlags.Static | BindingFlags.NonPublic);
RankingPowerScore Score(Actor actor) => (RankingPowerScore)assess.Invoke(null, new object[] { actor });
double Part(RankingPowerScore score, string key) => score.Parts.GetValueOrDefault(Prefix + key);
bool SameScore(RankingPowerScore a, RankingPowerScore b, string key) => Math.Abs(Part(a, key) - Part(b, key)) < 0.11;

string id = "provider-test:" + Guid.NewGuid().ToString("N")[..6];
string mana = id + ":mana";
double liveMana = 10;
var tile = World.world.tiles_list.First(item => !item.main_type.liquid && item.zone != null);
Actor Unit(string name)
{
    var actor = World.world.units.createNewUnit("human", tile, pAdultAge: true);
    actor.data.name = name;
    actor.data.custom_name = true;
    actor.stats[S.health] = 120;
    actor.stats[S.damage] = 11;
    actor.stats[S.damage_range] = 1;
    actor.stats[S.critical_chance] = 0;
    actor.stats[S.armor] = 0;
    actor.stats[S.range] = 2;
    actor.stats[S.speed] = 28;
    actor.stats[S.attack_speed] = 1;
    return actor;
}

Actor vanilla = Unit("普通单位"), onePool = Unit("共用一份灵力"), recharge = Unit("持续回能"),
    locked = Unit("条件不满足"), reaction = Unit("远程护盾"), badRead = Unit("读取失败"), badCondition = Unit("条件异常");
Actor rapid = Unit("自由施放"), cooldown = Unit("独立冷却"), limited = Unit("两次充能");
var controlled = new HashSet<Actor>
    { vanilla, onePool, recharge, locked, reaction, badRead, badCondition, rapid, cooldown, limited };
foreach (var owner in WorldBoxMod.LoadedMods.Select(mod => mod.GetDeclaration())
    .Where(mod => mod.UID == "CULTIWAY" || mod.UID == "TURBO_SAKUGA"))
    Check(RankingRegistry.RegisterCombatProvider(new RankingCombatProvider
    {
        ID = id + ":controlled:" + owner.UID, Owner = owner,
        Read = (actor, _) => controlled.Contains(actor)
    }), "The controlled samples should only use their declared benchmark data.");
var origin = new RankingCombatProvider
{
    ID = id + ":pool", Name = "测试资源池",
    Read = (actor, data) =>
    {
        if (actor == onePool || actor == recharge)
        {
            Check(data.AddResource(mana, 10, actor == recharge ? 10 : 0, actor == recharge ? 0 : liveMana), "Shared pool accepted.");
            Check(!data.AddResource(mana, 10) && !data.AddResource(id + ":invalid", double.NaN),
                "Duplicate and invalid resource descriptions are rejected.");
            Check(data.AddSkill(new RankingCombatSkill
            {
                ID = "bolt", Amount = 28, Range = 32, RecoverySeconds = 0.25, CooldownSeconds = 10, ResourceID = mana, Cost = 10
            }), "First ranged skill consumes the shared pool.");
            return true;
        }
        return false;
    }
};
Check(RankingRegistry.RegisterCombatProvider(origin) && !RankingRegistry.RegisterCombatProvider(origin),
    "Provider registrations use stable unique IDs.");
Check(RankingRegistry.RegisterCombatProvider(new RankingCombatProvider
{
    ID = id + ":skills", Name = "测试技能包",
    Read = (actor, data) =>
    {
        if (actor == onePool || actor == recharge || actor == locked || actor == badCondition)
        {
            bool shared = actor == onePool || actor == recharge;
            var skill = new RankingCombatSkill
            {
                ID = "arc", Amount = 40, Range = 32, RecoverySeconds = 0.25, CooldownSeconds = 1,
                ResourceID = shared ? mana : null, Cost = shared ? 10 : 0,
                When = ctx => shared ? ctx.Resource(mana) >= 10 && ctx.OpponentHealthFraction > 0 :
                    actor == badCondition ? throw new InvalidOperationException("test condition error") : ctx.Seconds < 0
            };
            Check(data.AddSkill(skill) && !data.AddSkill(skill), "Stable skill IDs prevent duplicate effects.");
            skill.Amount = 0; // Submission must copy data that the provider can still edit.
            return true;
        }
        if (actor == rapid || actor == cooldown || actor == limited)
            return data.AddSkill(new RankingCombatSkill
            {
                ID = "pulse", Amount = 20, Range = 32, RecoverySeconds = 0.25,
                CooldownSeconds = actor == cooldown ? 4 : 0, Uses = actor == limited ? 2 : null
            });
        if (actor == reaction)
        {
            Check(data.AddSkill(new RankingCombatSkill
            {
                ID = "ward", Trigger = RankingCombatTrigger.IncomingDamage,
                Effect = RankingCombatEffect.PreventDamage, Amount = 0.9, Range = 20,
                When = ctx => ctx.Opponent == RankingCombatOpponent.Ranged &&
                    ctx.HealthFraction < 0.9 && ctx.IncomingDamage > 0 && ctx.Distance <= 20
            }), "Health- and matchup-dependent reactions can be provided.");
            return true;
        }
        if (actor == badRead)
        {
            data.AddSkill(new RankingCombatSkill { ID = "partial", Amount = 1000000 });
            throw new InvalidOperationException("test provider error");
        }
        return false;
    }
}), "A second provider can publish a skill using an existing pool.");

var baseline = Score(vanilla);
var scarce = Score(onePool);
var sustained = Score(recharge);
var gated = Score(locked);
var defended = Score(reaction);
Check(Part(scarce, "casts") == 1 && Part(sustained, "casts") > 1 && sustained.Total > scarce.Total,
    $"Shared budget and regeneration affect rank. Scarce {scarce.Total}, sustained {sustained.Total}.");
Check(liveMana == 10 && Score(recharge).Total == sustained.Total,
    "Re-evaluation is deterministic and never consumes live resources.");
Check(Part(gated, "casts") == 0 && SameScore(gated, baseline, "melee") && SameScore(gated, baseline, "ranged") &&
    SameScore(gated, baseline, "armored"), "A failed condition does not confer any combat benefit.");
Check(Part(defended, "reactions") > 0 && Part(defended, "ranged") > Part(baseline, "ranged") &&
    SameScore(defended, baseline, "melee") && SameScore(defended, baseline, "armored"),
    $"The damage reaction only improves the matchup allowed by its conditions. " +
    $"Before {string.Join(" / ", baseline.Parts.Values)}, after {string.Join(" / ", defended.Parts.Values)}.");
double rapidCasts = Part(Score(rapid), "casts");
double cooldownCasts = Part(Score(cooldown), "casts");
double limitedCasts = Part(Score(limited), "casts");
Check(rapidCasts > cooldownCasts && cooldownCasts > 0 && rapidCasts > limitedCasts && limitedCasts == 2,
    $"Cooldown and charge count act independently. Rapid {rapidCasts}, cooldown {cooldownCasts}, charges {limitedCasts}.");
var failedRead = Score(badRead);
var failedCondition = Score(badCondition);
Check(failedRead.Total == baseline.Total && failedCondition.Total == baseline.Total &&
    failedRead.Details.Contains("部分模组资料不可用") && failedCondition.Details.Contains("技能条件异常"),
    $"Provider and predicate errors leave ordinary combat intact and are explained. " +
    $"Baseline {baseline.Total}, read {failedRead.Total} ({failedRead.Details}), " +
    $"condition {failedCondition.Total} ({failedCondition.Details}).");

var combat = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(mod => mod.GetType("Sakuga.Fighters") != null);
bool optInReplacesFallback = false;
if (combat != null)
{
    var callbackOwner = WorldBoxMod.LoadedMods.Select(mod => mod.GetDeclaration()).Single(mod => mod.UID == "TURBO_SAKUGA");
    var dedicated = Unit("主动接入止水");
    var compatibility = Unit("内置接入止水");
    foreach (var actor in new[] { dedicated, compatibility })
    {
        actor.addTrait("sakuga_still_water");
        combat.GetType("Sakuga.Fighters").GetMethod("Get", new[] { typeof(Actor) }).Invoke(null, new object[] { actor });
    }
    Check(Part(Score(dedicated), "parries") > 0, "The real CombatGods fallback is available.");
    Check(RankingRegistry.RegisterCombatProvider(new RankingCombatProvider
    {
        ID = id + ":combatgods", Name = "实验接入", Owner = callbackOwner,
        Read = (actor, data) =>
        {
            if (actor != dedicated) return false;
            data.AddNote("实验战斗适配");
            return data.AddSkill(new RankingCombatSkill { ID = "active", Amount = 40, Range = 30 });
        }
    }), "A provider can use its explicitly declared mod owner.");
    var overrideScore = Score(dedicated);
    optInReplacesFallback = Part(overrideScore, "casts") > 0 && Part(overrideScore, "parries") == 0 &&
        overrideScore.Details.Contains("实验战斗适配") && Part(Score(compatibility), "parries") > 0;
    Check(optInReplacesFallback, "An active provider takes priority for its actor, with fallback for other actors.");
}

var preset = new RankingPreset
{
    BoardID = "godtools:actors", Name = "旧版战力设置",
    Sort = new() { new RankingSort { Field = Previous } }, PinnedColumns = new() { Previous },
    Conditions = new() { new RankingToken { Symbol = RankingSymbol.Value,
        Condition = new RankingCondition { Field = Previous, Operator = RankingOperator.AtLeast, Value = "1000" } } }
};
var basePreset = new RankingPreset
{
    BoardID = "godtools:actor-power", Name = "基础战力条件",
    Sort = new() { new RankingSort { Field = "power" }, new RankingSort { Field = Current, Ascending = true } },
    PinnedColumns = new() { Current, "power" },
    Conditions = new() { new RankingToken { Symbol = RankingSymbol.Value,
        Condition = new RankingCondition { Field = "power", Operator = RankingOperator.AtLeast, Value = "150" } } }
};
var invalidated = (HashSet<string>)assembly.GetType("GodTools.Features.Rankings.RankingStore", true)
    .GetMethod("ConsolidatePresets", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new object[] { new[] { preset, basePreset } });
Check(preset.Sort[0].Field == Current && preset.PinnedColumns[0] == Current &&
    preset.Conditions[0].Condition.Field == Current && preset.Conditions[0].Condition.Value == "1000",
    "An existing preset follows the new version with its three reference types intact.");
Check(basePreset.Sort.Single().Field == Current && !basePreset.Sort[0].Ascending &&
    basePreset.PinnedColumns.SequenceEqual(new[] { Current }) && basePreset.Conditions[0].Condition.Field == "power" &&
    invalidated.Contains(basePreset.ID),
    "Old score columns migrate once, while old numeric thresholds remain available for manual correction.");

string board = "字段接口验收榜 " + id;
const string Grade = "品阶验收";
const string Rate = "回能速率";
var rows = new[] { (Name: "第一阶素材", Key: "low", Label: "初阶", Order: 1, Energy: 4d),
    (Name: "第二阶素材", Key: "middle", Label: "中阶", Order: 2, Energy: 8d),
    (Name: "第三阶素材", Key: "top", Label: "高阶", Order: 3, Energy: 12d) };
Check(RankingRegistry.RegisterSource(new RankingSource<(string Name, string Key, string Label, int Order, double Energy)>
{
    ID = id, Read = () => rows, Key = entry => entry.Key, Name = entry => entry.Name
}), "A consumer can create a data source.");
Check(RankingRegistry.RegisterField(id, new RankingField<(string Name, string Key, string Label, int Order, double Energy)>
{
    ID = "grade", Name = Grade, Kind = RankingFieldKind.Choice, Ordered = true,
    IconPath = "ui/icons/iconStone", Description = "可按品阶上限和下限筛选",
    Read = entry => RankingValue.Choice(entry.Key, entry.Label, entry.Order)
}) && RankingRegistry.RegisterField(id, new RankingField<(string Name, string Key, string Label, int Order, double Energy)>
{
    ID = "rate", Name = Rate, Unit = "灵力/秒", Kind = RankingFieldKind.Number, IconPath = "ui/icons/iconDamage",
    Read = entry => RankingValue.Numeric(entry.Energy)
}), "External field metadata registers on the ordinary API.");
Check(RankingRegistry.RegisterBoard(new RankingBoard
{
    ID = id, SourceID = id, Name = board, DefaultSort = "grade", Columns = new[] { "grade", "rate" }
}), "The metadata can be used by the default rankings UI.");

ScrollWindow.hideAllEvent(pWithAnimation: false);
while (ScrollWindow.isAnimationActive()) await Game.NextFrame();
ScrollWindow.showWindow("WindowRankings", pSkipAnimation: true);
for (int i = 0; i < 8; i++) await Game.NextFrame();
var window = WindowRankings.Instance;
Button ButtonIn(Component root, string name) => root.GetComponentsInChildren<Button>().FirstOrDefault(item => item.name == name)
    ?? throw new InvalidOperationException("Missing button " + name);
ButtonIn(window, board).onClick.Invoke();
RectTransform Region(string name) => window.GetComponentsInChildren<RectTransform>().Single(item => item.name == name);
var headers = Region("RankingColumns").GetComponentsInChildren<Text>().Select(item => item.text).ToArray();
Check(headers.Contains(Rate + " (灵力/秒)"), "The extension's unit appears in the table.");
var barLabels = Region("RankingDistribution").GetComponentsInChildren<Text>()
    .Select(item => item.text).Where(label => rows.Any(entry => entry.Label == label)).ToArray();
Check(barLabels.SequenceEqual(rows.Select(entry => entry.Label)), "The chart follows declared tier order.");
ButtonIn(Region("RankingRightTabs"), "筛选").onClick.Invoke();
var field = ButtonIn(Region("RankingRightSidebar"), Grade);
Check(field.GetComponentsInChildren<Image>().Any(item => item.sprite == SpriteTextureLoader.getSprite("ui/icons/iconStone")),
    "The native filter grid uses the extension's icon.");
Tooltip.hideTooltipNow();
field.GetComponent<TipButton>().hoverAction.Invoke();
var hint = Tooltip.findActive(item => item.name.text == Grade);
Check(hint?.description.text == "可按品阶上限和下限筛选", "The registered description appears in the native tooltip body.");
Tooltip.hideTooltipNow();
field.onClick.Invoke();
ButtonIn(Region("RankingRightSidebar"), "至少").onClick.Invoke();
ButtonIn(Region("RankingRightSidebar"), "中阶").onClick.Invoke();
ButtonIn(window, "应用筛选").onClick.Invoke();
await Game.NextFrame();
Check(window.GetComponentsInChildren<Button>().Any(item => item.name == "第二阶素材") &&
    window.GetComponentsInChildren<Button>().Any(item => item.name == "第三阶素材") &&
    !window.GetComponentsInChildren<Button>().Any(item => item.name == "第一阶素材"),
    "Clicking a named tier creates an ordered cutoff and updates the board.");

return new
{
    baseline = baseline.Total,
    scarce = new { score = scarce.Total, casts = Part(scarce, "casts") },
    sustained = new { score = sustained.Total, casts = Part(sustained, "casts") },
    ranged = new { baseline = Part(baseline, "ranged"), defended = Part(defended, "ranged"), reactions = Part(defended, "reactions") },
    skillLimits = new { rapidCasts, cooldownCasts, limitedCasts },
    optInReplacesFallback, headers, barLabels, version = Current
};
