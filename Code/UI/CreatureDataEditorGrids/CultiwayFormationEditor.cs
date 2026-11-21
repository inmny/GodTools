using GodTools.Features.Rankings;

namespace GodTools.UI.CreatureDataEditorGrids;

internal static class CultiwayFormationEditor
{
    internal static object Read(Actor actor, string name, string field) =>
        CultiwayCompatibility.Member(Formation(actor, name), field);

    internal static void Set(Actor actor, string name, string field, object value)
    {
        var component = Formation(actor, name);
        if (component == null) return;
        component.GetType().GetProperty(field).SetValue(component, value);
        CultiwayCompatibility.Find().SetComponent(actor, component);
    }

    private static object Formation(Actor actor, string name)
    {
        var component = CultiwayCompatibility.Find().Component(actor, name);
        var formation = CultiwayCompatibility.Member(component, "formation");
        return CultiwayCompatibility.Member(formation, "IsValid") is true ? component : null;
    }
}
