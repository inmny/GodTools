using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

internal static class NativeListPane
{
    internal const float ScrollbarGutter = 9;

    internal static RectTransform Configure(ScrollRect scroll, Transform parent, float width, float height, float x, float y)
    {
        var layout = (RectTransform)scroll.transform;
        if (layout.parent != parent) layout.SetParent(parent, false);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(0.5f, 0.5f);
        layout.sizeDelta = new Vector2(width + ScrollbarGutter, height);
        layout.anchoredPosition = new Vector2(x, y);
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        ConfigureScrollbar(scroll);
        scroll.viewport.anchorMin = Vector2.zero;
        scroll.viewport.anchorMax = Vector2.one;
        scroll.viewport.offsetMin = Vector2.zero;
        scroll.viewport.offsetMax = new Vector2(-ScrollbarGutter, 0);
        var content = scroll.content;
        if (content.TryGetComponent<LayoutGroup>(out var group)) group.enabled = false;
        if (content.TryGetComponent<ContentSizeFitter>(out var fitter)) fitter.enabled = false;
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
        content.anchoredPosition = Vector2.zero;
        return content;
    }

    private static void ConfigureScrollbar(ScrollRect scroll)
    {
        var bar = scroll.verticalScrollbar;
        if (bar == null) return;
        var track = (RectTransform)bar.transform;
        var oldMask = track.parent;
        if (oldMask != scroll.transform)
        {
            track.SetParent(scroll.transform, false);
            if (oldMask.childCount == 0) oldMask.gameObject.SetActive(false);
        }
        track.SetAsLastSibling();
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(-ScrollbarGutter, 4);
        track.offsetMax = new Vector2(-1, -4);

        var oldBackground = track.Find("Background");
        if (oldBackground != null) oldBackground.gameObject.SetActive(false);
        var groove = track.GetComponent<Image>() ?? track.gameObject.AddComponent<Image>();
        groove.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        groove.type = Image.Type.Sliced;
        groove.pixelsPerUnitMultiplier = 2.5f;
        groove.color = new Color32(36, 41, 34, 255);
        groove.raycastTarget = true;

        var slide = (RectTransform)bar.handleRect.parent;
        slide.anchorMin = Vector2.zero;
        slide.anchorMax = Vector2.one;
        slide.offsetMin = Vector2.one;
        slide.offsetMax = -Vector2.one;
        bar.handleRect.offsetMin = bar.handleRect.offsetMax = Vector2.zero;

        var thumb = bar.image;
        thumb.sprite = groove.sprite;
        thumb.type = Image.Type.Sliced;
        thumb.pixelsPerUnitMultiplier = 2f;
        thumb.color = Color.white;
        bar.transition = Selectable.Transition.ColorTint;
        var colors = bar.colors;
        colors.normalColor = new Color32(231, 83, 64, 255);
        colors.highlightedColor = new Color32(247, 109, 89, 255);
        colors.pressedColor = new Color32(197, 61, 46, 255);
        colors.selectedColor = colors.normalColor;
        bar.colors = colors;
    }
}
