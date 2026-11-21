using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.Prefabs;

public class SavedActorDataAvatar : APrefab<SavedActorDataAvatar>
{
    public Image image { get; private set; }

    private static void _init()
    {
        GameObject obj = new(nameof(SavedActorDataAvatar), typeof(Image));
        obj.transform.SetParent(Main.prefabs);
        Prefab = obj.AddComponent<SavedActorDataAvatar>();
    }

    protected override void Init()
    {
        if (Initialized) return;
        base.Init();
        image = GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    [Hotfixable]
    public void Setup(ActorData data, Sprite portrait = null)
    {
        Init();
        image.sprite = portrait ?? GetSprite(data);
    }

    private Sprite GetSprite(ActorData data)
    {
        ActorAsset asset = AssetManager.actor_library.get(data.asset_id);
        return asset?.getSpriteIcon() ?? SpriteTextureLoader.getSprite("ui/icons/iconQuestionMark");
    }
}