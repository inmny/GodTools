using System;
using System.Collections.Generic;
using GodTools.Features;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine.UI;

namespace GodTools.UI;

public class WindowPrinterEditor : AutoLayoutWindow<WindowPrinterEditor>
{
    public static string WindowId;
    public static TileTypeBase SelectedTileType { get; private set; }
    public static float WidthScale { get; private set; } = 1;
    public static float HeightScale { get; private set; } = 1;
    private List<SimpleButton> _tileButtons = new();
    private PrintDirection _direction;
    public enum PrintDirection
    {        
        Default,
        InsideToOutside,
        OutsideToInside,
    }
    protected override void Init()
    {
        WindowId = ScrollWindowComponent.screen_id;
        var vert = this.BeginVertGroup();

        var print_direction_grid = vert.BeginGridGroup(3, pCellSize: new(56, 16));
        foreach (var item in Enum.GetValues(typeof(PrintDirection)))
        {
            var direction = (PrintDirection)item;
            SimpleButton button = Instantiate(SimpleButton.Prefab);
            print_direction_grid.AddChild(button.gameObject);
            button.Setup(() =>
            {
                _direction = direction;
                PrinterEditor.RecalcAllPrintSteps(_direction);
            }, null, LM.Get(direction.ToString()));
        }

        var print_scale_group = vert.BeginHoriGroup();
        var width_scale = TextInput.Instantiate();
        width_scale.Setup("1", str =>
        {
            WidthScale = float.Parse(str);
            PrinterEditor.RecalcAllPrintSteps(_direction);
        });
        width_scale.input.characterValidation = InputField.CharacterValidation.Decimal;
        width_scale.SetSize(new(60, 20));
        print_scale_group.AddChild(width_scale.gameObject);
        var height_scale = TextInput.Instantiate();
        height_scale.Setup("1", str =>
        {
            HeightScale = float.Parse(str);
            PrinterEditor.RecalcAllPrintSteps(_direction);
        });
        height_scale.SetSize(new(60, 20));
        height_scale.input.characterValidation = InputField.CharacterValidation.Decimal;
        print_scale_group.AddChild(height_scale.gameObject);

        var print_tile_grid = vert.BeginGridGroup(5, pCellSize: new(32,32));
        AssetManager.topTiles.ForEach<TopTileType, TopTileLibrary>(top_tile_asset =>
        {
            var button = SimpleButton.Instantiate();
            print_tile_grid.AddChild(button.gameObject);
            button.Setup(() =>
                {
                    SelectedTileType = top_tile_asset;
                    foreach (var other_button in _tileButtons)
                    {
                        other_button.Background.sprite = SpriteTextureLoader.getSprite("ui/button");
                    }
                    button.Background.sprite = SpriteTextureLoader.getSprite("ui/button2");
                }, top_tile_asset.sprites.getVariation(0).sprite,
                pTipType: "tip",
                pTipData: new TooltipData()
                {
                    tip_name = top_tile_asset.id
                }
            );
            button.Background.sprite = SpriteTextureLoader.getSprite("special/button");
            _tileButtons.Add(button);
        });
        AssetManager.tiles.ForEach<TileType, TileLibrary>(tile_asset =>
        {
            var button = SimpleButton.Instantiate();
            
            print_tile_grid.AddChild(button.gameObject);

            button.Setup(() =>
                {
                    SelectedTileType = tile_asset;
                    foreach (var other_button in _tileButtons)
                    {
                        other_button.Background.sprite = SpriteTextureLoader.getSprite("ui/button");
                    }
                    button.Background.sprite = SpriteTextureLoader.getSprite("special/button");
                }, tile_asset.sprites.getVariation(0).sprite,
                pTipType: "tip",
                pTipData: new TooltipData()
                {
                    tip_name = tile_asset.id
                }
            );
            button.Background.sprite = SpriteTextureLoader.getSprite("ui/button");
            _tileButtons.Add(button);
        });
    }
}