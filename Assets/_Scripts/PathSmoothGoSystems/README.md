# Car paths

1. Add one PathDatabase component to the scene.
2. In its Inspector, click **Add Car Path**, then click the ground in Scene view to place waypoints. Enter or Escape finishes placement. Repeat to create the collection.
3. Drag **CarPathAgent.prefab** into the scene and put your car model under its **Visuals** child, facing local +Z. Alternatively add **Car Path Driver** (GlobalPathDriver) to your existing car prefab; PathWalker is added automatically.
4. Set **Min Speed** and **Max Speed** on the driver (world units per second). Each activation chooses a valid route uniformly and samples one cruise speed. **Random Start Position** (enabled by default) spreads cars along the chosen route by picking a random distance, with the car facing its direction of travel. Disable it to start at waypoint zero. Set both speeds equal for fixed speed. Disable **Random Path** and enter a path id for a fixed route; random starting positions also work on fixed routes.
5. Press Play. The movement tick system is created automatically if the scene has none.

Routes are open: the car follows the last waypoint, instantly snaps to the first waypoint and initial heading, then repeats the same route at the same speed. It never drives a closing segment. Random choices occur at startup and when a pooled car is enabled again, or through DriveRandomPath(); a lap does not choose a new route, speed, or starting position. Random starts distribute cars independently and do not enforce minimum spacing.

Enable **Reverse** on an individual path to travel its waypoints in the opposite order. Its last authored waypoint becomes START and its first becomes END; finishing a lap snaps back to the last authored waypoint. Random starts and vehicle headings follow the selected direction. This keeps authored waypoint positions, smoothing, ground placement and segment backing flags intact. Changes apply when a car next starts or is assigned that path.

Each path has **Snap To Ground**, **Ground Layers**, and **Y Offset** settings. Ground snapping projects every smoothed point downward onto a non-trigger collider, then adds the same Y offset to the entire route. Y Offset also works with snapping disabled. Use ground-only layers to exclude cars and props; an empty mask automatically uses the Ground layer if present, otherwise the default raycast layers. Ground Ray Height and Ground Ray Distance control the downward search; a missed ray preserves the authored height plus the offset. The **Snap Selected Path to Ground** button enables snapping and refreshes the selected route. Rebuild Smoothed Routes refreshes ground placement after terrain or colliders change. Placement does not modify authored points, so rebuilding does not accumulate offsets.

Smoothing and per-waypoint reverse and sharp flags are supported. Adjust individual car model pivots using the Visuals child. Waypoints are world positions; moving the database object does not move existing routes. Movement writes the Transform directly, so configure any car Rigidbody as kinematic.

Existing globalPaths data and script GUIDs are preserved. Wave Enter/Exit data, wave selection/claims, and one-shot playback have been removed. The scene editor supports Undo, drag handles, and adding points; full point lists and smoothing settings remain editable in the Inspector.
