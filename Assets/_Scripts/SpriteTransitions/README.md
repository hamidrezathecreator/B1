# Sprite crossfade transitions

Add `SpriteCrossfadePair` to a parent object with two child `SpriteRenderer`s. Assign the renderers (or let the component discover the first two), choose the starting renderer, fade duration, and that pair's random delay range.

Add `SpriteCrossfadeController` to a parent collection and use **Refresh Pairs** in its component menu. `BroadcastCrossfade()` is available to UnityEvents and Timeline Signal Receivers. It sends the command to every pair, while each pair chooses its own random delay. `BroadcastFrom(Sprite)` restricts the broadcast to pairs currently showing a particular sprite.

For a direct Timeline track, add a **Sprite Crossfade Track**, bind it to the controller, and place a **Sprite Crossfade Clip**. The clip triggers once when playback enters it.
