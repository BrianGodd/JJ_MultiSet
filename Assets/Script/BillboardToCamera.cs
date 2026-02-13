using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    public Camera cam;

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;

        if (forward.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(forward);
    }
}
