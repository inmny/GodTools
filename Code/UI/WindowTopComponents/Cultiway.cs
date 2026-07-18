#if CULTIWAY
using Cultiway.Content;
using Cultiway.Content.Components;
using Cultiway.Content.Extensions;
using Cultiway.Core;
using Cultiway.Core.Components;
using Cultiway.Utils.Extension;
using GodTools.UI.Prefabs;
using NeoModLoader.api.attributes;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_CULTIWAY()
    {
        TitledGrid cultiway_keyword_grid = new_keyword_grid("cultiway");
        new_keyword(cultiway_keyword_grid, "xian_level", "cultiway/icons/iconCultivation", [Hotfixable](a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            return a_extend.GetCultisysLevelForSort<Xian>().CompareTo(b_extend.GetCultisysLevelForSort<Xian>());
        }, a =>
        {
            var ae = a.GetExtend();
            if (ae.HasCultisys<Xian>())
            {
                var xian = ae.GetCultisys<Xian>();
                return $"{Cultisyses.Xian.GetLevelName(xian.CurrLevel)}";
            }

            return "凡人";
        });
        new_keyword(cultiway_keyword_grid, "magic_level", "cultiway/icons/iconMagic", [Hotfixable](a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            return a_extend.GetCultisysLevelForSort<Magic>().CompareTo(b_extend.GetCultisysLevelForSort<Magic>());
        }, a =>
        {
            var ae = a.GetExtend();
            if (ae.HasCultisys<Magic>())
            {
                var magic = ae.GetCultisys<Magic>();
                return $"{Cultisyses.Magic.GetLevelName(magic.CurrLevel)}";
            }

            return "无魔法";
        });
        new_keyword(cultiway_keyword_grid, "knight_level", "cultiway/icons/iconKnight", [Hotfixable](a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            return a_extend.GetCultisysLevelForSort<Knight>().CompareTo(b_extend.GetCultisysLevelForSort<Knight>());
        }, a =>
        {
            var ae = a.GetExtend();
            if (ae.HasCultisys<Knight>())
            {
                var knight = ae.GetCultisys<Knight>();
                return $"{Cultisyses.Knight.GetLevelName(knight.CurrLevel)}";
            }

            return "无骑士";
        });
        new_keyword(cultiway_keyword_grid, "xian_talent", "cultiway/icons/iconElement", (a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            var a_has = a_extend.HasElementRoot();
            var b_has = b_extend.HasElementRoot();
            if (!a_has && !b_has) return 0;
            if (!a_has) return -1;
            if (!b_has) return 1;
            return a_extend.GetElementRoot().GetStrength().CompareTo(b_extend.GetElementRoot().GetStrength());
        }, a =>
        {
            var ae = a.GetExtend();
            if (ae.HasElementRoot())
            {
                var er = ae.GetElementRoot();
                return $"{(int)(er.GetStrength() * 100)} 修仙天赋";
            }

            return "无天赋";
        });
        new_keyword(cultiway_keyword_grid, "spell_count", "cultiway/icons/iconWakan", (a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            return a_extend.all_skills.Count.CompareTo(b_extend.all_skills.Count);
        }, a =>
        {
            var ae = a.GetExtend();
            return $"{ae.all_skills.Count} 法术数量";
        });
        new_keyword(cultiway_keyword_grid, "main_cultibook_level", "books/custom_book_covers/cultibook/01", (a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            var a_level = (int)(a_extend.GetMainCultibook()?.Level ?? 0);
            var b_level = (int)(b_extend.GetMainCultibook()?.Level ?? 0);
            return a_level.CompareTo(b_level);
        }, a =>
        {
            var ae = a.GetExtend();
            return $"{ae.GetMainCultibook()?.Level.GetName() ?? "无功法"}";
        });
        new_keyword(cultiway_keyword_grid, "apprentice_count", "cultiway/icons/iconMasterApprentice", (a, b) =>
        {
            ActorExtend a_extend = a.GetExtend();
            ActorExtend b_extend = b.GetExtend();
            return GetApprenticeCount(a_extend).CompareTo(GetApprenticeCount(b_extend));
        }, a =>
        {
            var ae = a.GetExtend();
            return $"{GetApprenticeCount(ae)} 徒弟数量";
        });
        TitledGrid cultiway_filter_grid = new_filter_grid("cultisys");
        new_filter(cultiway_filter_grid, "xian", "cultiway/icons/iconCultivation",
            a => a.GetExtend().HasCultisys<Xian>());
        new_filter(cultiway_filter_grid, "magic", "cultiway/icons/iconMagic",
            a => a.GetExtend().HasCultisys<Magic>());
        new_filter(cultiway_filter_grid, "knight", "cultiway/icons/iconKnight",
            a => a.GetExtend().HasCultisys<Knight>());
    }

    private static int GetApprenticeCount(ActorExtend pActorExtend)
    {
        return pActorExtend.TryGetComponent(out MasterApprenticeState state) ? state.ApprenticeCount : 0;
    }
}
#endif
