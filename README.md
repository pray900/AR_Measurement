# ARMeasure

A tape measure that lives in your phone. Point the camera at a floor or a table, tap two points, and ARMeasure tells you how far apart they are. It can also stand a height up off a surface, combine width with height, and wrap a full 3D box around an object so you get length, width and height in one go.

Built in Unity with AR Foundation, so the same project builds for both Android (ARCore) and iOS (ARKit).

## What it does

**Four measuring modes**, each one a button along the bottom of the screen:

| Mode | Taps | What you get |
|---|---|---|
| Distance | 2 | Straight line distance between two points on a surface |
| Height | 2 | Vertical height from a floor point up to wherever you aim |
| W + H | 3 | A width across a surface, then a height rising from it |
| Box | 4 | A wireframe bounding box with length, width and height labels |

**Other things it does while you measure:**

- A white guide dot sits in the middle of the screen and snaps onto whatever surface the camera is looking at, so you always know where a tap is going to land.
- Every tap drops a red anchor marker that stays welded to the real world, and buzzes the phone so you know it registered.
- Lines are dashed and colour coded. Cyan for length and distance, green for height, orange for width, yellow for the box wireframe. Green translucent overlay shows detected planes.
- Distance labels float at the midpoint of each line, always turn to face you, and grow or shrink with camera distance so they stay readable without swallowing the view.
- "Move your phone slowly to scan surfaces" shows up until AR Foundation finds its first plane, then gets out of the way.
- Undo peels back one step at a time. It understands partial measurements, so if you are halfway through a box it removes the last corner rather than the whole thing. Reset wipes the scene clean.
- Save hides the UI for one frame, grabs the screen, and writes a PNG. On Android it copies the file into `/sdcard/Pictures/ARMeasure/` and pokes the media scanner so the shot turns up in your gallery. On iOS the file lands in the app's documents folder, reachable through the Files app.
- On iPhones and iPads with LiDAR, the mesh manager switches itself on at runtime for denser scene geometry. Everything else ignores it and carries on.

## How to use it

1. Open the app and sweep the phone slowly across the floor or a table. Wait for the green plane overlay to appear.
2. Pick a mode from the bottom row.
3. Line the centre dot up on your first point and tap anywhere on the screen. Repeat for the remaining points.
4. The measurement appears in centimetres the moment the last point lands.
5. Tap Save for a screenshot, Undo to step back, or Reset to start over.

Height and box modes need a starting point on a real detected surface. After that, the height point is projected onto an invisible vertical plane that passes through the base point, which is how you can measure up the side of something that has no trackable surface of its own.

## Requirements

- Unity **6000.4.10f1** (Unity 6.4)
- Android device with ARCore support, Android 7.1 or newer (min SDK 25, ARM64)
- or an iPhone / iPad running iOS 16.0 or newer with ARKit

Packages come from the manifest and restore themselves on first open:

- AR Foundation 6.4.3
- ARCore XR Plugin 6.4.3
- ARKit XR Plugin 6.4.3
- Input System 1.19.0
- TextMesh Pro (via uGUI)

## Building

Clone it, open the folder in Unity Hub with 6000.4.10f1, and let the package manager settle. The only scene in the build list is `Assets/MainScene.unity`.

**Android:** switch the platform to Android, confirm ARCore is ticked under Project Settings > XR Plug-in Management, and build. Bundle id is `com.yush.armeasure`.

**iOS:** there is a ready build profile at `Assets/Settings/Build Profiles/iOS.asset`. Switch to iOS, build out the Xcode project, open it on a Mac, set your signing team, and deploy to a real device. Bundle id is `com.iosyush.armeasure`. The camera permission string is already filled in. Simulators will not work, ARKit needs actual hardware.

Neither platform runs in the Unity editor play mode, since there is no camera feed or tracking to work with. Everything gets tested on device.

## Project layout

```
Assets/
  MainScene.unity              the only scene, holds AR Session, XR Origin, Canvas and managers
  Scripts/
    PlaceAnchors.cs            all four modes, tap handling, lines, labels, undo, screenshots
    LiDARSetup.cs              turns the mesh manager on when the device supports it
  AnchorMarker.prefab          red sphere dropped at each tapped point
  MeasurementLine.prefab       LineRenderer plus a TextMeshPro label child
  GuideDot.prefab              the white dot that tracks the centre of the screen
  AR Plane Debug.prefab        visualiser for detected planes
  *.mat                        one material per line colour, plus plane and marker
  XR/Loaders/                  ARCore and ARKit loader assets
  Settings/Build Profiles/     iOS build profile
```

`PlaceAnchors.cs` is where nearly everything happens. Each mode has its own tap handler, they all share the same anchor creation and line drawing helpers, and `LateUpdate` handles billboarding and scaling every label. The dashed line effect is built by hand, laying down 2cm dashes with 1cm gaps along the line rather than relying on a texture.

## How it was built

Nine phases, each one a commit, starting from plane detection and working up:

1. Phase 4, two point measurement
2. Phase 5, guide dot, undo and reset
3. Phase 6, multiple measurements and the height and W+H modes
4. Phase 7, object bounding box
5. Phase 8, colour coded lines and gallery screenshots
6. Phase 9, iOS support

## Known rough edges

- Screenshots on iOS stop at the app's documents folder. Getting them into the Photos app properly needs a small native plugin, which is not written yet.
- Measurements are shown in centimetres only. No imperial units and no unit toggle.
- The measurement log is kept in memory and vanishes when the app closes.
- Accuracy depends entirely on tracking quality. Good light and textured surfaces help a lot, blank white walls and dim rooms do not.
