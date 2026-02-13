using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// glTFast
using GLTFast;
using GLTFast.Logging;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MultiSet
{
    public class MapQuery : MonoBehaviour
    {

        #region MAP-Download

        public event Action<float> OnProgress;

        [Serializable]
        private class FileData
        {
            public string url;
        }

        // Accept onProgress and forward to the download routine
        public async Task<GameObject> LoadMapFromURL(string meshUrl, string mapName = "MultiSetMap", Action<float> onProgress = null)
        {
            if (string.IsNullOrEmpty(meshUrl))
            {
                Debug.LogError("LoadMapFromURL: meshUrl is null or empty");
                return null;
            }

            string resolvedUrl = meshUrl;
            try
            {
                resolvedUrl = await ResolveFileUrlAsync(meshUrl);
                if (string.IsNullOrEmpty(resolvedUrl))
                {
                    Debug.LogError("LoadMapFromURL: resolved URL is null or empty");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoadMapFromURL: failed to resolve url: {ex.Message}");
                return null;
            }

            // Now load from resolvedUrl and pass progress callback
            return await LoadMapFromMultiSetAsync(resolvedUrl, mapName, onProgress);
        }

        // Helper to convert callback-based GetFileUrl into Task<string>
        private Task<string> ResolveFileUrlAsync(string fileKey)
        {
            var tcs = new TaskCompletionSource<string>();

            try
            {
                MultiSetApiManager.GetFileUrl(fileKey, (success, data, status) =>
                {
                    if (!success)
                    {
                        tcs.TrySetException(new Exception($"GetFileUrl failed ({status}): {data}"));
                        return;
                    }

                    try
                    {
                        var fd = JsonUtility.FromJson<FileData>(data);
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

            return tcs.Task;
        }

        // Public so other classes (CustomUIManager) can call and await it.
        public async Task<GameObject> LoadMapFromMultiSetAsync(string meshUrl, string mapName = "MultiSetMap", Action<float> onProgress = null)
        {
            if (string.IsNullOrEmpty(meshUrl))
            {
                Debug.LogError("LoadMapFromMultiSetAsync: meshUrl is null or empty");
                return null;
            }

            try
            {
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
                        float progress = Mathf.Clamp01((float)totalRead / (float)contentLength);
                        if (onProgress != null)
                        {
                            onProgress(progress);
                        }
                    }
                }

                var glbBytes = ms.ToArray();

                return await LoadGlbFromBytesAsync(glbBytes, mapName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoadMapFromMultiSetAsync failed: {ex.Message}");
                return null;
            }
        }

        // Public to allow awaited instantiation from other classes if needed.
        public async Task<GameObject> LoadGlbFromBytesAsync(byte[] glbBytes, string objectName = "MultiSetMap")
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

            bool success = await gltf.LoadGltfBinary(
                glbBytes,
                new Uri("https://api.multiset.ai/"),
                settings
            );

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
                UnityEngine.Object.Destroy(root);
                return null;
            }

            return root;
        }


        #endregion
    }
}