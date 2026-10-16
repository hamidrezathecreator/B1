# Independent A-to-B missile system

Add `MissileLauncherSystem` to an empty controller. Assign missile prefab assets to `Missile Prefabs`, the endpoint `Point B`, `Flight Duration` in seconds, and a quad sequencer prefab. Trigger `Fire()` from a UnityEvent or Timeline Signal Receiver.

`Missile Count` controls the total shots per `Fire()` call, independently of the prefab array length. Each shot randomly chooses a non-empty prefab entry; assigning one prefab fires that same type for every shot. The first shot launches immediately. `Min Launch Delay` and `Max Launch Delay` set the random gap in seconds between subsequent shots. Set both to zero for simultaneous shots, or equal positive values for a fixed interval. Reversed limits are sorted automatically. `On Completed` fires after the entire salvo has arrived, including shots waiting to launch.

There is no Point A transform. `Launch Offset` sets the launch center relative to Point B in world axes; `(0, 100, 0)` launches 100 units above it. `Launch Randomness` adds an independent random offset of plus/minus each axis per missile; `(25, 10, 25)` scatters launches horizontally by up to 25 units and vertically by up to 10 units. Zero disables randomness on that axis.

`End Point Radius` scatters each missile's destination uniformly inside a horizontal circle around Point B, retaining its Y coordinate. Zero hits Point B exactly. Each missile samples and captures its own launch position and destination when fired. Select the controller to see its launch volume and impact radius.

Each missile spawns facing its own randomized endpoint, stays facing that captured destination during flight, and arrives after `Flight Duration` seconds regardless of distance. Movement is linear, with speed determined by each missile's distance and duration. Set `Model Forward Axis` to the direction the nose points in the prefab's local space: `(0, 0, 1)` for +Z or `(0, 1, 0)` for +Y. Straight vertical flights are supported.

At arrival the missile instance is removed and a fresh quad sequence plays at the exact endpoint. The effect cleans itself up after playback. Every `Fire()` call adds another salvo: earlier missiles, pending launches, and impact effects continue. Each salvo captures Point B when triggered and tracks its own completion; `On Started` and `On Completed` fire once per salvo, including overlapping salvos. `ResetSystem()` cancels all pending launches and flights and clears all spawned missiles and effects. Disabling the component also cancels and clears everything. Events expose launch, each impact position, salvo completion, and reset.

Assign sequence materials to `Explosion Materials` on the controller. Each impact quad independently picks a random non-empty entry, using that material's own frame count and playback speed. Repeats are allowed. If no entries are assigned, existing single-material assignments are preserved, otherwise the quad prefab's material is used. Each quad owns its material instance, so playback remains independent.

Choose the impact quad's camera-facing mesh axis using `Facing Axis` on its prefab's `QuadSequncerController` (positive/negative X, Y, or Z). The launcher's `Facing` mode and `Flip Facing` still control how that axis faces the camera. `World Y Offset` on that same prefab component raises or lowers only the spawned effect along world Y; zero keeps it at the exact impact point.

Set `Min Quad Size` and `Max Quad Size` on the launcher for random impact sizes. Each instance gets an independent uniform multiplier of its prefab scale, preserving its proportions. For example, `0.75` to `1.5` gives 75% to 150% size. Both default to `1` to preserve existing sizes. Equal values give a fixed size; reversed limits are sorted and nonpositive values are clamped to `0.01`.

This component is independent of `AToBSystem`, `MissileLauncherController`, and `MissileProjectile`.
