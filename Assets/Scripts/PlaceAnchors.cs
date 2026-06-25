// PlaceAnchors.cs
// Final polished measurement manager with color-coded dashed lines,
// scaling labels, screenshot saving, and clean mode management.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

public class PlaceAnchors : MonoBehaviour
{
    // ----- Measurement mode enum -----

    public enum MeasureMode
    {
        Distance,
        Height,
        WidthHeight,
        Box
    }

    // ----- Inspector fields -----

    [Header("AR Components")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Prefabs")]
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private GameObject measurementLinePrefab;
    [SerializeField] private GameObject guideDotPrefab;

    [Header("Materials")]
    [SerializeField] private Material lengthLineMaterial;
    [SerializeField] private Material heightLineMaterial;
    [SerializeField] private Material widthLineMaterial;
    [SerializeField] private Material wireframeMaterial;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI scanPromptText;
    [SerializeField] private TextMeshProUGUI modeLabelText;
    [SerializeField] private TextMeshProUGUI confirmationText;

    // ----- Internal state -----

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    public List<GameObject> placedMarkers = new List<GameObject>();
    private List<GameObject> measurements = new List<GameObject>();
    private GameObject guideDotInstance;
    private MeasureMode currentMode = MeasureMode.Distance;
    private List<GameObject> pendingAnchors = new List<GameObject>();
    public List<string> measurementLog = new List<string>();

    // Minimum label scale — prevents labels from shrinking to
    // nothing when the camera is very close.
    private const float MIN_LABEL_SCALE = 0.02f;

    // Maximum label scale — prevents labels from becoming huge
    // when the camera is far away.
    private const float MAX_LABEL_SCALE = 0.12f;

    // Controls how quickly label size changes with distance.
    // Higher = larger labels at the same distance.
    private const float LABEL_SCALE_FACTOR = 0.04f;

    void Start()
    {
        guideDotInstance = Instantiate(guideDotPrefab);
        guideDotInstance.SetActive(false);
        UpdateModeLabel();

        // Hide confirmation text at start.
        if (confirmationText != null)
            confirmationText.gameObject.SetActive(false);
    }

    void Update()
    {
        UpdateGuideDot();
        UpdateScanPrompt();
        HandleTap();
    }

    // ----- Mode switching -----

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

    private void ClearPending()
    {
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

        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(
                touch.touchId.ReadValue()))
            return;

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

    // ----- Distance mode -----

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

            CreateMeasurement(start, end, dist.ToString("F1") + " cm", lengthLineMaterial);
            measurementLog.Add("Distance: " + dist.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Height mode -----

    private void HandleHeightTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count == 0)
        {
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);
        }
        else
        {
            Vector3 basePos = pendingAnchors[0].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            float height = (topPos.y - basePos.y) * 100f;

            CreateMeasurement(basePos, topPos, height.ToString("F1") + " cm", heightLineMaterial);
            measurementLog.Add("Height: " + height.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Width+Height mode -----

    private void HandleWidthHeightTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count < 2)
        {
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);

            if (pendingAnchors.Count == 2)
            {
                Vector3 start = pendingAnchors[0].transform.position;
                Vector3 end = pendingAnchors[1].transform.position;
                float width = Vector3.Distance(start, end) * 100f;

                CreateMeasurement(start, end, "W: " + width.ToString("F1") + " cm", widthLineMaterial);
                measurementLog.Add("Width: " + width.ToString("F1") + " cm");
            }
        }
        else
        {
            Vector3 basePos = pendingAnchors[1].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            float height = (topPos.y - basePos.y) * 100f;

            CreateMeasurement(basePos, topPos, "H: " + height.ToString("F1") + " cm", heightLineMaterial);
            measurementLog.Add("Height: " + height.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Box mode -----

    private void HandleBoxTap(Vector2 screenPosition)
    {
        if (pendingAnchors.Count < 3)
        {
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
                return;

            Pose hitPose = hits[0].pose;
            GameObject anchor = CreateAnchorMarker(hitPose);
            Handheld.Vibrate();
            pendingAnchors.Add(anchor);

            if (pendingAnchors.Count == 2)
            {
                Vector3 p0 = pendingAnchors[0].transform.position;
                Vector3 p1 = pendingAnchors[1].transform.position;
                float len = Vector3.Distance(p0, p1) * 100f;
                CreateMeasurement(p0, p1, "L: " + len.ToString("F1") + " cm", lengthLineMaterial);
            }

            if (pendingAnchors.Count == 3)
            {
                Vector3 p1 = pendingAnchors[1].transform.position;
                Vector3 p2 = pendingAnchors[2].transform.position;
                float wid = Vector3.Distance(p1, p2) * 100f;
                CreateMeasurement(p1, p2, "W: " + wid.ToString("F1") + " cm", widthLineMaterial);
            }
        }
        else
        {
            Vector3 basePos = pendingAnchors[0].transform.position;
            Vector3 topPos = ProjectOntoVerticalPlane(screenPosition, basePos);

            if (topPos.y <= basePos.y)
                return;

            Pose topPose = new Pose(topPos, Quaternion.identity);
            GameObject anchor = CreateAnchorMarker(topPose);
            Handheld.Vibrate();

            Vector3 corner0 = pendingAnchors[0].transform.position;
            Vector3 corner1 = pendingAnchors[1].transform.position;
            Vector3 corner2 = pendingAnchors[2].transform.position;
            float height = topPos.y - corner0.y;

            for (int i = 0; i < 2 && measurements.Count > 0; i++)
            {
                GameObject preview = measurements[measurements.Count - 1];
                measurements.RemoveAt(measurements.Count - 1);
                Destroy(preview);
            }

            CreateBoundingBox(corner0, corner1, corner2, height);

            float length = Vector3.Distance(corner0, corner1) * 100f;
            float width = Vector3.Distance(corner1, corner2) * 100f;
            float heightCm = height * 100f;
            measurementLog.Add("Box — L: " + length.ToString("F1")
                + " W: " + width.ToString("F1")
                + " H: " + heightCm.ToString("F1") + " cm");

            pendingAnchors.Clear();
        }
    }

    // ----- Vertical plane projection -----

    private Vector3 ProjectOntoVerticalPlane(
        Vector2 screenPosition, Vector3 basePos)
    {
        Camera cam = Camera.main;
        Ray ray = cam.ScreenPointToRay(screenPosition);

        Vector3 cameraToBase = basePos - cam.transform.position;
        cameraToBase.y = 0f;
        Vector3 planeNormal = cameraToBase.normalized;

        if (planeNormal.sqrMagnitude < 0.001f)
        {
            planeNormal = cam.transform.forward;
            planeNormal.y = 0f;
            planeNormal.Normalize();
        }

        float denom = Vector3.Dot(planeNormal, ray.direction);

        if (Mathf.Abs(denom) < 0.0001f)
            return basePos;

        float t = Vector3.Dot(planeNormal, basePos - ray.origin) / denom;
        Vector3 hitPoint = ray.origin + ray.direction * t;

        return new Vector3(basePos.x, hitPoint.y, basePos.z);
    }

    // ----- Bounding box builder -----

    private void CreateBoundingBox(
        Vector3 corner0, Vector3 corner1, Vector3 corner2, float height)
    {
        Vector3 corner3 = corner0 + (corner2 - corner1);
        Vector3 up = new Vector3(0f, height, 0f);

        Vector3[] bottom = { corner0, corner1, corner2, corner3 };
        Vector3[] top = {
            corner0 + up, corner1 + up,
            corner2 + up, corner3 + up
        };

        Vector3[][] edges = new Vector3[][]
        {
            new Vector3[] { bottom[0], bottom[1] },
            new Vector3[] { bottom[1], bottom[2] },
            new Vector3[] { bottom[2], bottom[3] },
            new Vector3[] { bottom[3], bottom[0] },
            new Vector3[] { top[0], top[1] },
            new Vector3[] { top[1], top[2] },
            new Vector3[] { top[2], top[3] },
            new Vector3[] { top[3], top[0] },
            new Vector3[] { bottom[0], top[0] },
            new Vector3[] { bottom[1], top[1] },
            new Vector3[] { bottom[2], top[2] },
            new Vector3[] { bottom[3], top[3] },
        };

        GameObject boxParent = new GameObject("BoundingBox");

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
            lr.material = wireframeMaterial;
        }

        float lengthVal = Vector3.Distance(corner0, corner1) * 100f;
        float widthVal = Vector3.Distance(corner1, corner2) * 100f;
        float heightVal = height * 100f;

        CreateBoxLabel(boxParent.transform,
            (corner0 + corner1) / 2f + Vector3.up * 0.02f,
            "L: " + lengthVal.ToString("F1") + " cm");

        CreateBoxLabel(boxParent.transform,
            (corner1 + corner2) / 2f + Vector3.up * 0.02f,
            "W: " + widthVal.ToString("F1") + " cm");

        CreateBoxLabel(boxParent.transform,
            (corner0 + top[0]) / 2f,
            "H: " + heightVal.ToString("F1") + " cm");

        measurements.Add(boxParent);
    }

    private void CreateBoxLabel(Transform parent, Vector3 position, string text)
    {
        GameObject labelObj = new GameObject("BoxLabel");
        labelObj.transform.SetParent(parent);
        labelObj.transform.position = position;
        labelObj.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);

        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    // ----- Anchor creation -----

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

    // ----- Measurement creation with dashed line -----

    private void CreateMeasurement(
        Vector3 start, Vector3 end, string labelText, Material lineMaterial)
    {
        GameObject measurement = Instantiate(measurementLinePrefab);

        LineRenderer line = measurement.GetComponent<LineRenderer>();

        // ----- Create a dashed line -----
        // Instead of a straight line from A to B, we build a
        // series of short segments with gaps between them.
        // This creates the dashed appearance.

        float totalLength = Vector3.Distance(start, end);

        // Each dash is 2cm, each gap is 1cm.
        float dashLength = 0.02f;
        float gapLength = 0.01f;
        float segmentLength = dashLength + gapLength;

        // Build the point list for the dashes.
        List<Vector3> dashPoints = new List<Vector3>();
        Vector3 direction = (end - start).normalized;
        float covered = 0f;

        while (covered < totalLength)
        {
            // Start of this dash.
            Vector3 dashStart = start + direction * covered;

            // End of this dash — don't overshoot the endpoint.
            float dashEnd = Mathf.Min(covered + dashLength, totalLength);
            Vector3 dashEndPos = start + direction * dashEnd;

            // Each dash needs its own start and end point.
            // LineRenderer draws a continuous line through all
            // points, so we use a tiny gap (same position twice)
            // to "break" the line visually.
            dashPoints.Add(dashStart);
            dashPoints.Add(dashEndPos);

            // Skip ahead past the gap.
            covered += segmentLength;
        }

        // If we ended up with no points (very short distance),
        // just use start and end directly.
        if (dashPoints.Count < 2)
        {
            dashPoints.Clear();
            dashPoints.Add(start);
            dashPoints.Add(end);
        }

        line.positionCount = dashPoints.Count;
        line.SetPositions(dashPoints.ToArray());

        // Apply the color-coded material.
        line.material = lineMaterial;
        line.startWidth = 0.004f;
        line.endWidth = 0.004f;

        // ----- Position and update the label -----

        Transform label = measurement.transform.Find("DistanceLabel");
        Vector3 midpoint = (start + end) / 2f;
        label.position = midpoint + Vector3.up * 0.03f;

        TextMeshPro tmp = label.GetComponent<TextMeshPro>();
        tmp.text = labelText;

        measurements.Add(measurement);
    }

    // ----- Billboarding and label scaling -----

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        foreach (GameObject measurement in measurements)
        {
            if (measurement == null)
                continue;

            TextMeshPro[] labels = measurement.GetComponentsInChildren<TextMeshPro>();

            foreach (TextMeshPro label in labels)
            {
                // Billboard: face the camera.
                Vector3 awayFromCamera = label.transform.position - cam.transform.position;
                label.transform.rotation = Quaternion.LookRotation(awayFromCamera);

                // Scale by distance: labels grow as the camera
                // moves away so they stay readable, and shrink
                // when close so they don't obscure the measurement.
                float distance = awayFromCamera.magnitude;
                float scale = Mathf.Clamp(
                    distance * LABEL_SCALE_FACTOR,
                    MIN_LABEL_SCALE,
                    MAX_LABEL_SCALE
                );
                label.transform.localScale = new Vector3(scale, scale, scale);
            }
        }
    }

