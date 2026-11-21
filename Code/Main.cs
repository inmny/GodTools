using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GodTools.Abstract;
using NeoModLoader.api;
using UnityEngine;

namespace GodTools;

internal class Main : BasicMod<Main>, IReloadable
{
    public static Transform     game_ui_object_temp_library;

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
        Features.WorldScript.WorldScriptRecording.CheckExit();
        //pos_show_effect_controller.update(Time.fixedDeltaTime * C.pos_show_effect_time_scale);
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
        
        var dict = LocalizedTextManager.instance._localized_text;
        foreach (var k in dict.Keys.ToList())
        {
            dict[k.Underscore()] = dict[k];
        }
        LocalizedTextManager.updateTexts();
    }
}
