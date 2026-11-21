using System;

namespace GodTools.Features.WorldScript;

public sealed class ScriptRegion
{
    public long ID;
    public string Name;
    public int X1;
    public int Y1;
    public int X2;
    public int Y2;

    public bool Contains(int x, int y) =>
        x >= Math.Min(X1, X2) && x <= Math.Max(X1, X2) &&
        y >= Math.Min(Y1, Y2) && y <= Math.Max(Y1, Y2);

    internal bool Contains(WorldTile tile) => tile != null && Contains(tile.x, tile.y);

    internal WorldTile Center() => World.world?.GetTile(
        (int)(((long)X1 + X2) / 2), (int)(((long)Y1 + Y2) / 2));
}
