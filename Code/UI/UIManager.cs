using GodTools.Abstract;
using GodTools.Libraries;

namespace GodTools.UI;

[Dependency(typeof(GodPowers))]
internal class UIManager : IManager
{
    public void Initialize()
    {
        WindowModInfo.CreateWindow($"{C.mod_prefix}.{nameof(WindowModInfo)}", nameof(WindowModInfo));
#if ENABLE_SPRITE_EDITOR
        WindowCreatureSpriteEditor.CreateWindow(nameof(WindowCreatureSpriteEditor), nameof(WindowCreatureSpriteEditor));
#endif
        WindowCreatureDataEditor.CreateAndInit(nameof(WindowCreatureDataEditor), pSize: new(300, 250));
        WindowCreatureSearch.CreateWindow(nameof(WindowCreatureSearch), nameof(WindowCreatureSearch));
        WindowCreatureTitleEditor.CreateWindow(nameof(WindowCreatureTitleEditor), nameof(WindowCreatureTitleEditor));
        WindowCreatureSavedList.CreateAndInit(nameof(WindowCreatureSavedList));
        WindowConsistentCreaturePowerTop.CreateWindow(nameof(WindowConsistentCreaturePowerTop), nameof(WindowConsistentCreaturePowerTop));
        WindowSocializeTopicEditor.CreateWindow(nameof(WindowSocializeTopicEditor), nameof(WindowSocializeTopicEditor));
        WindowPrinterEditor.CreateWindow(nameof(WindowPrinterEditor), nameof(WindowPrinterEditor));
        WindowTops.CreateAndInit($"{C.mod_prefix}.{nameof(WindowTops)}");
        WindowDetailedHistory.CreateAndInit(nameof(WindowDetailedHistory));
        WindowWorldScript.CreateAndInit(nameof(WindowWorldScript));
        CanvasCreatureTitle.Init();
    }
}
