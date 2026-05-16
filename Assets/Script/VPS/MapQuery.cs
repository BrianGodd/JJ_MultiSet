using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiSet
{
    public class MapQuery : MonoBehaviour
    {
        [Header("Map Service")]
        [SerializeField] private MonoBehaviour mapServiceSource;

        [Header("Load DB")]
        [SerializeField] private TMP_Dropdown loadDbDropdown;
        [SerializeField] private Button loadDbButton;
        [SerializeField] private TextMeshProUGUI loadDbStatusText;
        [SerializeField] private EditorManager editorManager;
        [SerializeField] private ImportDB importDB;

        [Header("Map Cache")]
        [SerializeField] private bool enableMapCache = true;
        [SerializeField] private string mapCacheFolderName = "MapCache";

        private readonly List<string> matchedDbTitles = new List<string>();
        private IExternalMapService mapService;

        public event Action<float> OnProgress;

        private void Awake()
        {
            mapService = ResolveMapService();
            if (importDB == null)
            {
                importDB = FindObjectOfType<ImportDB>();
            }

            if (loadDbButton != null)
            {
                loadDbButton.onClick.RemoveListener(OnLoadDbButtonClicked);
                loadDbButton.onClick.AddListener(OnLoadDbButtonClicked);
            }

            SetLoadDbInteractable(false);
            SetLoadDbStatus("Load a map first");
        }

        public async Task<GameObject> LoadMapAsync(ExternalMapInfo mapInfo, Action<float> onProgress = null)
        {
            if (mapInfo == null)
            {
                Debug.LogError("LoadMapAsync: mapInfo is null");
                return null;
            }

            if (mapService == null)
            {
                Debug.LogError("LoadMapAsync: map service is not configured");
                return null;
            }

            string resolvedUrl;
            try
            {
                resolvedUrl = await mapService.ResolveMapDownloadUrlAsync(mapInfo);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoadMapAsync: failed to resolve url: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(resolvedUrl))
            {
                Debug.LogError("LoadMapAsync: resolved URL is null or empty");
                return null;
            }

            string mapName = string.IsNullOrEmpty(mapInfo.mapName) ? "ExternalMap" : mapInfo.mapName;
            var loadedMap = await LoadMapFromRemoteAsync(resolvedUrl, mapName, onProgress, mapInfo.mapCode);
            if (loadedMap != null)
            {
                await RefreshLoadDbOptionsAsync(mapName);
            }

            return loadedMap;
        }

        public async Task<GameObject> LoadMapFromURL(string meshUrl, string mapName = "ExternalMap", Action<float> onProgress = null, string mapCode = null)
        {
            if (string.IsNullOrEmpty(meshUrl))
            {
                Debug.LogError("LoadMapFromURL: meshUrl is null or empty");
                return null;
            }

            var loadedMap = await LoadMapFromRemoteAsync(meshUrl, mapName, onProgress, mapCode);
            if (loadedMap != null)
            {
                await RefreshLoadDbOptionsAsync(mapName);
            }

            return loadedMap;
        }

        public async Task<GameObject> LoadMapFromRemoteAsync(string meshUrl, string mapName = "ExternalMap", Action<float> onProgress = null, string mapCode = null)
        {
            if (string.IsNullOrEmpty(meshUrl))
            {
                Debug.LogError("LoadMapFromRemoteAsync: meshUrl is null or empty");
                return null;
            }

            try
            {
                string cachePath = GetMapCachePath(meshUrl, mapCode);
                if (enableMapCache && File.Exists(cachePath))
                {
                    byte[] cachedBytes = await File.ReadAllBytesAsync(cachePath);
                    onProgress?.Invoke(1f);
                    OnProgress?.Invoke(1f);
                    Debug.Log($"Loaded map from cache: {cachePath}");
                    return await LoadGlbFromBytesAsync(cachedBytes, mapName, meshUrl);
                }

                using var client = new HttpClient();
                using var response = await client.GetAsync(meshUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? -1L;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    totalRead += read;

                    if (contentLength > 0)
                    {
                        float progress = Mathf.Clamp01((float)totalRead / contentLength);
                        onProgress?.Invoke(progress);
                        OnProgress?.Invoke(progress);
                    }
                }

                var glbBytes = ms.ToArray();
                if (enableMapCache)
                {
                    await SaveMapCacheAsync(cachePath, glbBytes);
                }

                return await LoadGlbFromBytesAsync(glbBytes, mapName, meshUrl);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoadMapFromRemoteAsync failed: {ex.Message}");
                return null;
            }
        }

        public async Task<GameObject> LoadGlbFromBytesAsync(byte[] glbBytes, string objectName = "ExternalMap", string sourceUrl = null)
        {
            if (glbBytes == null || glbBytes.Length == 0)
            {
                Debug.LogError("LoadGlbFromBytesAsync: glbBytes is null or empty");
                return null;
            }

            var gltf = new GltfImport(null, null, null, new ConsoleLogger());

            var settings = new ImportSettings
            {
                GenerateMipMaps = true,
                AnisotropicFilterLevel = 4
            };

            Uri baseUri = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsedUri)
                ? parsedUri
                : new Uri("https://example.com/");

            bool success = await gltf.LoadGltfBinary(glbBytes, baseUri, settings);
            if (!success)
            {
                Debug.LogError("Failed to parse glb");
                return null;
            }

            var root = new GameObject(objectName);
            bool instantiated = await gltf.InstantiateMainSceneAsync(root.transform);
            if (!instantiated)
            {
                Debug.LogError("Failed to instantiate glTF scene");
                Destroy(root);
                return null;
            }

            return root;
        }

        public async Task RefreshLoadDbOptionsAsync(string mapName)
        {
            matchedDbTitles.Clear();

            if (loadDbDropdown != null)
            {
                loadDbDropdown.ClearOptions();
                loadDbDropdown.AddOptions(new List<string> { "No Data" });
                loadDbDropdown.value = 0;
                loadDbDropdown.RefreshShownValue();
            }

            SetLoadDbInteractable(false);

            if (string.IsNullOrEmpty(mapName))
            {
                SetLoadDbStatus("Map name is empty");
                return;
            }

            SetLoadDbStatus($"Loading saved data for {mapName}...");

            try
            {
                if (importDB == null)
                {
                    throw new InvalidOperationException("ImportDB not assigned.");
                }

                var titles = await importDB.GetSavedTitlesForMapAsync(mapName);
                matchedDbTitles.Clear();
                matchedDbTitles.AddRange(titles);
            }
            catch (Exception ex)
            {
                Debug.LogError($"RefreshLoadDbOptionsAsync failed: {ex.Message}");
                SetLoadDbStatus("Failed to read database");
                return;
            }

            if (loadDbDropdown != null)
            {
                loadDbDropdown.ClearOptions();
                if (matchedDbTitles.Count > 0)
                {
                    loadDbDropdown.AddOptions(matchedDbTitles);
                }
                else
                {
                    loadDbDropdown.AddOptions(new List<string> { "No Data" });
                }

                loadDbDropdown.value = 0;
                loadDbDropdown.RefreshShownValue();
            }

            bool hasData = matchedDbTitles.Count > 0;
            SetLoadDbInteractable(hasData);
            SetLoadDbStatus(hasData
                ? $"Found {matchedDbTitles.Count} saved item(s)"
                : $"No saved data for {mapName}");
        }

        private async void OnLoadDbButtonClicked()
        {
            if (loadDbDropdown == null || matchedDbTitles.Count == 0)
            {
                SetLoadDbStatus("No data available to import");
                return;
            }

            int index = Mathf.Clamp(loadDbDropdown.value, 0, matchedDbTitles.Count - 1);
            string title = matchedDbTitles[index];

            SetLoadDbInteractable(false);
            SetLoadDbStatus($"Importing {title}...");

            try
            {
                if (editorManager == null)
                {
                    editorManager = FindObjectOfType<EditorManager>();
                }

                if (importDB == null)
                {
                    importDB = FindObjectOfType<ImportDB>();
                }

                if (editorManager == null || importDB == null)
                {
                    Debug.LogError("MapQuery import failed: EditorManager or ImportDB not assigned.");
                    SetLoadDbStatus("Import dependencies not found");
                    return;
                }

                await importDB.QueueImportByTitleAsync(title);

                bool editorReady = editorManager.drawPanel != null && editorManager.drawPanel.gameObject.activeInHierarchy;
                if (editorReady)
                {
                    ApplyPendingImportedMarks();
                }
                else
                {
                    SetLoadDbStatus($"Queued {title}. Importing after Edit transition...");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"OnLoadDbButtonClicked failed: {ex}");
                SetLoadDbStatus("Import failed");
            }
            finally
            {
                SetLoadDbInteractable(matchedDbTitles.Count > 0);
            }
        }

        private string GetMapCachePath(string meshUrl, string mapCode)
        {
            string cacheDirectory = Path.Combine(Application.persistentDataPath, mapCacheFolderName);
            string fileName = GetCacheFileName(meshUrl, mapCode);
            return Path.Combine(cacheDirectory, fileName);
        }

        private string GetCacheFileName(string meshUrl, string mapCode)
        {
            if (!string.IsNullOrWhiteSpace(mapCode))
            {
                return SanitizeFileName(mapCode.Trim()) + ".glb";
            }

            return ComputeSha256(meshUrl) + ".glb";
        }

        private async Task SaveMapCacheAsync(string cachePath, byte[] glbBytes)
        {
            try
            {
                string directory = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(cachePath, glbBytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save map cache: {ex.Message}");
            }
        }

        public void ClearAllMapCache()
        {
            string cacheDirectory = Path.Combine(Application.persistentDataPath, mapCacheFolderName);
            if (!Directory.Exists(cacheDirectory))
            {
                Debug.Log($"Map cache directory does not exist: {cacheDirectory}");
                return;
            }

            try
            {
                Directory.Delete(cacheDirectory, true);
                Debug.Log($"Cleared all map cache: {cacheDirectory}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to clear map cache: {ex.Message}");
            }
        }

        public void ClearMapCacheByCode(string mapCode)
        {
            if (string.IsNullOrWhiteSpace(mapCode))
            {
                Debug.LogWarning("ClearMapCacheByCode called with empty mapCode.");
                return;
            }

            string cachePath = Path.Combine(
                Application.persistentDataPath,
                mapCacheFolderName,
                SanitizeFileName(mapCode.Trim()) + ".glb");

            if (!File.Exists(cachePath))
            {
                Debug.Log($"Map cache file does not exist: {cachePath}");
                return;
            }

            try
            {
                File.Delete(cachePath);
                Debug.Log($"Cleared map cache: {cachePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to clear map cache file: {ex.Message}");
            }
        }

        private static string ComputeSha256(string text)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] hashBytes;
            using (SHA256 sha = SHA256.Create())
            {
                hashBytes = sha.ComputeHash(inputBytes);
            }

            var sb = new StringBuilder(hashBytes.Length * 2);
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }

            return sb.ToString();
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "cached_map";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
            }

            string sanitized = sb.ToString();
            return string.IsNullOrWhiteSpace(sanitized) ? "cached_map" : sanitized;
        }

        public void ApplyPendingImportedMarks()
        {
            if (importDB == null)
            {
                return;
            }

            if (editorManager == null)
            {
                editorManager = FindObjectOfType<EditorManager>();
            }

            string importedTitle = importDB.PendingImportedTitle;
            importDB.ApplyPendingImportedMarks(editorManager);
            if (!importDB.HasPendingImport)
            {
                SetLoadDbStatus($"Imported {importedTitle}");
            }
        }

        private void SetLoadDbInteractable(bool interactable)
        {
            if (loadDbDropdown != null)
            {
                loadDbDropdown.interactable = interactable;
            }

            if (loadDbButton != null)
            {
                loadDbButton.interactable = interactable;
            }
        }

        private void SetLoadDbStatus(string message)
        {
            if (loadDbStatusText != null)
            {
                loadDbStatusText.text = message;
            }
        }

        private IExternalMapService ResolveMapService()
        {
            if (mapServiceSource is IExternalMapService serviceFromField)
            {
                return serviceFromField;
            }

            return GetComponent(typeof(IExternalMapService)) as IExternalMapService;
        }
    }
}
