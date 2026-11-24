# Controller hand grab poses

1. Select the box, microphone, or other object in the scene or open its prefab in Prefab Mode.
2. Choose **B1 > Hands > Add Grab Pose To Selected**, or add **Controller Grab Pose** in the Inspector. This also adds XR Grab Interactable and its required Rigidbody when needed. Add a collider if the Inspector warns that one is missing.
3. Choose **Left Hand** or **Right Hand**, then **Show Hand Preview**.
4. Move the wrist with **W**, rotate it with **E**, and adjust the five finger curl sliders.
   - Under **Advanced Finger Movement**, choose a finger to adjust sideways spread, roll, and independent base, middle, and tip bend offsets. The thumb also has an opposition control for moving it across the palm. Spread and roll axes are derived from the preview hand's palm and bones.
   - Enable **Edit Finger Joints In Scene**, click a joint marker, and rotate its rings directly for finger spread, thumb placement, or compound rotations. The Selected Joint dropdown also selects joints that are difficult to click.
   - **Individual Joint Rotations** exposes exact local Euler angles. These manual rotations are preserved when changing curl or advanced sliders. Open Hand, Closed Hand, and Reset Adjustments deliberately clear the applicable manual changes.
5. Click **Save Pose**, then repeat for the other hand. Save the scene or prefab normally. Preview edits are drafts until saved; hiding the preview discards unsaved changes.

The preview uses the visual prefabs, material, and optional texture from `Resources/ControllerHandsRig.prefab`. Assign a different Hand Rig Template on the object when needed. Preview hands are temporary, have no active tracking or physics, and are removed when the Inspector closes or Play Mode begins.

During a single-hand grab, the object aligns to the saved wrist position and rotation. Finger joints blend into the saved pose while the object is held, then return to controller input after release. Objects and hand sides without a saved pose keep their existing grab behavior. Configure the controller's Select input to use the grip button with hold behavior as usual; this component does not change input bindings.

The wrist pose is relative to the grabbable object's transform. Preview hand size stays at the actual rig size even when the object is scaled. Author both hands independently; mirroring a wrist position alone does not mirror finger anatomy. Use the same hand model for preview and gameplay so saved joint paths match.

Multiple-hand grabs retain the object's existing multiple-grab transformer behavior. This tool authors fixed single-hand poses; it does not calculate finger contact with arbitrary geometry automatically.
