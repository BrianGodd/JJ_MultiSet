using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class MarkController : MonoBehaviour
{
    public EditorManager editorManager;
    private Vector3 mouseDownPosition;
    private bool pendingSelectionClick;
    private const float ClickThresholdSqr = 25f;

    void Update()
    {
        if (editorManager == null) return;

        // only allow selection in Setting mode
        if (editorManager.currentMode == EditorManager.EditorMode.Simulation) return;

        if (Input.GetMouseButtonDown(0))
        {
            mouseDownPosition = Input.mousePosition;
            pendingSelectionClick = !IsPointerOverBlockingUI();
        }

        if (pendingSelectionClick && Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - mouseDownPosition;
            if (delta.sqrMagnitude > ClickThresholdSqr)
            {
                pendingSelectionClick = false;
            }
        }

        if (pendingSelectionClick && Input.GetMouseButtonUp(0))
        {
            pendingSelectionClick = false;
            TrySelectMarkerUnderMouse();
        }
    }

    private void TrySelectMarkerUnderMouse()
    {
        if (IsPointerOverBlockingUI()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Debug.Log("Mouse click detected, casting ray...");

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f))
        {
            var marker = FindMarkerRoot(hit.collider.transform);
            if (marker != null)
            {
                Debug.Log("Marker clicked: " + marker.name);
                editorManager.SelectMarker(marker);
            }
            else
            {
                editorManager.SelectMarker(null);
            }
        }
        else if (!editorManager.IsAnyEditPanelOpen())
        {
            editorManager.SelectMarker(null);
        }
    }

    private bool IsPointerOverBlockingUI()
    {
        if (EventSystem.current == null) return false;

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            if (go == null) continue;
            if (!IsBlockingUIObject(go)) continue;
            return true;
        }

        return false;
    }

    private bool IsBlockingUIObject(GameObject go)
    {
        if (editorManager == null) return true;

        if (editorManager.drawPanel != null)
        {
            Transform t = go.transform;

            if (t == editorManager.drawPanel || t.IsChildOf(editorManager.drawPanel))
            {
                if (editorManager.selectionRect != null && (t == editorManager.selectionRect || t.IsChildOf(editorManager.selectionRect)))
                {
                    return false;
                }

                if (editorManager.labelInputPanel != null && editorManager.labelInputPanel.activeInHierarchy &&
                    (t == editorManager.labelInputPanel.transform || t.IsChildOf(editorManager.labelInputPanel.transform)))
                {
                    return true;
                }

                if (editorManager.settingPanel != null && editorManager.settingPanel.activeInHierarchy &&
                    (t == editorManager.settingPanel.transform || t.IsChildOf(editorManager.settingPanel.transform)))
                {
                    return true;
                }

                if (editorManager.scriptingPanel != null && editorManager.scriptingPanel.activeInHierarchy &&
                    (t == editorManager.scriptingPanel.transform || t.IsChildOf(editorManager.scriptingPanel.transform)))
                {
                    return true;
                }

                if (editorManager.simulationPanel != null && editorManager.simulationPanel.activeInHierarchy &&
                    (t == editorManager.simulationPanel.transform || t.IsChildOf(editorManager.simulationPanel.transform)))
                {
                    return true;
                }

                if (editorManager.uploadPanel != null && editorManager.uploadPanel.activeInHierarchy &&
                    (t == editorManager.uploadPanel.transform || t.IsChildOf(editorManager.uploadPanel.transform)))
                {
                    return true;
                }

                if (editorManager.nextButton != null && editorManager.nextButton.activeInHierarchy &&
                    (t == editorManager.nextButton.transform || t.IsChildOf(editorManager.nextButton.transform)))
                {
                    return true;
                }

                return false;
            }
        }

        return true;
    }

    // climb the hierarchy to find a GameObject that looks like a marker (named "Mark_" prefix)
    private GameObject FindMarkerRoot(Transform t)
    {
        while (t != null)
        {
            if (t.gameObject.layer == 6) return t.gameObject; // Assuming layer 6 is the marker layer
            t = t.parent;
        }
        return null;
    }
}
