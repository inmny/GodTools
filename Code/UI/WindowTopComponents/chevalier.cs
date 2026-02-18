#if CHEVALIER
using GodTools.UI.Prefabs;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_CHEVALIER()
    {
        TitledGrid Chevalier_keyword_grid = new_keyword_grid("Chevalier_mod");
        
        new_keyword(Chevalier_keyword_grid, "Chevalier", "Chevalier", (a, b) =>
        {
            a.data.get("wushu.ChevalierNum", out float a_Chevalier, 0f);
            b.data.get("wushu.ChevalierNum", out float b_Chevalier, 0f);
            var res = a_Chevalier.CompareTo(b_Chevalier);
            return res;
        }, a =>
        {
            a.data.get("wushu.ChevalierNum", out float a_Chevalier, 0f);
            return $"{a_Chevalier:F1} 斗气";
        });
        
        new_keyword(Chevalier_keyword_grid, "Comprehension", "Comprehension", (a, b) =>
        {
            a.data.get("ComprehensionNum", out float a_Comprehension, 0f);
            b.data.get("ComprehensionNum", out float b_Comprehension, 0f);
            var res = a_Comprehension.CompareTo(b_Comprehension);
            return res;
        }, a =>
        {
            a.data.get("ComprehensionNum", out float a_Comprehension, 0f);
            return $"{a_Comprehension:F1} 领悟度";
        });
    }
}
#endif