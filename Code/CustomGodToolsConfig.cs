using NeoModLoader.api.attributes;
using UnityEngine;

namespace GodTools;

public static class CustomGodToolsConfig
{
    public static float AgeLightChangeSpeed { get; private set; } = 1f;
    public static float DarkAgeOverlayAlpha { get; private set; } = 0.3f;

    public static void SetAgeLightChangeSpeed(float value)
    {
        AgeLightChangeSpeed = value < 0f ? 0f : value;
    }
    [Hotfixable]
    public static void SetDarkAgeOverlayAlpha(float value)
    {
        DarkAgeOverlayAlpha = Mathf.Clamp01(value);

        var dark_age = AssetManager.era_library.get("age_dark");
        if (dark_age != null) dark_age.era_effect_overlay_alpha = DarkAgeOverlayAlpha;
        Main.LogInfo(PlayerConfig.getIntValue("age_overlay_effect").ToString());
    }

    public static bool HideCursor { get; private set; }

    public static void SetHideCursor(bool value)
    {
        HideCursor = value;
        TerraformLibrary.draw.flash = !value;
    }

    public static bool HideDrop { get; private set; }

    public static void SetHideDrop(bool value)
    {
        HideDrop = value;
    }

    public static bool HideUnitSelectionCircle { get; private set; }

    public static void SetHideUnitSelectionCircle(bool value)
    {
        HideUnitSelectionCircle = value;
    }

    public static bool HideMoveDestinationEffect { get; private set; }

    public static void SetHideMoveDestinationEffect(bool value)
    {
        HideMoveDestinationEffect = value;
    }
}
