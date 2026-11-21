using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;
using UnityEngine;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private string _newRegionName = "";

    private void BuildRegions()
    {
        Heading(L("region.list", "Map regions"));
        InputAt(FieldRow(L("region.name", "Region name")), _newRegionName, 206, value => _newRegionName = value);
        ButtonAt(_body, L("region.add", "Create region"), 146, () =>
        {
            string name = _newRegionName.Trim();
            if (name.Length == 0 || World.world == null) return;
            int x = MapBox.width / 2, y = MapBox.height / 2;
            var region = new ScriptRegion
            {
                ID = WorldScripts.Regions.Select(item => item.ID).DefaultIfEmpty(0L).Max() + 1,
                Name = name, X1 = x, X2 = x, Y1 = y, Y2 = y
            };
            WorldScripts.Regions.Add(region);
            _newRegionName = "";
            _expanded.Add(region);
            Render();
        }, NativeIcon("iconShovelPlus"), surface: Surface.Primary);

        foreach (var region in WorldScripts.Regions)
        {
            if (!FlowFold(region, $"{region.Name} · ({region.X1}, {region.Y1}) ↔ ({region.X2}, {region.Y2})",
                    "iconArrowAttackTarget")) continue;
            InputAt(FieldRow(L("region.name", "Region name")), region.Name, 206, value =>
            {
                if (!string.IsNullOrWhiteSpace(value)) region.Name = value.Trim();
            });
            var tools = Row(_body, EditorWidth, 23);
            ButtonAt(tools, L("region.pick_first", "Pick first corner"), 122,
                () => BeginMapSelection(tile => { region.X1 = tile.x; region.Y1 = tile.y; }),
                NativeIcon("iconArrowAttackTarget"), surface: Surface.Quiet);
            ButtonAt(tools, L("region.pick_second", "Pick opposite corner"), 122,
                () => BeginMapSelection(tile => { region.X2 = tile.x; region.Y2 = tile.y; }),
                NativeIcon("iconArrowAttackTarget"), surface: Surface.Quiet);
            IconCommand(tools, L("scenario.locate", "Locate on map"), "iconInspect", () =>
            {
                if (region.Center() is WorldTile tile) WorldLog.locationJump(new Vector3(tile.x, tile.y, 0));
            });
        }
    }
}
