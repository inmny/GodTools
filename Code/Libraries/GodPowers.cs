using GodTools.Abstract;
using GodTools.Features;
using GodTools.UI;
using strings;

namespace GodTools.Libraries;

public class GodPowers : ExtendLibrary<GodPower, GodPowers>
{
    public static GodPower place_saved_actor { get; private set; }
    public static GodPower place_saved_actor_in_top { get; private set; }

    [CloneSource("$template_drops$")]
    public static GodPower apply_socialize_topic { get; private set; }

    protected override void OnInit()
    {
        RegisterAssets();
        /*
        var powers = AssetManager.powers;

        // 强制攻击
        var power = new GodPower();
        power.id = C.force_attack;
        power.name = power.id;
        power.path_icon = C.icon_GP_force_attack;
        power.select_button_action = (PowerButtonClickAction)Delegate.Combine(power.select_button_action,
            new PowerButtonClickAction(
                MyPowerActionLibrary
                    .init_click_force_attack));
        power.click_special_action = (PowerActionWithID)Delegate.Combine(power.click_special_action,
            new PowerActionWithID(
                MyPowerActionLibrary.click_force_attack));
        powers.add(power);
        */
        place_saved_actor.name = place_saved_actor.id;
        place_saved_actor.path_icon = "../../gt_windows/save_actor_list";
        place_saved_actor.click_action = CreatureSavedList.SpawnSelectedSavedActor;
        place_saved_actor_in_top.name = place_saved_actor_in_top.id;
        place_saved_actor_in_top.path_icon = "gt_windows/save_actor_list";
        place_saved_actor_in_top.click_action = ConsistentCreaturePowerTop.SpawnSelectedSavedActor;

        apply_socialize_topic.name = "gt_socialize_topic_drop";
        apply_socialize_topic.path_icon = "actor_traits/iconCommunity";
        apply_socialize_topic.drop_id = apply_socialize_topic.id;
        apply_socialize_topic.falling_chance = 1f;
        apply_socialize_topic.show_tool_sizes = true;
        apply_socialize_topic.click_power_action = spawnDropsNoForceSurprise;

        var drop = AssetManager.drops.clone(apply_socialize_topic.id, S_Drop.inspiration);
        drop.action_landed = SocializeTopicEditor.ApplySelectedTopic;
        apply_socialize_topic.cached_drop_asset = drop;
    }
    private bool spawnDropsNoForceSurprise(WorldTile tTile, GodPower pPower)
	{
		BrushData tBrush = Config.current_brush_data;
		bool tSpawnPixel = false;
		if (tBrush.size == 0 && tBrush.fast_spawn)
		{
			if (World.world.player_control.timer_spawn_pixels <= 0f)
			{
				World.world.player_control.timer_spawn_pixels = 0.5f;
				tSpawnPixel = true;
			}
		}
		else if (tBrush.fast_spawn && Randy.randomBool())
		{
			if (World.world.player_control.timer_spawn_pixels <= 0f)
			{
				World.world.player_control.timer_spawn_pixels = 0.3f;
				tSpawnPixel = true;
			}
		}
		else
		{
			tSpawnPixel = Randy.randomChance(pPower.falling_chance);
		}
		if (World.world.player_control.first_click)
		{
			World.world.player_control.first_click = false;
			tSpawnPixel = true;
			World.world.player_control.timer_spawn_pixels = 0.3f;
		}
		if (tSpawnPixel)
		{
			World.world.drop_manager.spawn(tTile, pPower.cached_drop_asset, -1f, -1f, false, -1L).soundOn = true;
		}
		return true;
	}
}
