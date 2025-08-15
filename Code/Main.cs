using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GodTools.Abstract;
using GodTools.Features;
using NeoModLoader.api;
using NeoModLoader.utils;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GodTools;

internal class Main : BasicMod<Main>, IReloadable
{
    public static Transform     game_ui_object_temp_library;
    public static TMP_FontAsset default_font;

    public static Transform prefabs;

/*
    public PosShowEffect actor_select_effect;

    private bool initialized;
    private PosShowEffect last_pos_show;

    public SimpleEffectController<PosShowEffect> pos_show_effect_controller =
        new(Resources.Load<GameObject>("effects/PrefabUnitSelectionEffect"));
*/
    public static ModDeclare Mod { get; private set; }

    private void Update()
    {
        //pos_show_effect_controller.update(Time.fixedDeltaTime * C.pos_show_effect_time_scale);
        MoveActorWhenFollowing.Update();
    }

    public void Reload()
    {
    }

    private static List<Type> SortManagerTypes(List<Type> types)
    {
        var graph = new Dictionary<Type, List<Type>>();
        var in_degree = new Dictionary<Type, int>();

        foreach (Type type in types)
        {
            graph[type] = new List<Type>();
            in_degree[type] = 0;
        }

        foreach (Type type in types)
        {
            DependencyAttribute attribute = type.GetCustomAttributes(typeof(DependencyAttribute), false)
                .Cast<DependencyAttribute>()
                .FirstOrDefault();

            if (attribute == null) continue;
            foreach (Type dependency in attribute.Types)
            {
                if (!types.Contains(dependency)) continue;
                graph[dependency].Add(type);
                in_degree[type]++;
            }
        }

        var sorted = new List<Type>();
        var queue = new Queue<Type>(in_degree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        while (queue.Count > 0)
        {
            Type current = queue.Dequeue();
            sorted.Add(current);

            foreach (Type dependent in graph[current])
            {
                in_degree[dependent]--;
                if (in_degree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (sorted.Count != types.Count()) throw new InvalidOperationException("Circular dependency detected.");

        return sorted;
    }

    protected override void OnModLoad()
    {
        Mod = GetDeclaration();
        prefabs = PrefabLibrary;
        prefabs.localPosition = new Vector3(9999999, 9999999, 0);

        game_ui_object_temp_library = new GameObject("Game UI Objects").transform;
        game_ui_object_temp_library.SetParent(transform);
        game_ui_object_temp_library.localPosition = new Vector3(9999999, 9999999, 0);

        AssetBundle tmp_bundle =
            AssetBundle.LoadFromFile(Path.Combine(Mod.FolderPath, "ABPackages/tmp_essential_resources"));
        var asset_paths = tmp_bundle.GetAllAssetNames();
        foreach (var path in asset_paths)
        {
            Object asset = tmp_bundle.LoadAsset(path);
            var target_path = path.Replace("assets/textmesh pro/", "");
            target_path = target_path.Replace("resources/", "");
            target_path = target_path.Substring(0, target_path.LastIndexOf('.'));
            LogInfo($"Patch resource: \"{path}\" to \"{target_path}\"");
            ResourcesPatch.PatchResource(target_path, asset);
        }

        default_font = Resources.Load<TMP_FontAsset>("fonts & materials/notosanssc-regular sdf");

        var manager_types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IManager).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();
        manager_types = SortManagerTypes(manager_types);
        foreach (Type t in manager_types)
            try
            {
                var manager = (IManager)Activator.CreateInstance(t, true);
                manager?.Initialize();
                LogInfo($"Initialize manager: {t.FullName}(null?{manager == null})");
            }
            catch (Exception e)
            {
                LogWarning($"Failed to initialize manager: {t.FullName}\n{e.Message}\n{e.StackTrace}");
            }
    }
}