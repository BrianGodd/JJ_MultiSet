using UnityEngine;

internal class SimulationController
{
    private readonly EditorManager editorManager;
    private readonly MarkerService markerService;

    public SimulationController(EditorManager editorManager, MarkerService markerService)
    {
        this.editorManager = editorManager;
        this.markerService = markerService;
    }

    public void Tick()
    {
        if (editorManager.currentMode == EditorManager.EditorMode.Simulation)
        {
            EnsureSimulationActive(true);
            markerService.UpdateAllMarkersVisualization();
            UpdateSimulation();
            EvaluateSimulationAgainstMarks();
            return;
        }

        EnsureSimulationActive(false);
    }

    private void EnsureSimulationActive(bool active)
    {
        if (editorManager.simActive == active) return;
        editorManager.simActive = active;

        if (editorManager.simActive)
        {
            if (editorManager.simulationPanel != null) editorManager.simulationPanel.SetActive(true);

            if (editorManager.simPlayerMarker == null)
            {
                editorManager.simPlayerMarker = new GameObject("SimPlayerMarker");
                var sr = editorManager.simPlayerMarker.AddComponent<SpriteRenderer>();
                sr.sprite = editorManager.simulationMarkerSprite;
                sr.material = editorManager.simulationMarkerMaterial;
                editorManager.simPlayerMarker.transform.localScale = Vector3.one * GetSimulationMarkerScale();
                editorManager.simPlayerMarker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                editorManager.simPlayerMarker.SetActive(editorManager.simulationControlMode == EditorManager.SimulationControlMode.Mouse);
                var col = editorManager.simPlayerMarker.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }

            if (editorManager.simPlayerMarker != null)
            {
                editorManager.simPlayerMarker.transform.localScale = Vector3.one * GetSimulationMarkerScale();
            }

            float planeY = GetSimulationPlaneY();
            if (editorManager.simPlayerMarker != null)
            {
                if (editorManager.simulationControlMode == EditorManager.SimulationControlMode.Mouse)
                {
                    editorManager.simPlayerMarkerPlaced = true;
                    editorManager.simPlayerPosition = editorManager.simPlayerMarker.transform.position;
                    if (editorManager.simPlayerPosition == Vector3.zero)
                    {
                        editorManager.simPlayerPosition = GetSimulationSpawnPosition(planeY);
                    }
                    editorManager.simPlayerPosition.y = planeY;
                    editorManager.simPlayerMarker.transform.position = editorManager.simPlayerPosition + Vector3.up * 0.1f;
                    editorManager.simPlayerMarker.SetActive(true);
                }
                else
                {
                    editorManager.simPlayerMarkerPlaced = false;
                    editorManager.simPlayerPosition = GetSimulationSpawnPosition(planeY);
                    editorManager.simPlayerMarker.transform.position = editorManager.simPlayerPosition + Vector3.up * 0.1f;
                    editorManager.simPlayerMarker.SetActive(false);
                }
            }
        }
        else
        {
            if (editorManager.simulationPanel != null) editorManager.simulationPanel.SetActive(false);
            if (editorManager.simPlayerMarker != null) Object.Destroy(editorManager.simPlayerMarker);
            editorManager.simPlayerMarker = null;

            if (editorManager.nearestOutputText != null) editorManager.nearestOutputText.text = "null";
            if (editorManager.directionOutputText != null) editorManager.directionOutputText.text = "null";
            if (editorManager.insideOutputText != null) editorManager.insideOutputText.text = "null";
            if (editorManager.simpleMSGText != null) editorManager.simpleMSGText.text = "No mark nearby.";
            editorManager.simPlayerMarkerPlaced = false;

            for (int i = 0; i < editorManager.createdMarkers.Count; i++)
            {
                var m = editorManager.createdMarkers[i];
                if (m == null) continue;
                var vis = m.transform.Find("Visualization");
                if (vis != null) Object.Destroy(vis.gameObject);
            }
        }
    }

