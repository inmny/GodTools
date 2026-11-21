using System;
using System.Linq;
using GodTools.Features;
using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings
{
    private const string PeakTooltip = "inmny.godtools.ranking.peak_tooltip";
    private static readonly Color PeakMuted = new Color32(184, 191, 166, 255);
    private static readonly Color PeakAvailable = new Color32(185, 223, 148, 255);
    private static readonly Color PeakWarning = new Color32(255, 198, 126, 255);

    private sealed class PeakTooltipData : TooltipData
    {
        internal RankingPeak Record;
        internal Sprite Portrait;
    }

    private static void RegisterPeakTooltip() => AssetManager.tooltips.add(new TooltipAsset
    {
        id = PeakTooltip,
        callback = (tip, _, data) => DrawPeakTooltip(tip, (PeakTooltipData)data)
    });

    private static void ShowPeakTooltip(GameObject target, RankingRow row) => Tooltip.show(target, PeakTooltip,
        new PeakTooltipData { Record = row.Peak, Portrait = row.SavedPortrait ?? row.Icon });

    private static void HidePeakTooltip() => Tooltip.findActive(tip => tip.asset.id == PeakTooltip)?.hide();

    private static string PeakStatus(RankingPeak record) => record.Creature?.ActorData == null ? L("peak.score_only") :
        CreatureSavedList.CanPlace(record.Creature) ? L("peak.ready") : L("peak.missing_actor");

    private static void DrawPeakTooltip(Tooltip tip, PeakTooltipData data)
    {
        var record = data.Record;
        var creature = record.Creature;
        var unit = creature?.ActorData;
        // 保留原版提示框的背景和定位，标题与正文使用可换行的布局。
        tip.topGraphics.transform.parent.gameObject.SetActive(false);
        tip.name.GetComponent<LocalizedText>().checkSpecialLanguages();
        tip.GetComponent<VerticalLayoutGroup>().spacing = 0;
        if (tip.transform.Find("PeakBackdrop") == null)
        {
            // 加深原版半透明底，悬停在文字和图标密集的排行榜上时更容易读清。
            var backdrop = Rect("PeakBackdrop", tip.transform, 0, 0, 0, 0);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.SetAsFirstSibling();
            backdrop.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = backdrop.gameObject.AddComponent<Image>();
            image.sprite = tip.background.sprite;
            image.type = tip.background.type;
            image.raycastTarget = false;
        }
        if (tip.transform.Find("PeakTooltipContent") is Transform previous)
        {
            previous.name = "";
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
        }
        float scale = CanvasMain.instance.canvas_tooltip.scaleFactor * data.tooltip_scale;
        float width = Mathf.Clamp(Screen.width / scale - 18, 130, unit == null ? 164 : 187);
        SetWidth(tip.transform, width);
        var content = PeakStack(tip.transform, "PeakTooltipContent", 0);
        SetWidth(content, width);
        var font = tip.name.font;
        var header = PeakLine(content, "PeakHeader", 6);
        header.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(9, 9, 5, 5);
        var headerBackground = header.gameObject.AddComponent<Image>();
        headerBackground.sprite = tip.tooltipTopGraphicsFlat;
        headerBackground.type = Image.Type.Sliced;
        headerBackground.raycastTarget = false;
        if (data.Portrait != null)
        {
            var portrait = PeakImage(header, data.Portrait, 24, 24);
            portrait.name = "PeakPortrait";
        }
        var headings = PeakStack(header, "PeakHeadings", 1);
        headings.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        PeakText(headings, "PeakName", record.ActorName, font, 12, HeadingTint);
        PeakText(headings, "PeakTime", string.Format(L("peak.time_score"), Date.getYear(record.Time),
            RankingValue.FormatNumber(record.Score)), font, 9, PeakMuted);

        var body = PeakStack(content, "PeakSavedContents", 4);
        body.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(9, 9, 4, 1);
        bool missing = false;
        if (unit != null)
        {
            const int labelWidth = 39;
            float detailWidth = width - 18 - labelWidth - 4;
            int iconLimit = Mathf.Max(1, Mathf.FloorToInt((detailWidth - 19) / 16));
            var unknown = Icon("iconQuestionMark");
            Transform Section(string key)
            {
                var line = PeakLine(body, "PeakSection-" + key, 4, TextAnchor.UpperLeft);
                var label = PeakText(line, "PeakLabel", L("peak." + key), font, 9, PeakMuted);
                PeakSize(label.transform, labelWidth, 14);
                var detail = PeakStack(line, "PeakDetails", 2);
                detail.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                return detail;
            }

            var traits = unit.saved_traits?.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal)
                .Select(id => AssetManager.traits.get(id)).ToArray();
            if (traits is { Length: > 0 })
            {
                var row = PeakLine(Section("traits"), "PeakTraitIcons", 2);
                foreach (var trait in traits.Take(iconLimit)) PeakImage(row, trait?.getSprite() ?? unknown, 14, 14);
                PeakOverflow(row, traits.Length - iconLimit, font);
                missing |= traits.Any(trait => trait == null);
            }

            var equipment = creature.ItemDatas?.Where(item => item != null && unit.saved_items?.Contains(item.id) == true)
                .Select(item => (Data: item, Asset: AssetManager.items.get(item.asset_id)))
                .OrderByDescending(item => item.Asset?.equipment_type == EquipmentType.Weapon).ToArray();
            if (equipment is { Length: > 0 })
            {
                var section = Section("gear");
                var row = PeakLine(section, "PeakEquipmentIcons", 2);
                foreach (var (item, asset) in equipment.Take(iconLimit))
                {
                    var icon = PeakImage(row, asset?.getSprite() ?? unknown, 14, 14);
                    var color = PeakItemColor(item, asset);
                    if (color != Color.white)
                    {
                        var outline = icon.gameObject.AddComponent<Outline>();
                        outline.effectColor = color;
                        outline.effectDistance = new Vector2(0.65f, 0.65f);
                    }
                }
                var (weapon, weaponAsset) = equipment[0];
                if (weaponAsset?.equipment_type == EquipmentType.Weapon)
                {
                    ItemTools.getTooltipTitle(weaponAsset, out string type, out string material);
                    PeakText(section, "PeakWeapon", string.IsNullOrEmpty(weapon.name) ? material + type : weapon.name,
                        font, 9, PeakItemColor(weapon, weaponAsset));
                }
                PeakOverflow(row, equipment.Length - iconLimit, font);
                missing |= equipment.Any(item => item.Asset == null ||
                    item.Data.modifiers.Any(id => AssetManager.items_modifiers.get(id) == null));
            }
            missing |= (unit.saved_items?.Distinct().Count() ?? 0) > (equipment?.Length ?? 0);

            var resources = unit.inventory?.getResources()?.Values.Where(resource => resource.amount > 0)
                .OrderBy(resource => AssetManager.resources.get(resource.id)?.order ?? int.MaxValue)
                .ThenBy(resource => resource.id, StringComparer.Ordinal).ToArray();
            if (resources is { Length: > 0 })
            {
                var section = Section("bag");
                foreach (var resource in resources.Take(4))
                {
                    var asset = AssetManager.resources.get(resource.id);
                    var row = PeakLine(section, "PeakResource", 3);
                    PeakImage(row, asset?.getSpriteIcon() ?? unknown, 14, 14);
                    PeakText(row, "PeakResourceName", Name(resource.id), font, 9, Color.white)
                        .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                    PeakText(row, "PeakResourceAmount", $"×{resource.amount:N0}", font, 9, PeakAvailable);
                }
                PeakOverflow(section, resources.Length - 4, font);
                missing |= resources.Any(resource => !AssetManager.resources.has(resource.id));
            }

            Transform affiliations = null;
            void Affiliation(MetaObjectData saved, string key, GenericBannerLibrary banners, ColorLibrary colors,
                int background, int symbol, Sprite species = null)
            {
                if (saved == null) return;
                affiliations ??= Section("affiliations");
                var line = PeakLine(affiliations, "PeakAffiliation-" + key, 3);
                var color = colors.getColorByIndex(Math.Max(0, saved.color_id));
                var flag = PeakImage(line, banners.getSpriteBackground(Math.Max(0, background)), 15, 15);
                flag.color = color.getColorMainSecond();
                var emblem = IconImage(flag.transform, 7.5f, species ?? (symbol < 0 ? unknown : banners.getSpriteIcon(symbol)));
                emblem.rectTransform.sizeDelta = new Vector2(10, 10);
                if (species == null) emblem.color = color.getColorBanner();
                PeakText(line, "PeakAffiliationName", $"{Name(key)}  {saved.name}", font, 9, color.getColorText())
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            }
            if (creature.CultureData is { } culture)
                Affiliation(culture, "culture", AssetManager.culture_banners_library, AssetManager.culture_colors_library,
                    culture.banner_decor_id, culture.banner_element_id);
            if (creature.ClanData is { } clan)
                Affiliation(clan, "clan", AssetManager.clan_banners_library, AssetManager.clan_colors_library,
                    clan.banner_background_id, clan.banner_icon_id);
            if (creature.LanguageData is { } language)
                Affiliation(language, "language", AssetManager.language_banners_library, AssetManager.languages_colors_library,
                    language.banner_background_id, language.banner_icon_id);
            if (creature.SubspeciesData is { } subspecies)
                Affiliation(subspecies, "subspecies", AssetManager.subspecies_banners_library, AssetManager.subspecies_colors_library,
                    subspecies.banner_background_id, -1, AssetManager.actor_library.get(subspecies.species_id)?.getSpriteIcon());

            var modules = creature.ModData?.Where(part => part.Value?.Data != null).OrderBy(part => part.Key, StringComparer.Ordinal).ToArray();
            if (modules is { Length: > 0 })
            {
                var section = Section("mods");
                PeakText(section, "PeakModCount", string.Format(L("peak.mod_count"), modules.Length), font, 9, Color.white);
                foreach (var (id, payload) in modules)
                {
                    if (section.childCount >= 3) break;
                    var summary = CreatureSaveExtensions.Describe(id, payload);
                    if (string.IsNullOrWhiteSpace(summary)) continue;
                    summary = string.Join(" ", summary.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                    if (summary.Length > 90) summary = summary.Substring(0, 89) + "…";
                    PeakText(section, "PeakModSummary", summary, font, 9, Color.white);
                }
                missing |= modules.Any(part => !CreatureSaveExtensions.IsRegistered(part.Key));
            }
        }

        bool canPlace = CreatureSavedList.CanPlace(creature);
        if (body.childCount > 0)
        {
            var rule = Rect("PeakSeparator", body, 0, 1, 0, 0);
            rule.gameObject.AddComponent<LayoutElement>().preferredHeight = 1;
            var line = rule.gameObject.AddComponent<Image>();
            line.color = new Color(0.66f, 0.68f, 0.54f, 0.32f);
            line.raycastTarget = false;
        }
        PeakText(body, "PeakStatus", PeakStatus(record), font, 9, canPlace ? PeakAvailable : unit == null ? PeakMuted : PeakWarning);
        if (canPlace && missing) PeakText(body, "PeakUnavailable", L("peak.partial"), font, 9, PeakWarning);
    }

    private static Color PeakItemColor(ItemData item, EquipmentAsset asset)
    {
        var rarity = asset?.quality ?? Rarity.R0_Normal;
        foreach (string id in item.modifiers)
            if (AssetManager.items_modifiers.get(id) is { } modifier && modifier.quality > rarity) rarity = modifier.quality;
        return rarity.getRarityColor();
    }

    private static void PeakOverflow(Transform parent, int count, Font font)
    {
        if (count > 0) PeakText(parent, "PeakOverflow", $"+{count}", font, 9, PeakMuted);
    }

    private static RectTransform PeakStack(Transform parent, string name, float gap)
    {
        var rect = Rect(name, parent, 0, 0, 0, 0);
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = gap;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = layout.childControlHeight = true;
        return rect;
    }

    private static RectTransform PeakLine(Transform parent, string name, float gap, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var rect = Rect(name, parent, 0, 0, 0, 0);
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = gap;
        layout.childAlignment = alignment;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return rect;
    }

    private static void PeakSize(Transform item, float width, float height)
    {
        var element = item.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = width;
        element.minHeight = element.preferredHeight = height;
    }

    private static Image PeakImage(Transform parent, Sprite sprite, int width, int height)
    {
        var rect = Rect("Icon", parent, width, height, 0, 0);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        PeakSize(rect, width, height);
        return image;
    }

    private static Text PeakText(Transform parent, string name, string value, Font font, int size, Color tint)
    {
        var text = Rect(name, parent, 0, 0, 0, 0).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = tint;
        text.text = value;
        text.alignment = TextAnchor.UpperLeft;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
