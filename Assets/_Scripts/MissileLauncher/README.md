# Missile launcher setup

1. Add `MissileLauncherController` to each launcher and assign its rotating `container`.
2. Add `MissileProjectile` to each missile. A slot accepts a prefab asset (spawned at `launchPoint`, falling back to the container) or a scene object (launched from its current pose, without cloning).
3. `localSkyDirection` is the missile model's local nose axis, for example `(0,1,0)` for a model pointing along local Y or `(0,0,1)` for local Z. Each missile transforms this axis using its own orientation after phase 1. It is not a container-relative/world sky vector. Prefab missiles use their launch point's orientation. Scene missiles keep their authored orientation and detach on launch.
4. Configure rotation, launch spacing, distance, arc, particles, sounds, and launch light as needed.

## Flight speed and attached particles

On `MissileProjectile`, **Acceleration Power** defaults to 2: travel starts slowly and builds speed throughout **Flight Duration**. Increase the duration for a slower flight, or increase the power for a longer slow start. Power 1 gives constant path progress. The height curve is applied along this accelerated path and returns smoothly to the target without an endpoint snap.

Assign **Flight Particle Prefab** to spawn exhaust/trail particles attached to the missile on launch. Optionally assign a child nozzle as **Flight Particle Anchor**, then adjust **Flight Particle Local Offset** and **Flight Particle Local Euler Angles**. The prefab's emission, size, and speed settings define full power; use World simulation space on its particle systems if emitted smoke should stay behind as the missile moves. Emission stops at flight completion so existing particles can fade naturally. Reset clears and removes the spawned effect, and another launch creates a fresh instance. Existing `launchParticles` and the stationary launch-light effect still work separately.

Attached flight particles ramp from **Particle Starting Power** (default 0.05, or 5%) to the prefab's full power over **Particle Ramp Duration** (default 1.25 seconds). This smoothly scales emission rates, burst counts, starting size, and starting speed, including child particle systems. Prewarmed particles are cleared so the effect starts gently. Increase the duration for a slower buildup, or set starting power to 0 to start from nothing. These controls affect the attached flight effect; separate launch/ignition particles remain unchanged.

## Attached trembling flight light

Assign **Flight Light Prefab** to a prefab containing `MissileLaunchLight`. The provided **Flight Light.prefab** uses intensity 6–12, tremble speed 18, and a random noise offset per instance so missiles flicker independently. Optionally assign a child nozzle as **Flight Light Anchor** and set the local position/rotation offsets. It follows the missile, builds brightness using the same starting-power/ramp-duration controls as the attached particles, and is removed on flight completion or reset. The missile in `Scenes/Missiles.unity` is already assigned this prefab. The stationary launch flash retains its own prefab and lifetime.

## Test individual phases

In Play Mode use the launcher Inspector buttons or component **Test** context menu: rotate, launch, reset. Launch respects `requireRotationPhase`; uncheck it for isolated flight testing. Reset stops flights, restores scene missiles and removes spawned missiles. Scene missiles launch once per reset.

## Tutorial with one or many launchers

Click **Create VR Tutorial Setup** on a launcher Inspector/context menu in Edit Mode. It creates a separate root with:

- `MissileLauncherButtons`: two XR sample push-button models and press events. This adapter controls which button is interactable and knows nothing about launcher behavior.
- `MissileLauncherActions`: assigned launcher array, `RotateAll()`, `FireAll()`, `ResetAll()`, and aggregate completion/failure UnityEvents. Add every participating launcher to this array; duplicates are handled once. All assigned entries must be valid, active and idle. Firing requires every rotation to have completed.
- `MissileLauncherTutorialOrchestrator`: tutorial state, button gating, and group event subscriptions.
- References to CanvasGroups on your existing rotation/fire cards. The setup does not create a canvas or change card contents.

Add a CanvasGroup to each existing card root in your world-space canvas, then assign those groups to **Rotate Card** and **Fire Card** on the orchestrator. Keep both card objects active; the orchestrator hides them by alpha and fades them in/out using unscaled time. Existing sprites, text, layout, and UI remain authored in the canvas. Set fade durations, add other launchers to the actions array, and position the physical button panel within reach. **Begin After Delay** schedules the tutorial automatically; set **Begin Delay** in seconds (default 3). For Timeline-only initiation, turn off Begin After Delay and call `Begin()` from a signal/UnityEvent. Every Begin call waits the configured delay before the first sound/card, keeping both cards hidden and buttons disabled. Begin also resets all assigned launchers for a fresh attempt.

The sequence is:

1. Play **Rotate Intro Sound** and wait for playback to finish. Fade in the rotation card, then enable only Rotate.
2. Rotate press hides the card, disables both buttons, and rotates every assigned launcher.
3. Wait for `rotationCompleted` from every launcher. Play **Fire Intro Sound** and wait for playback to finish. Fade in the fire card, then enable only Fire.
4. Fire press hides the card, disables both buttons, and fires every assigned launcher.
5. Wait for all `missileSequenceCompleted` events. These mean all sequences dispatched their missiles, not that missile flights have ended.
6. With **Show Final Logo** enabled, wait **Final Logo Delay**, prepare and play the logo video while fading in its existing CanvasGroup, then return to the menu through `SceneTransitionManager` when the video finishes. `tutorialCompleted` is emitted when the menu transition is requested. With Show Final Logo off, completion is emitted immediately after step 5.

