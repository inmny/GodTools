using GodTools.UI.Prefabs;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

public abstract class SimpleInputEditor : CreatureDataEditorGrid
{
    protected TextInput TextInput { get; private set; }
    protected Actor Actor { get; private set; }
    public sealed override void Setup()
    {
        TextInput = TextInput.Instantiate(transform, pName: "TextInput");
        TextInput.Setup("", UpdateValue);
        TextInput.SetSize(new(176, 20));
        TextInput.GetComponent<RectTransform>().pivot = new(0.5f, 1);
        TextInput.input.characterValidation = InputField.CharacterValidation.Integer;
        OnSetup();
        TitleKey = GetTitleKey();
    }
    protected abstract string GetTitleKey();
    protected virtual void OnSetup()
    {
        
    }

    protected abstract void UpdateValue(string value);
    public sealed override void EnabledWith(Actor actor)
    {
        Actor = actor;
        TextInput.Setup(GetInitValue(), UpdateValue);
    }

    protected abstract string GetInitValue();
}
