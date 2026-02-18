#if XIAN_TU
using GodTools.UI.Prefabs;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_XIAN_TU()
    {
        TitledGrid xian_tu_keyword_grid = new_keyword_grid("xian_tu_mod");
        
        new_keyword(xian_tu_keyword_grid, "xian_tu", "XianTu", (a, b) =>
        {
            a.data.get("xiantu.xianTuNum", out float a_xian_tu, 0f);
            b.data.get("xiantu.xianTuNum", out float b_xian_tu, 0f);
            var res = a_xian_tu.CompareTo(b_xian_tu);
            return res;
        }, a =>
        {
            a.data.get("xiantu.xianTuNum", out float a_xian_tu, 0f);
            return $"{a_xian_tu:F1} 真元";
        });
        
        new_keyword(xian_tu_keyword_grid, "qi_xue", "QiXue", (a, b) =>
        {
            a.data.get("xiantu.qiXueNum", out float a_qi_xue, 0f);
            b.data.get("xiantu.qiXueNum", out float b_qi_xue, 0f);
            var res = a_qi_xue.CompareTo(b_qi_xue);
            return res;
        }, a =>
        {
            a.data.get("xiantu.qiXueNum", out float a_qi_xue, 0f);
            return $"{a_qi_xue:F1} 气血";
        });
        
        new_keyword(xian_tu_keyword_grid, "wu_xing", "WuXing", (a, b) =>
        {
            a.data.get("xiantu.wuXingNum", out float a_wu_xing, 0f);
            b.data.get("xiantu.wuXingNum", out float b_wu_xing, 0f);
            var res = a_wu_xing.CompareTo(b_wu_xing);
            return res;
        }, a =>
        {
            a.data.get("xiantu.wuXingNum", out float a_wu_xing, 0f);
            return $"{a_wu_xing:F1} 悟性";
        });
    }
}
#endif