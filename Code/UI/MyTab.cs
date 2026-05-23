using System.Collections.Generic;
using GodTools.Abstract;
using GodTools.Libraries;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

internal enum TabButtonType
{
    INFO,
    WORLD,
    ACTOR,
    BUILDING,
    CITY,
    KINGDOM,
    CULTURE,
    CLAN,
    TILE,
    OTHERS,
    DEBUG
}

[Dependency(typeof(UIManager))]
internal class MyTab : IManager
{
    public static           PowersTab                            powers_tab;
    private static readonly Dictionary<TabButtonType, Transform> button_groups = new();
    private static          RectTransform                        top_container;


    public void Initialize()
    {
        top_container = new GameObject("TopContainer", typeof(RectTransform)).GetComponent<RectTransform>();
        top_container.pivot = new Vector2(0, 0.5f);

        powers_tab = TabManager.CreateTab("GodTools", "GodTools", "GodTools Description",
            SpriteTextureLoader.getSprite("inmny/godtools/icons/iconTab"));
        powers_tab.SetLayout(new List<string>
        {
            "Controller", "Container"
        });
        powers_tab.PutElement("Container", top_container, new Vector2(-13, -19));

        ConstructTabContainer(TabButtonType.INFO,  SpriteTextureLoader.getSprite("ui/icons/iconAbout"));
        ConstructTabContainer(TabButtonType.WORLD, SpriteTextureLoader.getSprite("ui/icons/iconWorldInfo"));
        ConstructTabContainer(TabButtonType.ACTOR, SpriteTextureLoader.getSprite("ui/icons/iconHumans"));
        ConstructTabContainer(TabButtonType.DEBUG, SpriteTextureLoader.getSprite("ui/icons/iconDebug"));

        powers_tab.UpdateLayout();
        AddInfoButtons();
        AddWorldButtons();
        AddCreatureButtons();
        AddDebugButtons();

        SwitchTab(TabButtonType.INFO);
    }
    
    private static void AddDebugButtons()
    {
        PowerButton button;
        button = PowerButtonCreator.CreateWindowButton($"{C.mod_prefix}.debug", "debug",
            SpriteTextureLoader.getSprite("ui/icons/iconDebug"));
        AddButton(TabButtonType.DEBUG, button);
        button = PowerButtonCreator.CreateSimpleButton($"{C.mod_prefix}.console", World.world.console.Show,
            SpriteTextureLoader.getSprite("ui/icons/iconCommunity"));
        AddButton(TabButtonType.DEBUG, button);
        button = PowerButtonCreator.CreateSimpleButton($"{C.mod_prefix}.debug_info",
            () => { DebugConfig.createTool("Game Info"); },
            SpriteTextureLoader.getSprite("ui/icons/iconNewWorld"));
        AddButton(TabButtonType.DEBUG, button);
        button = PowerButtonCreator.CreateSimpleButton($"{C.mod_prefix}.benchmark",
            () => { DebugConfig.createTool("Benchmark All"); },
            SpriteTextureLoader.getSprite("ui/icons/iconStatistics"));
        AddButton(TabButtonType.DEBUG, button);
    }

    private static void AddWorldButtons()
    {
        PowerButton button;
        //button = PowerButtonCreator.CreateWindowButton(WindowSubWorldCreator.WINDOW_ID, WindowSubWorldCreator.WINDOW_ID,
         //   SpriteTextureLoader.getSprite("ui/icons/iconAbout"));
       // AddButton(TabButtonType.WORLD, button);
       button = PowerButtonCreator.CreateWindowButton(WindowDetailedHistory.WindowId, WindowDetailedHistory.WindowId,
           SpriteTextureLoader.getSprite("ui/icons/iconWorldLog"));
       AddButton(TabButtonType.WORLD, button);
       button = PowerButtonCreator.CreateWindowButton(WindowWorldScript.WindowId, WindowWorldScript.WindowId,
           SpriteTextureLoader.getSprite("ui/icons/iconWorldLaws"));
       AddButton(TabButtonType.WORLD, button);
       button = PowerButtonCreator.CreateWindowButton(WindowPrinterEditor.WindowId, WindowPrinterEditor.WindowId,
           SpriteTextureLoader.getSprite("ui/icons/iconWorldLaws"));
       AddButton(TabButtonType.WORLD, button);
    }

