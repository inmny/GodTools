using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GodTools.Abstract;

public class MonoObjPool<T> where T : MonoBehaviour
{
    private readonly Action<T>            _active_action;
    private readonly Action<T>            _create_action;
    private readonly Action<T>            _deactive_action;
    private readonly Dictionary<int, int> _idx_dict;
    private readonly Transform            _parent;

    private readonly List<T> _pool;
    private readonly T       _prefab;
    private          int     _unused_first_idx;

    public MonoObjPool(T prefab, Transform parent, Action<T> create_action = null, Action<T> active_action = null,
                       Action<T> deactive_action = null)
    {
        _prefab = prefab;
        _parent = parent;
        _create_action = create_action;
        _active_action = active_action;
        _deactive_action = deactive_action;
        _pool = new List<T>();
        _idx_dict = new Dictionary<int, int>();
        _unused_first_idx = 0;
    }

    public IEnumerable<T> ActiveObjs => _pool.Take(_unused_first_idx);

    public void ResetToStart()
    {
        _unused_first_idx = 0;
    }

    public void ClearUnsed()
    {
        for (var i = _unused_first_idx; i < _pool.Count; i++)
        {
            T obj = _pool[i];
            obj.gameObject.SetActive(false);
            _deactive_action?.Invoke(obj);
        }
    }

    public void MarkUnused(Func<T, bool> check)
    {
        for (int i=0; i<_unused_first_idx; i++)
        {
            T obj = _pool[i];
            if (check(obj))
            {
                SwapTracked(i--, --_unused_first_idx);
            }
        }
    }
    public void Clear()
    {
        foreach (T obj in _pool)
        {
            obj.gameObject.SetActive(false);
            _deactive_action?.Invoke(obj);
        }

        _unused_first_idx = 0;
    }

    public void Return(T obj)
    {
        var instance_id = obj.GetInstanceID();
        if (!_idx_dict.TryGetValue(instance_id, out var idx))
        {
            obj.transform.SetParent(_parent);
            _pool.Add(obj);
            _idx_dict[instance_id] = _pool.Count - 1;
        }
        else
        {
            if (obj.transform.parent != _parent) obj.transform.SetParent(_parent);
            SwapTracked(idx, _unused_first_idx - 1);
            _unused_first_idx -= 1;
        }

        obj.gameObject.SetActive(false);
        _deactive_action?.Invoke(obj);
    }

    public T GetNext(int slibing_idx = -1, Transform parent = null)
    {
        T obj;
        var len = (parent ?? _parent).childCount;
        if (_pool.Count == 0 || _unused_first_idx == _pool.Count)
        {
            obj = Object.Instantiate(_prefab, parent ?? _parent);
            len++;
            obj.transform.SetSiblingIndex((slibing_idx + len) % len);
            _create_action?.Invoke(obj);
            if (!obj.gameObject.activeSelf) obj.gameObject.SetActive(true);

            _pool.Add(obj);
        }
        else
        {
            obj = _pool[_unused_first_idx];
            if (parent != null) obj.transform.SetParent(parent);

            obj.transform.SetSiblingIndex((slibing_idx + len) % len);
            obj.gameObject.SetActive(true);
        }

        _idx_dict[obj.GetInstanceID()] = _unused_first_idx;

        _unused_first_idx++;

        _active_action?.Invoke(obj);
        return obj;
    }

    private void SwapTracked(int first, int second)
    {
        if (first == second) return;
        var first_obj = _pool[first];
        var second_obj = _pool[second];
        _pool.Swap(first, second);
        _idx_dict[first_obj.GetInstanceID()] = second;
        _idx_dict[second_obj.GetInstanceID()] = first;
    }
}
