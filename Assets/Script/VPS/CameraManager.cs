using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

public class CameraManager : MonoBehaviour
{
    public CinemachineTargetGroup targetGroup;
    public CinemachineCamera virtualCamera; // changed from CinemachineVirtualCamera to CinemachineCamera

    // Update the given CinemachineTargetGroup so it covers all Renderers under `root` (includes children).
    // Each renderer's transform is added as a member; weight = bounds magnitude, radius = max extent.
    // After updating the group, reposition the camera to a top-down view that frames the group.
    public void UpdateTargetGroupFromRoot(GameObject root, bool includeInactive = true)
    {
        if (!TryGetCombinedBounds(root, out var combined, includeInactive))
            return;
        
        Vector3 center = combined.center;
        root.transform.GetChild(0).transform.position = -1*center;

        if (targetGroup == null || root == null)
        {
            Debug.LogWarning("UpdateTargetGroupFromRoot: targetGroup or root is null.");
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
        if (renderers == null || renderers.Length == 0)
        {
            // clear targets if none found
            targetGroup.m_Targets = new CinemachineTargetGroup.Target[0];
            return;
        }

        var list = new List<CinemachineTargetGroup.Target>(renderers.Length);

        foreach (var r in renderers)
        {
            if (r == null || r.transform == null) continue;

            var b = r.bounds;
            // weight proportional to renderer size (so large meshes influence camera more)
            float weight = b.size.magnitude;
            // radius used by Cinemachine to encompass target - use the largest extent
            float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);

            var t = new CinemachineTargetGroup.Target
            {
                Object = r.transform.root,
                Weight = Mathf.Max(0.01f, weight),
                Radius = Mathf.Max(0.01f, radius)
            };

            list.Add(t);
        }

        targetGroup.m_Targets = list.ToArray();

        // After updating the group, position the camera for a top-down view
        PositionCameraTopDown(root, includeInactive);
    }

    // Compute combined bounds from renderers under root
    public static bool TryGetCombinedBounds(GameObject root, out Bounds combined, bool includeInactive = true)
    {
        combined = new Bounds();
        if (root == null) return false;

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
        if (renderers == null || renderers.Length == 0) return false;

        combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }
        return true;
    }

    // Position a Cinemachine virtual camera (or main Camera if none) to look top-down at the root's combined bounds.
    public void PositionCameraTopDown(GameObject root, bool includeInactive = true)
    {
        if (!TryGetCombinedBounds(root, out var combined, includeInactive))
            return;

        // Ensure we have a virtual camera reference
        if (virtualCamera == null)
        {
            virtualCamera = FindObjectOfType<CinemachineCamera>();
        }

        Vector3 center = combined.center;

        float maxExtent = Mathf.Max(combined.extents.x, combined.extents.y, combined.extents.z);

        // Compute a reasonable height to ensure the object fits in view.
        // Tunable factors: multiplier and minimum height.
        float heightMultiplier = 1.0f;
        float minHeight = 2f;
        float distance = Mathf.Max(minHeight, maxExtent * heightMultiplier);

        // Target position directly above center
        Vector3 topPos = center + Vector3.up * distance;

        if (virtualCamera != null)
        {
            // Prefer using Cinemachine camera: set LookAt to the target group (so it orients down)
            virtualCamera.Follow = null;
            virtualCamera.LookAt = targetGroup.transform;

            // Move the camera transform to the top position and set rotation to look straight down
            virtualCamera.transform.position = topPos;
            //virtualCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
