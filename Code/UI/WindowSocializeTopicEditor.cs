using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GodTools.Abstract;
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

        var icons = Resources.LoadAll<Sprite>("ui/icons");
        var all_buttons = new List<SimpleButton>();
        foreach (var icon in icons)
        {
            var button = SimpleButton.Instantiate();
            all_buttons.Add(button);

            _topicGrid.AddChild(button.gameObject);

            var local_icon = icon;
            button.Setup(() =>
                {
                    SelectedTopic = local_icon;
                    foreach (var other_button in all_buttons)
                    {
                        other_button.Background.sprite = SpriteTextureLoader.getSprite("ui/button");
                    }
                    button.Background.sprite = SpriteTextureLoader.getSprite("special/button2");
                }, local_icon,
                pTipType: "tip",
                pTipData: new TooltipData()
                {
                    tip_name = local_icon.name
                }
            );

            button.Background.sprite = SpriteTextureLoader.getSprite("ui/button");
        }
    }
}