    private bool ScreenToGroundPlane(Vector2 screenPos, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        Camera cam = editorManager.mainCamera ?? Camera.main;
        if (cam == null) return false;

        float planeY = 0f;
        GameObject mapRoot = editorManager.uiManager != null ? editorManager.uiManager.currentMapObject : null;
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

    private void UpdateSimulation()
    {
        if (editorManager.simPlayerMarker != null)
        {
            if (editorManager.simulationControlMode == EditorManager.SimulationControlMode.Mouse)
            {
                if (!editorManager.simPlayerMarker.activeSelf) editorManager.simPlayerMarker.SetActive(true);
                if (!editorManager.simPlayerMarkerPlaced)
                {
                    float planeY = GetSimulationPlaneY();
                    editorManager.simPlayerPosition = GetSimulationSpawnPosition(planeY);
                    editorManager.simPlayerPosition.y = planeY;
                    editorManager.simPlayerMarker.transform.position = editorManager.simPlayerPosition + Vector3.up * 0.1f;
                    editorManager.simPlayerMarkerPlaced = true;
                }
            }
            else if (editorManager.simulationControlMode == EditorManager.SimulationControlMode.Keyboard && !editorManager.simPlayerMarkerPlaced && editorManager.simPlayerMarker.activeSelf)
            {
                editorManager.simPlayerMarker.SetActive(false);
            }
        }

        switch (editorManager.simulationControlMode)
        {
            case EditorManager.SimulationControlMode.Mouse:
                UpdateSimulationFromMouse();
                break;
            default:
                UpdateSimulationFromKeyboard();
                break;
        }
    }

    private void UpdateSimulationFromMouse()
    {
        Camera cam = editorManager.mainCamera ?? Camera.main;
        if (cam == null) return;

        if (ScreenToGroundPlane(Input.mousePosition, out var pos))
        {
            editorManager.simPlayerPosition = pos;
            if (editorManager.simPlayerMarker != null)
            {
                editorManager.simPlayerMarker.transform.position = pos + Vector3.up * 0.1f;
            }
        }
    }

    private void UpdateSimulationFromKeyboard()
    {
        if (editorManager.simPlayerMarker == null) return;

        float planeY = GetSimulationPlaneY();
        if (!editorManager.simPlayerMarkerPlaced)
        {
            if (Input.GetMouseButtonDown(0) && ScreenToGroundPlane(Input.mousePosition, out var clickedPos))
            {
                editorManager.simPlayerPosition = clickedPos;
                editorManager.simPlayerPosition.y = planeY;
                editorManager.simPlayerMarker.transform.position = editorManager.simPlayerPosition + Vector3.up * 0.1f;
                editorManager.simPlayerMarker.SetActive(true);
                editorManager.simPlayerMarkerPlaced = true;
            }

            if (editorManager.simpleMSGText != null) editorManager.simpleMSGText.text = "Click on the map to place the simulation marker.";
            if (editorManager.nearestOutputText != null) editorManager.nearestOutputText.text = "null";
            if (editorManager.directionOutputText != null) editorManager.directionOutputText.text = "null";
            if (editorManager.insideOutputText != null) editorManager.insideOutputText.text = "null";
            return;
        }

        float rotateInput = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) rotateInput -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) rotateInput += 1f;
        if (Mathf.Abs(rotateInput) > Mathf.Epsilon)
        {
            editorManager.simPlayerMarker.transform.Rotate(0f, rotateInput * editorManager.simulationRotateSpeed * Time.deltaTime, 0f, Space.World);
        }

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += Vector3.left;
        if (Input.GetKey(KeyCode.S)) move += Vector3.right;
        if (Input.GetKey(KeyCode.A)) move += Vector3.back;
        if (Input.GetKey(KeyCode.D)) move += Vector3.forward;
        move.y = 0f;
        if (move.sqrMagnitude > 1f) move.Normalize();

