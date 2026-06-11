// PlaceAnchors.cs
// Manages multi-mode measurement: distance, height, and width+height.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

public class PlaceAnchors : MonoBehaviour
{
    // ----- Measurement mode enum -----

    // Defines the three measurement types. An enum is a named
    // set of constants — cleaner than using strings or ints
    // to track which mode is active.
    public enum MeasureMode
    {
        Distance,     // Two points on surfaces, straight-line distance
        Height,       // Base point on surface, top point projected vertically
        WidthHeight,   // Three points: two on surface + one vertical
        Box
    }

    // ----- Inspector fields -----

    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private GameObject measurementLinePrefab;
    [SerializeField] private GameObject guideDotPrefab;
    [SerializeField] private TextMeshProUGUI scanPromptText;
    [SerializeField] private TextMeshProUGUI modeLabelText;

    // ----- Internal state -----

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    public List<GameObject> placedMarkers = new List<GameObject>();
    private List<GameObject> measurements = new List<GameObject>();
    private GameObject guideDotInstance;

    // Current measurement mode.
    private MeasureMode currentMode = MeasureMode.Distance;

    // Pending anchors for the current in-progress measurement.
    // Distance and Height use one pending anchor (the first tap).
    // WidthHeight uses two (first and second tap, waiting for third).
    private List<GameObject> pendingAnchors = new List<GameObject>();

    // Stores completed measurement data for the review list.
    // Each entry is a human-readable string like "Distance: 42.3 cm"
    public List<string> measurementLog = new List<string>();

    void Start()
    {
        guideDotInstance = Instantiate(guideDotPrefab);
        guideDotInstance.SetActive(false);
        UpdateModeLabel();
    }

    void Update()
    {
        UpdateGuideDot();
        UpdateScanPrompt();
        HandleTap();
    }

    // ----- Mode switching (called by buttons) -----

    public void SetModeDistance()
    {
        currentMode = MeasureMode.Distance;
        ClearPending();
        UpdateModeLabel();
    }

    public void SetModeHeight()
    {
        currentMode = MeasureMode.Height;
        ClearPending();
        UpdateModeLabel();
    }

    public void SetModeWidthHeight()
    {
        currentMode = MeasureMode.WidthHeight;
        ClearPending();
        UpdateModeLabel();
    }

    public void SetModeBox()
    {
        currentMode = MeasureMode.Box;
        ClearPending();
        UpdateModeLabel();
    }

    private void UpdateModeLabel()
    {
        if (modeLabelText != null)
        {
            string[] names = { "Distance", "Height", "W + H", "Box" };
            modeLabelText.text = "Mode: " + names[(int)currentMode];
        }
    }

    // Clears any pending anchors when switching modes, so a
    // half-finished measurement from one mode doesn't carry
    // into a different mode.
    private void ClearPending()
    {
        // Remove pending anchor GameObjects from the scene.
        foreach (GameObject anchor in pendingAnchors)
        {
            placedMarkers.Remove(anchor);
            Destroy(anchor);
        }
        pendingAnchors.Clear();
    }

    // ----- Guide dot -----

