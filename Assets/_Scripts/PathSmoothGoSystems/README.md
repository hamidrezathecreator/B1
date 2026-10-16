# Car paths

1. Add one PathDatabase component to the scene.
2. In its Inspector, click **Add Car Path**, then click the ground in Scene view to place waypoints. Enter or Escape finishes placement. Repeat to create the collection.
3. Drag **CarPathAgent.prefab** into the scene and put your car model under its **Visuals** child, facing local +Z. Alternatively add **Car Path Driver** (GlobalPathDriver) to your existing car prefab; PathWalker is added automatically.
4. Set **Min Speed** and **Max Speed** on the driver (world units per second). Each activation chooses a valid route uniformly and samples one cruise speed. Set both values equal for fixed speed. Disable **Random Path** and enter a path id for a fixed route.
5. Press Play. The movement tick system is created automatically if the scene has none.

Routes are open: the car follows the last waypoint, instantly snaps to the first waypoint and initial heading, then repeats the same route at the same speed. It never drives a closing segment. Random choices occur at startup and when a pooled car is enabled again, or through DriveRandomPath(); a lap does not choose a new route or speed.

Smoothing, per-waypoint reverse and sharp flags, and height offsets are supported. Keep car pivot offsets on the driver or Visuals child. Waypoints are world positions; moving the database object does not move existing routes. Movement writes the Transform directly, so configure any car Rigidbody as kinematic.

Existing globalPaths data and script GUIDs are preserved. Wave Enter/Exit data, wave selection/claims, and one-shot playback have been removed. The scene editor supports Undo, drag handles, and adding points; full point lists and smoothing settings remain editable in the Inspector.
