# Explosion Quads

This folder contains the supplied flipbook quad assets and the event driven emitter.

## Setup

1. Create a scene object and add `ExplosionQuads.ExplosionQuadEmitter`.
2. Assign one of the prefabs in `Sequencers` (or `sprites`) to **Quad Prefab**.
3. Add one or more entries to **Explosions**. Each entry supports a count, local position, delay, Y offset, radius, random rotation, and scale variation.
4. Optionally assign materials to **Explosion Variants** to select different flipbooks/colors per shot.
5. Choose a facing mode and camera. `FaceCamera` continuously billboards each quad; the yaw/pitch modes constrain that rotation.

`Play()` and `Trigger()` are public event methods. They can be called by another component, a UnityEvent, or a Timeline Signal Receiver. For a Timeline Activation Track, leave the emitter object inactive before the activation clip and keep **Play On Enable** enabled. Activation then calls `OnEnable`, which starts the collection.

The emitter raises `On Explosion Started`, `On Shot Spawned`, and `On Explosion Completed` events and clears spawned quads after the longest flipbook finishes.
