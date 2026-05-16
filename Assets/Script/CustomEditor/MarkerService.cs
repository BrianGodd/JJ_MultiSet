using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal class MarkerService
{
    private readonly EditorManager editorManager;

    public MarkerService(EditorManager editorManager)
    {
        this.editorManager = editorManager;
    }

    public void InitializeSelectionVisuals()
    {
        CacheSelectionRectVisuals();
        if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(false);
        EnsureSelectionRectOutline();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (editorManager.currentMode != EditorManager.EditorMode.Marking) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (editorManager.drawPanel == null) return;

        editorManager.dragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(editorManager.drawPanel, eventData.position, eventData.pressEventCamera, out editorManager.dragStart);
        editorManager.dragEnd = editorManager.dragStart;
        ApplyDragSelectionVisualState();
        UpdateSelectionVisual();
        if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(true);
        if (editorManager.labelInputPanel != null) editorManager.labelInputPanel.SetActive(false);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (editorManager.currentMode != EditorManager.EditorMode.Marking) return;
        if (!editorManager.dragging) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(editorManager.drawPanel, eventData.position, eventData.pressEventCamera, out editorManager.dragEnd);
        UpdateSelectionVisual();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (editorManager.currentMode != EditorManager.EditorMode.Marking) return;
        if (!editorManager.dragging) return;

        editorManager.dragging = false;
        Vector2 size = editorManager.dragEnd - editorManager.dragStart;
        if (editorManager.labelInputPanel != null && size.sqrMagnitude > 100)
        {
            editorManager.labelInputPanel.SetActive(true);
            editorManager.confirmButton.gameObject.SetActive(true);
            editorManager.renewButton.gameObject.SetActive(false);
            editorManager.deleteButton.interactable = false;
            if (editorManager.labelInput != null)
            {
                editorManager.labelInput.text = string.Empty;
                editorManager.labelInput.ActivateInputField();
            }
        }
    }

    public void OnCancelLabel()
    {
        if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(false);
        if (editorManager.labelInputPanel != null) editorManager.labelInputPanel.SetActive(false);
        SelectMarker(null);
    }

    public void OnDeleteLabel()
    {
        if (editorManager.selectedMarker != null)
        {
            MarkStorage.Remove(editorManager.selectedMarker.name);
            editorManager.createdMarkers.Remove(editorManager.selectedMarker);
            Object.Destroy(editorManager.selectedMarker);
            editorManager.selectedMarker = null;
            editorManager.selectedRenderer = null;
            editorManager.selectedOriginalMaterial = null;
        }
        OnCancelLabel();
    }

    public void OnConfirmLabel()
    {
        if (editorManager.labelInputPanel != null) editorManager.labelInputPanel.SetActive(false);
        if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(false);

        string label = editorManager.labelInput != null ? editorManager.labelInput.text.Trim() : "Label";
        if (string.IsNullOrEmpty(label)) label = "Label";

        GameObject mapRoot = editorManager.uiManager != null ? editorManager.uiManager.currentMapObject : null;
        if (editorManager.markersRoot == null)
        {
            editorManager.markersRoot = new GameObject("MarkersRoot");
            if (mapRoot != null) editorManager.markersRoot.transform.SetParent(mapRoot.transform, worldPositionStays: true);
        }

        Vector2 min = Vector2.Min(editorManager.dragStart, editorManager.dragEnd);
        Vector2 max = Vector2.Max(editorManager.dragStart, editorManager.dragEnd);

        Camera cam = editorManager.mainCamera;
        if (cam == null)
        {
            Debug.LogError("No Main Camera found.");
            return;
        }

        float planeY = 0f;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var combined, true))
        {
            planeY = combined.min.y;
        }
        Plane plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));

        Vector3 worldCenter;
        float worldWidth = 1f;
        float worldDepth = 1f;

        Vector3 worldMin = editorManager.drawPanel.TransformPoint(new Vector3(min.x, min.y, 0));
        Vector3 worldMax = editorManager.drawPanel.TransformPoint(new Vector3(max.x, max.y, 0));
        Vector3 worldMinXMaxY = editorManager.drawPanel.TransformPoint(new Vector3(min.x, max.y, 0));
        Vector3 worldMaxXMinY = editorManager.drawPanel.TransformPoint(new Vector3(max.x, min.y, 0));

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
            Vector3 minV = pts[0];
            Vector3 maxV = pts[0];
            foreach (var pt in pts)
            {
                minV = Vector3.Min(minV, pt);
                maxV = Vector3.Max(maxV, pt);
            }
            worldWidth = Vector3.Distance(new Vector3(minV.x, 0, minV.z), new Vector3(maxV.x, 0, minV.z));
            worldDepth = Vector3.Distance(new Vector3(minV.x, 0, minV.z), new Vector3(minV.x, 0, maxV.z));
            worldCenter = (worldTL + worldTR + worldBR + worldBL) / 4f;
        }
        else
        {
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cam, editorManager.drawPanel.TransformPoint((min + max) * 0.5f));
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

        float height = editorManager.minColumnHeight;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var cb2, true))
        {
            height = Mathf.Max(editorManager.minColumnHeight, cb2.size.y);
        }

        GameObject column = CreateMarkerObject(label, worldCenter + Vector3.up * (height * 0.5f), new Vector3(worldWidth, height, worldDepth));

        var md = new MarkStorage.MarkData
        {
            keyword = string.Empty,
            details = string.Empty,
            position = column.transform.position,
            scale = column.transform.localScale,
            margin = Mathf.Max(0.5f, Mathf.Max(worldWidth, worldDepth) * 0.5f),
            angle1 = -30f,
            angle2 = 30f
        };
        MarkStorage.Save(column.name, md);
    }

    public void LoadMarksFromData(Dictionary<string, MarkStorage.MarkData> marks)
    {
        if (marks == null)
        {
            Debug.LogWarning("LoadMarksFromData called with null marks.");
            return;
        }

        ClearMarkers();
        MarkStorage.Clear();

        foreach (var kv in marks)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;

            var source = kv.Value;
            var copied = new MarkStorage.MarkData
            {
                label = string.IsNullOrEmpty(source.label) ? kv.Key : source.label,
                position = source.position,
                scale = source.scale,
                margin = source.margin,
                angle1 = source.angle1,
                angle2 = source.angle2,
                keyword = source.keyword ?? string.Empty,
                details = source.details ?? string.Empty
            };

            CreateMarkerObject(kv.Key, copied.position, copied.scale);
            MarkStorage.Save(kv.Key, copied);
        }
    }

    public void SelectMarker(GameObject marker)
    {
        if (editorManager.selectedRenderer != null)
        {
            if (editorManager.selectedOriginalMaterial != null)
            {
                editorManager.selectedRenderer.material = editorManager.selectedOriginalMaterial;
            }
            editorManager.selectedRenderer = null;
            editorManager.selectedOriginalMaterial = null;
        }

        editorManager.selectedMarker = marker;

        if (editorManager.currentMode == EditorManager.EditorMode.Setting) HideOtherMarkersVisualization();

        if (editorManager.selectedMarker == null)
        {
            ApplyDragSelectionVisualState();
            if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(false);
            return;
        }

        var rend = editorManager.selectedMarker.GetComponent<Renderer>() ?? editorManager.selectedMarker.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            editorManager.selectedRenderer = rend;
            editorManager.selectedOriginalMaterial = rend.material;
            var highlightMat = new Material(editorManager.selectedOriginalMaterial);
            if (highlightMat.HasProperty("_Color")) highlightMat.color = editorManager.highlightColor;
            if (highlightMat.HasProperty("_EmissionColor"))
            {
                highlightMat.SetColor("_EmissionColor", editorManager.highlightColor * 0.5f);
                highlightMat.EnableKeyword("_EMISSION");
            }
            rend.material = highlightMat;
        }

        if (editorManager.currentMode == EditorManager.EditorMode.Setting)
        {
            ShowSettingPanelForMarker(editorManager.selectedMarker);
        }
        else if (editorManager.currentMode == EditorManager.EditorMode.Scripting)
        {
            ShowScriptingPanelForMarker(editorManager.selectedMarker);
        }
        else if (editorManager.currentMode == EditorManager.EditorMode.Marking)
        {
            ShowSelectionRectForMarker(editorManager.selectedMarker);
            ShowLabelingPanelForMarker(editorManager.selectedMarker);
        }
    }

    public void HideScriptingPanel()
    {
        if (editorManager.scriptingPanel != null) editorManager.scriptingPanel.SetActive(false);
        SelectMarker(null);
    }

    public void OnApplyScript()
    {
        if (editorManager.selectedMarker == null) return;

        string keyword = editorManager.keywordInput != null ? editorManager.keywordInput.text.Trim() : string.Empty;
        string details = editorManager.detailsInput != null ? editorManager.detailsInput.text.Trim() : string.Empty;

        if (MarkStorage.TryGet(editorManager.selectedMarker.name, out var data))
        {
            data.keyword = keyword;
            data.details = details;
            MarkStorage.Save(editorManager.selectedMarker.name, data);
        }

        HideScriptingPanel();
    }

    public void HideSettingPanel()
    {
        if (editorManager.settingPanel != null) editorManager.settingPanel.SetActive(false);
        if (editorManager.selectedMarker != null)
        {
            var vis = editorManager.selectedMarker.transform.Find("Visualization");
            if (vis != null) Object.Destroy(vis.gameObject);
        }
        SelectMarker(null);
    }

    public void OnApplySettings()
    {
        if (editorManager.selectedMarker == null) return;

        if (!float.TryParse(editorManager.marginInput.text, out float margin)) margin = 1f;
        if (!float.TryParse(editorManager.angle1Input.text, out float a1)) a1 = -30f;
        if (!float.TryParse(editorManager.angle2Input.text, out float a2)) a2 = 30f;

        if (MarkStorage.TryGet(editorManager.selectedMarker.name, out var md))
        {
            md.margin = margin;
            md.angle1 = a1;
            md.angle2 = a2;
            MarkStorage.Save(editorManager.selectedMarker.name, md);
            UpdateSelectedVisualizationFromInputs();
        }

        HideSettingPanel();
    }

    public void UpdateSelectedVisualizationFromInputs()
    {
        if (editorManager.selectedMarker == null) return;

        if (!float.TryParse(editorManager.marginInput?.text, out float margin)) margin = 1f;
        if (!float.TryParse(editorManager.angle1Input?.text, out float a1)) a1 = -30f;
        if (!float.TryParse(editorManager.angle2Input?.text, out float a2)) a2 = 30f;

        var visRoot = GetOrCreateVisualization(editorManager.selectedMarker);

        float halfHeight = editorManager.selectedMarker.transform.localScale.y * 0.5f;
        Vector3 baseCenter = editorManager.selectedMarker.transform.position + Vector3.up * halfHeight;
        float halfX = editorManager.selectedMarker.transform.localScale.x * 0.5f;
        float halfZ = editorManager.selectedMarker.transform.localScale.z * 0.5f;
        float hx = halfX + margin;
        float hz = halfZ + margin;
        float lineWidth = GetVisualizationLineWidth(editorManager.selectedMarker);

        var marginLR = GetOrCreateLine(visRoot, "MarginLine");
        marginLR.loop = true;
        marginLR.widthMultiplier = lineWidth;
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
        a1LR.widthMultiplier = lineWidth;
        a2LR.widthMultiplier = lineWidth;
        a1LR.useWorldSpace = true;
        a2LR.useWorldSpace = true;

        Vector3 forward = editorManager.selectedMarker.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 dir1 = Quaternion.Euler(0f, a1, 0f) * forward;
        Vector3 dir2 = Quaternion.Euler(0f, a2, 0f) * forward;
        float rayLen = Mathf.Max(hx, hz) + 0.2f;

        a1LR.SetPosition(0, baseCenter);
        a1LR.SetPosition(1, baseCenter + dir1.normalized * rayLen);
        a2LR.SetPosition(0, baseCenter);
        a2LR.SetPosition(1, baseCenter + dir2.normalized * rayLen);
    }

    public void ClearMarkers()
    {
        for (int i = editorManager.createdMarkers.Count - 1; i >= 0; i--)
        {
            var go = editorManager.createdMarkers[i];
            if (go != null) Object.Destroy(go);
        }

        editorManager.createdMarkers.Clear();
        if (editorManager.markersRoot != null) Object.Destroy(editorManager.markersRoot);
        editorManager.markersRoot = null;
        SelectMarker(null);
    }

    public void UpdateAllMarkersVisualization()
    {
        for (int i = 0; i < editorManager.createdMarkers.Count; i++)
        {
            var mark = editorManager.createdMarkers[i];
            if (mark == null) continue;

            if (!MarkStorage.Marks.TryGetValue(mark.name, out var md))
            {
                var v = mark.transform.Find("Visualization");
                if (v != null) Object.Destroy(v.gameObject);
                continue;
            }

            var visRoot = GetOrCreateVisualization(mark);
            float halfHeight = mark.transform.localScale.y * 0.5f;
            Vector3 baseCenter = mark.transform.position + Vector3.up * halfHeight;
            float halfX = mark.transform.localScale.x * 0.5f;
            float halfZ = mark.transform.localScale.z * 0.5f;
            float hx = halfX + md.margin;
            float hz = halfZ + md.margin;
            float lineWidth = GetVisualizationLineWidth(mark);

            var marginLR = GetOrCreateLine(visRoot, "MarginLine");
            marginLR.loop = true;
            marginLR.widthMultiplier = lineWidth;
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
            a1LR.widthMultiplier = lineWidth;
            a2LR.widthMultiplier = lineWidth;
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

    internal Vector3 GetMarkerBaseCenter(GameObject mark, MarkStorage.MarkData md)
    {
        float halfHeight = mark.transform.localScale.y * 0.5f;
        return mark.transform.position - Vector3.up * halfHeight;
    }

    private void UpdateSelectionVisual()
    {
        if (editorManager.selectionRect == null || editorManager.drawPanel == null) return;
        Vector2 size = editorManager.dragEnd - editorManager.dragStart;
        editorManager.selectionRect.anchoredPosition = editorManager.dragStart + size * 0.5f;
        editorManager.selectionRect.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        UpdateSelectionRectOutline();
    }

    private GameObject CreateMarkerObject(string label, Vector3 position, Vector3 scale)
    {
        if (editorManager.markersRoot == null)
        {
            GameObject mapRoot = editorManager.uiManager != null ? editorManager.uiManager.currentMapObject : null;
            editorManager.markersRoot = new GameObject("MarkersRoot");
            if (mapRoot != null) editorManager.markersRoot.transform.SetParent(mapRoot.transform, worldPositionStays: true);
        }

        GameObject column = GameObject.CreatePrimitive(PrimitiveType.Cube);
        column.name = label;
        column.transform.position = position;
        column.transform.localScale = scale;
        column.transform.SetParent(editorManager.markersRoot.transform, worldPositionStays: true);

        if (editorManager.markMaterial != null)
        {
            var rend = column.GetComponent<Renderer>();
            rend.sharedMaterial = new Material(editorManager.markMaterial);
            rend.sharedMaterial.color = new Color(Random.value, Random.value, Random.value, 0.75f);
        }

        CreateMarkerLabel(column, label, scale.y);

        var mc = column.AddComponent<MarkController>();
        mc.editorManager = editorManager;

        var col = column.GetComponent<Collider>();
        if (col == null) column.AddComponent<BoxCollider>();

        column.layer = LayerMask.NameToLayer("Marker");
        editorManager.createdMarkers.Add(column);
        return column;
    }

    private void CreateMarkerLabel(GameObject column, string label, float height)
    {
        GameObject labelGO = new GameObject("Label_" + label);
        labelGO.transform.SetParent(column.transform, worldPositionStays: false);
        labelGO.transform.localPosition = Vector3.zero;

        Vector3 desiredWorldScale = Vector3.one;
        Vector3 parentScale = labelGO.transform.parent.lossyScale;
        float safeLength = Mathf.Max(1f, label.Length * 0.2f);

        labelGO.transform.localScale = (Mathf.Sqrt(parentScale.x * parentScale.z) / safeLength) * new Vector3(
            desiredWorldScale.z / parentScale.z,
            desiredWorldScale.x / parentScale.x,
            1f);

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
        bb.cam = editorManager.mainCamera;
        labelGO.layer = LayerMask.NameToLayer("Marker");
    }

    public void OnRenewLabel()
    {
        if (editorManager.selectedMarker == null || editorManager.labelInput == null) return;

        string newLabel = editorManager.labelInput.text.Trim();
        if (string.IsNullOrEmpty(newLabel)) newLabel = "Label";
        if (newLabel != editorManager.selectedMarker.name && MarkStorage.Marks.ContainsKey(newLabel))
        {
            Debug.LogError($"A marker with the name '{newLabel}' already exists. Please choose a different name.");
            return;
        }

        string oldName = editorManager.selectedMarker.name;
        editorManager.selectedMarker.name = newLabel;

        if (MarkStorage.Marks.ContainsKey(oldName))
        {
            var data = MarkStorage.Marks[oldName];
            MarkStorage.Remove(oldName);
            MarkStorage.Save(newLabel, data);
        }

        var labelGO = editorManager.selectedMarker.transform.Find("Label_" + oldName);
        if (labelGO != null)
        {
            var tmp = labelGO.GetComponent<TextMeshPro>();
            if (tmp != null) tmp.text = newLabel;
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

    private void EnsureSelectionRectOutline()
    {
        if (editorManager.selectionRect == null) return;

        editorManager.selectionRectOutline = editorManager.selectionRect.GetComponent<Outline>();
        if (editorManager.selectionRectOutline == null)
        {
            editorManager.selectionRectOutline = editorManager.selectionRect.gameObject.AddComponent<Outline>();
        }

        UpdateSelectionRectOutline();
        editorManager.selectionRectOutline.enabled = false;
    }

    private void CacheSelectionRectVisuals()
    {
        if (editorManager.selectionRect == null) return;
        if (editorManager.selectionRectImage == null)
        {
            editorManager.selectionRectImage = editorManager.selectionRect.GetComponent<Image>();
            if (editorManager.selectionRectImage != null)
            {
                editorManager.selectionRectBaseColor = editorManager.selectionRectImage.color;
            }
        }
    }

    private void UpdateSelectionRectOutline()
    {
        if (editorManager.selectionRectOutline == null) return;
        editorManager.selectionRectOutline.effectColor = editorManager.selectionOutlineColor;
        editorManager.selectionRectOutline.effectDistance = editorManager.selectionOutlineDistance;
        editorManager.selectionRectOutline.useGraphicAlpha = false;
    }

    private void ApplyDragSelectionVisualState()
    {
        CacheSelectionRectVisuals();

        if (editorManager.selectionRectImage != null)
        {
            editorManager.selectionRectImage.color = editorManager.selectionRectBaseColor;
        }

        if (editorManager.selectionRectOutline != null)
        {
            UpdateSelectionRectOutline();
            editorManager.selectionRectOutline.enabled = false;
        }
    }

    private void ShowSelectionRectForMarker(GameObject marker)
    {
        if (editorManager.selectionRect == null || editorManager.drawPanel == null || marker == null) return;

        Camera cam = editorManager.mainCamera ?? Camera.main;
        if (cam == null) return;

        Bounds bounds = GetMarkerScreenBounds(marker);
        Vector3 screenMin = cam.WorldToScreenPoint(bounds.min);
        Vector3 screenMax = cam.WorldToScreenPoint(bounds.max);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(editorManager.drawPanel, screenMin, cam, out var localMin);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(editorManager.drawPanel, screenMax, cam, out var localMax);

        Vector2 min = Vector2.Min(localMin, localMax);
        Vector2 max = Vector2.Max(localMin, localMax);
        editorManager.selectionRect.anchoredPosition = (min + max) * 0.5f;
        editorManager.selectionRect.sizeDelta = max - min;
        editorManager.selectionRect.gameObject.SetActive(true);

        if (editorManager.selectionRectOutline != null)
        {
            editorManager.selectionRectOutline.effectColor = editorManager.highlightColor;
            editorManager.selectionRectOutline.enabled = true;
        }
    }

    private Bounds GetMarkerScreenBounds(GameObject marker)
    {
        var renderers = marker.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return new Bounds(marker.transform.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private void ShowLabelingPanelForMarker(GameObject marker)
    {
        if (editorManager.labelInputPanel == null) return;
        editorManager.labelInputPanel.SetActive(true);

        if (editorManager.labelInput != null)
        {
            editorManager.labelInput.text = marker.name;
            editorManager.labelInput.ActivateInputField();
        }

        editorManager.deleteButton.interactable = true;
        editorManager.renewButton.gameObject.SetActive(true);
        editorManager.confirmButton.gameObject.SetActive(false);
    }

    private void ShowScriptingPanelForMarker(GameObject marker)
    {
        if (editorManager.scriptingPanel == null) return;
        editorManager.scriptingPanel.SetActive(true);

        if (MarkStorage.TryGet(marker.name, out var data))
        {
            if (editorManager.keywordInput != null) editorManager.keywordInput.text = data.keyword;
            if (editorManager.detailsInput != null) editorManager.detailsInput.text = data.details;
        }
        else
        {
            if (editorManager.keywordInput != null) editorManager.keywordInput.text = string.Empty;
            if (editorManager.detailsInput != null) editorManager.detailsInput.text = string.Empty;
        }
    }

    private void ShowSettingPanelForMarker(GameObject marker)
    {
        if (editorManager.settingPanel == null) return;
        editorManager.settingPanel.SetActive(true);

        if (MarkStorage.TryGet(marker.name, out var data))
        {
            if (editorManager.marginInput != null) editorManager.marginInput.text = data.margin.ToString("F2");
            if (editorManager.angle1Input != null) editorManager.angle1Input.text = data.angle1.ToString("F1");
            if (editorManager.angle2Input != null) editorManager.angle2Input.text = data.angle2.ToString("F1");
        }
        else
        {
            if (editorManager.marginInput != null) editorManager.marginInput.text = "1.0";
            if (editorManager.angle1Input != null) editorManager.angle1Input.text = "-30";
            if (editorManager.angle2Input != null) editorManager.angle2Input.text = "30";
        }

        UpdateSelectedVisualizationFromInputs();
    }

    private void HideOtherMarkersVisualization()
    {
        foreach (var mark in editorManager.createdMarkers)
        {
            if (mark == null || mark == editorManager.selectedMarker) continue;
            var vis = mark.transform.Find("Visualization");
            if (vis != null) Object.Destroy(vis.gameObject);
        }
    }

    private Transform GetOrCreateVisualization(GameObject marker)
    {
        var t = marker.transform.Find("Visualization");
        if (t != null) return t;

        var go = new GameObject("Visualization");
        go.transform.SetParent(marker.transform, worldPositionStays: true);
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
        lr.widthMultiplier = EditorManager.LineWidth;
        lr.loop = loop;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private float GetVisualizationLineWidth(GameObject marker)
    {
        if (marker == null) return EditorManager.LineWidth;

        Vector3 scale = marker.transform.localScale;
        float smallestFootprint = Mathf.Max(0.01f, Mathf.Min(scale.x, scale.z));

        // Scale line thickness with the marker footprint so small labels do not get oversized guides.
        return Mathf.Clamp(smallestFootprint * 0.08f, 0.02f, 0.14f);
    }

    private LineRenderer GetOrCreateLine(Transform visRoot, string name)
    {
        var t = visRoot.Find(name);
        if (t != null) return t.GetComponent<LineRenderer>();
        return CreateLineRenderer(visRoot.gameObject, name, Color.white, name == "MarginLine");
    }
}
