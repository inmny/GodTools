
using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Abstract;
using GodTools.UI.Prefabs;
using GodTools.UI.WindowTopComponents;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.Game.extensions;
using strings;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace

namespace GodTools.UI;

public partial class WindowTops
{
    private void CreateGrid_VANILLA()
    {
        /*
        TitledGrid race_filter_grid = new_filter_grid("race");
        AssetManager.actor_library.ForEach<ActorAsset, ActorAssetLibrary>(actor_asset =>
        {
            if (!LM.Has(actor_asset.name_locale) ||
                string.IsNullOrEmpty(actor_asset.path_icon) || SpriteTextureLoader.getSprite(actor_asset.path_icon) == null) return;
            var local_race_id = actor_asset.id;
            try
            {
                LM.AddToCurrentLocale($"{C.mod_prefix}.ui.filter.race.{actor_asset.name_locale}",
                    LM.Get(actor_asset.name_locale));
                new_filter(race_filter_grid, actor_asset.name_locale, actor_asset.path_icon, a => a.asset.id == local_race_id);
            }
            catch (Exception)
            {
                // ignored
            }
        });*/
        asset_filter_grid = new_filter_grid("asset");
        kingdom_filter_grid = new_filter_grid("kingdom");
        trait_filter_grid = new_filter_grid("trait");
        asset_filter_grid.Grid.gameObject.SetActive(false);
        kingdom_filter_grid.Grid.gameObject.SetActive(false);
        trait_filter_grid.Grid.gameObject.SetActive(false);
        filter_button_in_grid_pool_dict[kingdom_filter_grid] = new MonoObjPool<FilterButtonInGrid>(
            FilterButtonInGrid.Prefab, kingdom_filter_grid.Grid.transform,
            obj =>
            {
                var icon_part = new GameObject("Icon", typeof(Image));
                icon_part.transform.SetParent(obj.transform);
                icon_part.transform.localPosition = Vector3.zero;
                icon_part.transform.localScale = Vector3.one;
                icon_part.GetComponent<Image>().raycastTarget = false;
            });
        
        var profession_filter_grid = new_filter_grid("profession");
        //new_filter(profession_filter_grid, "baby", "ui/icons/worldrules/icon_lastofus", x=>x.isProfession(UnitProfession.Nothing));
        new_filter(profession_filter_grid, "unit", "ui/icons/iconPopulation", x=>x.isProfession(UnitProfession.Unit));
        new_filter(profession_filter_grid, "warrior", "ui/icons/items/icon_sword_adamantine", x=>x.isProfession(UnitProfession.Warrior));
        new_filter(profession_filter_grid, "king", "ui/icons/achievements/achievements_theKing", x=>x.isProfession(UnitProfession.King));
        new_filter(profession_filter_grid, "leader", "ui/icons/iconLeaders", x=>x.isProfession(UnitProfession.Leader));

        TitledGrid vanilla_keyword_grid = new_keyword_grid("vanilla");
        new_keyword(vanilla_keyword_grid, "level", "ui/icons/iconLevels", (a, b) =>
        {
            var res = a.data.level.CompareTo(b.data.level);

            return res;
        }, a => $"{a.data.level} 级");
        new_keyword(vanilla_keyword_grid, "kills", "ui/icons/iconSkulls",
            (a, b) => a.data.kills.CompareTo(b.data.kills), a=> $"{a.data.kills} 击杀");
        new_keyword(vanilla_keyword_grid, "birth", "ui/icons/iconAge",
            (a, b) => a.data.getAge().CompareTo(b.data.getAge()), a => $"{a.data.getAge()} 岁");
        new_keyword(vanilla_keyword_grid, "max_health", "ui/icons/iconHealth",
            (a, b) => a.stats[S.health].CompareTo(b.stats[S.health]), a => $"{a.stats[S.health]} 最大生命");
        new_keyword(vanilla_keyword_grid, "damage", "ui/icons/iconDamage",
            (a, b) => a.stats[S.damage].CompareTo(b.stats[S.damage]), a => $"{a.stats[S.damage]} 攻击");
        new_keyword(vanilla_keyword_grid, "births", "ui/icons/iconBirths",
            (a, b) => a.data.births.CompareTo(b.data.births), a => $"{a.data.births} 生育孩子数量");

    }
    private TitledGrid asset_filter_grid;
    private TitledGrid trait_filter_grid;
    private TitledGrid kingdom_filter_grid;
    [Hotfixable]
    private void CheckDynamicGrid_VANILLA()
    {
        if (filter_button_in_grid_pool_dict.TryGetValue(asset_filter_grid, out var pool))
        {
            pool.Clear();
        }
        if (filter_button_in_grid_pool_dict.TryGetValue(kingdom_filter_grid, out pool))
        {
            pool.Clear();
        }
        if (filter_button_in_grid_pool_dict.TryGetValue(trait_filter_grid, out pool))
        {
            pool.Clear();
        }
        
        var asset_set = new HashSet<string>();
        var kingdom_set = new HashSet<long>();
        var trait_set = new HashSet<string>();
        foreach (var actor in World.world.units.getSimpleList())
        {
            if (actor != null && actor.data != null && actor.isAlive() && actor.asset.can_be_inspected)
            {
                asset_set.Add(actor.asset.id);
                if (actor.kingdom != null)
                    kingdom_set.Add(actor.kingdom.data.id);
                
                trait_set.UnionWith(actor.traits.Select(x => x.id));
            }
        }
        
        foreach (var asset_id in asset_set)
        {
            var asset = AssetManager.actor_library.get(asset_id);
            if (asset.id.StartsWith("_")) continue;
            var icon_path = string.IsNullOrEmpty(asset.icon) || SpriteTextureLoader.getSprite($"ui/icons/{asset.icon}") == null ?
                "ui/icons/iconQuestionMark" : $"ui/icons/{asset.icon}";
            LM.AddToCurrentLocale($"{C.mod_prefix}.ui.filter.asset.{asset.id}",
                GetLocalizedTextOrFallback(asset.getLocaleID(), asset.name_locale, asset.id));
            new_filter(asset_filter_grid, asset.id, icon_path, a => a.asset.id == asset_id);
        }

        foreach (var trait_id in trait_set)
        {
            var asset = AssetManager.traits.get(trait_id);
            LM.AddToCurrentLocale($"{C.mod_prefix}.ui.filter.trait.{trait_id}",
                GetLocalizedTextOrFallback(asset.getLocaleID(), asset.id));
            new_filter(trait_filter_grid, asset.id, asset.path_icon, a => a.hasTrait(trait_id));
        }

        foreach (var kingdom_id in kingdom_set)
        {
            if (World.world.kingdoms_wild.dict.ContainsKey(kingdom_id)) continue;
            var kingdom = World.world.kingdoms.get(kingdom_id);
            LM.AddToCurrentLocale($"{C.mod_prefix}.ui.filter.kingdom.{kingdom_id}",
                kingdom.data.name);
            var filter_button_in_grid = new_filter(kingdom_filter_grid,
                new FilterSetting(kingdom_id.ToString(), kingdom.getElementBackground(), a => a.kingdom.data.id == kingdom_id)
                {
                    InnerColor = kingdom.kingdomColor.getColorBanner(),
                    InnerIcon = kingdom.getElementIcon(),
                    IconColor = kingdom.kingdomColor.getColorMainSecond()
                });

            var sub_icon = filter_button_in_grid.transform.Find("Icon").GetComponent<Image>();
            sub_icon.sprite = kingdom.getElementIcon();
            sub_icon.color = kingdom.kingdomColor.getColorBanner();
            sub_icon.rectTransform.sizeDelta = kingdom_filter_grid.Grid.cellSize;
        }
    }

    private static string GetLocalizedTextOrFallback(string locale_key, params string[] fallback_values)
    {
        if (!string.IsNullOrEmpty(locale_key) && LocalizedTextManager.stringExists(locale_key))
        {
            return LocalizedTextManager.getText(locale_key);
        }

        foreach (var fallback in fallback_values)
        {
            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }
        }

        return locale_key ?? string.Empty;
    }
}
