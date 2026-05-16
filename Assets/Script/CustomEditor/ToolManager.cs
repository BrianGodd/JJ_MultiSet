using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

public class ToolManager : MonoBehaviour
{
    public static ToolManager Instance { get; private set; }

    public EditorManager editorManager;

    public CinemachineCamera virtualCamera;
    public Camera previewCamera;
    public GameObject previewImage, camIcon;
    public Canvas canvas;

    [Tooltip("Offset from the mouse position in canvas local units. Default places preview to the bottom-right of the cursor.")]
    public Vector2 offset = new Vector2(10f, -10f);

    RectTransform _previewRect;
    RectTransform _camIconRect;
    RectTransform _canvasRect;

    void Start()
    {
        Instance = this;

        if (previewImage == null)
        {
            Debug.LogWarning("previewImage not assigned on ToolManager.");
            return;
        }

        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogWarning("No Canvas found for ToolManager. Preview positioning may fail.");
        }

        _previewRect = previewImage.GetComponent<RectTransform>();
        if (_previewRect == null)
            Debug.LogWarning("previewImage does not have a RectTransform (is it a UI element?).");

        _camIconRect = camIcon.GetComponent<RectTransform>();
        if (_camIconRect == null)
            Debug.LogWarning("camIcon does not have a RectTransform (is it a UI element?).");

        if (canvas != null)
            _canvasRect = canvas.GetComponent<RectTransform>();

        // Start hidden
        previewImage.SetActive(false);
    }

    void Update()
    {
        if (previewImage == null || _previewRect == null)
            return;

        // Show on right mouse button down
        if (Input.GetMouseButtonDown(1))
        {
            // set preview camera depending on the mouse position
            // update preview camera position so its XZ centers on the point under the cursor while keeping Y unchanged
            UpdatePreviewCameraPosition();
            previewImage.SetActive(true);
            camIcon.SetActive(true);
            UpdatePreviewPosition();
        }

        // While held, update position so it follows the cursor
        if (Input.GetMouseButton(1) && previewImage.activeSelf)
        {
            UpdatePreviewPosition();
            UpdatePreviewCameraPosition();
        }

        // Hide on right mouse button up or if user presses Escape
        if (Input.GetMouseButtonUp(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            previewImage.SetActive(false);
            camIcon.SetActive(false);
        }

        // left arrow key to turn the preview camera to left 45 degrees
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (previewCamera != null)
            {
                previewCamera.transform.Rotate(0f, -45f, 0f, Space.World);
                camIcon.transform.Rotate(0f, 0f, 45f);
            }
            if (editorManager.simPlayerMarker != null)
            {
                editorManager.simPlayerMarker.transform.Rotate(0f, -45f, 0f, Space.World);
            }
        }

        // right arrow key to turn the preview camera to right 45 degrees
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (previewCamera != null)
            {
                previewCamera.transform.Rotate(0f, 45f, 0f, Space.World);
                camIcon.transform.Rotate(0f, 0f, -45f);
            }
            if (editorManager.simPlayerMarker != null)
            {
                editorManager.simPlayerMarker.transform.Rotate(0f, 45f, 0f, Space.World);
            }
        }

        // scroll wheel to zoom in/out the cinemachine virtual camera with cinemachine group framing's framing size
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f && virtualCamera != null)
        {
            virtualCamera.GetComponent<CinemachineGroupFraming>().FramingSize += scroll * 5f; // adjust zoom speed with multiplier
            //max = 2, min = 0.01
            virtualCamera.GetComponent<CinemachineGroupFraming>().FramingSize = Mathf.Clamp(virtualCamera.GetComponent<CinemachineGroupFraming>().FramingSize, 0.01f, 2f);
        }

    }

    // Move the preview camera so that its X,Z position centers on the point under the mouse cursor
    void UpdatePreviewCameraPosition()
    {
        if (previewCamera == null)
            return;

        // choose a camera to cast the picking ray (prefer main camera)
        Camera caster = Camera.main != null ? Camera.main : (Camera.current != null ? Camera.current : previewCamera);
        if (caster == null)
            return;

        // Define a horizontal plane at the preview camera's current Y
        float camY = previewCamera.transform.position.y;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, camY, 0f));

        Ray ray = caster.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hit = ray.GetPoint(enter);
            Vector3 pos = previewCamera.transform.position;
            pos.x = hit.x;
            pos.z = hit.z;
            previewCamera.transform.position = pos;
        }
    }

    void UpdatePreviewPosition()
    {
        if (_canvasRect == null)
        {
            // Fallback: position in screen space via Transform.position
            Vector3 screenPos = Input.mousePosition;
            // place to bottom-right of cursor
            screenPos += new Vector3(offset.x, offset.y, 0f);
            // Convert to world point for the preview's transform
            Vector3 worldPos = Camera.main != null ? Camera.main.ScreenToWorldPoint(screenPos) : screenPos;
            // Keep original z
            worldPos.z = _previewRect.position.z;
            _previewRect.position = worldPos;
            return;
        }

        Vector2 localPoint;
        Camera cam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, cam, out localPoint);

        // Make preview pivot such that top-left of the preview aligns with the anchored position
        _previewRect.pivot = new Vector2(0f, 1f);

        Vector2 anchored = localPoint + offset;

        // Clamp so the preview stays inside the canvas rect
        Rect canvasRect = _canvasRect.rect;
        Vector2 previewSize = _previewRect.rect.size;

        float minX = canvasRect.xMin;
        float maxX = canvasRect.xMax - previewSize.x; // since pivot.x = 0
        float maxY = canvasRect.yMax;
        float minY = canvasRect.yMin + previewSize.y; // since pivot.y = 1

        anchored.x = Mathf.Clamp(anchored.x, minX, maxX);
        anchored.y = Mathf.Clamp(anchored.y, minY, maxY);

        _previewRect.anchoredPosition = anchored;

        _camIconRect.anchoredPosition = localPoint;
    }

    public void ResetRotation()
    {
        if (previewCamera != null)
        {
            previewCamera.transform.localRotation = Quaternion.identity; // reset to default angle
            camIcon.transform.localRotation = Quaternion.identity; // reset icon rotation
        }
        if (editorManager.simPlayerMarker != null)
        {
            editorManager.simPlayerMarker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // reset marker rotation
        }
    }
}
