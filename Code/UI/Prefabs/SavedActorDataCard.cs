using GodTools.Features;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static GodTools.UI.WindowCreatureSavedList;

namespace GodTools.UI.Prefabs;

public class SavedActorDataCard : APrefab<SavedActorDataCard>
{
    public SavedActorDataAvatar Avatar { get; private set; }
    public RawText Name { get; private set; }
    public Button Button { get; private set; }
    public SimpleButton DeleteButton { get; private set; }
    public SimpleButton VillageButton { get; private set; }
    public SimpleButton FavoriteButton { get; private set; }
    private SimpleButton _placeButton;
    private RawText _summary;
    private RawText _level;
    private CreatureSavedList.SavedActorPacket _packet;

    internal void Setup(CreatureSavedList.SavedActorPacket packet, string name, string summary, bool confirmDelete,
        UnityAction place, UnityAction placeInVillage, UnityAction favorite, UnityAction delete)
    {
        Init();
        _packet = packet;
        Avatar.Setup(packet.ActorData, packet.Portrait?.GetSprite());
        Name.Text.text = name;
        _summary.Text.text = summary;
        _level.Text.text = packet.ActorData.level.ToString();
        Button.onClick.RemoveAllListeners();
        Button.onClick.AddListener(place);
        Button.interactable = CreatureSavedList.CanPlace(packet);

        SetCommand(_placeButton, place, Icon("iconShovelPlus"), LM.Get("inmny.godtools.place_saved_actor"));
        _placeButton.Button.interactable = Button.interactable;
        SetCommand(VillageButton, placeInVillage, Icon("iconCitySelect"), LM.Get("inmny.godtools.place_saved_actor_in_village"));
        VillageButton.Button.interactable = CreatureSavedList.CanPlaceInVillage(packet);
        SetCommand(FavoriteButton, favorite, Icon("iconFavoriteStar"), L(packet.Favorite ? "unfavorite" : "favorite"));
        FavoriteButton.Icon.color = packet.Favorite ? Color.white : new Color(0.65f, 0.65f, 0.65f, 0.64f);
        SetCommand(DeleteButton, delete, SpriteTextureLoader.getSprite("inmny/godtools/icons/iconRankingDelete"),
            L(confirmDelete ? "confirm_delete" : "delete"));
        DeleteButton.Background.color = confirmDelete ? new Color32(232, 94, 75, 255) : Color.white;
    }

    private static void SetCommand(SimpleButton button, UnityAction action, Sprite icon, string hint)
    {
        button.Setup(action, icon, pSize: new Vector2(20, 20), pTipType: "tip",
            pTipData: new TooltipData { tip_name = hint });
        button.Background.sprite = ControlSurface();
    }

    protected override void Init()
    {
        if (Initialized) return;
        base.Init();

        Avatar = GetComponentInChildren<SavedActorDataAvatar>();
        Name = transform.Find(nameof(Name)).GetComponent<RawText>();
        _summary = transform.Find("OriginalAffiliation").GetComponent<RawText>();
        _level = transform.Find("Level").GetComponent<RawText>();
        Button = GetComponent<Button>();
        DeleteButton = transform.Find(nameof(DeleteButton)).GetComponent<SimpleButton>();
        VillageButton = transform.Find(nameof(VillageButton)).GetComponent<SimpleButton>();
        FavoriteButton = transform.Find(nameof(FavoriteButton)).GetComponent<SimpleButton>();
        _placeButton = transform.Find("PlaceButton").GetComponent<SimpleButton>();
        var tip = gameObject.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(() =>
        {
            if (_packet != null)
                WindowRankings.ShowSavedCreatureTooltip(gameObject, _packet, Avatar.image.sprite, Name.Text.text);
        }, false);
    }

    private static void _init()
    {
        var row = new GameObject(nameof(SavedActorDataCard), typeof(Image), typeof(Button), typeof(ScrollableButton));
        row.transform.SetParent(Main.prefabs, false);
        Position((RectTransform)row.transform, 0, 0, RowWidth, RowHeight - 2);
        row.GetComponent<ScrollableButton>()._scroll_wheel_only = true;
        var background = row.GetComponent<Image>();
        background.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        background.type = Image.Type.Sliced;
        background.color = new Color(0.17f, 0.20f, 0.17f, 0.67f);
        row.GetComponent<Button>().targetGraphic = background;

        var frame = new GameObject("PortraitFrame", typeof(Image)).GetComponent<Image>();
        frame.transform.SetParent(row.transform, false);
        Position(frame.rectTransform, 3, 1, 38, 32);
        frame.sprite = SpriteTextureLoader.getSprite("ui/special/windowAvatarElement");
        frame.raycastTarget = false;
        var avatar = Instantiate(SavedActorDataAvatar.Prefab, frame.transform, false);
        avatar.name = nameof(Avatar);
        Position((RectTransform)avatar.transform, 7, 5, 24, 22);

        MakeText(nameof(Name), 47, 3, IdentityWidth - 52, 16, 11, Color.white, TextAnchor.MiddleLeft);
        MakeText("OriginalAffiliation", 47, 18, IdentityWidth - 52, 13, 8, new Color32(190, 197, 170, 255), TextAnchor.MiddleLeft);
        MakeText("Level", IdentityWidth, 0, LevelWidth, RowHeight - 2, 10, Color.white, TextAnchor.MiddleCenter);
        MakeButton("PlaceButton", 235);
        MakeButton(nameof(VillageButton), 256);
        MakeButton(nameof(FavoriteButton), 277);
        MakeButton(nameof(DeleteButton), 298);
        Prefab = row.AddComponent<SavedActorDataCard>();

        void MakeText(string name, float x, float y, float width, float height, int size, Color color, TextAnchor align)
        {
            var label = RawText.Instantiate(row.transform);
            label.name = name;
            label.Setup("", color, align);
            Position((RectTransform)label.transform, x, y, width, height);
            label.Text.fontSize = size;
            label.Text.supportRichText = false;
            label.Text.raycastTarget = false;
            label.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.Text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void MakeButton(string name, float x)
        {
            var button = SimpleButton.Instantiate(row.transform);
            button.name = name;
            Position((RectTransform)button.transform, x, 7, 20, 20);
            var wheel = button.GetComponent<ScrollableButton>() ?? button.gameObject.AddComponent<ScrollableButton>();
            wheel._scroll_wheel_only = true;
        }
    }
}