    private static void AddInfoButtons()
    {
        PowerButton button;
        button = PowerButtonCreator.CreateWindowButton(WindowModInfo.WINDOW_ID, WindowModInfo.WINDOW_ID,
            SpriteTextureLoader.getSprite("ui/icons/iconAbout"));
        AddButton(TabButtonType.INFO, button);
    }

    private static void AddCreatureButtons()
    {
        PowerButton button;
        button = PowerButtonCreator.CreateWindowButton(WindowCreatureSearch.WindowId, WindowCreatureSearch.WindowId,
            SpriteTextureLoader.getSprite("ui/icons/iconInspect"));
        AddButton(TabButtonType.ACTOR, button);
        button = PowerButtonCreator.CreateWindowButton(WindowCreatureSavedList.WindowId,
            WindowCreatureSavedList.WindowId,
            SpriteTextureLoader.getSprite("gt_windows/save_actor_list"));
        AddButton(TabButtonType.ACTOR, button);
        button = PowerButtonCreator.CreateWindowButton(WindowTops.WindowId, WindowTops.WindowId,
            SpriteTextureLoader.getSprite("inmny/godtools/icons/iconCreatureTop"));
        AddButton(TabButtonType.ACTOR, button);
        button = PowerButtonCreator.CreateWindowButton(WindowConsistentCreaturePowerTop.WindowId, WindowConsistentCreaturePowerTop.WindowId,
            SpriteTextureLoader.getSprite("inmny/godtools/icons/iconCreatureTop"));
        AddButton(TabButtonType.ACTOR, button);
        button = PowerButtonCreator.CreateWindowButton(WindowSocializeTopicEditor.WindowId,
            WindowSocializeTopicEditor.WindowId,
            SpriteTextureLoader.getSprite("ui/icons/iconCommunity"));
        AddButton(TabButtonType.ACTOR, button);
        button = PowerButtonCreator.CreateGodPowerButton(GodPowers.apply_socialize_topic.id,
            SpriteTextureLoader.getSprite("ui/icons/actor_traits/iconCommunity"));
        AddButton(TabButtonType.ACTOR, button);
    }

    private static void AddButton(TabButtonType type, PowerButton button)
    {
        if (!button_groups.TryGetValue(type, out Transform group))
        {
            ConstructTabContainer(type, null);
            group = button_groups[type];
        }

        button.transform.SetParent(group);
        button.transform.localScale = Vector3.one;
    }

    private static void ConstructTabContainer(TabButtonType type, Sprite icon)
    {
        powers_tab.AddPowerButton("Controller",
            PowerButtonCreator.CreateSimpleButton(type.ToString(), () => { SwitchTab(type); },
                icon));
        Transform transform = new GameObject(type.ToString(), typeof(GridLayoutGroup), typeof(ContentSizeFitter))
            .transform;
        transform.SetParent(top_container);
        transform.localPosition = Vector3.zero;
        transform.localScale = Vector3.one;
        ((RectTransform)transform).pivot = new Vector2(0, 0.5f);

        var layout = transform.GetComponent<GridLayoutGroup>();
        layout.spacing = new Vector2(4, 4);
        layout.startAxis = GridLayoutGroup.Axis.Vertical;
        layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        layout.constraintCount = 2;
        layout.cellSize = new Vector2(32, 32);

        var fitter = transform.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        button_groups[type] = transform;
    }

    private static void SwitchTab(TabButtonType type)
    {
        foreach (var pair in button_groups) pair.Value.gameObject.SetActive(pair.Key == type);
    }
}
