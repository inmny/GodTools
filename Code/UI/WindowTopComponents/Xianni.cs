#if XIAN_NI_MOD
using System;
using GodTools.UI.Prefabs;
using strings;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    // 仙逆模组境界ID数组
    private static readonly string[] XIANNI_REALM_IDS = {
        "realm_01_qi","realm_02_foundation","realm_03_core","realm_04_nascent",
        "realm_05_deity","realm_06_infantchg","realm_07_wending","realm_08_kuinie",
        "realm_09_jingnie","realm_10_suinie","realm_11_kongnie","realm_12_kongling",
        "realm_13_kongxuan","realm_14_gtianzun","realm_15_half_tatian","realm_16_tatian"
    };

    // 仙逆模组trait group常量
    private const string GROUP_ANCIENT_REALM = "ancient_god_realm";
    private const string GROUP_BEAST_STAGE = "beast_stage";

    private void CreateGrid_XIANNI()
    {
        TitledGrid xianni_keyword_grid = new_keyword_grid("xianni");

        // 战力排序 - 参考仙逆模组的战力计算公式（包含境界系数）
        new_keyword(xianni_keyword_grid, "combat_power", "stats/chartsui",
            (a, b) => CalcXianniCombatPower(a).CompareTo(CalcXianniCombatPower(b)),
            a => $"{CalcXianniCombatPower(a):N0} 战力");

        // 修为值 (long类型)
        new_keyword(xianni_keyword_grid, "xiuwei", "stats/xiuwei", (a, b) =>
        {
            a.data.get("xn.stat.xiuwei", out long a_xiuwei, 0L);
            b.data.get("xn.stat.xiuwei", out long b_xiuwei, 0L);
            return a_xiuwei.CompareTo(b_xiuwei);
        }, a =>
        {
            a.data.get("xn.stat.xiuwei", out long xiuwei, 0L);
            return $"{xiuwei} 修为";
        });

        // 古神之力 (int类型)
        new_keyword(xianni_keyword_grid, "gushen_power", "stats/gushen", (a, b) =>
        {
            a.data.get("xn.stat.gushen_power", out int a_val, 0);
            b.data.get("xn.stat.gushen_power", out int b_val, 0);
            return a_val.CompareTo(b_val);
        }, a =>
        {
            a.data.get("xn.stat.gushen_power", out int val, 0);
            return $"{val} 古神之力";
        });

        // 妖力 (int类型)
        new_keyword(xianni_keyword_grid, "yaoli", "stats/yaoli", (a, b) =>
        {
            a.data.get("xn.stat.yaoli", out int a_val, 0);
            b.data.get("xn.stat.yaoli", out int b_val, 0);
            return a_val.CompareTo(b_val);
        }, a =>
        {
            a.data.get("xn.stat.yaoli", out int val, 0);
            return $"{val} 妖力";
        });

        // 悟性 (int类型)
        new_keyword(xianni_keyword_grid, "wuxin", "stats/wuxin", (a, b) =>
        {
            a.data.get("xn.stat.wuxin", out int a_val, 0);
            b.data.get("xn.stat.wuxin", out int b_val, 0);
            return a_val.CompareTo(b_val);
        }, a =>
        {
            a.data.get("xn.stat.wuxin", out int val, 0);
            return $"{val} 悟性";
        });

        // 气运 (int类型)
        new_keyword(xianni_keyword_grid, "qiyun", "stats/qiyun", (a, b) =>
        {
            a.data.get("xn.stat.qiyun", out int a_val, 0);
            b.data.get("xn.stat.qiyun", out int b_val, 0);
            return a_val.CompareTo(b_val);
        }, a =>
        {
            a.data.get("xn.stat.qiyun", out int val, 0);
            return $"{val} 气运";
        });

        // 心魔 (int类型)
        new_keyword(xianni_keyword_grid, "xinmo", "stats/xinmo", (a, b) =>
        {
            a.data.get("xn.stat.xinmo", out int a_val, 0);
            b.data.get("xn.stat.xinmo", out int b_val, 0);
            return a_val.CompareTo(b_val);
        }, a =>
        {
            a.data.get("xn.stat.xinmo", out int val, 0);
            return $"{val} 心魔";
        });
    }

    /// <summary>
    /// 计算战力分 - 参考仙逆模组的战力计算公式
    /// 战力 = ((攻击力 × 攻速 × (1 + 暴击率 × 暴伤倍数) + 最大生命 × 0.1 + 护甲 × 1.5) × (1 + 击杀 × 0.0025) × (1 + 年龄 × 0.001))^0.8 × 境界系数
    /// </summary>
    private static long CalcXianniCombatPower(Actor a)
    {
        if (a == null) return 0;

        // 读取并做基本夹逼，避免极端值影响排序稳定性
        double dmg   = a.stats[S.damage];           if (dmg   < 0) dmg = 0;   if (dmg   > 2_100_000_000d) dmg   = 2_100_000_000d;
        double aspd  = a.stats[S.attack_speed];     if (aspd  < 0) aspd = 0;
        double cRate = a.stats[S.critical_chance];  if (cRate < 0) cRate = 0; if (cRate > 1) cRate = 1;
        double cMult = a.stats[S.critical_damage_multiplier]; if (cMult < 1) cMult = 1;
        double armor = a.stats[S.armor];            if (armor < 0) armor = 0;
        double hpMax = a.getMaxHealth();            if (hpMax < 0) hpMax = 0; if (hpMax > 2_100_000_000d) hpMax = 2_100_000_000d;

        // 基础战力：攻击力 × 攻速 × (1 + 暴击率 × 暴伤倍数) + 最大生命 × 0.1 + 护甲 × 1.5
        double dps  = dmg * aspd * (1.0 + cRate * cMult);
        double bulk = hpMax * 0.1 + armor * 1.5;
        double basePower = dps + bulk;

        // 获取击杀数和年龄
        int kills = a.data.kills; if (kills < 0) kills = 0;
        int age = a.data.getAge(); if (age < 0) age = 0;
        // 年龄上限为20
        if (age > 20) age = 20;

        // 应用击杀和年龄加成：(1 + 击杀 × 0.0025) × (1 + 年龄 × 0.001)
        double multiplier = (1.0 + kills * 0.0025) * (1.0 + age * 0.001);
        double powered = basePower * multiplier;

        // 应用0.8次方
        if (powered <= 0) return 0;
        double result = Math.Pow(powered, 0.8);

        // 获取境界系数并应用
        double realmCoeff = GetXianniRealmCoefficient(a);
        result = result * realmCoeff;

        // 保留一位小数
        result = Math.Round(result, 1);

        if (result <= 0) return 0;
        if (result >= long.MaxValue) return long.MaxValue;
        return (long)result;
    }

    /// <summary>
    /// 获取境界系数 - 如果没有仙逆模组境界，默认系数0.1
    /// </summary>
    private static double GetXianniRealmCoefficient(Actor a)
    {
        if (a == null) return 0.1;

        var ts = a.getTraits();
        if (ts == null) return 0.1;

        // 先检查古神星数
        int ancientStar = GetXianniAncientStar(a, ts);
        if (ancientStar > 0)
        {
            // 古神星数对应修士境界的系数
            switch (ancientStar)
            {
                case 1: return GetXianniRealmCoeffByIndex(2);  // 1星对标结丹 (index 2)
                case 2: return GetXianniRealmCoeffByIndex(4);  // 2星对标化神 (index 4)
                case 3: return GetXianniRealmCoeffByIndex(6);  // 3星对标问鼎 (index 6)
                case 4: return GetXianniRealmCoeffByIndex(7);  // 4星对标窥涅 (index 7)
                case 5: return GetXianniRealmCoeffByIndex(8);  // 5星对标净涅 (index 8)
                case 6: return GetXianniRealmCoeffByIndex(9);  // 6星对标碎涅 (index 9)
                case 7: return GetXianniRealmCoeffByIndex(10); // 7星对标空涅 (index 10)
                case 8: return GetXianniRealmCoeffByIndex(11); // 8星对标空劫 (index 11)
                case 9: return GetXianniRealmCoeffByIndex(13); // 9星对标天尊 (index 13)
                case 10: return GetXianniRealmCoeffByIndex(14); // 10星对标半步踏天 (index 14)
                default: return 0.1;
            }
        }

        // 再检查妖兽阶数
        int beastStage = GetXianniBeastStage(a, ts);
        if (beastStage > 0)
        {
            // 妖兽阶数对应修士境界的系数（与古神相同）
            switch (beastStage)
            {
                case 1: return GetXianniRealmCoeffByIndex(2);  // 1阶对标结丹 (index 2)
                case 2: return GetXianniRealmCoeffByIndex(4);  // 2阶对标化神 (index 4)
                case 3: return GetXianniRealmCoeffByIndex(6);  // 3阶对标问鼎 (index 6)
                case 4: return GetXianniRealmCoeffByIndex(7);  // 4阶对标窥涅 (index 7)
                case 5: return GetXianniRealmCoeffByIndex(8);  // 5阶对标净涅 (index 8)
                case 6: return GetXianniRealmCoeffByIndex(9);  // 6阶对标碎涅 (index 9)
                case 7: return GetXianniRealmCoeffByIndex(10); // 7阶对标空涅 (index 10)
                case 8: return GetXianniRealmCoeffByIndex(11); // 8阶对标空劫 (index 11)
                case 9: return GetXianniRealmCoeffByIndex(13); // 9阶对标天尊 (index 13)
                case 10: return GetXianniRealmCoeffByIndex(14); // 10阶对标半步踏天 (index 14)
                default: return 0.1;
            }
        }

        // 检查修士境界
        int realmIndex = GetXianniRealmIndex(a, ts);
        if (realmIndex >= 0)
        {
            return GetXianniRealmCoeffByIndex(realmIndex);
        }

        // 无任何境界，默认系数0.1
        return 0.1;
    }

    /// <summary>
    /// 根据境界索引获取系数：使用指数增长 1.3^index，让高境界差距更大
    /// </summary>
    private static double GetXianniRealmCoeffByIndex(int index)
    {
        if (index < 0) return 0.1;
        return Math.Pow(1.3, index);
    }

    /// <summary>
    /// 获取修士境界索引
    /// </summary>
    private static int GetXianniRealmIndex(Actor a, System.Collections.Generic.IReadOnlyCollection<ActorTrait> ts)
    {
        if (a == null || ts == null) return -1;
        int idx = -1;
        foreach (var t in ts)
        {
            if (t == null) continue;
            for (int i = 0; i < XIANNI_REALM_IDS.Length; i++)
            {
                if (t.id == XIANNI_REALM_IDS[i])
                {
                    if (i > idx) idx = i;
                }
            }
        }
        return idx;
    }

    /// <summary>
    /// 获取古神星数
    /// </summary>
    private static int GetXianniAncientStar(Actor a, System.Collections.Generic.IReadOnlyCollection<ActorTrait> ts)
    {
        if (a == null || ts == null) return 0;
        int star = 0;
        foreach (var t in ts)
        {
            if (t == null || t.group_id != GROUP_ANCIENT_REALM) continue;
            // trait id格式: ancient_XX_star，提取XX部分
            if (t.id.Length >= 14 && int.TryParse(t.id.Substring(8, 2), out int n) && n > star)
            {
                star = n;
            }
        }
        return star;
    }

    /// <summary>
    /// 获取妖兽阶数
    /// </summary>
    private static int GetXianniBeastStage(Actor a, System.Collections.Generic.IReadOnlyCollection<ActorTrait> ts)
    {
        if (a == null || ts == null) return 0;
        int stage = 0;
        foreach (var t in ts)
        {
            if (t == null || t.group_id != GROUP_BEAST_STAGE) continue;
            // trait id格式: beast_XX_stage，提取XX部分
            if (t.id.Length >= 13 && int.TryParse(t.id.Substring(6, 2), out int n) && n > stage)
            {
                stage = n;
            }
        }
        return stage;
    }
}
#endif