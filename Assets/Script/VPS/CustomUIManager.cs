using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MultiSet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CustomUIManager : MonoBehaviour
{
    public MapQuery mapQuery;
    public CameraManager cameraManager;

    public GameObject AuthPanel, MapPanel, PreviewPanel, TransitionPanel;
    public GameObject MainCanvas, EditCanvas, DrawCanvas, ToolManager;

    public TMP_InputField clientIdInput;
    public TMP_InputField clientSecretInput;
    public Button fetchButton;

    public TextMeshProUGUI mapInfo;
    public Button downloadButton;

    public Button editButton;
    public TextMeshProUGUI outputText;

    public TMP_Dropdown mapDropdown;
    [SerializeField] private MonoBehaviour mapServiceSource;

    public GameObject currentMapObject;

    private IExternalMapService mapService;

    private void Awake()
    {
        if (currentMapObject != null)
        {
            cameraManager.UpdateTargetGroupFromRoot(currentMapObject);
        }

        mapService = ResolveMapService();
        if (mapService == null)
        {
            Debug.LogError("CustomUIManager: mapServiceSource must implement IExternalMapService.");
        }

        fetchButton.onClick.AddListener(Fetch);
        downloadButton.onClick.AddListener(OnDownloadButtonClicked);
        editButton.onClick.AddListener(OnEditButtonClicked);
    }

    private async void Fetch()
    {
        if (mapService == null)
        {
            outputText.text = "Map service not configured.\n";
            return;
        }

        outputText.text = "Authenticating...\n";
        AuthPanel.SetActive(false);

        try
        {
            await mapService.AuthenticateAsync(
                clientIdInput.text.Trim(),
                clientSecretInput.text.Trim());

            await FetchMapsAsync();
        }
        catch (System.Exception ex)
        {
            outputText.text = ex.Message;
            AuthPanel.SetActive(true);
        }
    }

    private async Task FetchMapsAsync()
    {
        outputText.text += "Token OK. Fetching maps...\n";

        IReadOnlyList<ExternalMapInfo> maps;
        try
        {
            maps = await mapService.GetMapsAsync();
        }
        catch (System.Exception ex)
        {
            outputText.text = ex.Message;
            return;
        }

        var stored = maps
            .Where(m => m != null && (!string.IsNullOrEmpty(m.mapName) || !string.IsNullOrEmpty(m.mapCode)))
            .OrderBy(m => string.IsNullOrEmpty(m.mapName) ? m.mapCode : m.mapName)
            .ToList();

        var optionLabels = stored
            .Select(m => m.DisplayName)
            .ToList();

        var codes = stored
            .Where(m => !string.IsNullOrEmpty(m.mapCode))
            .Select(m => m.mapCode)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Total maps: {stored.Count}");
        sb.AppendLine($"Total mapCodes: {codes.Count}");
        sb.AppendLine();
        foreach (var c in codes)
        {
            sb.AppendLine(c);
        }
        outputText.text = sb.ToString();

        if (mapDropdown != null)
        {
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(optionLabels);
        }

        if (mapDropdown != null && stored.Count > 0)
        {
            mapDropdown.onValueChanged.RemoveAllListeners();
            mapDropdown.onValueChanged.AddListener(index =>
            {
                var selectedMap = stored[index];
                mapInfo.text = $"Map Name: {selectedMap.mapName}\nMap Code: {selectedMap.mapCode}\nCreated At: {selectedMap.createdAt}";
            });
            mapDropdown.onValueChanged.Invoke(0);
        }

        MapPanel.SetActive(true);
    }

    public async void OnDownloadButtonClicked()
    {
        if (mapDropdown == null || mapDropdown.options.Count == 0)
        {
            return;
        }

        MapPanel.SetActive(false);

        var selectedIndex = mapDropdown.value;
        var selectedOption = mapDropdown.options[selectedIndex].text;

        if (!MapStorage.TryGet(selectedOption, out var mapItem))
        {
            Debug.LogError($"Map '{selectedOption}' not found in MapStorage.");
            outputText.text = $"Map '{selectedOption}' not found in MapStorage.\n";
            return;
        }

        Debug.Log($"Downloading map mesh for: {mapItem.DisplayName} ...");
        outputText.text = $"Downloading map mesh for: {mapItem.DisplayName} ...\n";

        var mapObject = await mapQuery.LoadMapAsync(mapItem, progress =>
        {
            int downloadedMB = Mathf.RoundToInt((float)(mapItem.storageInMb * progress));
            int totalMB = Mathf.RoundToInt((float)mapItem.storageInMb);

            outputText.text = $"Downloading map mesh: {downloadedMB}/{totalMB} MB ({progress * 100f:F2}%)\n";
        });

        if (mapObject != null)
        {
            Debug.Log($"Map '{mapItem.DisplayName}' loaded successfully.");
            outputText.text = $"Map '{mapItem.DisplayName}' loaded successfully.\n";
        }
        else
        {
            Debug.LogError($"Failed to load map '{selectedOption}'.");
            outputText.text = $"Failed to load map '{mapItem.DisplayName}'.\n";
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
        if (mapQuery != null)
        {
            mapQuery.ApplyPendingImportedMarks();
        }
    }

    public void EndTransition()
    {
        TransitionPanel.SetActive(false);
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
