# Level 1 pickup and placement

Open `Scenes/Production/Lvl_1.unity` and select **Lvl1 Build Level State Machine**.
Its **Tutorial** reference points to the scene's **Lvl1 Tutorial** component on the same object.

- **Pickup Cases** assigns the objects that advance the tutorial. Each needs an authored Rigidbody, collider and XR Grab Interactable. The current two cases already have these components. The first case's saved hand poses are preserved.
- **Placement Zones** assigns the scene's **Tutorial Placement A** and **Tutorial Placement B** objects. Move or rotate either object to choose the final case pose. Its yellow preview is a child and follows it in the editor.
- Each zone's **Accepted Object** or **Accepted Objects** controls which pickups it accepts. Set **Snap Radius**, **Pull In Radius** and **Snap Seconds** on the zone. An unassigned zone accepts nothing.
- **Hide When Filled** assigns the optional preview to hide on placement. Keep that preview separate from the zone's GameObject so hiding it does not stop the snap coroutine.
- **Scene References** assigns the player origin, Start button, right controller, font and teleport rays directly. Object names do not affect pickup or placement.

Normal progression: Start → move → pick up an assigned case → place it in an assigned zone → finish snapping → completion message → first missile and explosion → remaining level sequence. Missing assignments block completion and report an error. Picking up or dropping a case elsewhere cannot start the explosions.

The state machine Inspector has a button to select the tutorial setup. Explosion shortcuts are explicit Play Mode debugging overrides; advancing the tutorial itself cannot bypass placement.
