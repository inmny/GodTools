using GodTools.Abstract;
using GodTools.Libraries;

namespace GodTools.UI;

[Dependency(typeof(GodPowers), typeof(Features.Rankings.BuiltInRankings))]
internal class UIManager : IManager
{
    public void Initialize()
    {
#if ENABLE_SPRITE_EDITOR
        WindowCreatureSpriteEditor.CreateWindow(nameof(WindowCreatureSpriteEditor), nameof(WindowCreatureSpriteEditor));
#endif
        WindowCreatureDataEditor.CreateAndInit(nameof(WindowCreatureDataEditor), pSize: new(300, 250));
        WindowCreatureSearch.CreateWindow(nameof(WindowCreatureSearch), nameof(WindowCreatureSearch));
        WindowCreatureTitleEditor.CreateWindow(nameof(WindowCreatureTitleEditor), nameof(WindowCreatureTitleEditor));
        WindowCreatureSavedList.CreateAndInit(nameof(WindowCreatureSavedList), pSize: new(352, 275));
        WindowSocializeTopicEditor.CreateWindow(nameof(WindowSocializeTopicEditor), nameof(WindowSocializeTopicEditor));
        WindowPrinterEditor.CreateAndInit(nameof(WindowPrinterEditor), pSize: new(454, 275));
        WindowRankings.CreateAndInit(nameof(WindowRankings));
        WindowDetailedHistory.CreateAndInit(nameof(WindowDetailedHistory), pSize: new(534, 275));
        WindowWorldScript.CreateAndInit(nameof(WindowWorldScript));
        CanvasCreatureTitle.Init();
    }
}