        editorManager.simPlayerPosition += move * GetSimulationMoveSpeed() * Time.deltaTime;
        editorManager.simPlayerPosition.y = planeY;
        editorManager.simPlayerMarker.transform.position = editorManager.simPlayerPosition + Vector3.up * 0.1f;
    }

    private float GetSimulationPlaneY()
    {
        float planeY = 0f;
        GameObject mapRoot = editorManager.uiManager != null ? editorManager.uiManager.currentMapObject : null;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var combined, true))
        {
            planeY = combined.min.y;
        }
        return planeY;
    }

    private float GetSimulationMarkerScale()
    {
        if (editorManager.createdMarkers.Count == 0) return 0.2f;

        float totalFootprint = 0f;
        int count = 0;

        for (int i = 0; i < editorManager.createdMarkers.Count; i++)
        {
            var mark = editorManager.createdMarkers[i];
            if (mark == null) continue;

            Vector3 scale = mark.transform.localScale;
            float footprint = Mathf.Max(0.01f, Mathf.Min(scale.x, scale.z));
            totalFootprint += footprint;
            count++;
        }

        if (count == 0) return 0.2f;

        float averageFootprint = totalFootprint / count;
        return Mathf.Clamp(averageFootprint * 0.08f, 0.05f, 0.2f);
    }

    private float GetSimulationMoveSpeed()
    {
        if (editorManager.createdMarkers.Count == 0) return editorManager.simulationMoveSpeed;

        float totalFootprint = 0f;
        int count = 0;

        for (int i = 0; i < editorManager.createdMarkers.Count; i++)
        {
            var mark = editorManager.createdMarkers[i];
            if (mark == null) continue;

            Vector3 scale = mark.transform.localScale;
            float footprint = Mathf.Max(0.01f, Mathf.Min(scale.x, scale.z));
            totalFootprint += footprint;
            count++;
        }

        if (count == 0) return editorManager.simulationMoveSpeed;

        float averageFootprint = totalFootprint / count;
        return Mathf.Clamp(editorManager.simulationMoveSpeed * averageFootprint * 0.4f, 0.2f, 6f);
    }

    private Vector3 GetSimulationSpawnPosition(float planeY)
    {
        GameObject mapRoot = editorManager.uiManager != null ? editorManager.uiManager.currentMapObject : null;
        if (mapRoot != null && CameraManager.TryGetCombinedBounds(mapRoot, out var combined, true))
        {
            return new Vector3(combined.center.x, planeY, combined.center.z);
        }
        return new Vector3(0f, planeY, 0f);
    }

    private void EvaluateSimulationAgainstMarks()
    {
        if (editorManager.simulationControlMode == EditorManager.SimulationControlMode.Keyboard && !editorManager.simPlayerMarkerPlaced)
        {
            editorManager.currentSituation = EditorManager.Situation.None;
            return;
        }

        GameObject nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var mark in editorManager.createdMarkers)
        {
            if (mark == null) continue;
            if (!MarkStorage.TryGet(mark.name, out var md)) continue;

            if (IsPointInMarkArea(editorManager.simPlayerPosition, mark, md))
            {
                Vector3 baseCenter = markerService.GetMarkerBaseCenter(mark, md);
                float dist = Vector2.SqrMagnitude(new Vector2(editorManager.simPlayerPosition.x - baseCenter.x, editorManager.simPlayerPosition.z - baseCenter.z));
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = mark;
                }
            }
        }

        if (nearest != null)
        {
            string label = nearest.name;
            editorManager.nearestOutputText.text = label;

            Vector3 baseCenter = markerService.GetMarkerBaseCenter(nearest, MarkStorage.Marks.ContainsKey(nearest.name) ? MarkStorage.Marks[nearest.name] : new MarkStorage.MarkData { position = nearest.transform.position });
            Vector3 toMark = baseCenter - editorManager.simPlayerPosition;
            editorManager.directionOutputText.text = GetRelatedDirection(toMark);

            bool inside = IsPointInMarkInside(editorManager.simPlayerPosition, nearest, MarkStorage.Marks[nearest.name]);
            editorManager.insideOutputText.text = inside ? "Inside" : "Outside";
            editorManager.currentSituation = inside ? EditorManager.Situation.Inside : EditorManager.Situation.Near;
        }
        else
        {
            editorManager.nearestOutputText.text = "null";
            editorManager.directionOutputText.text = "null";
            editorManager.insideOutputText.text = "null";
            editorManager.currentSituation = EditorManager.Situation.None;
        }

        if (editorManager.insideOutputText.text == "Inside")
        {
            editorManager.simpleMSGText.text = $"The user is right inside the {editorManager.nearestOutputText.text}.";
        }
        else if (editorManager.insideOutputText.text == "Outside")
        {
            editorManager.simpleMSGText.text = $"The user is now near {editorManager.nearestOutputText.text}, the {editorManager.nearestOutputText.text} is {editorManager.directionOutputText.text} of the user.";
        }
        else
        {
            editorManager.simpleMSGText.text = "No mark nearby.";
        }
    }

    private bool IsPointInMarkInside(Vector3 point, GameObject mark, MarkStorage.MarkData md)
    {
        if (mark == null || md == null) return false;

        Vector3 baseCenter = markerService.GetMarkerBaseCenter(mark, md);
        float halfX = mark.transform.localScale.x * 0.5f;
        float halfZ = mark.transform.localScale.z * 0.5f;
        return Mathf.Abs(point.x - baseCenter.x) <= halfX + 1e-6f &&
               Mathf.Abs(point.z - baseCenter.z) <= halfZ + 1e-6f;
    }

    private bool IsPointInMarkArea(Vector3 point, GameObject mark, MarkStorage.MarkData md)
    {
        if (mark == null || md == null) return false;

        Vector3 baseCenter = markerService.GetMarkerBaseCenter(mark, md);
        Vector3 localPoint = new Vector3(point.x - baseCenter.x, 0f, point.z - baseCenter.z);
        float halfX = mark.transform.localScale.x * 0.5f;
        float halfZ = mark.transform.localScale.z * 0.5f;
        float allowedX = halfX + md.margin;
        float allowedZ = halfZ + md.margin;
        bool insideRect = Mathf.Abs(localPoint.x) <= allowedX + 1e-6f && Mathf.Abs(localPoint.z) <= allowedZ + 1e-6f;
        if (!insideRect) return false;

        Vector3 forward = mark.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 toPoint = new Vector3(point.x - baseCenter.x, 0f, point.z - baseCenter.z);
        if (toPoint.sqrMagnitude < 1e-6f) return true;

        toPoint.Normalize();
        float signed = Vector3.SignedAngle(forward, toPoint, Vector3.up);
        float a1 = md.angle1;
        float a2 = md.angle2;

        if (a1 > a2)
        {
            var tmp = a1;
            a1 = a2;
            a2 = tmp;
        }

        if (a2 - a1 >= 360f - 1e-3f) return true;
        return signed >= a1 - 1e-6f && signed <= a2 + 1e-6f;
    }

    private string GetRelatedDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return "Here";
        dir.Normalize();

        float playerAngle = editorManager.simPlayerMarker != null ? editorManager.simPlayerMarker.transform.eulerAngles.y : 0f;
        float dirAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float rel = dirAngle - playerAngle;
        if (rel < 0f) rel += 360f;

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
}
