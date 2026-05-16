using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ExampleMapService : MonoBehaviour, IExternalMapService
{
    // Replace this with the base URL of the provider you want to integrate.
    [SerializeField] private string baseUrl = "https://api.example.com";

    public string AccessToken { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public Task AuthenticateAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Replace this block with the provider's real authentication flow.
        // Store any token or session data in AccessToken so IsAuthenticated works consistently.
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new Exception("clientId or clientSecret is empty.");
        }

        AccessToken = "example-token";
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExternalMapInfo>> GetMapsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("AccessToken is empty. Authenticate first.");
        }

        // Replace this sample data with the provider's map-list API response.
        // The key step is mapping the provider's fields into ExternalMapInfo.
        IReadOnlyList<ExternalMapInfo> maps = new List<ExternalMapInfo>
        {
            new ExternalMapInfo
            {
                id = "example-map-id",
                mapName = "Example Map",
                mapCode = "EXAMPLE001",
                createdAt = "2026-04-09T00:00:00Z",
                storageInMb = 10,
                meshLink = $"{baseUrl}/maps/example-map.glb"
            }
        };

        return Task.FromResult(maps);
    }

    public Task<string> ResolveMapDownloadUrlAsync(ExternalMapInfo mapInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mapInfo == null)
        {
            throw new ArgumentNullException(nameof(mapInfo));
        }

        if (string.IsNullOrWhiteSpace(mapInfo.meshLink))
        {
            throw new Exception($"Map '{mapInfo.DisplayName}' has no mesh link.");
        }

        // If the provider already returns a direct download URL, return it here.
        // Otherwise, replace this with a request that converts a file ID or asset key into a real URL.
        return Task.FromResult(mapInfo.meshLink);
    }
}
