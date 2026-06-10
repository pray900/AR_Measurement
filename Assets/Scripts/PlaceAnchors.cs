// PlaceAnchors.cs
// Manages two-tap measurement flow: first tap places start marker,
// second tap places end marker, draws a line, and shows distance.

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
    [SerializeField] private GameObject markerPrefab;

    // NEW: the measurement line prefab with LineRenderer + label.
    [SerializeField] private GameObject measurementLinePrefab;

    // ----- Internal state -----

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    public List<GameObject> placedMarkers = new List<GameObject>();

    // Tracks whether we're waiting for the first or second tap.
    // Null means no start point yet — next tap is a "start."
    // Non-null means we have a start point — next tap is an "end."
    private GameObject firstAnchor = null;

    // Stores all active measurement line objects so we can
    // manage or delete them later.
    private List<GameObject> measurements = new List<GameObject>();

    void Update()
    {
        // ----- Detect tap -----

        var touchscreen = Touchscreen.current;
        if (touchscreen == null)
            return;

        var touch = touchscreen.primaryTouch;
        if (!touch.press.wasPressedThisFrame)
            return;

        Vector2 screenPosition = touch.position.ReadValue();

        // ----- Raycast -----

        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
            return; // Didn't hit a plane — ignore the tap.

        Pose hitPose = hits[0].pose;

        // ----- Place anchor and marker -----

        GameObject anchorObject = CreateAnchorMarker(hitPose);

        // ----- Measurement logic -----

        if (firstAnchor == null)
        {
            // This is the FIRST tap of a new measurement.
            // Store the anchor and wait for the second tap.
            firstAnchor = anchorObject;
        }
        else
        {
            // This is the SECOND tap — complete the measurement.
            // Get the two world-space positions.
            Vector3 startPos = firstAnchor.transform.position;
            Vector3 endPos = anchorObject.transform.position;

            // Create the measurement visualization.
            CreateMeasurement(startPos, endPos);

            // Reset for the next measurement. Setting firstAnchor
            // to null means the next tap starts a new pair.
            firstAnchor = null;
        }
    }

    // Creates an anchor with a visible marker at the given pose.
    // Extracted into a method because both the first and second
    // tap need the same logic.
    private GameObject CreateAnchorMarker(Pose pose)
    {
        GameObject anchorObject = new GameObject("Anchor");
        anchorObject.transform.position = pose.position;
        anchorObject.transform.rotation = pose.rotation;
        anchorObject.AddComponent<ARAnchor>();

        GameObject marker = Instantiate(
            markerPrefab, pose.position, pose.rotation
        );
        marker.transform.SetParent(anchorObject.transform);

        placedMarkers.Add(anchorObject);
        return anchorObject;
    }

    // Creates a line between two points with a distance label.
    private void CreateMeasurement(Vector3 start, Vector3 end)
    {
        // Instantiate the measurement line prefab.
        // Position doesn't matter — the LineRenderer uses world-space
        // coordinates, and we'll position the label manually.
        GameObject measurement = Instantiate(measurementLinePrefab);

        // ----- Set line positions -----

        // The LineRenderer draws a line between world-space points.
        LineRenderer line = measurement.GetComponent<LineRenderer>();
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        // ----- Calculate distance -----

        // Vector3.Distance computes the Euclidean distance in meters.
        // Multiply by 100 to convert to centimeters for display.
        float distanceMeters = Vector3.Distance(start, end);
        float distanceCm = distanceMeters * 100f;

        // ----- Position and update the label -----

        // Find the DistanceLabel child we created in the prefab.
        // transform.Find searches immediate children by name.
        Transform label = measurement.transform.Find("DistanceLabel");

        // Place the label at the midpoint of the line.
        // This is the average of the start and end positions.
        Vector3 midpoint = (start + end) / 2f;

        // Offset the label slightly upward (3cm) so it floats
        // above the line rather than sitting on top of it.
        label.position = midpoint + Vector3.up * 0.02f;

        // Update the text to show the measured distance.
        // F1 formats to one decimal place: "42.3 cm"
        TextMeshPro tmp = label.GetComponent<TextMeshPro>();
        tmp.text = distanceCm.ToString("F1") + " cm";

        measurements.Add(measurement);
    }

    // Called every frame AFTER Update. LateUpdate is the right
    // place for camera-facing logic because the camera's position
    // has been finalized by then.
    void LateUpdate()
    {
        // Make every distance label face the camera.
        // This is called "billboarding" — the label rotates
        // to always show its front face to the viewer.
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

            // LookAt points the label's forward axis (Z) at the camera.
            // But LookAt makes the text face AWAY from the camera
            // (the Z axis points at the target, and text faces -Z).
            // So we look at a point OPPOSITE the camera relative to
            // the label — this flips it to face the camera correctly.
            Vector3 awayFromCamera = label.position - cam.transform.position;
            label.rotation = Quaternion.LookRotation(awayFromCamera);
        }
    }
}