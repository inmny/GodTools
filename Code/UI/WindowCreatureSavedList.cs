using System;
using System.Collections.Generic;
using GodTools.Features;
using GodTools.Libraries;
using GodTools.UI.Prefabs;
using NeoModLoader.api;
using NeoModLoader.General;
using UnityEngine;

namespace GodTools.UI;

public class WindowCreatureSavedList : AbstractWindow<WindowCreatureSavedList>
{
    private const float single_element_height = 56;
    private const float start_y               = -single_element_height / 2;

    private List<CreatureSavedList.SavedActorPacket>                           _list;
    private ObjectPoolGenericMono<SavedActorDataCard> _pool;
    private PowerButton                               _power_button;
    private PowerButton                               _village_power_button;

    private RectTransform                 content_rect;
    private RectTransform                 scroll_view_rect;

    private void Update()
    {
        OnUpdate();
    }

    protected override void Init()
    {
        content_rect = ContentTransform.GetComponent<RectTransform>();
        scroll_view_rect = content_rect.parent.parent.GetComponent<RectTransform>();
        content_rect.pivot = new Vector2(0.5f, 1);
        _pool = new ObjectPoolGenericMono<SavedActorDataCard>(SavedActorDataCard.Prefab, ContentTransform);
        _power_button = PowerButtonCreator.CreateGodPowerButton(GodPowers.place_saved_actor.id,
            SpriteTextureLoader.getSprite("gt_windows/save_actor_list"), Main.prefabs);
        _village_power_button = PowerButtonCreator.CreateGodPowerButton(GodPowers.place_saved_actor_in_village.id,
            SpriteTextureLoader.getSprite("ui/icons/iconCitySelect"), Main.prefabs);
    }

    private void OnUpdate()
    {
        content_rect.sizeDelta = new Vector2(0, _list.Count * single_element_height);
        Vector3 curr_position = ContentTransform.localPosition;

        var view_y_start = curr_position.y;
        var view_y_end = curr_position.y + scroll_view_rect.sizeDelta.y;

        var view_start_idx = (int)(view_y_start / single_element_height);
        var view_end_idx = Math.Min((int)(view_y_end / single_element_height) + 1, _list.Count - 1);

        _pool.clear();
        for (var i = view_start_idx; i <= view_end_idx; i++)
        {
            var packet = _list[i];
            SavedActorDataCard card = _pool.getNext();
            card.Setup(packet, () => PlaceCatalogEntry(packet, false), () => PlaceCatalogEntry(packet, true), () =>
            {
                CreatureSavedList.UnsaveActor(packet);
                _list.Remove(packet);
                Main.LogInfo("delete");
            });
            card.transform.localPosition = new Vector2(0, start_y - i * single_element_height);
        }
    }

    public override void OnNormalEnable()
    {
        _list = new (CreatureSavedList.SavedActors.Values);
    }

    private void PlaceCatalogEntry(CreatureSavedList.SavedActorPacket packet, bool inVillage)
    {
        if (!CanArmPlacement(packet, inVillage)) return;
        CreatureSavedList.SelectActorToPlace(packet);
        ArmPlacement(inVillage);
    }

    internal static void PlaceSnapshot(CreatureSavedList.SavedActorPacket packet, bool inVillage = false)
    {
        if (!CanArmPlacement(packet, inVillage)) return;
        CreatureSavedList.SelectSnapshotToPlace(packet);
        Instance.ArmPlacement(inVillage);
    }

    private static bool CanArmPlacement(CreatureSavedList.SavedActorPacket packet, bool inVillage) =>
        inVillage ? CreatureSavedList.CanPlaceInVillage(packet) : CreatureSavedList.CanPlace(packet);

    private void ArmPlacement(bool inVillage)
    {
        ScrollWindow.hideAllEvent();
        if (inVillage)
        {
            if (World.world.selected_power != GodPowers.place_saved_actor_in_village) _village_power_button.clickActivePower();
            WorldTip.showNowTop("inmny.godtools.place_saved_actor_in_village description", true);
        }
        else if (World.world.selected_power != GodPowers.place_saved_actor) _power_button.clickActivePower();
    }
}
