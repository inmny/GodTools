using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GodTools.Utils;
using NeoModLoader.api;

namespace GodTools.Features.Rankings;

internal static class OptionalActorRankingFields
{
    private const string Locale = "inmny.godtools.ui.keyword.";
    private static readonly string[] ModIDs =
    {
        "INMNY_CUSTOMMODT001",
        "INMNY_CUSTOMMODT002",
        "ZHETIAN",
        "DOUPOCANGQIONG",
        "WITCHCRAFT_WORLDBOX",
        "WITCHCRAFT_KNIGHT",
        "XULIE",
        "WARRIOR",
        "XIAN_TU",
        "XIAN_NI_MOD",
        "THEFANTASYWORLD",
        "CHEVALIER",
    };

    internal static void Install()
    {
        foreach (string id in ModIDs)
        {
            var mod = OptionalMods.Find(id);
            if (mod != null) InstallFor(id, mod);
        }
    }

    internal static void InstallFor(string id, ModDeclare mod) =>
        RankingRegistry.InstallFallback(mod, () => Register(id, mod));

    private static void Register(string id, ModDeclare mod)
    {
        switch (id)
        {
            case "INMNY_CUSTOMMODT001":
                Float("inmny_custommodt001", "talent", "inmny.custommodt001.talent", "inmny/custommodt001/talent");
                Realm("inmny_custommodt001", "inmny.custommodt001.wudao_level", "inmny.custommodt001.wudao_exp",
                    "inmny/custommodt001/cultilevel");
                break;
            case "INMNY_CUSTOMMODT002":
                RegisterSummons(mod, OptionalMods.AssemblyFor(id));
                break;
            case "ZHETIAN":
                Float("zhetian", "talent", "StartBoard.custommodt001.talent", "ui/icons/iconDamage");
                Realm("zhetian", "StartBoard.custommodt001.zhetian_level", "StartBoard.custommodt001.zhetian_exp",
                    "ui/icons/iconDamage", "StartBoard.custommodt001.zhetian.");
                break;
            case "DOUPOCANGQIONG":
                Float("doupocangqiong", "talent", "d_talent", "ui/icons/iconDamage");
                Realm("doupocangqiong", "d_level", "d_exp", "ui/icons/iconDamage", "trait_Grade");
                break;
            case "WITCHCRAFT_WORLDBOX":
                Integer("wushu", "yuanneng", "wushu.yuannengNum", "ui/yuanneng");
                Float("wushu", "meditation", "wushu.meditationNum", "ui/Meditation");
                break;
            case "WITCHCRAFT_KNIGHT":
                Float("knight", "knight", "Knight", "Knight");
                break;
            case "XULIE":
                Float("guimi_mod", "xulie", "xulie.xuLieNum", "XuLie");
                Float("guimi_mod", "jin_bang", "xulie.jinBangNum", "JinBang");
                break;
            case "WARRIOR":
                Float("warrior_mod", "warrior", "wushu.warriorNum", "Warrior");
                Float("warrior_mod", "true_gang", "wushu.trueGangNum", "TrueGang");
                Float("warrior_mod", "pattern", "wushu.patternNum", "Pattern");
                break;
            case "XIAN_TU":
                Float("xian_tu_mod", "xian_tu", "xiantu.xianTuNum", "XianTu");
                Float("xian_tu_mod", "qi_xue", "xiantu.qiXueNum", "QiXue");
                Float("xian_tu_mod", "wu_xing", "xiantu.wuXingNum", "WuXing");
                break;
            case "XIAN_NI_MOD":
                Add(mod, "xianni", "combat_power", "stats/chartsui", RankingFieldKind.Integer,
                    actor =>
                    {
                        long score = XianniRankingFormula.Calculate(actor);
                        return RankingValue.Whole(score, score.ToString("N0", CultureInfo.CurrentCulture));
                    });
                Long("xianni", "xiuwei", "xn.stat.xiuwei", "stats/xiuwei");
                Integer("xianni", "gushen_power", "xn.stat.gushen_power", "stats/gushen");
                Integer("xianni", "yaoli", "xn.stat.yaoli", "stats/yaoli");
                Integer("xianni", "wuxin", "xn.stat.wuxin", "stats/wuxin");
                Integer("xianni", "qiyun", "xn.stat.qiyun", "stats/qiyun");
                Integer("xianni", "xinmo", "xn.stat.xinmo", "stats/xinmo");
                break;
            case "THEFANTASYWORLD":
                Float("fantasy_mod", "experience", "wushu.careerexperienceNum", "ui/careerexperience");
                Float("fantasy_mod", "Determination", "wushu.DeterminationNum", "ui/Determination");
                Float("fantasy_mod", "Defydeath", "wushu.DefydeathNum", "ui/Defydeath");
                Float("fantasy_mod", "BattleIntent", "wushu.BattleIntentNum", "ui/BattleIntent");
                Float("fantasy_mod", "PaladinTough", "wushu.PaladinToughNum", "ui/PaladinTough");
                Float("fantasy_mod", "PhilosophersStone", "wushu.PhilosophersStoneNum", "ui/PhilosophersStone");
                Float("fantasy_mod", "Anger", "wushu.AngerNum", "ui/Anger");
                Float("fantasy_mod", "DodgeEvade", "wushu.DodgeEvadeNum", "ui/DodgeEvade");
                Float("fantasy_mod", "hitthetarget", "wushu.hitthetargetNum", "ui/hitthetarget");
                Float("fantasy_mod", "MagicApplication", "wushu.MagicApplicationNum", "ui/MagicApplication");
                Float("fantasy_mod", "MeditationExperience", "wushu.MeditationExperienceNum", "ui/MeditationExperience");
                Float("fantasy_mod", "MeditationInsight", "wushu.MeditationInsightNum", "ui/MeditationInsight");
                Float("fantasy_mod", "MeditationTier", "wushu.MeditationTierNum", "ui/MeditationTier");
                Float("fantasy_mod", "MagicShield", "wushu.MagicShield", "ui/MagicShield");
                Float("fantasy_mod", "FixedPhysicalDamage", "wushu.FixedPhysicalDamage", "ui/FixedPhysicalDamage");
                Float("fantasy_mod", "FixedMagicalDamage", "wushu.FixedMagicalDamage", "ui/FixedMagicalDamage");
                Float("fantasy_mod", "Restorehealth", "wushu.Restorehealth", "ui/Restorehealth");
                Float("fantasy_mod", "MagicReply", "wushu.MagicReply", "ui/MagicReply");
                Float("fantasy_mod", "MagicDamage", "wushu.MagicDamage", "ui/MagicDamage");
                Float("fantasy_mod", "MagicResistance", "wushu.MagicResistance", "ui/MagicResistance");
                Float("fantasy_mod", "PhysicalDamage", "wushu.PhysicalDamage", "ui/PhysicalDamage");
                Float("fantasy_mod", "PhysicalDefense", "wushu.PhysicalDefense", "ui/PhysicalDefense");
                Float("fantasy_mod", "LegendaryStatus", "wushu.LegendaryStatus", "ui/LegendaryStatus");
                break;
            case "CHEVALIER":
                Float("Chevalier_mod", "Chevalier", "wushu.ChevalierNum", "Chevalier");
                Float("Chevalier_mod", "Comprehension", "ComprehensionNum", "Comprehension");
                break;
        }

        void Float(string group, string field, string key, string icon) =>
            Add(mod, group, field, icon, RankingFieldKind.Number,
                actor => actor.data.custom_data_float?.TryGetValue(key, out float value) == true
                    ? RankingValue.Numeric(value) : null);

        void Integer(string group, string field, string key, string icon) =>
            Add(mod, group, field, icon, RankingFieldKind.Integer,
                actor => actor.data.custom_data_int?.TryGetValue(key, out int value) == true
                    ? RankingValue.Integer(value) : null);

        void Long(string group, string field, string key, string icon) =>
            Add(mod, group, field, icon, RankingFieldKind.Integer,
                actor => actor.data.custom_data_long?.TryGetValue(key, out long value) == true
                    ? RankingValue.Whole(value) : null);

        void Realm(string group, string levelKey, string progressKey, string icon, string namePrefix = null) =>
            Add(mod, group, "level", icon, RankingFieldKind.Ordinal, actor =>
            {
                if (actor.data.custom_data_int?.TryGetValue(levelKey, out int level) != true) return null;
                actor.data.get(progressKey, out int progress);
                string key = level.ToString(CultureInfo.InvariantCulture);
                string label = namePrefix == null ? key : namePrefix + key;
                return RankingValue.OrderedChoice(key, label, level, progress);
            });
    }

