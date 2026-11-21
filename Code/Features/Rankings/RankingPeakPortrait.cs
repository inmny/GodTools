using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GodTools.Features.Rankings;

[JsonObject(MemberSerialization.OptIn)]
internal sealed class RankingPeakPortrait
{
    private const int MaxSide = 96;
    private const int MaxBytes = 65536;
    [JsonProperty] private byte[] Png;
    private Sprite _sprite;
    private bool _decoded;

    internal static RankingPeakPortrait Capture(Actor actor)
    {
        Sprite frame;
        var asset = actor.asset;
        if (asset.has_override_avatar_frames) frame = asset.get_override_avatar_frames(actor)?.FirstOrDefault();
        else if (asset.has_override_sprite) frame = asset.get_override_sprite(actor);
        else
        {
            var appearance = new ActorAvatarData();
            appearance.setData(actor);
            frame = RenderFrame(appearance, actor.getUnitTexturePath());
        }

        frame ??= actor.getLastColoredSprite();
        if (frame == null) return null;
        var source = frame.texture;
        var area = frame.textureRect;
        if (area.width <= 0 || area.height <= 0 || source == null) return null;

        float scale = Mathf.Min(1, MaxSide / Mathf.Max(area.width, area.height));
        int width = Mathf.Max(1, Mathf.RoundToInt(area.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(area.height * scale));
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false, !source.isDataSRGB);
        try
        {
            if (!CopyPixels(source, area, pixels)) return null;
            var png = pixels.EncodeToPNG();
            return png?.Length is > 0 and <= MaxBytes ? new RankingPeakPortrait { Png = png } : null;
        }
        finally
        {
            Object.Destroy(pixels);
        }
    }

    private static bool CopyPixels(Texture2D source, Rect area, Texture2D portrait)
    {
        // The game fills the current atlas in CPU memory and uploads it later in the frame.
        var units = DynamicSpritesLibrary.units.getAtlas();
        var boats = DynamicSpritesLibrary.boats.getAtlas();
        var atlasPixels = source == units.texture ? units.pixels : source == boats.texture ? boats.pixels : null;
        if (atlasPixels != null)
        {
            var pixels = new Color32[portrait.width * portrait.height];
            for (int y = 0; y < portrait.height; y++)
            {
                int row = (int)(area.y + (y + 0.5f) * area.height / portrait.height) * source.width;
                for (int x = 0; x < portrait.width; x++)
                    pixels[y * portrait.width + x] = atlasPixels[row + (int)(area.x + (x + 0.5f) * area.width / portrait.width)];
            }
            if (!pixels.Any(pixel => pixel.a != 0)) return false;
            portrait.SetPixels32(pixels);
            return true;
        }

        var restore = RenderTexture.active;
        var target = RenderTexture.GetTemporary(portrait.width, portrait.height, 0, RenderTextureFormat.ARGB32,
            source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(source, target, new Vector2(area.width / source.width, area.height / source.height),
                new Vector2(area.x / source.width, area.y / source.height));
            RenderTexture.active = target;
            portrait.ReadPixels(new Rect(0, 0, portrait.width, portrait.height), 0, 0, false);
            return portrait.GetPixels32().Any(pixel => pixel.a != 0);
        }
        finally
        {
            RenderTexture.active = restore;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    internal Sprite GetSprite()
    {
        if (_decoded) return _sprite;
        _decoded = true;
        if (Png == null || Png.Length < 24 || Png.Length > MaxBytes) return null;
        // Check the PNG header dimensions before decoding a portrait from disk.
        if (Png[0] != 137 || Png[1] != 80 || Png[2] != 78 || Png[3] != 71 ||
            Png[12] != 'I' || Png[13] != 'H' || Png[14] != 'D' || Png[15] != 'R' ||
            !SmallSide(16) || !SmallSide(20)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        try
        {
            if (!texture.LoadImage(Png, true)) return null;
            _sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            _sprite.name = "RankingPeakPortrait";
            return _sprite;
        }
        catch (UnityException)
        {
            return null;
        }
        finally
        {
            if (_sprite == null) Object.Destroy(texture);
        }

        bool SmallSide(int offset) => Png[offset] == 0 && Png[offset + 1] == 0 && Png[offset + 2] == 0 &&
            Png[offset + 3] is > 0 and <= MaxSide;
    }

    internal void Release()
    {
        if (_sprite == null) return;
        Object.Destroy(_sprite.texture);
        Object.Destroy(_sprite);
        _sprite = null;
        _decoded = false;
    }

    internal static Sprite FromSavedCreature(RankingPeak peak)
    {
        var creature = peak.Creature;
        var unit = creature?.ActorData;
        if (string.IsNullOrEmpty(unit?.asset_id)) return null;
        var asset = AssetManager.actor_library.get(unit.asset_id);
        if (asset == null || asset.has_override_sprite || asset.has_override_avatar_frames || asset.is_boat) return null;
        try
        {
            var subspecies = World.world.subspecies.get(unit.subspecies);
            var mutation = creature.SubspeciesData?.saved_traits?.Select(AssetManager.subspecies_traits.get)
                .FirstOrDefault(trait => trait?.is_mutation_skin == true);
            int age = Date.getYear0(Math.Max(0, peak.Time - unit.created_time)) + unit.age_overgrowth;
            return RenderFrame(new ActorAvatarData
            {
                asset = asset, mutation_skin_asset = mutation, actor_id = unit.id, head_id = unit.head,
                sex = unit.sex, phenotype_index = unit.phenotype_index, phenotype_skin_shade = unit.phenotype_shade,
                is_adult = !asset.has_baby_form || subspecies == null || age >= subspecies.age_adult,
                is_king = unit.profession == UnitProfession.King, is_wise = unit.saved_traits?.Contains("wise") == true,
                kingdom_color = World.world.kingdoms.get(unit.civ_kingdom_id)?.getColor() ??
                    World.world.kingdoms_wild.get(asset.kingdom_id_wild)?.getColor() ??
                    AssetManager.kingdom_colors_library.list.FirstOrDefault()
            });
        }
        catch (Exception)
        {
            // Old records can refer to resources from mods that are no longer installed.
            return null;
        }
    }

    private static Sprite RenderFrame(ActorAvatarData appearance, string texturePath = null)
    {
        var texture = appearance.getTextureAsset();
        if (texture == null || appearance.is_egg && appearance.egg_asset == null) return null;
        var frames = DynamicActorSpriteCreatorUI.getContainerForUI(appearance.asset, appearance.is_adult, texture,
            appearance.mutation_skin_asset, appearance.is_egg, appearance.egg_asset, texturePath);
        var frame = frames?.idle?.frames?.FirstOrDefault() ?? frames?.walking?.frames?.FirstOrDefault();
        return frame == null ? null : appearance.getColoredSprite(frame, frames);
    }
}
