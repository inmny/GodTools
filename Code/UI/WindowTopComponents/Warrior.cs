#if WARRIOR
using GodTools.UI.Prefabs;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_WARRIOR()
    {
        TitledGrid warrior_keyword_grid = new_keyword_grid("warrior_mod");
        
        new_keyword(warrior_keyword_grid, "warrior", "Warrior", (a, b) =>
        {
            a.data.get("wushu.warriorNum", out float a_warrior, 0f);
            b.data.get("wushu.warriorNum", out float b_warrior, 0f);
            var res = a_warrior.CompareTo(b_warrior);
            return res;
        }, a =>
        {
            a.data.get("wushu.warriorNum", out float a_warrior, 0f);
            return $"{a_warrior:F1} 气血";
        });
        
        new_keyword(warrior_keyword_grid, "true_gang", "TrueGang", (a, b) =>
        {
            a.data.get("wushu.trueGangNum", out float a_true_gang, 0f);
            b.data.get("wushu.trueGangNum", out float b_true_gang, 0f);
            var res = a_true_gang.CompareTo(b_true_gang);
            return res;
        }, a =>
        {
            a.data.get("wushu.trueGangNum", out float a_true_gang, 0f);
            return $"{a_true_gang:F1} 真罡";
        });

        new_keyword(warrior_keyword_grid, "pattern", "Pattern", (a, b) =>
        {
            a.data.get("wushu.patternNum", out float a_pattern, 0f);
            b.data.get("wushu.patternNum", out float b_pattern, 0f);
            var res = a_pattern.CompareTo(b_pattern);
            return res;
        }, a =>
        {
            a.data.get("wushu.patternNum", out float a_pattern, 0f);
            return $"{a_pattern:F1} 阵纹";
        });
    }
}
#endif