using System.Collections.Generic;
using UnityEngine;

public static class MapStorage
{
    // key: mapName (fallback to mapCode if mapName is empty)
    public static Dictionary<string, ExternalMapInfo> Maps { get; } = new Dictionary<string, ExternalMapInfo>();

    public static void Save(ExternalMapInfo item)
    {
        if (item == null) return;

        var key = !string.IsNullOrEmpty(item.mapName) ? item.mapName : item.mapCode;
        if (string.IsNullOrEmpty(key)) return;

        Maps[key] = item;
    }

    public static bool TryGet(string mapName, out ExternalMapInfo item)
    {
        return Maps.TryGetValue(mapName, out item);
    }

    public static void Clear()
    {
        Maps.Clear();
    }
}