    internal static void RegisterSummons(ModDeclare mod, Assembly assembly)
    {
        if (assembly == null) return;
        var methods = assembly.GetExportedTypes().Where(type => type.Namespace == "CustomModT002")
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public))
            .ToLookup(method => method.Name);
        var list = methods["GetSummonList"].FirstOrDefault(method =>
            method.GetParameters() is var args && args.Length == 2 && args[0].ParameterType == typeof(Actor));
        var level = FindGetter("GetSummonLevel");
        var levelName = FindGetter("GetSummonLevelName");
        var traits = assembly.GetType("CustomModT002.Traits");
        var data = traits?.GetProperty("AllTraits")?.GetValue(null) ?? traits?.GetField("AllTraits")?.GetValue(null);
        if (list == null || level == null || data is not IEnumerable allTraits) return;

        const string group = "inmny_custommodt002";
        const string icon = "inmny/custommodt002/icon";
        bool logged = false;
        Add(mod, group, "summon_count", icon, RankingFieldKind.Integer, actor => Read(actor, () =>
            RankingValue.Integer(allTraits.Cast<object>().Sum(trait =>
                (list.Invoke(null, new[] { actor, trait }) as IEnumerable)?.Cast<object>().Count() ?? 0))));
        Add(mod, group, "summon_level", icon, RankingFieldKind.Ordinal, actor => Read(actor, () =>
        {
            var args = new object[] { actor };
            var raw = level.Invoke(null, args);
            if (raw == null) return null;
            double order = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            string key = order.ToString("R", CultureInfo.InvariantCulture);
            return RankingValue.OrderedChoice(key, levelName?.Invoke(null, args)?.ToString(), order);
        }));

        MethodInfo FindGetter(string name) => methods[name].FirstOrDefault(method =>
            method.GetParameters() is var args && args.Length == 1 && args[0].ParameterType == typeof(Actor));

        RankingValue Read(Actor actor, Func<RankingValue> read)
        {
            try { return read(); }
            catch (Exception error)
            {
                if (!logged)
                {
                    logged = true;
                    Main.LogWarning($"Could not read {mod.Name} ranking data for actor {actor.data.id}: {error}");
                }
                return null;
            }
        }
    }

    private static void Add(ModDeclare mod, string group, string field, string icon, RankingFieldKind kind,
        Func<Actor, RankingValue> read) =>
        RankingRegistry.RegisterField(BuiltInRankings.Actors, new RankingField<Actor>
        {
            ID = group.ToLowerInvariant() + ":" + field.ToLowerInvariant(), Name = Locale + group + "." + field,
            Kind = kind, IconPath = icon, Read = read, Owner = mod
        });
}