    private void UpdateGuideDot()
    {
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            guideDotInstance.SetActive(true);
            guideDotInstance.transform.position = hitPose.position;
            guideDotInstance.transform.rotation = hitPose.rotation;
        }
        else
        {
            guideDotInstance.SetActive(false);
        }
    }

    // ----- Scan prompt -----

    private void UpdateScanPrompt()
    {
        if (scanPromptText != null)
        {
            scanPromptText.gameObject.SetActive(planeManager.trackables.count == 0);
        }
    }

    // ----- Tap handling -----

    private void HandleTap()
    {
        var touchscreen = Touchscreen.current;
        if (touchscreen == null)
            return;

        var touch = touchscreen.primaryTouch;
        if (!touch.press.wasPressedThisFrame)
            return;

        Vector2 screenPosition = touch.position.ReadValue();

        // Ignore taps on UI.
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(
                touch.touchId.ReadValue()))
            return;

        // Route to the appropriate handler based on current mode.
        switch (currentMode)
        {
            case MeasureMode.Distance:
                HandleDistanceTap(screenPosition);
                break;
            case MeasureMode.Height:
                HandleHeightTap(screenPosition);
                break;
            case MeasureMode.WidthHeight:
                HandleWidthHeightTap(screenPosition);
                break;
            case MeasureMode.Box:
                HandleBoxTap(screenPosition);
                break;
        }
    }

    // ----- Distance mode (same as before) -----

    private void HandleDistanceTap(Vector2 screenPosition)
    {
        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
            return;

        Pose hitPose = hits[0].pose;
        GameObject anchor = CreateAnchorMarker(hitPose);
        Handheld.Vibrate();

        if (pendingAnchors.Count == 0)
        {
            pendingAnchors.Add(anchor);
        }
        else
        {
            Vector3 start = pendingAnchors[0].transform.position;
            Vector3 end = anchor.transform.position;
            float dist = Vector3.Distance(start, end) * 100f;

            CreateMeasurement(start, end, dist.ToString("F1") + " cm");
            measurementLog.Add("Distance: " + dist.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Height mode -----

    private void HandleHeightTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count == 0)
        {
            // FIRST TAP: must hit a horizontal plane. This is
            // the base of the object (where it meets the surface).
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);
        }
        else
        {
            // SECOND TAP: project onto vertical plane above base.
            Vector3 basePos = pendingAnchors[0].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            // Only accept if the top is above the base.
            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            float height = (topPos.y - basePos.y) * 100f;

            CreateMeasurement(basePos, topPos, height.ToString("F1") + " cm");
            measurementLog.Add("Height: " + height.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Width+Height mode -----

    private void HandleWidthHeightTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count < 2)
        {
            // FIRST and SECOND taps: both on surfaces (the width).
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);

            // After the second tap, create the width line immediately.
            if (pendingAnchors.Count == 2)
            {
                Vector3 start = pendingAnchors[0].transform.position;
                Vector3 end = pendingAnchors[1].transform.position;
                float width = Vector3.Distance(start, end) * 100f;

                CreateMeasurement(start, end, "W: " + width.ToString("F1") + " cm");
                measurementLog.Add("Width: " + width.ToString("F1") + " cm");
            }
        }
        else
        {
            // THIRD TAP: height from the second anchor upward.
            Vector3 basePos = pendingAnchors[1].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            float height = (topPos.y - basePos.y) * 100f;

            CreateMeasurement(basePos, topPos, "H: " + height.ToString("F1") + " cm");
            measurementLog.Add("Height: " + height.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Math utility -----
    // Projects a screen tap onto a vertical plane that passes
    // through basePos and faces the camera. Returns a point
    // directly above (or below) basePos at the tapped height.
    //
    // Why a vertical plane: imagine a glass wall standing at
    // the base of the object, facing you. When you tap the
    // screen, your ray goes through the glass and hits it at
    // some height. That height is the measurement. The glass
    // wall is the vertical plane.
    private Vector3 ProjectOntoVerticalPlane(
        Vector2 screenPosition, Vector3 basePos)
    {
        Camera cam = Camera.main;

        // Create a ray from the camera through the tapped pixel.
        Ray ray = cam.ScreenPointToRay(screenPosition);

        // The plane's normal faces the camera horizontally.
        // We zero out the y component so the plane is truly
        // vertical (not tilted if the camera looks up/down).
        Vector3 cameraToBase = basePos - cam.transform.position;
        cameraToBase.y = 0f;
        Vector3 planeNormal = cameraToBase.normalized;

        // If the camera is directly above the base point,
        // the horizontal direction is zero. Fall back to the
        // camera's forward direction (also flattened).
        if (planeNormal.sqrMagnitude < 0.001f)
        {
            planeNormal = cam.transform.forward;
            planeNormal.y = 0f;
            planeNormal.Normalize();
        }

        // Standard ray-plane intersection.
        // Plane equation: dot(normal, point - basePos) = 0
        // Ray equation: point = rayOrigin + t * rayDir
        // Solving for t:
        float denom = Vector3.Dot(planeNormal, ray.direction);

        // If denom is near zero, the ray is parallel to the
        // plane — no intersection. Return basePos as fallback.
        if (Mathf.Abs(denom) < 0.0001f)
            return basePos;

        float t = Vector3.Dot(planeNormal, basePos - ray.origin) / denom;

        // The hit point on the vertical plane.
        Vector3 hitPoint = ray.origin + ray.direction * t;

        // Constrain to directly above/below the base point.
        // We only care about the height (y), not horizontal drift.
        // This ensures the measurement line is perfectly vertical.
        return new Vector3(basePos.x, hitPoint.y, basePos.z);
    }

    // ----- Anchor creation -----

    // ----- Box mode -----

    private void HandleBoxTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count < 3)
        {
            // TAPS 1, 2, 3: base corners on the horizontal plane.
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);

            // After tap 2, draw a preview line for the first edge
            // so the user can see what they've defined so far.
            if (pendingAnchors.Count == 2)
            {
                Vector3 p0 = pendingAnchors[0].transform.position;
                Vector3 p1 = pendingAnchors[1].transform.position;
                float len = Vector3.Distance(p0, p1) * 100f;
                CreateMeasurement(p0, p1, "L: " + len.ToString("F1") + " cm");
            }

            // After tap 3, draw a preview line for the second edge.
            if (pendingAnchors.Count == 3)
            {
                Vector3 p1 = pendingAnchors[1].transform.position;
                Vector3 p2 = pendingAnchors[2].transform.position;
                float wid = Vector3.Distance(p1, p2) * 100f;
                CreateMeasurement(p1, p2, "W: " + wid.ToString("F1") + " cm");
            }
        }
        else
        {
            // TAP 4: height point above the first corner.
            Vector3 basePos = pendingAnchors[0].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            // Gather the three base corners.
            Vector3 corner0 = pendingAnchors[0].transform.position;
            Vector3 corner1 = pendingAnchors[1].transform.position;
            Vector3 corner2 = pendingAnchors[2].transform.position;
            float height = topPos.y - corner0.y;

            // Remove the two preview lines (length and width)
            // that were drawn after taps 2 and 3. The wireframe
            // will replace them with a complete box.
            for (int i = 0; i < 2 && measurements.Count > 0; i++)
            {
                GameObject preview = measurements[measurements.Count - 1];
                measurements.RemoveAt(measurements.Count - 1);
                Destroy(preview);
            }

            // Build the full wireframe box.
            CreateBoundingBox(corner0, corner1, corner2, height);

            // Log all three dimensions.
            float length = Vector3.Distance(corner0, corner1) * 100f;
            float width = Vector3.Distance(corner1, corner2) * 100f;
            float heightCm = height * 100f;
            measurementLog.Add("Box — L: " + length.ToString("F1")
                + " W: " + width.ToString("F1")
                + " H: " + heightCm.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // Builds a wireframe box from three base corners and a height.
    //
    // The three corners define two edges of the base:
    //   corner0 → corner1 (length edge)
    //   corner1 → corner2 (width edge)
    //
    // The fourth base corner is calculated:
    //   corner3 = corner0 + (corner2 - corner1)
    //
    // Why this formula: corner2 - corner1 gives the width
    // direction vector. Adding it to corner0 gives the point
    // that completes the rectangle.
    //
    // The top four corners are the base corners shifted up
    // by the height value.
    private void CreateBoundingBox(
        Vector3 corner0, Vector3 corner1, Vector3 corner2, float height)
    {
        // Calculate the fourth base corner.
        Vector3 corner3 = corner0 + (corner2 - corner1);

        // Height offset vector (straight up).
        Vector3 up = new Vector3(0f, height, 0f);

        // All 8 corners of the box.
        Vector3[] bottom = { corner0, corner1, corner2, corner3 };
        Vector3[] top = {
            corner0 + up,
            corner1 + up,
            corner2 + up,
            corner3 + up
        };

        // A box has 12 edges: 4 bottom, 4 top, 4 vertical.
        // Each edge is a pair of points.
        Vector3[][] edges = new Vector3[][]
        {
            // Bottom rectangle
            new Vector3[] { bottom[0], bottom[1] },
            new Vector3[] { bottom[1], bottom[2] },
            new Vector3[] { bottom[2], bottom[3] },
            new Vector3[] { bottom[3], bottom[0] },

            // Top rectangle
            new Vector3[] { top[0], top[1] },
            new Vector3[] { top[1], top[2] },
            new Vector3[] { top[2], top[3] },
            new Vector3[] { top[3], top[0] },

            // Vertical edges connecting bottom to top
            new Vector3[] { bottom[0], top[0] },
            new Vector3[] { bottom[1], top[1] },
            new Vector3[] { bottom[2], top[2] },
            new Vector3[] { bottom[3], top[3] },
        };

        // Create a parent object to hold all wireframe lines
        // so they can be destroyed together on undo/reset.
        GameObject boxParent = new GameObject("BoundingBox");

        // Draw each edge as a LineRenderer.
        foreach (Vector3[] edge in edges)
        {
            GameObject lineObj = new GameObject("BoxEdge");
            lineObj.transform.SetParent(boxParent.transform);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, edge[0]);
            lr.SetPosition(1, edge[1]);
            lr.startWidth = 0.003f;
            lr.endWidth = 0.003f;

            // Use the same line material from your MeasurementLine prefab.
            // We create a simple unlit material inline here so the
            // wireframe doesn't depend on any prefab.
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = Color.yellow;
        }

        // ----- Add dimension labels -----

        float lengthVal = Vector3.Distance(corner0, corner1) * 100f;
        float widthVal = Vector3.Distance(corner1, corner2) * 100f;
        float heightVal = height * 100f;

        // Length label: midpoint of the corner0 → corner1 edge.
        CreateBoxLabel(
            boxParent.transform,
            (corner0 + corner1) / 2f + Vector3.up * 0.02f,
            "L: " + lengthVal.ToString("F1") + " cm"
        );

        // Width label: midpoint of the corner1 → corner2 edge.
        CreateBoxLabel(
            boxParent.transform,
            (corner1 + corner2) / 2f + Vector3.up * 0.02f,
            "W: " + widthVal.ToString("F1") + " cm"
        );

        // Height label: midpoint of the corner0 vertical edge.
        CreateBoxLabel(
            boxParent.transform,
            (corner0 + top[0]) / 2f,
            "H: " + heightVal.ToString("F1") + " cm"
        );

        measurements.Add(boxParent);
    }

    // Creates a single floating text label for the bounding box.
    private void CreateBoxLabel(Transform parent, Vector3 position, string text)
    {
        GameObject labelObj = new GameObject("BoxLabel");
        labelObj.transform.SetParent(parent);
        labelObj.transform.position = position;

        // Scale down — TextMeshPro uses large default units.
        labelObj.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);

        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    private GameObject CreateAnchorMarker(Pose pose)
    {
        GameObject anchorObject = new GameObject("Anchor");
        anchorObject.transform.position = pose.position;
        anchorObject.transform.rotation = pose.rotation;
        anchorObject.AddComponent<ARAnchor>();

        GameObject marker = Instantiate(markerPrefab, pose.position, pose.rotation);
        marker.transform.SetParent(anchorObject.transform);

        placedMarkers.Add(anchorObject);
        return anchorObject;
    }

    // ----- Measurement creation -----

    private void CreateMeasurement(Vector3 start, Vector3 end, string labelText)
    {
        GameObject measurement = Instantiate(measurementLinePrefab);

        LineRenderer line = measurement.GetComponent<LineRenderer>();
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        Transform label = measurement.transform.Find("DistanceLabel");
        Vector3 midpoint = (start + end) / 2f;
        label.position = midpoint + Vector3.up * 0.03f;

        TextMeshPro tmp = label.GetComponent<TextMeshPro>();
        tmp.text = labelText;

        measurements.Add(measurement);
    }

    // ----- Billboarding -----

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        foreach (GameObject measurement in measurements)
        {
            if (measurement == null)
                continue;

            // Find all TextMeshPro components in the measurement,
            // including those nested inside bounding box objects.
            // GetComponentsInChildren searches the entire hierarchy.
            TextMeshPro[] labels = measurement.GetComponentsInChildren<TextMeshPro>();

            foreach (TextMeshPro label in labels)
            {
                Vector3 awayFromCamera = label.transform.position - cam.transform.position;
                label.transform.rotation = Quaternion.LookRotation(awayFromCamera);
            }
        }
    }

    // ----- Undo -----

    public void Undo()
    {
        // If there are pending anchors, remove the last pending one.
        if (pendingAnchors.Count > 0)
        {
            GameObject last = pendingAnchors[pendingAnchors.Count - 1];
            pendingAnchors.RemoveAt(pendingAnchors.Count - 1);
            placedMarkers.Remove(last);
            Destroy(last);

            // If we undid the second tap in WidthHeight mode,
            // also remove the width measurement line that was
            // already created.
            if (currentMode == MeasureMode.WidthHeight &&
                pendingAnchors.Count == 1 &&
                measurements.Count > 0)
            {
                GameObject lastMeasurement = measurements[measurements.Count - 1];
                measurements.RemoveAt(measurements.Count - 1);
                Destroy(lastMeasurement);

                if (measurementLog.Count > 0)
                    measurementLog.RemoveAt(measurementLog.Count - 1);
            }
            return;
        }

        // No pending anchors. Remove the last completed measurement.
        if (measurements.Count > 0)
        {
            GameObject lastMeasurement = measurements[measurements.Count - 1];
            measurements.RemoveAt(measurements.Count - 1);
            Destroy(lastMeasurement);

            if (measurementLog.Count > 0)
                measurementLog.RemoveAt(measurementLog.Count - 1);
        }

        // Remove the last two anchors (the pair that formed
        // the measurement we just deleted).
        for (int i = 0; i < 2 && placedMarkers.Count > 0; i++)
        {
            GameObject last = placedMarkers[placedMarkers.Count - 1];
            placedMarkers.RemoveAt(placedMarkers.Count - 1);
            Destroy(last);
        }
    }

    // ----- Reset -----

    public void ResetAll()
    {
        foreach (GameObject marker in placedMarkers)
        {
            if (marker != null) Destroy(marker);
        }
        placedMarkers.Clear();

        foreach (GameObject measurement in measurements)
        {
            if (measurement != null) Destroy(measurement);
        }
        measurements.Clear();

        foreach (GameObject anchor in pendingAnchors)
        {
            if (anchor != null) Destroy(anchor);
        }
        pendingAnchors.Clear();

        measurementLog.Clear();
    }
}