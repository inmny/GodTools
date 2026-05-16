namespace GodTools;

public static class CustomGodToolsConfig
{
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
