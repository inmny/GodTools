using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Utils;
using Newtonsoft.Json.Linq;

namespace GodTools.Features;

internal static class SavedCreatureSubspecies
{
    internal static long Resolve(MapBox world, string species, SubspeciesData saved)
    {
        if (saved?.species_id != species || AssetManager.actor_library.get(species)?.can_have_subspecies != true) return -1;

        // 克隆定义时保留原记录。移除缺失的特质，缺失的基因用空位填补以保留基因位置。
        var definition = saved.Copy();
        definition.saved_traits?.RemoveAll(id => AssetManager.subspecies_traits.get(id) == null);
        definition.saved_actor_birth_traits?.RemoveAll(id => AssetManager.traits.get(id) == null);
        if (definition.saved_chromosome_data is { } chromosomes)
        {
            foreach (var chromosome in chromosomes)
            {
                if (chromosome?.loci == null || AssetManager.chromosome_type_library.get(chromosome.chromosome_type) == null ||
                    chromosome.super_loci == null || chromosome.void_loci == null) return -1;
                for (int index = 0; index < chromosome.loci.Count; index++)
                    if (AssetManager.gene_library.get(chromosome.loci[index]) == null) chromosome.loci[index] = "empty";
            }
        }

        var genome = JArray.FromObject(definition.saved_chromosome_data ?? new List<ChromosomeData>());
        bool Matches(Subspecies current) => current?.isAlive() == true && current.isSpecies(species) &&
            current.data.skin_id == definition.skin_id && current.data.biome_variant == definition.biome_variant &&
            current.data.mutation == definition.mutation &&
            SameTraits(definition.saved_traits, current.getTraitsAsStrings()) &&
            SameTraits(definition.saved_actor_birth_traits, current.getActorBirthTraits().getTraitsAsStrings()) &&
            JToken.DeepEquals(genome, JArray.FromObject(current.nucleus.getListForSave()));

        var originalId = world.subspecies.get(saved.id);
        if (Matches(originalId)) return originalId.id;
        foreach (var current in world.subspecies)
            if (current != originalId && Matches(current)) return current.id;

        return world.subspecies.loadObject(new SubspeciesData
        {
            id = world.map_stats.getNextId("subspecies"), created_time = world.getCurWorldTime(),
            name = definition.name, custom_name = definition.custom_name,
            color_id = definition.color_id, banner_background_id = definition.banner_background_id,
            species_id = definition.species_id, skin_id = definition.skin_id, biome_variant = definition.biome_variant,
            mutation = definition.mutation, saved_traits = definition.saved_traits,
            saved_actor_birth_traits = definition.saved_actor_birth_traits, saved_chromosome_data = definition.saved_chromosome_data
        }).id;
    }

    private static bool SameTraits(List<string> saved, List<string> current) =>
        new HashSet<string>(saved ?? Enumerable.Empty<string>(), StringComparer.Ordinal)
            .SetEquals(current ?? Enumerable.Empty<string>());
}
