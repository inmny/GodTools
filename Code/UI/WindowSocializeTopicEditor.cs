using System.Collections.Generic;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace GodTools.UI;

public class WindowSocializeTopicEditor : AutoLayoutWindow<WindowSocializeTopicEditor>
{
    public static string WindowId;
    public static Sprite SelectedTopic { get; private set; }

    private AutoGridLayoutGroup _topicGrid;
    protected override void Init()
    {
        WindowId = ScrollWindowComponent.screen_id;

        var vert = this.BeginVertGroup();


        _topicGrid = vert.BeginGridGroup(5, pCellSize: new(32, 32));

    }
    public override void OnFirstEnable()
    {
        base.OnFirstEnable();

        var all_buttons = new List<SimpleButton>();
        var normal = SpriteTextureLoader.getSprite("ui/button");
        var selected = SpriteTextureLoader.getSprite("special/button2");

        // AMod 的推荐表情排在第一格；未安装 AMod 时可照常选用原版图标。
        var sweatingSoybean = SpriteTextureLoader.getSprite("inmny/amod/icons/socialize/sweating_soybean");
        if (sweatingSoybean != null)
            AddTopic(sweatingSoybean, LocalizedTextManager.getText("trait_inmny.amod.SweatingSoybean"));
        foreach (var icon in Resources.LoadAll<Sprite>("ui/icons"))
            AddTopic(icon, icon.name);

        void AddTopic(Sprite icon, string label)
        {
            var button = SimpleButton.Instantiate();
            all_buttons.Add(button);

            _topicGrid.AddChild(button.gameObject);

            button.Setup(() =>
                {
                    SelectedTopic = icon;
                    foreach (var other_button in all_buttons)
                    {
                        other_button.Background.sprite = normal;
                    }
                    button.Background.sprite = selected;
                }, icon,
                pTipType: "tip",
                pTipData: new TooltipData()
                {
                    tip_name = label
                }
            );

            button.Background.sprite = normal;
        }
    }
}
