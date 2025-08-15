using GodTools.Abstract;
using GodTools.Features;
using UnityEngine;

namespace GodTools.Libraries;

public class HotKeys : ExtendLibrary<HotkeyAsset, HotKeys>
{
    [GetOnly("left")]
    public static HotkeyAsset Left { get; private set; }
    [GetOnly("right")]
    public static HotkeyAsset Right { get; private set; }
    [GetOnly("up")]
    public static HotkeyAsset Up { get; private set; }
    [GetOnly("down")]
    public static HotkeyAsset Down { get; private set; }
    protected override void OnInit()
    {
        RegisterAssets();
        Left.holding_action += MoveActorWhenFollowing.Move;
        Left.allow_unit_control = true;
        Right.holding_action += MoveActorWhenFollowing.Move;
        Right.allow_unit_control = true;
        Up.holding_action += MoveActorWhenFollowing.Move;
        Up.allow_unit_control = true;
        Down.holding_action += MoveActorWhenFollowing.Move;
        Down.allow_unit_control = true;
    }
}