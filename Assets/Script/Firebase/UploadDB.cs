using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class UploadDB : MonoBehaviour
{
    void Start()
    {
        
    }

    // Public entry: upload all marks currently in MarkStorage.Marks to Firebase under /Marks
    public void UploadAllMarks()
    {
        StartCoroutine(UploadAllMarksCoroutine());
    }

    // Public entry: upload all marks under a "title" node.
    // Resulting DB structure: /Marks/{title}/{label} : { mark data }
    public void UploadMark(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            Debug.LogWarning("UploadMark called with null/empty title");
            return;
        }
        StartCoroutine(UploadMarkCoroutine(title));
    }

    private IEnumerator UploadAllMarksCoroutine()
    {
        if (MarkStorage.Marks == null || MarkStorage.Marks.Count == 0)
        {
            Debug.Log("UploadAllMarks: no marks to upload.");
            yield break;
        }

        var payload = new Dictionary<string, object>();

        foreach (var kv in MarkStorage.Marks)
        {
            var m = kv.Value;
            var entry = new Dictionary<string, object>
            {
                { "position", new Dictionary<string, float> {
                        { "x", m.position.x }, { "y", m.position.y }, { "z", m.position.z }
                    }
                },
                { "scale", new Dictionary<string, float> {
                        { "x", m.scale.x }, { "y", m.scale.y }, { "z", m.scale.z }
                    }
                },
                { "margin", m.margin },
                { "angle1", m.angle1 },
                { "angle2", m.angle2 },
                { "keyword", m.keyword ?? string.Empty },
                { "details", m.details ?? string.Empty }
            };

            payload[kv.Key] = entry;
        }

        string json = JsonConvert.SerializeObject(payload);
        string path = "/Marks.json";

        using (UnityWebRequest www = UnityWebRequest.Put(FirebaseConfig.DatabaseUrl + path, json))
        {
            www.method = UnityWebRequest.kHttpVerbPUT;
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"UploadAllMarks failed: {www.error}  Response: {www.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"UploadAllMarks succeeded. Uploaded {MarkStorage.Marks.Count} marks.");
            }
        }
    }

    private IEnumerator UploadMarkCoroutine(string title)
    {
        if (MarkStorage.Marks == null || MarkStorage.Marks.Count == 0)
        {
            Debug.Log("UploadMark: no marks to upload.");
            yield break;
        }

        var payload = new Dictionary<string, object>();

        string mapName = string.Empty;
        var uiManager = FindObjectOfType<CustomUIManager>();
        if (uiManager != null && uiManager.mapDropdown != null && uiManager.mapDropdown.options.Count > 0)
        {
            int selectedIndex = Mathf.Clamp(uiManager.mapDropdown.value, 0, uiManager.mapDropdown.options.Count - 1);
            mapName = uiManager.mapDropdown.options[selectedIndex].text;
        }

        if (!string.IsNullOrEmpty(mapName))
        {
            payload["map"] = mapName;
        }

        foreach (var kv in MarkStorage.Marks)
        {
            var m = kv.Value;
            var entry = new Dictionary<string, object>
            {
                { "position", new Dictionary<string, float> {
                        { "x", m.position.x }, { "y", m.position.y }, { "z", m.position.z }
                    }
                },
                { "scale", new Dictionary<string, float> {
                        { "x", m.scale.x }, { "y", m.scale.y }, { "z", m.scale.z }
                    }
                },
                { "margin", m.margin },
                { "angle1", m.angle1 },
                { "angle2", m.angle2 },
                { "keyword", m.keyword ?? string.Empty },
                { "details", m.details ?? string.Empty }
            };

            payload[kv.Key] = entry;
        }

        string json = JsonConvert.SerializeObject(payload);
        string safeTitle = UnityWebRequest.EscapeURL(title);
        string path = $"/Marks/{safeTitle}.json";

        using (UnityWebRequest www = UnityWebRequest.Put(FirebaseConfig.DatabaseUrl + path, json))
        {
            www.method = UnityWebRequest.kHttpVerbPUT;
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"UploadMark '{title}' failed: {www.error}  Response: {www.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"UploadMark '{title}' succeeded. Uploaded {MarkStorage.Marks.Count} labels under '{title}'.");
            }
        }
    }
}
