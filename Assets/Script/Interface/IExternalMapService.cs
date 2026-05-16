using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Contract for integrating external map providers into the shared map-loading flow.
/// Implementations are responsible for:
/// 1. Authenticating against the provider.
/// 2. Querying the provider's available map list.
/// 3. Converting provider-specific file identifiers or asset references into a downloadable map URL.
///
/// The rest of the application should depend on this interface instead of any provider-specific API format.
/// This keeps UI, download, and loading logic reusable when switching from one map platform to another.
/// </summary>
public interface IExternalMapService
{
    bool IsAuthenticated { get; }

    Task AuthenticateAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalMapInfo>> GetMapsAsync(CancellationToken cancellationToken = default);
    Task<string> ResolveMapDownloadUrlAsync(ExternalMapInfo mapInfo, CancellationToken cancellationToken = default);
}

[System.Serializable]
/// <summary>
/// Normalized map metadata shared by all external map service implementations.
/// Each provider should map its own response model into this type before passing data
/// to the rest of the application.
/// </summary>
public class ExternalMapInfo
{
    public string id;
    public string mapName;
    public string mapCode;
    public string thumbnailUrl;
    public double storageInMb;
    public string createdAt;
    public string meshLink;

    public string DisplayName => !string.IsNullOrEmpty(mapName) ? mapName : mapCode;
}
