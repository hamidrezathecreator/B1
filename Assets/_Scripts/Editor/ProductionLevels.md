# Level 2 and 3 scene setup

Gameplay objects, interaction colliders, destinations and flow components are saved in the production scenes. Runtime setup does not spawn these objects or resolve targets by name. Renaming objects keeps their assignments intact.

## Level 2

Select **Level 2 Flow** in `Scenes/Production/Lvl2_MissileTunnel.unity`.

- **Elevator Door Intro** references the cabin, doors, floor-button interactables, barrier, tablet, table, player, fade overlay and audio sources.
- **Elevator Boundary** stays active through fade, button waiting, travel and door opening. It is a child of the moving cabin. **Level Boundary** starts inactive and replaces it only after both doors finish opening. The enforcer follows the headset horizontally, including room-scale movement.
- Fade completion comes from the intro's actual fade routine. Only then are the floor buttons enabled. A single accepted press starts the ride; tablet progress cannot bypass the closed doors.
- Move **Player Spawn** inside the cabin to set the starting position. Move **Elevator Arrival** to set the cabin's destination. Keep the arrival outside the moving cabin hierarchy.
- Move the **Door 1/2 Open Pose** and **Floor Button Pressed Pose** anchors to edit the animation destinations. Keep these anchors beside their moving objects, not beneath them.
- **Tablet Path Start**, **Tablet Path Corner** and **Tablet Path Destination** define the route after the doors open.
- The tablet has its grab, rigidbody and three option trigger colliders in the scene. Its starting placement is preserved. Assign **Left Hand Root** and **Left Attach** on Tablet Grab Interactable; **Capture Left Hold Pose** updates the assigned attach anchor from the hold settings in Edit Mode.
- Transition waits for the elevator doors to open and all three options on the assigned tablet to be checked. Unchecking an option cancels the pending transition.

## Level 3

Select **Level 3 Flow** in `Scenes/Production/LVL3_MissileLaunch.unity`.

- **Orchestrator** uses **Launch Buttons Only** for this level. Each of the two buttons launches its own tutorial missile and immediately hides the tutorial card beside that button. There is no rotation step, card transition or narration wait. The controller hint remains until both buttons have been pressed.
- The accepted second launch button press immediately completes the tutorial and starts **Auto Launcher Salvo** after **Delay After Tutorial Before Salvo**. The tutorial waits for that press without a timeout. No extra tutorial completion or missile arrival is required.
- **Automatic Launches** contains the four scene missile projectiles and heading waypoints in launch order. The state machine launches them directly with random gaps between **Min/Max Delay Between Auto Launches**; automatic launches have no buttons or interaction colliders.
- Every level 3 projectile has **Continue Beyond Destination** enabled. The destination establishes its final heading; the missile keeps flying at arrival speed with its exhaust and audio until disabled, destroyed or reset.
- **Drone Root**, **Drone Flight** and **Drone Strikes** reference the existing drone and its two intact/wreck pairs. The strikes finish before the tracker pickup stage starts.
- **Tracker Scope** and **Tracker Path Target** assign the weapon and guidance destination. The tracker has an authored grab collider, rigidbody, camera, target and missile launch origin.
- **Xr Level Play Setup** references the player spawn, ground, teleport rays, cameras, tutorial button adapter, all six launch projectiles, audio and feedback recipients. It configures XR and presentation; the state machine owns progression. Ground colliders and teleportation areas exist in the scene.

State machine Inspector shortcuts explicitly skip gameplay waits for testing. Normal gameplay waits for the assigned interactions. Missing required assignments report errors and block progression. Path graphics, hints, scope rendering, missiles and impact effects remain runtime presentation.
