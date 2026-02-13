using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class EditorManager : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public enum EditorMode
    {
        None,
        Marking,
        Setting,
        Scripting,
        Simulation
    }

    public enum Situation
    {
        None,
        Near,
        Inside
    }

    public EditorMode currentMode = EditorMode.Marking;

    [Header("References")]
    public Camera mainCamera;
    public CustomUIManager uiManager;
    public Material markMaterial;
    public RectTransform drawPanel;
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
    public GameObject simulationPanel;               // panel shown in Simulation mode
    public Sprite simulationMarkerSprite;              // sprite used for simulated player marker
    public TextMeshProUGUI nearestOutputText;       // shows nearest label or "null"
    public TextMeshProUGUI directionOutputText;     // shows intermediate direction
    public TextMeshProUGUI insideOutputText;        // shows "Inside" or "Outside"
    public TextMeshProUGUI simpleMSGText;
    public Situation currentSituation = Situation.None; // current situation in simulation (None, Near, Inside)
    public Material simulationMarkerMaterial;          // material for simulated player marker

    public float minColumnHeight = 0.1f;
    private Vector2 dragStart;
    private Vector2 dragEnd;
    public bool dragging;
    private List<GameObject> createdMarkers = new List<GameObject>();
    private GameObject markersRoot;

    // Selection support
    [Header("Selection")]
    public Color highlightColor = Color.yellow;
    private GameObject selectedMarker;
    private Renderer selectedRenderer;
    private Material selectedOriginalMaterial;

    // visualization parameters
    private const int CircleSegments = 64;
    private const float LineWidth = 0.03f;

    // simulation runtime
    public GameObject simPlayerMarker;
    private Vector3 simPlayerPosition;
    private bool simActive;

    [Header("UI")]
    public GameObject Mask1, Mask2, Mask3;
    public Button ModeButton1, ModeButton2, ModeButton3;

    private void Awake()
    {
        if (selectionRect != null) selectionRect.gameObject.SetActive(false);
        if (labelInputPanel != null) labelInputPanel.SetActive(false);
        if (settingPanel != null) settingPanel.SetActive(false);
        if (simulationPanel != null) simulationPanel.SetActive(false);

        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmLabel);
        if (renewButton != null) renewButton.onClick.AddListener(OnRenewLabel);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelLabel);
        if (deleteButton != null) deleteButton.onClick.AddListener(OnDeleteLabel);

        if (applySettingButton != null) applySettingButton.onClick.AddListener(OnApplySettings);
        if (closeSettingButton != null) closeSettingButton.onClick.AddListener(HideSettingPanel);

        if (applyScriptButton != null) applyScriptButton.onClick.AddListener(OnApplyScript);
        if (closeScriptButton != null) closeScriptButton.onClick.AddListener(HideScriptingPanel);

        if (marginInput != null) marginInput.onValueChanged.AddListener(_ => UpdateSelectedVisualizationFromInputs());
        if (angle1Input != null) angle1Input.onValueChanged.AddListener(_ => UpdateSelectedVisualizationFromInputs());
        if (angle2Input != null) angle2Input.onValueChanged.AddListener(_ => UpdateSelectedVisualizationFromInputs());
    }

    private void Update()
    {
        // Simulation mode runtime behavior
        if (currentMode == EditorMode.Simulation)
        {
            EnsureSimulationActive(true);
            // keep visuals for all markers visible in simulation
            UpdateAllMarkersVisualization();
            UpdateSimulationFromMouse();
            EvaluateSimulationAgainstMarks();
            return;
        }
        else
        {
            EnsureSimulationActive(false);
        }

        // other modes handled by pointer handlers etc.
    }

    private void EnsureSimulationActive(bool active)
    {
        if (simActive == active) return;
        simActive = active;

        if (simActive)
        {
            if (simulationPanel != null) simulationPanel.SetActive(true);

            if (simPlayerMarker == null)
            {
                simPlayerMarker = new GameObject("SimPlayerMarker");
                var sr = simPlayerMarker.AddComponent<SpriteRenderer>();
                sr.sprite = simulationMarkerSprite;
                sr.material = simulationMarkerMaterial;
                simPlayerMarker.name = "SimPlayerMarker";
                simPlayerMarker.transform.localScale = Vector3.one * 0.1f;
                simPlayerMarker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var col = simPlayerMarker.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
        }
        else
        {
            if (simulationPanel != null) simulationPanel.SetActive(false);
            if (simPlayerMarker != null) Destroy(simPlayerMarker);
            simPlayerMarker = null;

            if (nearestOutputText != null) nearestOutputText.text = "null";
            if (directionOutputText != null) directionOutputText.text = "null";

            // remove per-marker visualization when leaving simulation
            for (int i = 0; i < createdMarkers.Count; i++)
            {
                var m = createdMarkers[i];
                if (m == null) continue;
                var vis = m.transform.Find("Visualization");
                if (vis != null) Destroy(vis.gameObject);
            }
        }
    }

    // Map screen mouse position to world on ground plane (map bounds min.y if available)
    private bool ScreenToGroundPlane(Vector2 screenPos, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        Camera cam = mainCamera ?? Camera.main;
        if (cam == null) return false;

        float planeY = 0f;
        GameObject mapRoot = uiManager != null ? uiManager.currentMapObject : null;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var combined, true))
        {
            planeY = combined.min.y;
        }
        Plane plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
        {
            worldPos = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    // Update simulated player position from mouse
    private void UpdateSimulationFromMouse()
    {
        Camera cam = mainCamera ?? Camera.main;
        if (cam == null) return;

        Vector2 mouse = Input.mousePosition;
        if (ScreenToGroundPlane(mouse, out var pos))
        {
            simPlayerPosition = pos;
            if (simPlayerMarker != null) 
            {
                simPlayerMarker.transform.position = pos + Vector3.up * 0.1f;
            }
        }
    }

    // Evaluate all marks (from createdMarkers and MarkStorage) and update UI
    private void EvaluateSimulationAgainstMarks()
    {
        if (nearestOutputText == null || directionOutputText == null)
        {
            // find if not assigned
        }

        GameObject nearest = null;
        float nearestDist = float.MaxValue;

        // iterate created markers list (only those present in scene)
        foreach (var mark in createdMarkers)
        {
            if (mark == null) continue;
            if (!MarkStorage.TryGet(mark.name, out var md)) continue;

            if (IsPointInMarkArea(simPlayerPosition, mark, md))
            {
                // distance in XZ to marker base center
                Vector3 baseCenter = GetMarkerBaseCenter(mark, md);
                float dist = Vector2.SqrMagnitude(new Vector2(simPlayerPosition.x - baseCenter.x, simPlayerPosition.z - baseCenter.z));
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = mark;
                }
            }
        }

        if (nearest != null)
        {
            // set nearest label
            string label = nearest.name;
            nearestOutputText.text = label;

            // direction relative to player (intermediate 8 directions)
            Vector3 baseCenter = GetMarkerBaseCenter(nearest, MarkStorage.Marks.ContainsKey(nearest.name) ? MarkStorage.Marks[nearest.name] : new MarkStorage.MarkData{ position = nearest.transform.position });
            Vector3 toMark = baseCenter - simPlayerPosition;
            string dirName = GetRelatedDirection(toMark);
            directionOutputText.text = dirName;
            
            // show inside/outside depending on whether the player is inside the mark area (see the mark position and size, not include the angle and margin)
            bool inside = IsPointInMarkInside(simPlayerPosition, nearest, MarkStorage.Marks[nearest.name]);
            insideOutputText.text = inside ? "Inside" : "Outside";
            currentSituation = inside ? Situation.Inside : Situation.Near;
        }
        else
        {
            nearestOutputText.text = "null";
            directionOutputText.text = "null";
            insideOutputText.text = "null";
            currentSituation = Situation.None;
        }

        // "The user is now near {label} , the {label} is {direction} of the user."
        // "The user is right inside the {label}."
        // "No mark nearby."

        if (insideOutputText.text == "Inside")
        {
            simpleMSGText.text = $"The user is right inside the {nearestOutputText.text}.";
        }
        else if (insideOutputText.text == "Outside")
        {
            simpleMSGText.text = $"The user is now near {nearestOutputText.text}, the {nearestOutputText.text} is {directionOutputText.text} of the user.";
        }
        else
        {
            simpleMSGText.text = "No mark nearby.";
        }
    }

    // return base center world position (on ground) for a marker (accounts for marker height)
    private Vector3 GetMarkerBaseCenter(GameObject mark, MarkStorage.MarkData md)
    {
        // prefer transform-based calculation to be robust
        float halfHeight = mark.transform.localScale.y * 0.5f;
        return mark.transform.position - Vector3.up * halfHeight;
    }

    private bool IsPointInMarkInside(Vector3 point, GameObject mark, MarkStorage.MarkData md)
    {
        if (mark == null || md == null) return false;
        
        Vector3 baseCenter = GetMarkerBaseCenter(mark, md);
        float halfX = mark.transform.localScale.x * 0.5f;
        float halfZ = mark.transform.localScale.z * 0.5f;

        // check if point is within the rectangle defined by base center and half extents
        bool inside = (Mathf.Abs(point.x - baseCenter.x) <= halfX + 1e-6f) && (Mathf.Abs(point.z - baseCenter.z) <= halfZ + 1e-6f);
        return inside;
    }

    // check if point (world XZ) is inside mark area (rectangle footprint expanded by margin) and within angle range
    private bool IsPointInMarkArea(Vector3 point, GameObject mark, MarkStorage.MarkData md)
    {
        if (mark == null || md == null) return false;

        // compute base center and local coordinates
        Vector3 baseCenter = GetMarkerBaseCenter(mark, md);

        // convert point to marker local space (ground plane)
        //Vector3 localPoint = mark.transform.InverseTransformPoint(new Vector3(point.x, baseCenter.y, point.z));
        Vector3 localPoint = new Vector3(point.x - baseCenter.x, 0f, point.z - baseCenter.z);

        float halfX = mark.transform.localScale.x * 0.5f;
        float halfZ = mark.transform.localScale.z * 0.5f;

        float allowedX = halfX + md.margin;
        float allowedZ = halfZ + md.margin;

        // rectangle test (axis-aligned in marker local space)
        bool insideRect = (Mathf.Abs(localPoint.x) <= allowedX + 1e-6f) && (Mathf.Abs(localPoint.z) <= allowedZ + 1e-6f);

        //Debug.Log($"IsPointInMarkArea: point={point}, localPoint={localPoint}, allowX={allowedX}, allowZ={allowedZ}, insideRect={insideRect}");
        if (!insideRect) return false;

        // angle test: compute signed angle between marker forward and vector from marker base to point
        Vector3 forward = mark.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 toPoint = new Vector3(point.x - baseCenter.x, 0f, point.z - baseCenter.z);
        if (toPoint.sqrMagnitude < 1e-6f) return true; // at center -> valid

        toPoint.Normalize();
        float signed = Vector3.SignedAngle(forward, toPoint, Vector3.up); // -180..180
        //Debug.Log($"Marker '{mark.name}', signed angle to player: {signed}");

        float a1 = md.angle1;
        float a2 = md.angle2;
        
        // ensure a1 <= a2
        if (a1 > a2)
        {
            var tmp = a1; a1 = a2; a2 = tmp;
        }

        // handle wrap-around (e.g., a1=-170, a2=170 meaning almost full circle)
        // convert to range -180..180 and test
        bool angleOk = false;
        if (a2 - a1 >= 360f - 1e-3f)
        {
            angleOk = true;
        }
        else
        {
            angleOk = (signed >= a1 - 1e-6f && signed <= a2 + 1e-6f);
        }
        return angleOk;
    }

    // convert vector to one of 8 relative directions based on the simulated player's facing
    // returns: Forward, Forward-Right, Right, Backward-Right, Backward, Backward-Left, Left, Forward-Left
    private string GetRelatedDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return "Here";
        dir.Normalize();

        // determine player's facing angle using simPlayerMarker's Z-axis rotation (in degrees)
        // Note: user requested using the Z rotation angle directly (simPlayerMarker.transform.eulerAngles.z)
        float playerAngle = 0f;
        if (simPlayerMarker != null)
        {
            playerAngle = simPlayerMarker.transform.eulerAngles.y;
        }

        // compute absolute angle for dir (0 = world +Z)
        float dirAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // relative angle from player's forward to dir (0 = forward)
        float rel = dirAngle - playerAngle;
        if (rel < 0f) rel += 360f;

        //Debug.Log($"GetRelatedDirection: dir={dir}, playerAngle={simPlayerMarker.transform.eulerAngles}, rel={rel}");

        // convert rel to 0~360
        if (rel < 0f) rel += 360f;

        // sectors centered at 0 (Forward), 45 (FR), 90 (Right), etc.
        if (InSector(rel, 337.5f, 360f) || InSector(rel, 0f, 22.5f)) return "Forward";
        if (InSector(rel, 22.5f, 67.5f)) return "Forward-Right";
        if (InSector(rel, 67.5f, 112.5f)) return "Right";
        if (InSector(rel, 112.5f, 157.5f)) return "Backward-Right";
        if (InSector(rel, 157.5f, 202.5f)) return "Backward";
        if (InSector(rel, 202.5f, 247.5f)) return "Backward-Left";
        if (InSector(rel, 247.5f, 292.5f)) return "Left";
        if (InSector(rel, 292.5f, 337.5f)) return "Forward-Left";
        return "Forward";
    }

    private bool InSector(float angle, float a, float b)
    {
        return angle >= a && angle < b;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (currentMode != EditorMode.Marking) return;

        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (drawPanel == null) return;

        dragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(drawPanel, eventData.position, eventData.pressEventCamera, out dragStart);
        dragEnd = dragStart;
        UpdateSelectionVisual();
        if (selectionRect != null) selectionRect.gameObject.SetActive(true);
        if (labelInputPanel != null) labelInputPanel.SetActive(false);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (currentMode != EditorMode.Marking) return;

        if (!dragging) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(drawPanel, eventData.position, eventData.pressEventCamera, out dragEnd);
        UpdateSelectionVisual();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (currentMode != EditorMode.Marking) return;

        if (!dragging) return;
        dragging = false;
        Vector2 size = dragEnd - dragStart;
        if (labelInputPanel != null && size.sqrMagnitude > 100)
        {
            labelInputPanel.SetActive(true);
            confirmButton.gameObject.SetActive(true);
            renewButton.gameObject.SetActive(false);
            deleteButton.interactable = false; // disable delete in creation mode
            if (labelInput != null) { labelInput.text = ""; labelInput.ActivateInputField(); }
        }
    }

    private void UpdateSelectionVisual()
    {
        if (selectionRect == null || drawPanel == null) return;
        Vector2 size = dragEnd - dragStart;
        selectionRect.anchoredPosition = dragStart + size * 0.5f;
        selectionRect.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
    }

    private void OnCancelLabel()
    {
        if (selectionRect != null) selectionRect.gameObject.SetActive(false);
        if (labelInputPanel != null) labelInputPanel.SetActive(false);
        SelectMarker(null);
    }

    private void OnDeleteLabel()
    {
        if (selectedMarker != null)
        {
            MarkStorage.Remove(selectedMarker.name);
            createdMarkers.Remove(selectedMarker);
            Destroy(selectedMarker);
            selectedMarker = null;
            selectedRenderer = null;
            selectedOriginalMaterial = null;
        }
        OnCancelLabel();
    }

    private void OnConfirmLabel()
    {
        if (labelInputPanel != null) labelInputPanel.SetActive(false);
        if (selectionRect != null) selectionRect.gameObject.SetActive(false);

        string label = labelInput != null ? labelInput.text.Trim() : "Label";
        if (string.IsNullOrEmpty(label)) label = "Label";

        GameObject mapRoot = uiManager != null ? uiManager.currentMapObject : null;
        if (markersRoot == null)
        {
            if (mapRoot != null)
            {
                markersRoot = new GameObject("MarkersRoot");
                markersRoot.transform.SetParent(mapRoot.transform, worldPositionStays: true);
            }
            else
            {
                markersRoot = new GameObject("MarkersRoot");
            }
        }

        // compute plane and convert selection to world positions (same logic as before)
        Vector2 localA = dragStart;
        Vector2 localB = dragEnd;
        Vector2 min = Vector2.Min(localA, localB);
        Vector2 max = Vector2.Max(localA, localB);

        Camera cam = mainCamera;
        if (cam == null) { Debug.LogError("No Main Camera found."); return; }

        float planeY = 0f;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var combined, true))
        {
            planeY = combined.min.y;
        }
        Plane plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));

        Vector3 worldCenter;
        float worldWidth = 1f;
        float worldDepth = 1f;

        // project four corners
        Vector3 worldMin = drawPanel.TransformPoint(new Vector3(min.x, min.y, 0));
        Vector3 worldMax = drawPanel.TransformPoint(new Vector3(max.x, max.y, 0));
        Vector3 worldMinXMaxY = drawPanel.TransformPoint(new Vector3(min.x, max.y, 0));
        Vector3 worldMaxXMinY = drawPanel.TransformPoint(new Vector3(max.x, min.y, 0));

        Vector2 screenTL = RectTransformUtility.WorldToScreenPoint(cam, worldMinXMaxY);
        Vector2 screenTR = RectTransformUtility.WorldToScreenPoint(cam, worldMax);
        Vector2 screenBL = RectTransformUtility.WorldToScreenPoint(cam, worldMin);
        Vector2 screenBR = RectTransformUtility.WorldToScreenPoint(cam, worldMaxXMinY);

        bool pTL = ScreenPointToPlane(screenTL, cam, plane, out Vector3 worldTL);
        bool pTR = ScreenPointToPlane(screenTR, cam, plane, out Vector3 worldTR);
        bool pBR = ScreenPointToPlane(screenBR, cam, plane, out Vector3 worldBR);
        bool pBL = ScreenPointToPlane(screenBL, cam, plane, out Vector3 worldBL);

        if (pTL && pTR && pBR && pBL)
        {
            Vector3[] pts = new[] { worldTL, worldTR, worldBR, worldBL };
            Vector3 minV = pts[0], maxV = pts[0];
            foreach (var pt in pts) { minV = Vector3.Min(minV, pt); maxV = Vector3.Max(maxV, pt); }
            worldWidth = Vector3.Distance(new Vector3(minV.x, 0, minV.z), new Vector3(maxV.x, 0, minV.z));
            worldDepth = Vector3.Distance(new Vector3(minV.x, 0, minV.z), new Vector3(minV.x, 0, maxV.z));
            worldCenter = (worldTL + worldTR + worldBR + worldBL) / 4f;
        }
        else
        {
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cam, drawPanel.TransformPoint((min + max) * 0.5f));
            if (!ScreenPointToPlane(screenCenter, cam, plane, out worldCenter))
            {
                worldCenter = new Vector3(0, planeY, 0);
            }
            Vector2 sizePixels = max - min;
            Vector3 leftWorld = ScreenPointToPlanePoint(new Vector2(screenCenter.x - sizePixels.x * 0.5f, screenCenter.y), cam, plane, worldCenter);
            Vector3 rightWorld = ScreenPointToPlanePoint(new Vector2(screenCenter.x + sizePixels.x * 0.5f, screenCenter.y), cam, plane, worldCenter);
            Vector3 topWorld = ScreenPointToPlanePoint(new Vector2(screenCenter.x, screenCenter.y + sizePixels.y * 0.5f), cam, plane, worldCenter);
            worldWidth = Mathf.Max(0.01f, Vector3.Distance(leftWorld, rightWorld));
            worldDepth = Mathf.Max(0.01f, Vector3.Distance(topWorld, worldCenter));
        }

        float height = minColumnHeight;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var cb2, true))
        {
            height = Mathf.Max(minColumnHeight, cb2.size.y);
        }

        // create column
        GameObject column = GameObject.CreatePrimitive(PrimitiveType.Cube);
        column.name = $"{label}";
        column.transform.position = worldCenter + Vector3.up * (height * 0.5f);
        column.transform.localScale = new Vector3(worldWidth, height, worldDepth);
        if (markMaterial != null)
        {
            var rend = column.GetComponent<Renderer>();
            rend.sharedMaterial = new Material(markMaterial);
            rend.sharedMaterial.color = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 0.75f);
        }
        column.transform.SetParent(markersRoot.transform, worldPositionStays: true);

        // label
        GameObject labelGO = new GameObject("Label_" + label);
        labelGO.transform.SetParent(column.transform, worldPositionStays: false);
        labelGO.transform.localPosition = new Vector3(0, 0, 0);
        Vector3 desiredWorldScale = new Vector3(1, 1, 1);
        Vector3 parentScale = labelGO.transform.parent.lossyScale;

        labelGO.transform.localScale = (Mathf.Sqrt(parentScale.x*parentScale.z) / (label.Length * 0.2f)) * new Vector3(
            desiredWorldScale.z / parentScale.z,
            desiredWorldScale.x / parentScale.x,
            1f
        );
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = Mathf.Max(1f, height * 0.5f) * 2;
        tmp.color = Color.black;
        var mr = tmp.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = 32767;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mr.sharedMaterial != null) mr.sharedMaterial.renderQueue = 5000;
        }
        var bb = labelGO.AddComponent<BillboardToCamera>();
        bb.cam = mainCamera;

        labelGO.layer = LayerMask.NameToLayer("Marker");

        // add MarkController to allow selection in Setting mode
        var mc = column.AddComponent<MarkController>();
        mc.editorManager = this;

        // ensure collider
        var col = column.GetComponent<Collider>();
        if (col == null) column.AddComponent<BoxCollider>();

        column.layer = LayerMask.NameToLayer("Marker");

        createdMarkers.Add(column);

        // create default mark data and save
        var md = new MarkStorage.MarkData
        {
            keyword = "",
            details = "",
            position = column.transform.position,
            scale = column.transform.localScale,
            margin = Mathf.Max(0.5f, Mathf.Max(worldWidth, worldDepth) * 0.5f),
            angle1 = -30f,
            angle2 = 30f
        };
        MarkStorage.Save(column.name, md);
    }

    private void OnRenewLabel()
    {
        if (selectedMarker == null) return;
        if (labelInput == null) return;

        string newLabel = labelInput.text.Trim();
        if (string.IsNullOrEmpty(newLabel)) newLabel = "Label";

        // check for name conflict
        if (newLabel != selectedMarker.name && MarkStorage.Marks.ContainsKey(newLabel))
        {
            Debug.LogError($"A marker with the name '{newLabel}' already exists. Please choose a different name.");
            return;
        }

        // rename marker and update storage key
        string oldName = selectedMarker.name;
        selectedMarker.name = newLabel;

        if (MarkStorage.Marks.ContainsKey(oldName))
        {
            var data = MarkStorage.Marks[oldName];
            MarkStorage.Remove(oldName);
            MarkStorage.Save(newLabel, data);
        }

        // update label text
        var labelGO = selectedMarker.transform.Find("Label_" + oldName);
        if (labelGO != null)
        {
            var tmp = labelGO.GetComponent<TextMeshPro>();
            if (tmp != null)
            {
                tmp.text = newLabel;
            }
        }

        OnCancelLabel();
    }

    private bool ScreenPointToPlane(Vector2 screenPoint, Camera cam, Plane plane, out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (plane.Raycast(ray, out float enter))
        {
            worldPoint = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    private Vector3 ScreenPointToPlanePoint(Vector2 screenPoint, Camera cam, Plane plane, Vector3 fallback)
    {
        if (ScreenPointToPlane(screenPoint, cam, plane, out var p)) return p;
        return fallback;
    }

    // Called by MarkController when user clicks a marker (or elsewhere to deselect)
    public void SelectMarker(GameObject marker)
    {
        // restore previous selection
        if (selectedRenderer != null)
        {
            if (selectedOriginalMaterial != null)
            {
                selectedRenderer.material = selectedOriginalMaterial;
            }
            selectedRenderer = null;
            selectedOriginalMaterial = null;
        }

        selectedMarker = marker;

        if (currentMode == EditorMode.Setting) HideOtherMarkersVisualization();

        if (selectedMarker == null)
        {
            return;
        }

        // try to highlight the selected marker
        var rend = selectedMarker.GetComponent<Renderer>() ?? selectedMarker.GetComponentInChildren<Renderer>();

        if (rend != null)
        {
            selectedRenderer = rend;
            selectedOriginalMaterial = rend.material;
            var highlightMat = new Material(selectedOriginalMaterial);
            if (highlightMat.HasProperty("_Color")) highlightMat.color = highlightColor;
            if (highlightMat.HasProperty("_EmissionColor"))
            {
                highlightMat.SetColor("_EmissionColor", highlightColor * 0.5f);
                highlightMat.EnableKeyword("_EMISSION");
            }
            rend.material = highlightMat;
        }

        // if in Setting mode, show setting panel for this marker
        if (currentMode == EditorMode.Setting)
        {
            ShowSettingPanelForMarker(selectedMarker);
        }
        else if (currentMode == EditorMode.Scripting)
        {
            ShowScriptingPanelForMarker(selectedMarker);
        }
        else if (currentMode == EditorMode.Marking)
        {
            ShowLabelingPanelForMarker(selectedMarker);
        }
    }

    private void ShowLabelingPanelForMarker(GameObject marker)
    {
        if (labelInputPanel == null) return;
        labelInputPanel.SetActive(true);

        string currentLabel = marker.name;

        if (labelInput != null)
        {
            labelInput.text = currentLabel;
            labelInput.ActivateInputField();
        }

        deleteButton.interactable = true; // enable delete in labeling mode
        renewButton.gameObject.SetActive(true);
        confirmButton.gameObject.SetActive(false);
    }

    // Show scripting UI for a specific marker
    private void ShowScriptingPanelForMarker(GameObject marker)
    {
        if (scriptingPanel == null) return;
        scriptingPanel.SetActive(true);

        if (MarkStorage.TryGet(marker.name, out var data))
        {
            if (keywordInput != null) keywordInput.text = data.keyword;
            if (detailsInput != null) detailsInput.text = data.details;
        }
        else
        {
            if (keywordInput != null) keywordInput.text = "";
            if (detailsInput != null) detailsInput.text = "";
        }
    }

    private void HideScriptingPanel()
    {
        if (scriptingPanel != null) scriptingPanel.SetActive(false);
        SelectMarker(null);
    }

    private void OnApplyScript()
    {
        if (selectedMarker == null) return;

        string keyword = keywordInput != null ? keywordInput.text.Trim() : "";
        string details = detailsInput != null ? detailsInput.text.Trim() : "";

        if (MarkStorage.TryGet(selectedMarker.name, out var data))
        {
            data.keyword = keyword;
            data.details = details;
            MarkStorage.Save(selectedMarker.name, data);
        }

        HideScriptingPanel();
    }

    // Show setting UI filled with existing saved values (if any)
    private void ShowSettingPanelForMarker(GameObject marker)
    {
        if (settingPanel == null) return;
        settingPanel.SetActive(true);

        // load from MarkStorage if exists
        if (MarkStorage.TryGet(marker.name, out var data))
        {
            if (marginInput != null) marginInput.text = data.margin.ToString("F2");
            if (angle1Input != null) angle1Input.text = data.angle1.ToString("F1");
            if (angle2Input != null) angle2Input.text = data.angle2.ToString("F1");
        }
        else
        {
            // defaults
            if (marginInput != null) marginInput.text = "1.0";
            if (angle1Input != null) angle1Input.text = "-30";
            if (angle2Input != null) angle2Input.text = "30";
        }

        UpdateSelectedVisualizationFromInputs();
    }

    private void HideSettingPanel()
    {
        if (settingPanel != null) settingPanel.SetActive(false);
        // optionally remove visualization for previously selected marker
        if (selectedMarker != null)
        {
            var vis = selectedMarker.transform.Find("Visualization");
            if (vis != null) Destroy(vis.gameObject);
        }
        SelectMarker(null);
    }

    private void HideOtherMarkersVisualization()
    {
        foreach (var mark in createdMarkers)
        {
            if (mark == null || mark == selectedMarker) continue;
            var vis = mark.transform.Find("Visualization");
            if (vis != null) Destroy(vis.gameObject);
        }
    }

    private void OnApplySettings()
    {
        if (selectedMarker == null) return;

        Debug.Log("Applying settings for marker: " + selectedMarker.name);

        if (!float.TryParse(marginInput.text, out float margin)) margin = 1f;
        if (!float.TryParse(angle1Input.text, out float a1)) a1 = -30f;
        if (!float.TryParse(angle2Input.text, out float a2)) a2 = 30f;

        if (MarkStorage.TryGet(selectedMarker.name, out var md))
        {
            md.margin = margin;
            md.angle1 = a1;
            md.angle2 = a2;
            MarkStorage.Save(selectedMarker.name, md);
            UpdateSelectedVisualizationFromInputs();
        }

        //hide setting panel after applying
        HideSettingPanel();
    }

    // Update visualization (margin rectangle and two angle rays) using current inputs for selected marker
    private void UpdateSelectedVisualizationFromInputs()
    {
        if (selectedMarker == null) return;

        if (!float.TryParse(marginInput?.text, out float margin)) margin = 1f;
        if (!float.TryParse(angle1Input?.text, out float a1)) a1 = -30f;
        if (!float.TryParse(angle2Input?.text, out float a2)) a2 = 30f;

        var visRoot = GetOrCreateVisualization(selectedMarker);

        // compute base center (ground center of the column)
        float halfHeight = selectedMarker.transform.localScale.y * 0.5f;
        Vector3 baseCenter = selectedMarker.transform.position + Vector3.up * halfHeight;

        // column half extents on XZ
        float halfX = selectedMarker.transform.localScale.x * 0.5f;
        float halfZ = selectedMarker.transform.localScale.z * 0.5f;

        // expanded extents including margin
        float hx = halfX + margin;
        float hz = halfZ + margin;

        // margin rectangle (outline of column footprint expanded by margin)
        var marginLR = GetOrCreateLine(visRoot, "MarginLine");
        marginLR.loop = true;
        marginLR.widthMultiplier = LineWidth;
        marginLR.useWorldSpace = true;

        // 4 corners (clockwise) and close back to first
        marginLR.positionCount = 5;
        Vector3 c0 = baseCenter + new Vector3(-hx, 0f, -hz);
        Vector3 c1 = baseCenter + new Vector3(hx, 0f, -hz);
        Vector3 c2 = baseCenter + new Vector3(hx, 0f, hz);
        Vector3 c3 = baseCenter + new Vector3(-hx, 0f, hz);
        marginLR.SetPosition(0, c0);
        marginLR.SetPosition(1, c1);
        marginLR.SetPosition(2, c2);
        marginLR.SetPosition(3, c3);
        marginLR.SetPosition(4, c0);

        // angle rays
        var a1LR = GetOrCreateLine(visRoot, "Angle1");
        var a2LR = GetOrCreateLine(visRoot, "Angle2");
        a1LR.positionCount = 2;
        a2LR.positionCount = 2;
        a1LR.widthMultiplier = LineWidth;
        a2LR.widthMultiplier = LineWidth;
        a1LR.useWorldSpace = true;
        a2LR.useWorldSpace = true;

        // reference forward direction (use marker forward)
        Vector3 forward = selectedMarker.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 dir1 = Quaternion.Euler(0f, a1, 0f) * forward;
        Vector3 dir2 = Quaternion.Euler(0f, a2, 0f) * forward;

        // use a reasonable length for the angle rays (cover margin rectangle)
        float rayLen = Mathf.Max(hx, hz) + 0.2f;

        a1LR.SetPosition(0, baseCenter);
        a1LR.SetPosition(1, baseCenter + dir1.normalized * rayLen);

        a2LR.SetPosition(0, baseCenter);
        a2LR.SetPosition(1, baseCenter + dir2.normalized * rayLen);
    }

    // Create or return an existing Visualization child under marker
    private Transform GetOrCreateVisualization(GameObject marker)
    {
        var t = marker.transform.Find("Visualization");
        if (t != null) return t;

        var go = new GameObject("Visualization");
        go.transform.SetParent(marker.transform, worldPositionStays: true);
        // create three LineRenderers (margin + 2 angle lines)
        CreateLineRenderer(go, "MarginLine", Color.white * 0.9f, true);
        CreateLineRenderer(go, "Angle1", Color.green, false);
        CreateLineRenderer(go, "Angle2", Color.red, false);
        return go.transform;
    }

    private LineRenderer CreateLineRenderer(GameObject parent, string name, Color color, bool loop)
    {
        var lrGO = new GameObject(name);
        lrGO.transform.SetParent(parent.transform, worldPositionStays: true);
        var lr = lrGO.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.material.color = color;
        lr.widthMultiplier = LineWidth;
        lr.loop = loop;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private LineRenderer GetOrCreateLine(Transform visRoot, string name)
    {
        var t = visRoot.Find(name);
        if (t != null) return t.GetComponent<LineRenderer>();
        // shouldn't happen because CreateLineRenderer pre-creates them, but fallback:
        return CreateLineRenderer(visRoot.gameObject, name, Color.white, name == "MarginLine");
    }

    // helper to clear all created markers
    public void ClearMarkers()
    {
        for (int i = createdMarkers.Count - 1; i >= 0; i--)
        {
            var go = createdMarkers[i];
            if (go != null) Destroy(go);
        }
        createdMarkers.Clear();
        if (markersRoot != null) Destroy(markersRoot);
        markersRoot = null;

        // clear selection
        SelectMarker(null);
    }
    

    public void ActiveMode(int mode)
    {
        nextButton.SetActive(mode < 4);
        switch (mode)
        {
            case 0:
                break;
            case 1:
                Mask1.SetActive(false);
                Mask2.SetActive(true);
                Mask3.SetActive(true);
                break;
            case 2:
                Mask2.SetActive(false);
                Mask1.SetActive(true);
                Mask3.SetActive(true);
                //Mask4.SetActive(true);
                ModeButton2.interactable = true;
                break;
            case 3:
                Mask3.SetActive(false);
                Mask1.SetActive(true);
                Mask2.SetActive(true);
                //Mask4.SetActive(true);
                ModeButton3.interactable = true;
                break;
            case 4:
                //Mask4.SetActive(false);
                Mask1.SetActive(true);
                Mask2.SetActive(true);
                Mask3.SetActive(true);
                ToolManager.Instance.ResetRotation();
                //ModeButton4.interactable = true;
                break;
        }
    }

    public void NextMode()
    {
        switch (currentMode)
        {
            case EditorMode.None:
                currentMode = EditorMode.Marking;
                ActiveMode(1);
                break;
            case EditorMode.Marking:
                currentMode = EditorMode.Setting;
                ActiveMode(2);
                break;
            case EditorMode.Setting:
                currentMode = EditorMode.Scripting;
                ActiveMode(3);
                break;
            case EditorMode.Scripting:
                currentMode = EditorMode.Simulation;
                ActiveMode(4);
                break;
            case EditorMode.Simulation:
                currentMode = EditorMode.None;
                break;
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

    // Show visualization (margin rect + angle rays) for all markers using saved MarkStorage data
    private void UpdateAllMarkersVisualization()
    {
        for (int i = 0; i < createdMarkers.Count; i++)
        {
            var mark = createdMarkers[i];
            if (mark == null) continue;

            if (!MarkStorage.Marks.TryGetValue(mark.name, out var md))
            {
                // no saved config -> remove visualization if any
                var v = mark.transform.Find("Visualization");
                if (v != null) Destroy(v.gameObject);
                continue;
            }

            var visRoot = GetOrCreateVisualization(mark);

            // base center on ground under the marker
            float halfHeight = mark.transform.localScale.y * 0.5f;
            Vector3 baseCenter = mark.transform.position + Vector3.up * halfHeight;

            float halfX = mark.transform.localScale.x * 0.5f;
            float halfZ = mark.transform.localScale.z * 0.5f;
            float hx = halfX + md.margin;
            float hz = halfZ + md.margin;

            var marginLR = GetOrCreateLine(visRoot, "MarginLine");
            marginLR.loop = true;
            marginLR.widthMultiplier = LineWidth;
            marginLR.useWorldSpace = true;
            marginLR.positionCount = 5;
            Vector3 c0 = baseCenter + new Vector3(-hx, 0f, -hz);
            Vector3 c1 = baseCenter + new Vector3(hx, 0f, -hz);
            Vector3 c2 = baseCenter + new Vector3(hx, 0f, hz);
            Vector3 c3 = baseCenter + new Vector3(-hx, 0f, hz);
            marginLR.SetPosition(0, c0);
            marginLR.SetPosition(1, c1);
            marginLR.SetPosition(2, c2);
            marginLR.SetPosition(3, c3);
            marginLR.SetPosition(4, c0);

            var a1LR = GetOrCreateLine(visRoot, "Angle1");
            var a2LR = GetOrCreateLine(visRoot, "Angle2");
            a1LR.positionCount = 2;
            a2LR.positionCount = 2;
            a1LR.widthMultiplier = LineWidth;
            a2LR.widthMultiplier = LineWidth;
            a1LR.useWorldSpace = true;
            a2LR.useWorldSpace = true;

            Vector3 forward = mark.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 dir1 = Quaternion.Euler(0f, md.angle1, 0f) * forward;
            Vector3 dir2 = Quaternion.Euler(0f, md.angle2, 0f) * forward;
            float rayLen = Mathf.Max(hx, hz) + 0.2f;

            a1LR.SetPosition(0, baseCenter);
            a1LR.SetPosition(1, baseCenter + dir1.normalized * rayLen);
            a2LR.SetPosition(0, baseCenter);
            a2LR.SetPosition(1, baseCenter + dir2.normalized * rayLen);
        }
    }
}