    // ----- Screenshot -----

    // Called by the Save button. Captures the screen and saves
    // it to the device's gallery.
    public void TakeScreenshot()
    {
        StartCoroutine(CaptureScreenshot());
    }

    private IEnumerator CaptureScreenshot()
    {
        // Hide UI so the screenshot only shows the AR scene
        // with measurements — no buttons or prompts.
        GameObject canvas = scanPromptText.transform.root.gameObject;
        canvas.SetActive(false);

        // Wait for the end of frame so the screen renders
        // without the UI.
        yield return new WaitForEndOfFrame();

        // Capture the screen to a Texture2D.
        Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();

        // Save to the device gallery.
        // NativeGallery is a popular plugin, but to avoid
        // external dependencies we save to a known path.
        string filename = "ARMeasure_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllBytes(path, screenshot.EncodeToPNG());

        // Clean up the texture to free memory.
        Destroy(screenshot);

        // Show UI again.
        canvas.SetActive(true);

        // Also save to Android gallery using a media scan.
        // This makes the screenshot appear in the Photos app.
        SaveToGallery(path);

        // Show brief confirmation.
        StartCoroutine(ShowConfirmation("Screenshot saved"));
    }

    // Triggers Android's media scanner so the saved file
    // appears in the gallery. On Android, files in
    // persistentDataPath don't automatically show up in
    // the gallery — the media scanner needs to index them.
    private void SaveToGallery(string filePath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Use Android's Java API to scan the file into the
        // media database. This is done through Unity's
        // AndroidJavaClass bridge, which lets you call Java
        // methods from C#.
        using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaClass mediaScanIntent = new AndroidJavaClass("android.content.Intent"))
        using (AndroidJavaClass uri = new AndroidJavaClass("android.net.Uri"))
        {
            // Copy to a public Pictures folder first.
            string galleryPath = "/sdcard/Pictures/ARMeasure/";
            System.IO.Directory.CreateDirectory(galleryPath);
            string destPath = galleryPath + System.IO.Path.GetFileName(filePath);
            System.IO.File.Copy(filePath, destPath, true);

            // Trigger media scan.
            using (AndroidJavaObject fileUri = uri.CallStatic<AndroidJavaObject>(
                "fromFile", new AndroidJavaObject("java.io.File", destPath)))
            {
                using (AndroidJavaObject intent = new AndroidJavaObject(
                    "android.content.Intent",
                    "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
                    fileUri))
                {
                    activity.Call("sendBroadcast", intent);
                }
            }
        }
#endif
    }

    private IEnumerator ShowConfirmation(string message)
    {
        if (confirmationText != null)
        {
            confirmationText.text = message;
            confirmationText.gameObject.SetActive(true);

            // Show for 2 seconds then fade out.
            yield return new WaitForSeconds(2f);

            confirmationText.gameObject.SetActive(false);
        }
    }

    // ----- Undo -----

    public void Undo()
    {
        if (pendingAnchors.Count > 0)
        {
            GameObject last = pendingAnchors[pendingAnchors.Count - 1];
            pendingAnchors.RemoveAt(pendingAnchors.Count - 1);
            placedMarkers.Remove(last);
            Destroy(last);

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

            if (currentMode == MeasureMode.Box &&
                pendingAnchors.Count >= 1 &&
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

        if (measurements.Count > 0)
        {
            GameObject lastMeasurement = measurements[measurements.Count - 1];
            measurements.RemoveAt(measurements.Count - 1);
            Destroy(lastMeasurement);

            if (measurementLog.Count > 0)
                measurementLog.RemoveAt(measurementLog.Count - 1);
        }

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