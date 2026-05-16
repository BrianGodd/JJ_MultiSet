using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EditorManager : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public enum EditorMode { None, Marking, Setting, Scripting, Simulation, Upload }
    public enum Situation { None, Near, Inside }
    public enum SimulationControlMode { Keyboard, Mouse }

    public EditorMode currentMode = EditorMode.Marking;

    [Header("References")]
    public Camera mainCamera;
    public CustomUIManager uiManager;
    public Material markMaterial;
    public RectTransform drawPanel;
    public RectTransform menuPanel;
    public RectTransform editorPanel;
    public RectTransform selectionRect;
    public GameObject nextButton;

    [Header("Labeling UI")]
    public GameObject labelInputPanel;
    public TMP_InputField labelInput;
    public Button confirmButton;
    public Button cancelButton;
    public Button renewButton;
    public Button deleteButton;

    [Header("Setting UI")]
    public GameObject settingPanel;
    public TMP_InputField marginInput;
    public TMP_InputField angle1Input;
    public TMP_InputField angle2Input;
    public Button applySettingButton;
    public Button closeSettingButton;

    [Header("Scripting UI")]
    public GameObject scriptingPanel;
    public TMP_InputField keywordInput;
    public TMP_InputField detailsInput;
    public Button applyScriptButton;
    public Button closeScriptButton;

    [Header("Simulation UI")]
    public GameObject simulationPanel;
    public Sprite simulationMarkerSprite;
    public TextMeshProUGUI nearestOutputText;
    public TextMeshProUGUI directionOutputText;
    public TextMeshProUGUI insideOutputText;
    public TextMeshProUGUI simpleMSGText;
    public Situation currentSituation = Situation.None;
    public Material simulationMarkerMaterial;
    public SimulationControlMode simulationControlMode = SimulationControlMode.Keyboard;
    public float simulationMoveSpeed = 3f;
    public float simulationRotateSpeed = 120f;

    [Header("Upload UI")]
    public GameObject uploadPanel;
    public RectTransform marksInfoPanel;
    public TextMeshProUGUI marksInfo;
    public TMP_InputField uploadTitleInput;
    public Button applyUploadButton;
    public Button backtoMenuButton;
    public UploadDB uploadDB;

    public float minColumnHeight = 0.1f;

    [Header("Selection")]
    public Color highlightColor = Color.yellow;
    public Color selectionOutlineColor = Color.yellow;
    public Vector2 selectionOutlineDistance = new Vector2(3f, 3f);

    [Header("UI")]
    public GameObject Mask1, Mask2, Mask3;
    public Button ModeButton1, ModeButton2, ModeButton3;

    internal const int CircleSegments = 64;
    internal const float LineWidth = 0.5f;

    internal Vector2 dragStart;
    internal Vector2 dragEnd;
    public bool dragging;
    internal readonly List<GameObject> createdMarkers = new List<GameObject>();
    internal GameObject markersRoot;
    internal GameObject selectedMarker;
    internal Renderer selectedRenderer;
    internal Material selectedOriginalMaterial;
    internal Outline selectionRectOutline;
    internal Image selectionRectImage;
    internal Color selectionRectBaseColor;

    public GameObject simPlayerMarker;
    internal Vector3 simPlayerPosition;
    internal bool simActive;
    internal bool simPlayerMarkerPlaced;

    private MarkerService markerService;
    private PanelController panelController;
    private SimulationController simulationController;

    private void Awake()
    {
        EnsureServicesInitialized();

        panelController.InitializePanels();
        markerService.InitializeSelectionVisuals();

        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmLabel);
        if (renewButton != null) renewButton.onClick.AddListener(OnRenewLabel);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelLabel);
        if (deleteButton != null) deleteButton.onClick.AddListener(OnDeleteLabel);
        if (applySettingButton != null) applySettingButton.onClick.AddListener(OnApplySettings);
        if (closeSettingButton != null) closeSettingButton.onClick.AddListener(HideSettingPanel);
        if (applyScriptButton != null) applyScriptButton.onClick.AddListener(OnApplyScript);
        if (closeScriptButton != null) closeScriptButton.onClick.AddListener(HideScriptingPanel);
        if (applyUploadButton != null) applyUploadButton.onClick.AddListener(OnApplyUpload);
        if (backtoMenuButton != null) backtoMenuButton.onClick.AddListener(OnBackToMenu);

        if (marginInput != null) marginInput.onValueChanged.AddListener(_ => markerService.UpdateSelectedVisualizationFromInputs());
        if (angle1Input != null) angle1Input.onValueChanged.AddListener(_ => markerService.UpdateSelectedVisualizationFromInputs());
        if (angle2Input != null) angle2Input.onValueChanged.AddListener(_ => markerService.UpdateSelectedVisualizationFromInputs());
    }

    private void Update()
    {
        EnsureServicesInitialized();
        simulationController.Tick();
    }

    public void OnPointerDown(PointerEventData eventData) => markerService.OnPointerDown(eventData);
    public void OnDrag(PointerEventData eventData) => markerService.OnDrag(eventData);
    public void OnPointerUp(PointerEventData eventData) => markerService.OnPointerUp(eventData);

    public void LoadMarksFromData(Dictionary<string, MarkStorage.MarkData> marks)
    {
        EnsureServicesInitialized();
        markerService.LoadMarksFromData(marks);
    }

    public void SelectMarker(GameObject marker)
    {
        EnsureServicesInitialized();
        markerService.SelectMarker(marker);
    }

    public void ClearMarkers()
    {
        EnsureServicesInitialized();
        markerService.ClearMarkers();
    }

    public void OnApplyUpload()
    {
        EnsureServicesInitialized();
        panelController.OnApplyUpload();
    }

    public void OnBackToMenu()
    {
        EnsureServicesInitialized();
        panelController.OnBackToMenu();
    }

    public void showUploadInfo()
    {
        EnsureServicesInitialized();
        panelController.ShowUploadInfo();
    }

    public void ActiveMode(int mode)
    {
        EnsureServicesInitialized();
        panelController.ActiveMode(mode);
    }

    public void NextMode()
    {
        switch (currentMode)
        {
            case EditorMode.None: currentMode = EditorMode.Marking; ActiveMode(1); break;
            case EditorMode.Marking: currentMode = EditorMode.Setting; ActiveMode(2); break;
            case EditorMode.Setting: currentMode = EditorMode.Scripting; ActiveMode(3); break;
            case EditorMode.Scripting: currentMode = EditorMode.Simulation; ActiveMode(4); break;
            case EditorMode.Simulation: currentMode = EditorMode.Upload; ActiveMode(5); break;
            case EditorMode.Upload: currentMode = EditorMode.None; break;
        }
        Debug.Log("Switched to mode: " + currentMode);
    }

    public void ChangeMode(int mode)
    {
        if (mode < 0 || mode >= System.Enum.GetValues(typeof(EditorMode)).Length) return;
        currentMode = (EditorMode)mode;
        ActiveMode(mode);
        Debug.Log("Changed to mode: " + currentMode);
    }

    public bool IsAnyEditPanelOpen()
    {
        bool settingOpen = settingPanel != null && settingPanel.activeSelf;
        bool scriptingOpen = scriptingPanel != null && scriptingPanel.activeSelf;
        bool labelOpen = labelInputPanel != null && labelInputPanel.activeSelf;
        return settingOpen || scriptingOpen || labelOpen;
    }

    private void OnCancelLabel() => markerService.OnCancelLabel();
    private void OnDeleteLabel() => markerService.OnDeleteLabel();
    private void OnConfirmLabel() => markerService.OnConfirmLabel();
    private void OnRenewLabel() => markerService.OnRenewLabel();
    private void HideScriptingPanel() => markerService.HideScriptingPanel();
    private void OnApplyScript() => markerService.OnApplyScript();
    private void HideSettingPanel() => markerService.HideSettingPanel();
    private void OnApplySettings() => markerService.OnApplySettings();

    private void EnsureServicesInitialized()
    {
        if (markerService == null)
        {
            markerService = new MarkerService(this);
        }

        if (panelController == null)
        {
            panelController = new PanelController(this);
        }

        if (simulationController == null)
        {
            simulationController = new SimulationController(this, markerService);
        }
    }
}
