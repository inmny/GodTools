using GodTools.UI;
using UnityEngine;

namespace GodTools.Features;

public static class SocializeTopicEditor
{
    public static void ApplySelectedTopic(WorldTile tile = null, string dropId = null)
    {
        Sprite topic = WindowSocializeTopicEditor.SelectedTopic;
        if (topic == null)
        {
            WorldTip.showNow("请先选择社交话题图标", false, "top");
            return;
        }

        Actor actor = ActionLibrary.getActorFromTile(tile);
        if (actor == null)
        {
            return;
        }

        actor.cloneTopicSprite(topic);
        actor.is_forced_socialize_icon = true;
        actor.is_forced_socialize_timestamp = World.world.getCurWorldTime();
    }
}
