using System;
using DG.Tweening;
using GodTools.Features;
using GodTools.Libraries;
using GodTools.UI;
using HarmonyLib;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GodTools.Patch;

internal static class PatchWindowCreatureInfo
{
    private static bool initialized;

    private static float _last_y = 84;
    private static Image gender_image;
    private static Image saved_image;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.OnEnable))]
    public static void OnEnable_postfix(UnitWindow __instance)
    {
        Actor actor = __instance.actor;
        if (actor == null) return;
        if (!initialized)
        {
            initialized = true;
            var gender = new GameObject("Gender", typeof(Image), typeof(Button));
            gender.transform.SetParent(__instance._avatar_element.transform);
            var gender_rect = gender.GetComponent<RectTransform>();
            gender_rect.localPosition = new Vector3(17.5f, -16.8f);
            gender_rect.localScale = Vector3.one;
            gender_rect.sizeDelta = new Vector2(16, 16);
            gender_image = gender.GetComponent<Image>();
            gender.GetComponent<Button>().onClick.AddListener(() =>
            {
                Actor actor = __instance.actor;
                if (actor.data.sex != ActorSex.Male)
                    actor.data.sex = ActorSex.Male;
                else
                    actor.data.sex = ActorSex.Female;

                actor.clearSprites();
                __instance._avatar_element.show(actor);
                CheckGender(actor);
            });
#if ENABLE_SPRITE_EDITOR
            AddEntryButtonForWindow(__instance, nameof(WindowCreatureSpriteEditor), "ui/icons/iconAttractive");
#endif
            AddEntryButtonForWindow(__instance, nameof(WindowCreatureDataEditor), "ui/icons/iconOptions");
            AddEntryButtonForWindow(__instance, nameof(WindowCreatureTitleEditor), "ui/icons/iconOptions");
            AddSideButton(__instance, "RemoveScar", () =>
            {
                SelectedUnit.unit.removeTrait("scar_of_divinity");
            },"ui/icons/iconDivineScar");
            AddSideButton(__instance, "inmny.godtools.ui.data_editor.backpack",
                WindowCreatureDataEditor.OpenBackpack, "ui/icons/iconCityInventory");

            GameObject save_actor =
                Object.Instantiate(__instance._icon_favorite.transform.parent.gameObject, __instance._icon_favorite.transform.parent.parent);
            save_actor.transform.localPosition = new Vector3(116.8f, -112f);
            save_actor.transform.localScale = new Vector3(1,         1);
            saved_image = save_actor.transform.Find("Icon").GetComponent<Image>();
            saved_image.sprite =
                Resources.Load<Sprite>("gt_windows/save_actor");
            var button = save_actor.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener([Hotfixable]() =>
            {
                if (CreatureSavedList.ActorSaved(__instance.actor))
                    CreatureSavedList.UnsaveActor(__instance.actor);
                else
                    CreatureSavedList.SaveActor(__instance.actor);

                CheckSave(__instance.actor, saved_image);
            });
            button.GetComponent<TipButton>().textOnClick = "save_actor";

        }
#if ENABLE_STATUS_EDITOR
        WindowStatusEffectEditor.init(__instance);
#endif
#if ENABLE_ITEM_EDITOR
        if (actor.asset.use_items)
        {
            WindowItemEditor.init(__instance);
            WindowItemEditor.entry_button.SetActive(true);
        }
        else if (WindowItemEditor.entry_button != null)
        {
            WindowItemEditor.entry_button.SetActive(false);
        }
#endif

        CheckGender(actor);
        CheckSave(actor, saved_image);
    }

    private static void CheckSave(Actor actor, Image image)
    {
        if (CreatureSavedList.ActorSaved(actor))
            image.color = Color.white;
        else
            image.color = Color.gray;
    }

    private static void CheckGender(Actor actor)
    {
        if (actor.data.sex == ActorSex.None)
        {
            gender_image.enabled = false;
        }
        else
        {
            gender_image.enabled = true;
            gender_image.sprite =
                SpriteTextureLoader.getSprite(actor.data.sex == ActorSex.Male
                    ? "gt_windows/male"
                    : "gt_windows/female");
        }
    }

    private static void AddEntryButtonForWindow(UnitWindow target_window, string entry_window_id, string icon)
    {
        PowerButtonCreator.CreateWindowButton(entry_window_id, entry_window_id,
            SpriteTextureLoader.getSprite(icon),
            target_window.transform.Find("Background"), new Vector2(156, _last_y));
        _last_y -= 40;
    }

    private static void AddSideButton(UnitWindow target_window, string id, UnityAction action, string icon)
    {
        PowerButtonCreator.CreateSimpleButton(id, action,
            SpriteTextureLoader.getSprite(icon),
            target_window.transform.Find("Background"), new Vector2(156, _last_y));
        _last_y -= 40;
    }
}
