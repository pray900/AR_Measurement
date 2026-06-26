// LiDARSetup.cs
// Enables LiDAR-enhanced features on devices that support them.
// On non-LiDAR devices, this script does nothing — it checks
// for support at runtime and gracefully skips.

using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class LiDARSetup : MonoBehaviour
{
    // The mesh manager handles the 3D scene mesh that LiDAR
    // generates. It's similar to plane detection but produces
    // dense triangle meshes instead of flat plane boundaries.
    [SerializeField] private ARMeshManager meshManager;

    // Reference to the raycast manager so we can enable
    // mesh-based raycasting when available.
    [SerializeField] private ARRaycastManager raycastManager;

    void Start()
    {
        // Disable mesh manager by default. It will be enabled
        // only if the device supports it.
        if (meshManager != null)
        {
            meshManager.enabled = false;
        }

        // Check for mesh support after a short delay to let
        // the AR session initialize.
        Invoke("CheckMeshSupport", 1.0f);
    }

    void CheckMeshSupport()
    {
        // ARMeshManager.descriptor tells us whether the current
        // device supports scene meshing. On LiDAR devices running
        // ARKit, this returns a valid descriptor. On non-LiDAR
        // devices and on Android, it returns null or unsupported.
        if (meshManager != null &&
            meshManager.subsystem != null &&
            meshManager.subsystem.running)
        {
            meshManager.enabled = true;
            Debug.Log("LiDAR mesh enabled");
        }
        else
        {
            Debug.Log("LiDAR mesh not available on this device");
        }
    }
}