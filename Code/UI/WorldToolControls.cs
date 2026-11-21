using System;
using GodTools.UI.Prefabs;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

internal static class WorldToolControls
{
    internal static readonly Color Muted = new Color32(190, 197, 172, 255);
    internal static readonly Color Selected = new Color32(169, 187, 129, 255);
    internal static readonly Color Gold = new Color32(240, 193, 116, 255);

    internal static void Place(RectTransform rect, float x, float y, float width, float height) =>
        WindowCreatureSavedList.Position(rect, x, y, width, height);

    internal static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, x, y, width, height);
        return rect;
    }

    internal static Image Panel(Transform parent, string name, float x, float y, float width, float height)
    {
        var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        return image;
    }

    internal static RectTransform ScrollPane(ScrollRect scroll, RectTransform parent, float x, float y, float width, float height)
    {
        var content = NativeListPane.Configure(scroll, parent, width, height,
            x + (width + NativeListPane.ScrollbarGutter - parent.rect.width) / 2,
            (parent.rect.height - height) / 2 - y);
        if (scroll.verticalScrollbar == null) return content;

        // 原版用局部 X 坐标滑入滚动条，槽位固定在视口右侧。
        var track = (RectTransform)scroll.verticalScrollbar.transform;
        var slot = Rect(scroll.transform, "ScrollbarSlot", 0, 0, 0, 0);
        slot.anchorMin = new Vector2(1, 0);
        slot.anchorMax = new Vector2(1, 1);
        slot.pivot = new Vector2(0.5f, 0.5f);
        slot.offsetMin = new Vector2(-NativeListPane.ScrollbarGutter, 4);
        slot.offsetMax = new Vector2(-1, -4);
        slot.gameObject.AddComponent<RectMask2D>();
        track.SetParent(slot, false);
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = track.offsetMax = Vector2.zero;
        return content;
    }

    internal static Text Label(Transform parent, string name, string text, float x, float y, float width, float height,
        int size = 9, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var label = RawText.Instantiate(parent);
        label.name = name;
        label.Setup(text, color ?? Color.white, alignment);
        Place((RectTransform)label.transform, x, y, width, height);
        label.Text.fontSize = size;
        label.Text.resizeTextForBestFit = false;
        label.Text.supportRichText = false;
        label.Text.raycastTarget = false;
        return label.Text;
    }

    internal static SimpleButton Button(Transform parent, string name, string text, float x, float y, float width,
        float height, Action action, Sprite icon = null, string hint = null)
    {
        var button = SimpleButton.Instantiate(parent);
        button.name = name;
        button.Setup(() => action(), icon, pSize: new Vector2(width, height));
        button.Background.sprite = WindowCreatureSavedList.ControlSurface();
        button.Icon.enabled = icon != null;
        if (text != null)
            Label(button.transform, "Caption", text, 2, 0, width - 4, height, 9, alignment: TextAnchor.MiddleCenter);
        if (!string.IsNullOrEmpty(hint))
        {
            button.TipButton.enabled = true;
            button.TipButton.setHoverAction(() => Tooltip.show(button.gameObject, "normal",
                new TooltipData { tip_name = hint }), false);
        }
        Place((RectTransform)button.transform, x, y, width, height);
        return button;
    }

    internal static InputField Input(Transform parent, string name, string placeholder, float x, float y,
        float width, Action changed, InputField.CharacterValidation validation = InputField.CharacterValidation.None)
    {
        var prefab = TextInput.Instantiate(parent);
        prefab.name = name;
        prefab.Setup("", _ => { });
        prefab.SetSize(new Vector2(width, 20));
        Place((RectTransform)prefab.transform, x, y, width, 20);
        var input = prefab.input;
        input.lineType = InputField.LineType.SingleLine;
        input.characterValidation = validation;
        if (input.placeholder != null) input.placeholder.gameObject.SetActive(false);
        input.placeholder = Label(prefab.transform, "Placeholder", placeholder, 4, 0, width - 17, 20, 8, Muted);
        var text = input.textComponent.rectTransform;
        text.anchorMin = Vector2.zero;
        text.anchorMax = Vector2.one;
        text.offsetMin = new Vector2(4, 0);
        text.offsetMax = new Vector2(-13, 0);
        input.textComponent.fontSize = 9;
        input.onValueChanged.AddListener(_ => changed?.Invoke());
        return input;
    }
}
