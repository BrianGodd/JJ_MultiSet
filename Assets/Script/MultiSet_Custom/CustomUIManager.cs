using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MultiSet;
using System.Threading.Tasks;

public class CustomUIManager : MonoBehaviour
{
    public MapQuery mapQuery;
    public CameraManager cameraManager;

    public GameObject AuthPanel, MapPanel, PreviewPanel, TransitionPanel;
    public GameObject MainCanvas, EditCanvas, DrawCanvas, ToolManager;

    // Auth UI
    public TMP_InputField clientIdInput;
    public TMP_InputField clientSecretInput;
    public Button fetchButton;

    // Map UI
    public TextMeshProUGUI mapInfo;
    public Button downloadButton;

    // Preview UI
    public Button editButton;

    // Output
    public TextMeshProUGUI outputText;

    public TMP_Dropdown mapDropdown;
    public CustomAPIManager api;

    public GameObject currentMapObject;

    private void Awake()
    {
        if(currentMapObject != null)
            cameraManager.UpdateTargetGroupFromRoot(currentMapObject);

        fetchButton.onClick.AddListener(Fetch);
        downloadButton.onClick.AddListener(OnDownloadButtonClicked);
        editButton.onClick.AddListener(OnEditButtonClicked);
    }

    private void Fetch()
    {
        outputText.text = "Authenticating...\n";

        StartCoroutine(api.Authenticate(
            clientIdInput.text.Trim(),
            clientSecretInput.text.Trim(),
            onOk: () => StartCoroutine(FetchMaps()),
            onErr: err => outputText.text = err
        ));

        AuthPanel.SetActive(false);
    }

    private IEnumerator FetchMaps()
    {
        outputText.text += "Token OK. Fetching maps...\n";

        List<CustomAPIManager.MapItem> maps = null;
        string err = null;

        yield return api.GetMaps(
            onOk: list => maps = list,
            onErr: e => err = e
        );

        if (!string.IsNullOrEmpty(err))
        {
            outputText.text = err;
            yield break;
        }

        // 從 MapStorage 取得已儲存的 maps（MapStorage 的 key 為 mapName 或 fallback mapCode）
        var stored = MapStorage.Maps.Values
            .Where(m => m != null && (!string.IsNullOrEmpty(m.mapName) || !string.IsNullOrEmpty(m.mapCode)))
            .OrderBy(m => string.IsNullOrEmpty(m.mapName) ? m.mapCode : m.mapName)
            .ToList();

        var optionLabels = stored
            .Select(m => string.IsNullOrEmpty(m.mapName) ? m.mapCode : m.mapName)
            .ToList();

        var codes = stored
            .Where(m => !string.IsNullOrEmpty(m.mapCode))
            .Select(m => m.mapCode)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // 顯示文字
        var sb = new StringBuilder();
        sb.AppendLine($"Total maps: {stored.Count}");
        sb.AppendLine($"Total mapCodes: {codes.Count}");
        sb.AppendLine();
        foreach (var c in codes) sb.AppendLine(c);
        outputText.text = sb.ToString();

        if (mapDropdown != null)
        {
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(optionLabels);
        }

        // mapinfo = first mapDropdown.options, include mapName, mapCode, createdAt
        if (mapDropdown != null && stored.Count > 0)
        {
            mapDropdown.onValueChanged.RemoveAllListeners();
            mapDropdown.onValueChanged.AddListener(index =>
            {
                var selectedMap = stored[index];
                mapInfo.text = $"Map Name: {selectedMap.mapName}\nMap Code: {selectedMap.mapCode}\nCreated At: {selectedMap.createdAt}";
            });
            // Trigger initial display
            mapDropdown.onValueChanged.Invoke(0);
        }
        MapPanel.SetActive(true);
    }

    public async void OnDownloadButtonClicked()
    {
        if (mapDropdown == null || mapDropdown.options.Count == 0) return;

        MapPanel.SetActive(false);

        var selectedIndex = mapDropdown.value;
        var selectedOption = mapDropdown.options[selectedIndex].text;

        if (!MapStorage.TryGet(selectedOption, out var mapItem))
        {
            Debug.LogError($"Map '{selectedOption}' not found in MapStorage.");
            outputText.text = $"Map '{selectedOption}' not found in MapStorage.\n";
            return;
        }

        var meshUrl = mapItem.mapMesh?.texturedMesh?.meshLink;
        Debug.Log($"Downloading map mesh from URL: {meshUrl} ...");
        outputText.text = $"Downloading map mesh from URL: {meshUrl} ...\n";

        var mapObject = await mapQuery.LoadMapFromURL(meshUrl, mapItem.mapName, progress =>
        {
            int downloadedMB = Mathf.RoundToInt(((float)mapItem.storage * progress));
            int totalMB = Mathf.RoundToInt((float)mapItem.storage);

            outputText.text =
                $"Downloading map mesh: {downloadedMB}/{totalMB} MB ({progress * 100f:F2}%)\n";
        });

        if (mapObject != null)
        {
            Debug.Log($"Map '{mapItem.mapName}' loaded successfully.");
            outputText.text = $"Map '{mapItem.mapName}' loaded successfully.\n";
        }
        else
        {
            Debug.LogError($"Failed to load map '{selectedOption}'.");
            outputText.text = $"Failed to load map '{mapItem.mapName}'.\n";
        }

        currentMapObject = mapObject;

        PreviewPanel.SetActive(true);
    }

    public void OnEditButtonClicked()
    {
        if (currentMapObject == null)
        {
            Debug.LogError("No map loaded to edit.");
            return;
        }

        cameraManager.UpdateTargetGroupFromRoot(currentMapObject);
        StartTransition();
    }

    public void StartTransition()
    {
        TransitionPanel.SetActive(true);
    }

    public void OnTransition()
    {
        PreviewPanel.SetActive(false);
        MainCanvas.SetActive(false);
        EditCanvas.SetActive(true);
        DrawCanvas.SetActive(true);
        ToolManager.SetActive(true);
    }

    public void EndTransition()
    {
        TransitionPanel.SetActive(false);
    }
    
}