
namespace GodTools.UI.CreatureDataEditorGrids;

public class ZheTianLevelEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.zhetian.zhetian_level";
    }

    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int int_value))
        {
            Actor.data.set("StartBoard.custommodt001.zhetian_level", int_value);
            Actor.setStatsDirty();
        }
    }

    protected override void OnSetup()
    {
        TextInput.input.onValidateInput += (text, charIndex, addedChar) =>
        {
            if (addedChar < '0' || addedChar > '9')
            {
                return '\0';
            }
            if (int.TryParse(text + addedChar, out int value))
            {
                if (value > 11)
                {
                    return '\0';
                }
            }
            return addedChar;
        };
    }

    protected override string GetInitValue()
    {
        Actor.data.get("inmny.custommodt001.wudao_level", out int value);
        return value.ToString();
    }
}
