# Model crossfades

## Scene setup

1. Put `ModelCrossfader` on a parent object with two separate model roots beneath it. Assign **First** (for example Standing) and **Second** (Running). The roots must not contain one another or the crossfader itself.
2. Configure **First Starts Visible**, **Fade Duration**, **Random Delay Min/Max** on each pair. Every command samples a separate delay for each pair; there is no controller-wide delay. Set both delay values equal for a fixed delay, or both to zero for an immediate start.
3. Use materials with alpha transparency. For URP Lit, Simple Lit or Unlit, set **Surface Type = Transparent**, **Blending Mode = Alpha**, and turn off **Alpha Clipping**. Configure this on the material assets before building so transparent shader variants are included. Custom shaders need alpha transparency and a `_BaseColor` or `_Color` property. Opaque materials will switch visibility at the endpoints but cannot fade smoothly; the component logs a warning.
4. Add `ModelCrossfadeController` to the collection's parent. Use **Refresh Pairs** in its component context menu to collect descendants, including inactive ones, or fill the list manually. Refresh again after adding pairs.

MeshRenderer and SkinnedMeshRenderer descendants and all their material slots are supported. The component creates private material copies at runtime, preserves their original tint/alpha, and releases the copies on destruction. It leaves authored renderer enabled states alone. Each model root is inactive when fully hidden; while fading both roots run their scripts/animators and colliders. Keep colliders on a separate parent if they must remain constant.

## Timeline events

Use a Timeline **Signal Track**, with a **Signal Emitter** at each transition time. Bind the track to a **Signal Receiver**, add the matching signal reaction, drag in the controller object and select:

- `ModelCrossfadeController.BroadcastToSecond()` for Standing -> Running.
- `ModelCrossfadeController.BroadcastToFirst()` for Running -> Standing.
- `ModelCrossfadeController.BroadcastCrossfade()` to toggle every active pair.
- `ModelCrossfadeController.ResetAll()` to immediately restore the configured starting states.

Use explicit first/second commands when signals may repeat. A repeated command to the same target does not restart its delay. A reverse command replaces the previous command and fades from the current blend after the newly sampled delay. Duplicate collection entries are ignored.

`BroadcastTo(GameObject)` and `BroadcastFrom(GameObject)` filter by exact scene model root references; use separate controller collections for groups with different models.

Signals initiate transitions during Play mode; the fade runs in game time (or unscaled time when selected). Timeline clip duration, pause and scrubbing do not control an already triggered fade. If playback loops, disable **Emit Once** on signals that should fire on every loop; send an initial-state signal when you need to reset between runs.
