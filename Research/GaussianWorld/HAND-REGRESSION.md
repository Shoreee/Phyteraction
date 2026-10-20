# visionOS hand regression, 2026-10-20

The visible hand follows every visionOS joint. Each physical finger bone is
kinematic in XR mode and retains its collider. This prevents the old dynamic
joint chain from oscillating independently of the tracked hand. PhysX objects
receive contact from the colliders; the Gaussian solver receives their positions
and finite-difference velocities. The tracked hand itself does not recoil or
provide haptic force feedback.

The imported OVR thumb has an extra terminal segment. In the pre-fix device CSV
`Logs/device/telephone-1653.csv`, median thumb-tip errors were 30.35 mm left and
30.73 mm right; index-tip errors were 4.66 mm and 1.92 mm. The current XR path
positions the final visual thumb bone so its marker coincides with the raw
visionOS ThumbTip, and the kinematic terminal collision bone follows that target.
The latter's actual device-space alignment and contact behavior require a new
device recording.

Run `Physics Hands/Validation/Stress Direct Tracked Hands` in the Unity editor
for the synthetic rapid-motion test. Its latest JSON result is
`Logs/finger-stability-direct.json`: passed, 0 dynamic finger bones, 23 hand
contact shapes, and a dynamic sphere displaced by contact. This is a path test,
not a force-quality benchmark. A 0.9 m/s synthetic motion produced up to about
2.4 cm transient collision-bone separation; this is a remaining accuracy limit.

The existing `Builds/VisionOS/Unity-VisionOS.xcodeproj` builds successfully.
The corrected app is installed on the paired headset. `devicectl process launch`
timed out while the headset was not available for a remote run; no post-fix CSV
has been collected. To finish validation, wear/unlock the headset, launch
`VPPhysicsHands`, shake both hands, pinch and push a GS object, then inspect the
new `Documents/gaussian-*.csv` in the app data container for thumb/index-tip
error, shapes, contact pairs, reaction, and frame times. Compare the thumb error
with the pre-fix log and inspect any finger/skin separation visually.
