using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class ImportDB : MonoBehaviour
{
    private Dictionary<string, MarkStorage.MarkData> pendingImportedMarks;
    private string pendingImportedTitle;

    public bool HasPendingImport => pendingImportedMarks != null;
    public string PendingImportedTitle => pendingImportedTitle;

    public async Task<List<string>> GetSavedTitlesForMapAsync(string mapName)
    {
        var matchedTitles = new List<string>();
        if (string.IsNullOrEmpty(mapName))
        {
            return matchedTitles;
        }

        string json = await GetFirebaseJsonAsync("/Marks.json");
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return matchedTitles;
        }

        var root = JObject.Parse(json);
        foreach (var child in root.Properties())
        {
            JObject entry = child.Value as JObject;
            if (entry == null)
            {
                continue;
            }

            string savedMapName = ExtractMapName(entry["map"]);
            if (string.Equals(savedMapName, mapName, StringComparison.Ordinal))
            {
                matchedTitles.Add(child.Name);
            }
        }

        return matchedTitles;
    }

    public async Task QueueImportByTitleAsync(string title)
    {
        pendingImportedMarks = await LoadMarksByTitleAsync(title);
        pendingImportedTitle = title;
    }

    public void ApplyPendingImportedMarks(EditorManager editorManager)
    {
        if (pendingImportedMarks == null || editorManager == null)
        {
            return;
        }

        editorManager.LoadMarksFromData(pendingImportedMarks);
        pendingImportedMarks = null;
        pendingImportedTitle = null;
    }

    private async Task<Dictionary<string, MarkStorage.MarkData>> LoadMarksByTitleAsync(string title)
    {
        string safeTitle = UnityWebRequest.EscapeURL(title);
        string json = await GetFirebaseJsonAsync($"/Marks/{safeTitle}.json");

        var result = new Dictionary<string, MarkStorage.MarkData>();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return result;
        }

        var root = JObject.Parse(json);
        foreach (var child in root.Properties())
        {
            if (child.Name == "map")
            {
                continue;
            }

            JObject markObject = child.Value as JObject;
            if (markObject == null)
            {
                continue;
            }

            result[child.Name] = new MarkStorage.MarkData
            {
                label = child.Name,
                position = ReadVector3(markObject["position"] as JObject),
                scale = ReadVector3(markObject["scale"] as JObject, Vector3.one),
                margin = markObject["margin"]?.Value<float>() ?? 0f,
                angle1 = markObject["angle1"]?.Value<float>() ?? -30f,
                angle2 = markObject["angle2"]?.Value<float>() ?? 30f,
                keyword = markObject["keyword"]?.Value<string>() ?? string.Empty,
                details = markObject["details"]?.Value<string>() ?? string.Empty
            };
        }

        return result;
    }

    private async Task<string> GetFirebaseJsonAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(FirebaseConfig.DatabaseUrl))
        {
            throw new InvalidOperationException("FirebaseConfig.DatabaseUrl is null or empty.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("ImportDB Firebase request path is null or empty.");
        }

        string url = $"{FirebaseConfig.DatabaseUrl.TrimEnd('/')}{path}";
        using var request = UnityWebRequest.Get(url);
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception(request.error);
        }

        return request.downloadHandler.text;
    }

    private static Vector3 ReadVector3(JObject obj, Vector3? fallback = null)
    {
        Vector3 defaultValue = fallback ?? Vector3.zero;
        if (obj == null)
        {
            return defaultValue;
        }

        return new Vector3(
            obj["x"]?.Value<float>() ?? defaultValue.x,
            obj["y"]?.Value<float>() ?? defaultValue.y,
            obj["z"]?.Value<float>() ?? defaultValue.z);
    }

    private static string ExtractMapName(JToken token)
    {
        if (token == null)
        {
            return null;
        }

        if (token.Type == JTokenType.String)
        {
            return token.Value<string>();
        }

        JObject obj = token as JObject;
        if (obj == null)
        {
            return token.ToString();
        }

        string[] candidateKeys = { "name", "mapName", "title", "value" };
        foreach (string key in candidateKeys)
        {
            JToken value = obj[key];
            if (value != null && value.Type == JTokenType.String)
            {
                return value.Value<string>();
            }
        }

        foreach (var property in obj.Properties())
        {
            if (property.Value.Type == JTokenType.String)
            {
                return property.Value.Value<string>();
            }
        }

        return obj.ToString();
    }
}
