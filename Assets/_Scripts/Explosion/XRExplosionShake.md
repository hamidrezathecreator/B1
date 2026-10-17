# XR explosion shake

Add `XRExplosionShake` to the GameObject that has `XROrigin`. B1 and Missiles
are already configured. Leave **Shake Pivot** empty for automatic setup: on the
first shake the script creates an identity pivot under Camera Offset and moves
all offset children beneath it, preserving their local poses. Invalid old
assignments (including the XR Origin itself) also use this automatic setup.

```text
XR Origin  [XROrigin, XRExplosionShake]
  Camera Offset  [keep this assigned as Camera Floor Offset Object]
    Explosion Shake Pivot  [created at runtime, or assigned manually]
      Main Camera  [keep its tracked pose driver]
      Left Controller
      Right Controller
```

For a manually authored pivot, include all controller and tracked hand roots.
Keep the XR Origin's Camera reference pointing at Main Camera and Camera Floor
Offset Object pointing at Camera Offset. Only the shake script should animate
the pivot; locomotion still moves the XR Origin. Automatic hierarchy changes
exist only at runtime and are not saved into the scene.

## Explosion events

Missile shakes require no Timeline signals or Inspector event wiring. The rig
subscribes when enabled and unsubscribes when disabled. Events are filtered to
the rig's own scene, keeping the two missile systems independent even with
additive scene loading:

- `MissileAToB/MissileLauncherSystem` emits `MissileImpacted(source, position)`
  every time an individual missile reaches its endpoint, independently of quad
  playback. Enable **Shake On A To B Impact** on the rig (enabled in B1).
- `MissileLauncher/MissileLauncherController` emits `MissileFired(source)` for
  each missile immediately after `Launch`. Enable **Shake On Launcher Fire**
  on the rig (enabled in Missiles). Rotation, sequence completion, and reaching
  the sky endpoint do not trigger this shake.

The existing Timeline callbacks to `Shake()` have been disconnected in B1,
Menu, and Hospital. Other Timeline reactions are unchanged. Neither missile
system references the other or contains XR rig logic.

Other gameplay systems can call `Shake()` from a UnityEvent, or subscribe
`shake.Shake` to a custom C# event when enabled and unsubscribe when disabled.

Code can also call `shake.ShakeWithStrength(0.5f)` for half strength. The
argument is clamped to 0..1. Zero/negative values are ignored. These one-shot
calls have no distance attenuation: the explosion decides when to send the
event and what intensity to request. Rising shake handles distance separately.

Set **Delay** under **Small positional shake** to the shock travel time in
seconds. It defaults to 0 (immediate) and delays each launch/impact or explicit
shake call without shortening its duration. Rising shake is unaffected. The
delay follows **Use Unscaled Time**; stopping or disabling shake cancels pending
pulses.

Each small pulse raises the C# event `XRExplosionShake.SmallShakeStarted(source)`
when it actually starts, after **Delay**. Rising shake does not raise this event.
`VrBlinkEffect` listens automatically for pulses in its own scene when **Blink On
Small Shake** is enabled (default). Its **Small Blink Amount** defaults to 0.1
(10%). **Small Blink Duration** sets its total time (default 0.3 seconds), split
40% closing and 60% reopening, with no hold. Requests during an
active blink or while the eyelids are closed are ignored. Other gameplay events,
UnityEvents, and Timeline signals can call `VrBlinkEffect.SmallBlink()` directly.

## Rising shake

The container-to-sky `MissileProjectile` system also registers active flights.
With **Rising Shake Enabled**, the rig continuously measures each active
missile's distance from the headset. This is configured in Missiles and disabled
in B1; the A-to-B missile system does not participate.

- **Rising Intensity**: fraction of the main Strength at close range (default 0.6).
- **Rising Full Strength Distance**: full intensity within this distance (default 5 world units).
- **Rising Max Distance**: fades linearly to zero here (default 60 world units).
- **Rising Fade Time**: seconds to ramp one full intensity unit up/down (default 0.15), smoothing starts, ends, and source changes.

Only the strongest nearby missile contributes. Rising shake and launch/impact
pulses also use their maximum, never their sum. They share a continuous waveform
and the main Strength limit, so launching many missiles does not stack motion
or restart the rising waveform. Completed, reset, disabled, or destroyed missiles
leave the active-flight registry; there is no shake from missiles sitting at
their sky endpoints. Re-enabling the rig during flight picks up active missiles.
`SetShakeEnabled(false)` disables both effects. `StopShake()` clears the current
motion immediately; active missiles can start rising shake again next frame.

The component context menu **Test Shake (Play Mode)** lets you check the effect
without firing missiles. `StopShake()` immediately restores the pivot.

## Behavior and tuning

Defaults are a 0.18-second fading shake with 0.003/0.002 local-unit strength on
X/Y (3/2 mm at unit rig scale), no Z movement, and no rotation. The script updates
the pivot in LateUpdate and reapplies the same offset before XR rendering;
headset/controller tracking keeps owning the tracked children. It never writes
the tracked camera, locomotion origin, or floor-height offset. Repeated hits
refresh the pulse envelope without restarting a running waveform, so they cannot
stack or drift. When both the pulse and rising shake finish, the resting position
is restored. Stopping, disabling, and destruction also restore the pivot.
Scaled time is the default so paused gameplay also pauses
the effect; enable Use Unscaled Time when desired.

Small artificial view motion can still be uncomfortable in VR. Start with these
small values and evaluate in the headset. Connect a player comfort toggle to
`SetShakeEnabled(bool)` to disable the effect and stop any active shake.
