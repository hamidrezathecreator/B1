# Level 1 pickup and placement

Open `Scenes/Production/Lvl_1.unity` and select **Lvl1 Build Level State Machine**.
Its **Tutorial** reference points to the scene's **Lvl1 Tutorial** component on the same object.

- **Pickup Cases** assigns the objects that advance the tutorial. Each needs an authored Rigidbody, collider and XR Grab Interactable. The current two cases already have these components. The first case's saved hand poses are preserved.
- **Placement Zones** assigns the scene's **Tutorial Placement A** and **Tutorial Placement B** objects. Move or rotate either object to choose the final case pose. Its yellow preview is a child and follows it in the editor.
- Each zone's **Accepted Object** or **Accepted Objects** controls which pickups it accepts. Set **Snap Radius**, **Pull In Radius** and **Snap Seconds** on the zone. An unassigned zone accepts nothing.
- **Hide When Filled** assigns the optional preview to hide on placement. Keep that preview separate from the zone's GameObject so hiding it does not stop the snap coroutine.
- **Scene References** assigns the player origin, Start button, right controller, font and teleport rays directly. All hand hints use RTLTMPro with the assigned Persian font (Peyda Bold in the production scene). Object names do not affect pickup or placement.

Normal progression: Start → move → switch controller visuals to hands → pick up an assigned case → place it in an assigned zone → finish snapping → completion message → first missile and explosion → remaining level sequence. The hands appear when the grab instruction begins, with a controller vibration. Missing assignments block completion and report an error. Picking up or dropping a case elsewhere cannot start the explosions.

The state machine Inspector has a button to select the tutorial setup. Explosion shortcuts are explicit Play Mode debugging overrides; advancing the tutorial itself cannot bypass placement.

After placement, a fighter jet pass precedes the first missile. The first explosion and cloud form at full speed for 0.5 seconds, followed by a two-second slow-motion recovery. Cloud expansion and debris travel follow global slow motion. A separate pressure front compares distance traveled against explosion-to-headset distance and immediately triggers shake, CentralScope, vibration and **Objects To Throw** when it arrives, including during slow motion. Feedback works even if the cloud is hidden, destroyed or skipped by the pool. **Player Listener** points to the tracked camera; assigning an XR Origin or one of its children also resolves to its camera. Thrown mesh centers aim at the headset, and distance-based travel timing triggers their feedback on arrival without collisions. **Attack Fraction** sets cloud expansion time as a fraction of its lifetime (Level 1: 2.5 scaled seconds). **Throw Speed**, **Maximum Throw Seconds**, **Throw Spacing** and **Player Hit Radius** tune the barrage.

The second missile starts when the recovery finishes. Falling starts 0.15 seconds after its impact once the debris barrage ends. The fall continuously shakes through PrimeTween and slowly turns toward new random angles every 1.4 seconds. Ground contact stops the motion, wind and vibration; **Maximum Fall Seconds** bounds the wait before the scene transition when no ground is available. Missile whoosh and fighter jet clips can be assigned in the Inspector; empty assignments use generated audio.

CentralScope is off during idle gameplay. Edit `Assets/Scripts/CentralScope/CentralScope/CentralScopeSettings.asset` to tune its separate **Shake** (subtle) and **Fall** (extreme) blur/vignette presets. Shake pulses fade out automatically; falling holds the Fall preset until landing or cancellation. Android and Standalone renderers share this settings asset.
