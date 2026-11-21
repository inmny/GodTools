using GodTools.Abstract;

namespace GodTools.Features;

internal sealed class BackpackResources : IManager
{
    private const string BloodID = "inmny.godtools.backpack.blood";

    public void Initialize()
    {
        var resources = AssetManager.resources;
        if (resources.has("blood") || resources.has(BloodID)) return;

        const string spritePath = "items/resources/bag_default";
        resources.add(new ResourceAsset
        {
            id = BloodID,
            type = ResType.Ingredient,
            path_icon = "iconBloodRain",
            path_gameplay_sprite = "bag_default",
            full_sprite_path = spritePath,
            gameplay_sprites = SpriteTextureLoader.getSpriteList(spritePath),
            order = resources.list.Count
        });
    }
}
