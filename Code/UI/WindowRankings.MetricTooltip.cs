using System;
using System.Linq;
using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings
{
    private const string MetricTooltip = "inmny.godtools.ranking.metric_tooltip";

    private static bool HasMetricDetails(RankingValue value) =>
        value?.Parts?.Count > 0 || !string.IsNullOrWhiteSpace(value?.Details);

    private static void AddMetricTooltip(Button button, Func<string> title, Func<RankingValue> read)
    {
        var tip = button.gameObject.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(() =>
        {
            var value = read();
            if (!HasMetricDetails(value)) return;
            Tooltip.show(tip.gameObject, MetricTooltip, new TooltipData
            {
                tip_name = $"{title()}   {value.Label}".TrimEnd(),
                tip_description = value.Parts == null ? "" : string.Join("\n",
                    value.Parts.Select(part => $"{Name(part.Key)}    {part.Value:0.##}")),
                tip_description_2 = value.Details ?? ""
            });
        }, false);
    }

    private static void HideMetricTooltip() =>
        Tooltip.findActive(tip => tip.transform.name == MetricTooltip)?.hide();

    private static void RegisterMetricTooltip()
    {
        AssetManager.tooltips.add(new TooltipAsset
        {
            id = MetricTooltip,
            callback = (tip, _, data) =>
            {
                Format(tip.name, 11, HeadingTint);
                Format(tip.description, 10, Color.white);
                Format(tip.description_2, 9, new Color32(221, 224, 199, 255));
                tip.name.text = data.tip_name;
                tip.setDescription(data.tip_description);
                tip.setBottomDescription(data.tip_description_2);

                const float padding = 18;
                float wanted = Mathf.Max(Measure(tip.name), Measure(tip.description), Measure(tip.description_2)) + padding;
                float scale = CanvasMain.instance.canvas_tooltip.scaleFactor * data.tooltip_scale;
                float maxWidth = Mathf.Min(215, Mathf.Max(1, Screen.width / scale - 20));
                float width = Mathf.Clamp(Mathf.Ceil(wanted), Mathf.Min(105, maxWidth), maxWidth);
                float bodyWidth = Mathf.Max(1, width - padding);
                SetWidth(tip.transform, width);
                SetWidth(tip.topGraphics.transform, width);
                SetWidth(tip.name.transform, bodyWidth);
                SetWidth(tip.description.transform.parent, bodyWidth);
                SetWidth(tip.description.transform, bodyWidth);
                SetWidth(tip.description_2.transform.parent, bodyWidth);
                SetWidth(tip.description_2.transform, bodyWidth);
            }
        });

        static void Format(Text label, int size, Color color)
        {
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = false;
            label.resizeTextForBestFit = false;
            label.lineSpacing = 1.12f;
        }

        static float Measure(Text label)
        {
            var settings = label.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label.cachedTextGeneratorForLayout.GetPreferredWidth(label.text, settings) / label.pixelsPerUnit;
        }
    }
}
