using UnityEngine;
using UnityEngine.EventSystems;

public class MarkController : MonoBehaviour
{
    public EditorManager editorManager;

    void Update()
    {
        if (editorManager == null) return;

        // only allow selection in Setting mode
        if (editorManager.currentMode == EditorManager.EditorMode.Simulation) return;

        if (Input.GetMouseButtonDown(0))
        {
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
                    // clicked non-marker -> deselect
                    editorManager.SelectMarker(null);
                }
            }
            else if(!editorManager.settingPanel.active && !editorManager.scriptingPanel.active && !editorManager.labelInputPanel.active)
            {
                // nothing hit -> deselect
                editorManager.SelectMarker(null);
            }
        }
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
