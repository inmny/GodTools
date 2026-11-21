using GodTools.Features;
using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GodTools.UI.Prefabs;

public class SavedActorDataCard : APrefab<SavedActorDataCard>
{
    private int click_times;

    private float                last_click_time;
    public  SavedActorDataAvatar Avatar       { get; private set; }
    public  RawText              Name         { get; private set; }
    public  Button               Button       { get; private set; }
    public  SimpleButton         DeleteButton { get; private set; }
    public  SimpleButton         VillageButton { get; private set; }
    private CreatureSavedList.SavedActorPacket _packet;

    internal void Setup(CreatureSavedList.SavedActorPacket packet, UnityAction place, UnityAction placeInVillage, UnityAction delete_action)
    {
        Init();

        _packet = packet;
        Avatar.Setup(packet.ActorData);
        Name.Setup(packet.ActorData.name);
        Button.onClick.RemoveAllListeners();
        Button.onClick.AddListener(place);
        Button.interactable = CreatureSavedList.CanPlace(packet);
        VillageButton.Setup(placeInVillage, SpriteTextureLoader.getSprite("ui/icons/iconCitySelect"), pSize: new Vector2(22, 22),
            pTipType: "tip", pTipData: new TooltipData
            {
                tip_name = "inmny.godtools.place_saved_actor_in_village",
                tip_description = "inmny.godtools.place_saved_actor_in_village description"
            });
        VillageButton.Background.sprite = SpriteTextureLoader.getSprite("ui/special/special_buttonGray_insides");
        VillageButton.Button.interactable = CreatureSavedList.CanPlaceInVillage(packet);

        DeleteButton.Setup([Hotfixable]() =>
            {
                if (Time.time - last_click_time < 1f)
                {
                    click_times++;
                    if (click_times >= 3)
                    {
                        delete_action?.Invoke();
                        click_times = 0;
                    }
                }
                else
                {
                    click_times = 0;
                }

                last_click_time = Time.time;
                Tooltip.show(DeleteButton.gameObject, DeleteButton.TipButton.type, new TooltipData
                {
                    tip_name = $"再点击{3 - click_times}次删除"
                });
            }, SpriteTextureLoader.getSprite("ui/icons/iconClose"), pSize: new Vector2(32, 32), pTipType: "tip",
            pTipData: new TooltipData
            {
                tip_name = "连续点击4次删除"
            });
    }

    protected override void Init()
    {
        if (Initialized) return;
        base.Init();

        Avatar = transform.Find(nameof(Avatar)).GetComponent<SavedActorDataAvatar>();
        Name = transform.Find(nameof(Name)).GetComponent<RawText>();
        Button = GetComponent<Button>();
        DeleteButton = transform.Find(nameof(DeleteButton)).GetComponent<SimpleButton>();
        VillageButton = transform.Find(nameof(VillageButton)).GetComponent<SimpleButton>();
        var tooltip = gameObject.AddComponent<TipButton>();
        tooltip.showOnClick = false;
        tooltip.setHoverAction(() =>
        {
            if (_packet != null)
                WindowRankings.ShowSavedCreatureTooltip(gameObject, _packet, Avatar.image.sprite, _packet.ActorData.name);
        }, false);
    }

    private static void _init()
    {
        GameObject obj = new(nameof(SavedActorDataCard), typeof(Image), typeof(Button));
        obj.transform.SetParent(Main.prefabs);
        var rect_transform = obj.GetComponent<RectTransform>();

        var bg = obj.GetComponent<Image>();
        bg.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
        bg.type = Image.Type.Sliced;
        rect_transform.sizeDelta = new Vector2(180, 48);

        SavedActorDataAvatar avatar = Instantiate(SavedActorDataAvatar.Prefab, obj.transform);
        avatar.name = nameof(Avatar);
        avatar.transform.localPosition = new Vector3(-70, 0);
        avatar.transform.localScale = Vector3.one;
        avatar.SetSize(new Vector2(36, 36));


        RawText name = Instantiate(RawText.Prefab, obj.transform);
        name.SetSize(new Vector2(78, 30));
        name.name = nameof(Name);
        name.transform.localPosition = new Vector3(-10, 0);
        name.transform.localScale = Vector3.one;

        SimpleButton delete_button = Instantiate(SimpleButton.Prefab, obj.transform);
        delete_button.name = nameof(DeleteButton);
        delete_button.transform.localPosition = new Vector3(70, 0);
        delete_button.transform.localScale = Vector3.one;

        var village = Instantiate(SimpleButton.Prefab, obj.transform);
        village.name = nameof(VillageButton);
        village.transform.localPosition = new Vector3(42, 0);
        village.transform.localScale = Vector3.one;

        Prefab = obj.AddComponent<SavedActorDataCard>();
    }

}
