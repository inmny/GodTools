namespace GodTools.UI.CreatureDataEditorGrids;

public class ZheTianTalentEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.zhetian.talent";
    }
    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int talent))
            Actor.data.set("StartBoard.custommodt001.talent", (float)talent);
    }

    protected override string GetInitValue()
    {
        Actor.data.get("StartBoard.custommodt001.talent", out float talent);
        return ((int)talent).ToString();
    }
}
