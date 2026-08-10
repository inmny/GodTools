using System;
using System.Collections.Generic;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GodTools.UI.WindowTopComponents;

public class FilterButtonInGrid : APrefab<FilterButtonInGrid>
{
    [SerializeField]
    public Image icon;
    [SerializeField]
    private Button button;
    [SerializeField]
    private TipButton tipButton;
    private List<FilterSetting> target_list;
    private FilterSetting _filter;

    public void Setup(FilterSetting filter, List<FilterSetting> target_list)
    {
        _filter = filter;
        icon.sprite = filter.Icon;
        icon.color = filter.IconColor;
        tipButton.textOnClick = filter.ID;
        
        this.target_list = target_list;
        
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Toggle);
    }
    public void Setup(string filter_id, string icon_path, Func<Actor, bool> filter_action, List<FilterSetting> target_list)
    {
        icon.sprite = SpriteTextureLoader.getSprite(icon_path);
        icon.color = Color.white;
        tipButton.textOnClick = filter_id;
        _filter = new FilterSetting(filter_id, icon.sprite, filter_action);
        this.target_list = target_list;
        
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Toggle);
    }

    private void Toggle()
    {
        
        var idx = target_list.FindIndex(x=>x.ID == _filter.ID);
        if (idx != -1)
        {
            target_list[idx].ToBeRemoved = true;
        }
        else
        {
            target_list.Add(_filter);
            _filter.ToBeRemoved = false;
        }
    }
    private static void _init()
    {
        var obj = new GameObject(nameof(FilterButtonInGrid), typeof(Image), typeof(Button), typeof(TipButton));
        obj.transform.SetParent(Main.prefabs);
        
        Prefab = obj.AddComponent<FilterButtonInGrid>();
        Prefab.icon = obj.GetComponent<Image>();
        Prefab.button = obj.GetComponent<Button>();
        Prefab.tipButton = obj.GetComponent<TipButton>();
    }
}
