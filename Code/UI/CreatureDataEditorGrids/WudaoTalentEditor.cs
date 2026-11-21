
namespace GodTools.UI.CreatureDataEditorGrids;

public class WudaoTalentEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.inmny_custommodt001.talent";
    }
    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int talent))
            Actor.data.set("inmny.custommodt001.talent", (float)talent);
    }

    protected override string GetInitValue()
    {
        Actor.data.get("inmny.custommodt001.talent", out float talent);
        return ((int)talent).ToString();
    }
}