Assign both intro clips in the orchestrator's **Audio Before Each Card** section. **Tutorial Audio Source** should be a dedicated, active source; it defaults to the AudioSource on the orchestrator object. Both physical buttons stay disabled during playback and card fades. An unassigned clip skips that audio step. Restart, failure, or disabling the orchestrator cancels its pending sound/card progression. Playback completion is checked on the source, including AudioListener pauses, so it is not tied to the game's time scale or a hardcoded clip-length timer.

`actionFailed`/`tutorialFailed` report invalid or interrupted groups. Missing/destroyed/disabled launchers are not treated as successful completion. `Begin()` restarts after a failure. Disabling the orchestrator gates both buttons and removes its listeners; call Begin after re-enabling.

The orchestrator subscribes automatically; do not also wire button presses directly to launchers or actions when using this tutorial. Existing panels created by the earlier **Create Wired VR Push Buttons** command should be replaced by the new setup, since they call individual launchers directly. Input action references have been removed from launcher behavior; physical presses use events.

For non-tutorial use, the button adapter's `rotatePressed`/`firePressed` UnityEvents can instead call group actions. Call `EnableRotate()`/`EnableFire()` to choose the available button. Do not attach the tutorial orchestrator in that mode.

## Final logo and menu

In the orchestrator's **Final Logo And Return To Menu** section, assign **Final Logo Group** to the CanvasGroup containing your existing logo RawImage, **Final Logo Player** to its configured VideoPlayer, and **Scene Transition Manager** to the existing transition component. Keep these objects active and use a separate logo group from the rotate/fire cards. Set the VideoPlayer's clip or URL, target render texture, and RawImage texture as usual; the orchestrator leaves those rendering connections untouched.

**Final Logo Delay** defaults to 3 seconds after all firing sequences finish. Increase it to leave more time for missile flights. **Final Logo Fade In Duration** defaults to 1 second. The video prepares while hidden, then plays once during the fade; playback completion, not a guessed duration, triggers `SceneTransitionManager.LoadScene(menuSceneName)`. **Menu Scene Name** defaults to `Menu`, which is enabled in this project's Build Settings. Transition fade settings come from the assigned manager. The final video frame stays visible while the scene transition starts.

Preparation times out after **Final Logo Prepare Timeout** (default 30 seconds). Invalid references, a preparation failure, or video playback errors emit `tutorialFailed` and do not load the menu. Begin/restart and disabling cancel pending logo delay/preparation/playback and remove video event handlers.

## Music after pressing Fire

The music manager has **Fade In On Start** enabled by default: it starts at volume 0 and fades to **Fade In Volume** (default 0.2) at **Fade In Speed** (default 0.1 volume units per second). Turn off the toggle for manual control and call `FadeIn()` from a Timeline/UnityEvent. A Fire request during the startup fade smoothly retargets the volume from its current level. An event received before Start takes priority over the automatic startup fade.

Add `SingleMusicManager` to the object with your music AudioSource and assign its **Music Source** (defaults to the local source). Set that source's music clip, starting volume, and Loop as desired. Use a separate source from tutorial narration. The manager controls the source's volume directly.

On the tutorial orchestrator, assign **Music Manager**, **Fire Music Volume** (0–1), and **Fire Music Fade Speed** (volume units per second). An accepted Fire press emits **Music Raise Requested** with those settings; the assigned manager receives it automatically. No extra event wiring is needed. Already-playing music keeps its playback position. If stopped, the manager starts the assigned clip when **Play If Stopped** is enabled. Speed 0 applies the target immediately; positive speed fades from the current volume without overshooting, using unscaled time. A new request replaces the previous fade. **Volume Reached** is emitted when the target is reached. Leaving Music Manager empty keeps the music hookup optional; custom listeners can use the event instead.

## Physical button settings

For your own scene models, add **XR Simple Interactable** to each button root and give it a suitable collider. Add **XR Poke Filter**, assign that interactable and collider, and align the filter's poke direction with the intended push axis. Assign the XR Simple Interactable components to the adapter's Rotate Button / Fire Button fields. Those fields are typed as `XRBaseInteractable` to accept supported derived components; XR Base Interactable itself is abstract and cannot be added to a GameObject. Add **XR Poke Follow Affordance** with a child visual transform if you want the cap to move with the hand.

The generated buttons retain the XR Starter Assets sample's collider, `XRPokeFilter` (negative local Y, 45-degree approach threshold), moving cap, smoothing speed 16, 0.045 maximum travel, return on release, and visual affordance. Replace only the model mesh to use custom art, keeping the collider stationary and moving only the cap.

The rig needs an active XR Poke Interactor at the hands, a shared XR Interaction Manager, and matching interaction/physics layers (sample buttons use Default). The sample also permits ray selection; use interaction layers if only hands should reach the buttons. The adapter can also reference existing `VRPressButton` tutorial buttons; it resets their latch before enabling them.





