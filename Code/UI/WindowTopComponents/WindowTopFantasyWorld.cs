#if Thefantasyworld
using GodTools.UI.Prefabs;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_FANTASY_WORLD()
    {
        try
        {
            TitledGrid fantasy_keyword_grid = new_keyword_grid("fantasy_mod");
            // 基于经验的排行榜
            new_keyword(fantasy_keyword_grid, "experience", "ui/careerexperience", (a, b) =>
            {
                a.data.get("wushu.careerexperienceNum", out float a_experience, 0f);
                b.data.get("wushu.careerexperienceNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.careerexperienceNum", out float a_experience, 0f);
                return $"{a_experience:F1} 经验";
            });
            new_keyword(fantasy_keyword_grid, "Determination", "ui/Determination", (a, b) =>
            {
                a.data.get("wushu.DeterminationNum", out float a_experience, 0f);
                b.data.get("wushu.DeterminationNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.DeterminationNum", out float a_experience, 0f);
                return $"{a_experience:F1} 意志力";
            });
            new_keyword(fantasy_keyword_grid, "Defydeath", "ui/Defydeath", (a, b) =>
            {
                a.data.get("wushu.DefydeathNum", out float a_experience, 0f);
                b.data.get("wushu.DefydeathNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.DefydeathNum", out float a_experience, 0f);
                return $"{a_experience:F1} 跨越死亡";
            });
            new_keyword(fantasy_keyword_grid, "BattleIntent", "ui/BattleIntent", (a, b) =>
            {
                a.data.get("wushu.BattleIntentNum", out float a_experience, 0f);
                b.data.get("wushu.BattleIntentNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.BattleIntentNum", out float a_experience, 0f);
                return $"{a_experience:F1} 战意";
            });
            new_keyword(fantasy_keyword_grid, "PaladinTough", "ui/PaladinTough", (a, b) =>
            {
                a.data.get("wushu.PaladinToughNum", out float a_experience, 0f);
                b.data.get("wushu.PaladinToughNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.PaladinToughNum", out float a_experience, 0f);
                return $"{a_experience:F1} 坚韧";
            });
            new_keyword(fantasy_keyword_grid, "PhilosophersStone", "ui/PhilosophersStone", (a, b) =>
            {
                a.data.get("wushu.PhilosophersStoneNum", out float a_experience, 0f);
                b.data.get("wushu.PhilosophersStoneNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.PhilosophersStoneNum", out float a_experience, 0f);
                return $"{a_experience:F1} 贤者之石";
            });
            new_keyword(fantasy_keyword_grid, "Anger", "ui/Anger", (a, b) =>
            {
                a.data.get("wushu.AngerNum", out float a_experience, 0f);
                b.data.get("wushu.AngerNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.AngerNum", out float a_experience, 0f);
                return $"{a_experience:F1} 怒意";
            });
            new_keyword(fantasy_keyword_grid, "DodgeEvade", "ui/DodgeEvade", (a, b) =>
            {
                a.data.get("wushu.DodgeEvadeNum", out float a_experience, 0f);
                b.data.get("wushu.DodgeEvadeNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.DodgeEvadeNum", out float a_experience, 0f);
                return $"{a_experience:F1} 闪避";
            });
            new_keyword(fantasy_keyword_grid, "hitthetarget", "ui/hitthetarget", (a, b) =>
            {
                a.data.get("wushu.hitthetargetNum", out float a_experience, 0f);
                b.data.get("wushu.hitthetargetNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.hitthetargetNum", out float a_experience, 0f);
                return $"{a_experience:F1} 命中";
            });
            new_keyword(fantasy_keyword_grid, "MagicApplication", "ui/MagicApplication", (a, b) =>
            {
                a.data.get("wushu.MagicApplicationNum", out float a_experience, 0f);
                b.data.get("wushu.MagicApplicationNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MagicApplicationNum", out float a_experience, 0f);
                return $"{a_experience:F1} 魔法应用";
            });
            new_keyword(fantasy_keyword_grid, "MeditationExperience", "ui/MeditationExperience", (a, b) =>
            {
                a.data.get("wushu.MeditationExperienceNum", out float a_experience, 0f);
                b.data.get("wushu.MeditationExperienceNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MeditationExperienceNum", out float a_experience, 0f);
                return $"{a_experience:F1} 冥想法经验";
            });
            new_keyword(fantasy_keyword_grid, "MeditationInsight", "ui/MeditationInsight", (a, b) =>
            {
                a.data.get("wushu.MeditationInsightNum", out float a_experience, 0f);
                b.data.get("wushu.MeditationInsightNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MeditationInsightNum", out float a_experience, 0f);
                return $"{a_experience:F1} 冥想法悟性";
            });
            new_keyword(fantasy_keyword_grid, "MeditationTier", "ui/MeditationTier", (a, b) =>
            {
                a.data.get("wushu.MeditationTierNum", out float a_experience, 0f);
                b.data.get("wushu.MeditationTierNum", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MeditationTierNum", out float a_experience, 0f);
                return $"{a_experience:F1} 冥想法功法层数";
            });
            new_keyword(fantasy_keyword_grid, "MagicShield", "ui/MagicShield", (a, b) =>
            {
                a.data.get("wushu.MagicShield", out float a_experience, 0f);
                b.data.get("wushu.MagicShield", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MagicShield", out float a_experience, 0f);
                return $"{a_experience:F1} 护盾";
            });
            new_keyword(fantasy_keyword_grid, "FixedPhysicalDamage", "ui/FixedPhysicalDamage", (a, b) =>
            {
                a.data.get("wushu.FixedPhysicalDamage", out float a_experience, 0f);
                b.data.get("wushu.FixedPhysicalDamage", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.FixedPhysicalDamage", out float a_experience, 0f);
                return $"{a_experience:F1} 固定物伤";
            });
            new_keyword(fantasy_keyword_grid, "FixedMagicalDamage", "ui/FixedMagicalDamage", (a, b) =>
            {
                a.data.get("wushu.FixedMagicalDamage", out float a_experience, 0f);
                b.data.get("wushu.FixedMagicalDamage", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.FixedMagicalDamage", out float a_experience, 0f);
                return $"{a_experience:F1} 固定法伤";
            });
            new_keyword(fantasy_keyword_grid, "Restorehealth", "ui/Restorehealth", (a, b) =>
            {
                a.data.get("wushu.Restorehealth", out float a_experience, 0f);
                b.data.get("wushu.Restorehealth", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.Restorehealth", out float a_experience, 0f);
                return $"{a_experience:F1} 回血";
            });
            new_keyword(fantasy_keyword_grid, "MagicReply", "ui/MagicReply", (a, b) =>
            {
                a.data.get("wushu.MagicReply", out float a_experience, 0f);
                b.data.get("wushu.MagicReply", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MagicReply", out float a_experience, 0f);
                return $"{a_experience:F1} 回魔";
            });
            new_keyword(fantasy_keyword_grid, "MagicDamage", "ui/MagicDamage", (a, b) =>
            {
                a.data.get("wushu.MagicDamage", out float a_experience, 0f);
                b.data.get("wushu.MagicDamage", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MagicDamage", out float a_experience, 0f);
                return $"{a_experience:F1} 法伤";
            });
            new_keyword(fantasy_keyword_grid, "MagicResistance", "ui/MagicResistance", (a, b) =>
            {
                a.data.get("wushu.MagicResistance", out float a_experience, 0f);
                b.data.get("wushu.MagicResistance", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.MagicResistance", out float a_experience, 0f);
                return $"{a_experience:F1} 法抗";
            });
            new_keyword(fantasy_keyword_grid, "PhysicalDamage", "ui/PhysicalDamage", (a, b) =>
            {
                a.data.get("wushu.PhysicalDamage", out float a_experience, 0f);
                b.data.get("wushu.PhysicalDamage", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.PhysicalDamage", out float a_experience, 0f);
                return $"{a_experience:F1} 物伤";
            });
            new_keyword(fantasy_keyword_grid, "PhysicalDefense", "ui/PhysicalDefense", (a, b) =>
            {
                a.data.get("wushu.PhysicalDefense", out float a_experience, 0f);
                b.data.get("wushu.PhysicalDefense", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.PhysicalDefense", out float a_experience, 0f);
                return $"{a_experience:F1} 物抗";
            });
            new_keyword(fantasy_keyword_grid, "LegendaryStatus", "ui/LegendaryStatus", (a, b) =>
            {
                a.data.get("wushu.LegendaryStatus", out float a_experience, 0f);
                b.data.get("wushu.LegendaryStatus", out float b_experience, 0f);
                var res = a_experience.CompareTo(b_experience);
                return res;
            }, a =>
            {
                a.data.get("wushu.LegendaryStatus", out float a_experience, 0f);
                return $"{a_experience:F1} 传奇";
            });


        }
        catch (System.Exception)
        {
            // 发生错误时静默处理，避免影响其他功能
        }
    }
}

#endif