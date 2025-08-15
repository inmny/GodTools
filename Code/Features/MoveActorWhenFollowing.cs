using UnityEngine;

namespace GodTools.Features;

internal static class MoveActorWhenFollowing
{
    public static void Move(HotkeyAsset asset)
    {
        switch (asset.id)
        {
            case "left":
                _direction.x = -1;
                break;
            case "right":
                _direction.x = 1;
                break;
            case "up":
                _direction.y = 1;
                break;
            case "down":
                _direction.y = -1;
                break;
        }
    }

    private static Vector2Int _direction;
    public static void Update()
    {
        if (MoveCamera.focusUnit == null)
        {
            return;
        }

        if (_direction.x == 0 && _direction.y == 0)
        {
            return;
        }

        var tile = MoveCamera.focusUnit.currentTile;
        var new_tile = World.world.GetTile(tile.pos.x + _direction.x, tile.pos.y + _direction.y);
        if (new_tile != null)
        {
            MoveCamera.focusUnit.goTo(new_tile, true, true);
        }
        
        _direction.x = 0;
        _direction.y = 0;
    }
}