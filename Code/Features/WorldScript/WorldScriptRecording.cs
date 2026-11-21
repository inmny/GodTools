namespace GodTools.Features.WorldScript;

internal static class WorldScriptRecording
{
    private static (bool Cursor, bool Drop, bool Hover, bool Shake, bool UI)? _before;

    internal static void Start()
    {
        if (_before.HasValue) return;
        _before = (CustomGodToolsConfig.HideCursor, CustomGodToolsConfig.HideDrop,
            CustomGodToolsConfig.HideUnitHover, CustomGodToolsConfig.DisableCameraShake, Config.ui_main_hidden);
        CustomGodToolsConfig.SetHideCursor(true);
        CustomGodToolsConfig.SetHideDrop(true);
        CustomGodToolsConfig.SetHideUnitHover(true);
        CustomGodToolsConfig.SetDisableCameraShake(true);
        Config.ui_main_hidden = true;
    }

    internal static void CheckExit()
    {
        if (_before.HasValue && !Config.ui_main_hidden) End();
    }

    internal static void End()
    {
        if (_before is not { } settings) return;
        _before = null;
        CustomGodToolsConfig.SetHideCursor(settings.Cursor);
        CustomGodToolsConfig.SetHideDrop(settings.Drop);
        CustomGodToolsConfig.SetHideUnitHover(settings.Hover);
        CustomGodToolsConfig.SetDisableCameraShake(settings.Shake);
        Config.ui_main_hidden = settings.UI;
    }
}
