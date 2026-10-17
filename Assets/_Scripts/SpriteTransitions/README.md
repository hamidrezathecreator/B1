# Sprite material transitions

Add SpriteCrossfadeController and assign a collection of MeshRenderers and one Target Material for your atlas. Refresh Mesh Renderers in the component menu collects meshes below the controller, including inactive objects. No pair component or second renderer is needed.

Set Fade Duration, Random Delay Min/Max, and Use Unscaled Time on the controller. BroadcastCrossfade() works with UnityEvents and Timeline Signal Receivers. Each mesh waits its own random delay, fades out, switches all material slots to the target material, and fades in. Duration applies to each half; total time is the delay plus twice Fade Duration. Zero duration swaps immediately after the delay. Meshes and atlas UVs stay the same.

Both current and target materials must use alpha-transparent shaders exposing _BaseColor (preferred) or _Color. Configure materials for transparent alpha blending. Temporary material instances fade individual meshes without modifying shared material assets. On completion, meshes reference the target material itself. Existing renderer property blocks must not override the shader's color/alpha.

Retriggering or disabling the controller cancels pending fades and restores each material's original opacity. Meshes that already switched keep the target; other meshes keep their source materials. Temporary instances are cleaned up.

For Timeline, add a Sprite Crossfade Track, bind it to the controller, and place a Sprite Crossfade Clip. The existing clip triggers BroadcastCrossfade() once when playback enters it.