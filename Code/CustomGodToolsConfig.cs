namespace GodTools;

public static class CustomGodToolsConfig
{
    public static float AgeLightChangeSpeed { get; private set; } = 1f;

    public static void SetAgeLightChangeSpeed(float value)
    {
        AgeLightChangeSpeed = value < 0f ? 0f : value;
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
}
