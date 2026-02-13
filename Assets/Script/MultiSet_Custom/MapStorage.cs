using System.Collections.Generic;
using UnityEngine;

public static class MapStorage
{
    // key: mapName (fallback to mapCode if mapName is empty)
    public static Dictionary<string, CustomAPIManager.MapItem> Maps { get; } = new Dictionary<string, CustomAPIManager.MapItem>();

    public static void Save(CustomAPIManager.MapItem item)
    {
        if (item == null) return;

        var key = !string.IsNullOrEmpty(item.mapName) ? item.mapName : item.mapCode;
        if (string.IsNullOrEmpty(key)) return;

        Maps[key] = item;
    }

    public static bool TryGet(string mapName, out CustomAPIManager.MapItem item)
    {
        return Maps.TryGetValue(mapName, out item);
    }

    public static void Clear()
    {
        Maps.Clear();
    }
}
