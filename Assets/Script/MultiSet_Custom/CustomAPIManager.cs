using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class CustomAPIManager : MonoBehaviour
{
    private string baseUrl = "https://api.multiset.ai";
    public string AccessToken { get; private set; }

    [Serializable]
    private class TokenResponse
    {
        public string accessToken;
        public string access_token;
        public string token;
    }

    [Serializable]
    public class MapItem
    {
        public string _id;
        public string mapName;
        public string mapCode;
        public string thumbnail;
        public double storage;
        public string createdAt;  
        public MapMesh mapMesh;  
    }

    [Serializable]
    public class MapMesh
    {
        public MeshInfo rawMesh;
        public MeshInfo texturedMesh;
    }

    [Serializable]
    public class MeshInfo
    {
        public string type;
        public string meshLink;
    }

    [Serializable]
    private class MapListResponse
    {
        public int totalCount;
        public List<MapItem> maps;
    }

    public IEnumerator Authenticate(string clientId, string clientSecret, Action onOk, Action<string> onErr)
    {
        var url = $"{baseUrl}/v1/m2m/token";
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());

            req.SetRequestHeader("Authorization", $"Basic {basic}");
            req.SetRequestHeader("Accept", "*/*");
            req.SetRequestHeader("Content-Type", "text/plain");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"Auth failed ({req.responseCode}): {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            var json = req.downloadHandler.text;
            var tr = JsonUtility.FromJson<TokenResponse>(json);

            AccessToken = !string.IsNullOrEmpty(tr.accessToken) ? tr.accessToken
                        : !string.IsNullOrEmpty(tr.access_token) ? tr.access_token
                        : tr.token;

            if (string.IsNullOrEmpty(AccessToken))
            {
                onErr?.Invoke($"Auth ok but token not found. Response:\n{json}");
                yield break;
            }

            Debug.Log("Auth success.");
            onOk?.Invoke();
        }
    }

    public IEnumerator GetMaps(Action<List<MapItem>> onOk, Action<string> onErr)
    {
        if (string.IsNullOrEmpty(AccessToken))
        {
            onErr?.Invoke("AccessToken is empty. Authenticate first.");
            yield break;
        }

        var url = $"{baseUrl}/v1/vps/map?page=1&limit=100";

        using (var req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            var json = req.downloadHandler.text;

            Debug.Log($"GetMaps response:\n{json}");

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"GetMaps failed ({req.responseCode}): {req.error}\n{json}");
                yield break;
            }

            var mlr = JsonUtility.FromJson<MapListResponse>(json);
            if (mlr == null || mlr.maps == null)
            {
                onErr?.Invoke($"Parse failed (mlr/maps is null). Raw:\n{json}");
                yield break;
            }

            foreach (var m in mlr.maps)
            {
                MapStorage.Save(m);
                Debug.Log($"Saved map: {(string.IsNullOrEmpty(m.mapName) ? m.mapCode : m.mapName)}");
            }

            onOk?.Invoke(mlr.maps);
        }
    }
}
