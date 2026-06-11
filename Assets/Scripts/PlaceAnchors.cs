// PlaceAnchors.cs
// Full measurement manager with guide dot, haptic feedback,
// undo/reset, and scan prompt.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

public class PlaceAnchors : MonoBehaviour
{
    // ----- Inspector fields -----

    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private GameObject measurementLinePrefab;
    [SerializeField] private GameObject guideDotPrefab;
    [SerializeField] private TextMeshProUGUI scanPromptText;

    // ----- Internal state -----

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    public List<GameObject> placedMarkers = new List<GameObject>();
    private List<GameObject> measurements = new List<GameObject>();
    private GameObject firstAnchor = null;

    // The live guide dot instance — only one exists at a time.
    private GameObject guideDotInstance;

    // Tracks whether the guide dot is currently on a surface.
    // Used to prevent placing anchors when not aimed at a plane.
    private bool guideDotVisible = false;

    void Start()
    {
        // Instantiate the guide dot once at startup and hide it.
        // We reuse this single instance rather than creating and
        // destroying one every frame — much more efficient.
        guideDotInstance = Instantiate(guideDotPrefab);
        guideDotInstance.SetActive(false);
    }

    void Update()
    {
        UpdateGuideDot();
        UpdateScanPrompt();
        HandleTap();
    }

    // ----- Guide dot -----

    private void UpdateGuideDot()
    {
        // Raycast from the CENTER of the screen every frame.
        // This is different from the tap raycast — it's a
        // continuous preview showing where a tap would land.
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            // Hit a plane — show the guide dot at the intersection.
            Pose hitPose = hits[0].pose;
            guideDotInstance.SetActive(true);
            guideDotInstance.transform.position = hitPose.position;
            guideDotInstance.transform.rotation = hitPose.rotation;
            guideDotVisible = true;
        }
        else
        {
            // Not aimed at any plane — hide the guide dot.
            guideDotInstance.SetActive(false);
            guideDotVisible = false;
        }
    }

    // ----- Scan prompt -----

    private void UpdateScanPrompt()
    {
        // Show the prompt when no planes have been detected yet.
        // planeManager.trackables contains all detected planes.
        // Once any plane exists, hide the prompt — the user
        // has scanned enough to start measuring.
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

        // ----- Ignore taps on UI buttons -----
        // Without this check, tapping "Undo" or "Reset" would
        // also place an anchor behind the button.
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // ----- Raycast from tap position -----

        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
            return;

        Pose hitPose = hits[0].pose;
        GameObject anchorObject = CreateAnchorMarker(hitPose);

        // ----- Haptic feedback -----
        // Handheld.Vibrate() triggers a short vibration on Android.
        // It's a simple confirmation that the anchor was placed.
        Handheld.Vibrate();

        // ----- Measurement pairing -----

        if (firstAnchor == null)
        {
            firstAnchor = anchorObject;
        }
        else
        {
            Vector3 startPos = firstAnchor.transform.position;
            Vector3 endPos = anchorObject.transform.position;
            CreateMeasurement(startPos, endPos);
            firstAnchor = null;
        }
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

    // ----- Measurement creation -----

    private void CreateMeasurement(Vector3 start, Vector3 end)
    {
        GameObject measurement = Instantiate(measurementLinePrefab);

        LineRenderer line = measurement.GetComponent<LineRenderer>();
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        float distanceMeters = Vector3.Distance(start, end);
        float distanceCm = distanceMeters * 100f;

        Transform label = measurement.transform.Find("DistanceLabel");
        Vector3 midpoint = (start + end) / 2f;
        label.position = midpoint + Vector3.up * 0.03f;

        TextMeshPro tmp = label.GetComponent<TextMeshPro>();
        tmp.text = distanceCm.ToString("F1") + " cm";

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

            Transform label = measurement.transform.Find("DistanceLabel");
            if (label == null)
                continue;

            Vector3 awayFromCamera = label.position - cam.transform.position;
            label.rotation = Quaternion.LookRotation(awayFromCamera);
        }
    }

    // ----- Undo and Reset (called by buttons) -----

    // Removes the last placed anchor. If it was the first anchor
    // of an incomplete pair, clears the pending state. If it was
    // the second anchor of a completed pair, also removes the
    // measurement line.
    public void Undo()
    {
        if (placedMarkers.Count == 0)
            return;

        // Remove the last anchor.
        GameObject lastMarker = placedMarkers[placedMarkers.Count - 1];
        placedMarkers.RemoveAt(placedMarkers.Count - 1);
        Destroy(lastMarker);

        if (firstAnchor != null)
        {
            // We had a pending first anchor — that's the one we
            // just removed. Clear the pending state.
            firstAnchor = null;
        }
        else if (measurements.Count > 0)
        {
            // We just removed the second anchor of a completed
            // pair. Also remove its measurement line and restore
            // the first anchor as pending.
            GameObject lastMeasurement = measurements[measurements.Count - 1];
            measurements.RemoveAt(measurements.Count - 1);
            Destroy(lastMeasurement);

            // The remaining last marker is now the unpaired first
            // anchor of that measurement.
            if (placedMarkers.Count > 0)
            {
                firstAnchor = placedMarkers[placedMarkers.Count - 1];
            }
        }
    }

    // Removes everything — all anchors, markers, and measurements.
    // Back to a clean slate.
    public void ResetAll()
    {
        foreach (GameObject marker in placedMarkers)
        {
            if (marker != null)
                Destroy(marker);
        }
        placedMarkers.Clear();

        foreach (GameObject measurement in measurements)
        {
            if (measurement != null)
                Destroy(measurement);
        }
        measurements.Clear();

        firstAnchor = null;
    }
}