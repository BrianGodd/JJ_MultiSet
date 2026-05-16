using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using MultiSet;

public class MultiSetMapService : MonoBehaviour, IExternalMapService
{
    [SerializeField] private string baseUrl = "https://api.multiset.ai";

    public string AccessToken { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    [Serializable]
    private class TokenResponse
    {
        public string accessToken;
        public string access_token;
        public string token;
    }

    [Serializable]
    private class MapItem
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
    private class MapMesh
    {
        public MeshInfo rawMesh;
        public MeshInfo texturedMesh;
    }

    [Serializable]
    private class MeshInfo
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

    [Serializable]
    private class FileUrlResponse
    {
        public string url;
    }

    public async Task AuthenticateAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        var url = $"{baseUrl}/v1/m2m/token";
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        using var req = new UnityWebRequest(url, "POST");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
        req.SetRequestHeader("Authorization", $"Basic {basic}");
        req.SetRequestHeader("Accept", "*/*");
        req.SetRequestHeader("Content-Type", "text/plain");

        string json = await SendForTextAsync(req, cancellationToken);
        var tr = JsonUtility.FromJson<TokenResponse>(json);

        AccessToken = !string.IsNullOrEmpty(tr?.accessToken) ? tr.accessToken
            : !string.IsNullOrEmpty(tr?.access_token) ? tr.access_token
            : tr?.token;

        if (string.IsNullOrEmpty(AccessToken))
        {
            throw new Exception($"Auth ok but token not found. Response:\n{json}");
        }

        Debug.Log("MultiSet auth success.");
    }

    public async Task<IReadOnlyList<ExternalMapInfo>> GetMapsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("AccessToken is empty. Authenticate first.");
        }

        var url = $"{baseUrl}/v1/vps/map?page=1&limit=100";

        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
        req.SetRequestHeader("Accept", "application/json");

        string json = await SendForTextAsync(req, cancellationToken);
        Debug.Log($"GetMaps response:\n{json}");

        var mlr = JsonUtility.FromJson<MapListResponse>(json);
        if (mlr?.maps == null)
        {
            throw new Exception($"Parse failed (mlr/maps is null). Raw:\n{json}");
        }

        var result = new List<ExternalMapInfo>(mlr.maps.Count);
        MapStorage.Clear();

        foreach (var item in mlr.maps)
        {
            var mapInfo = ToExternalMapInfo(item);
            result.Add(mapInfo);
            MapStorage.Save(mapInfo);
            Debug.Log($"Saved map: {mapInfo.DisplayName}");
        }

        return result;
    }

    public Task<string> ResolveMapDownloadUrlAsync(ExternalMapInfo mapInfo, CancellationToken cancellationToken = default)
    {
        if (mapInfo == null)
        {
            throw new ArgumentNullException(nameof(mapInfo));
        }

        if (string.IsNullOrWhiteSpace(mapInfo.meshLink))
        {
            throw new Exception($"Map '{mapInfo.DisplayName}' has no mesh link.");
        }

        if (Uri.TryCreate(mapInfo.meshLink, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return Task.FromResult(mapInfo.meshLink);
        }

        var tcs = new TaskCompletionSource<string>();

        try
        {
            MultiSetApiManager.GetFileUrl(mapInfo.meshLink, (success, data, status) =>
            {
                if (!success)
                {
                    tcs.TrySetException(new Exception($"GetFileUrl failed ({status}): {data}"));
                    return;
                }

                try
                {
                    var fd = JsonUtility.FromJson<FileUrlResponse>(data);
                    if (fd == null || string.IsNullOrEmpty(fd.url))
                    {
                        tcs.TrySetException(new Exception($"GetFileUrl returned invalid data: {data}"));
                        return;
                    }

                    tcs.TrySetResult(fd.url);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    private static ExternalMapInfo ToExternalMapInfo(MapItem item)
    {
        return new ExternalMapInfo
        {
            id = item._id,
            mapName = item.mapName,
            mapCode = item.mapCode,
            thumbnailUrl = item.thumbnail,
            storageInMb = item.storage,
            createdAt = item.createdAt,
            meshLink = item.mapMesh?.texturedMesh?.meshLink ?? item.mapMesh?.rawMesh?.meshLink
        };
    }

    private static async Task<string> SendForTextAsync(UnityWebRequest request, CancellationToken cancellationToken)
    {
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"{request.error}\n{request.downloadHandler.text}");
        }

        return request.downloadHandler.text;
    }
}
