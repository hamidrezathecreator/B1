# XR explosion shake

Add `XRExplosionShake` to the GameObject that has `XROrigin`.
Create an empty `Shake Pivot` directly under the existing `Camera Offset`.
Reset the pivot's local position/rotation to zero and scale to one, then move
Main Camera and both controller roots beneath it, preserving their transforms:

```text
XR Origin  [XROrigin, XRExplosionShake]
  Camera Offset  [keep this assigned as Camera Floor Offset Object]
    Shake Pivot  [assign to XRExplosionShake.Shake Pivot]
      Main Camera  [keep its tracked pose driver]
      Left Controller
      Right Controller
```

Include tracked hand roots here too if your rig uses them. Keep the XR Origin's
Camera reference pointing at Main Camera. Only the shake script should animate
the pivot's local position; locomotion still moves the XR Origin. The script
does not automatically modify any scene or rig hierarchy.

## Explosion events

In an explosion's UnityEvent, add the XR Origin GameObject and choose
`XRExplosionShake > Shake()`. For example, use a missile's `flightCompleted`
event if impact is when the explosion occurs. For a custom C# event, subscribe
`shake.Shake` when enabled and unsubscribe when disabled.

Code can also call `shake.ShakeWithStrength(0.5f)` for half strength. The
argument is clamped to 0..1. Zero/negative values are ignored. There is no
automatic explosion detection or distance attenuation: the explosion decides
when to send the event and what intensity to request.

## Timeline signals

1. Add a `Signal Receiver` component to the XR Origin GameObject.
2. Add a Signal Track to the Timeline and bind it to that receiver.
3. Add a Signal Emitter at the explosion time and assign/create a Signal Asset.
4. In the receiver, add a reaction for that same Signal Asset. Add the XR Origin
   GameObject to the reaction and select `XRExplosionShake > Shake()`.

Use `StopShake()` for a separate stop signal if needed. The shake runs in Play
Mode; Timeline edit-mode scrubbing does not animate it. Use Emit Once on the
signal when it should fire once per playback rather than on every loop, and
leave Retroactive disabled if seeking past the explosion should not trigger it.

## Behavior and tuning

Defaults are a 0.18-second fading shake with 0.003/0.002 local-unit strength on
X/Y (3/2 mm at unit rig scale), no Z movement, and no rotation. PrimeTween updates
the pivot in LateUpdate; headset/controller tracking keeps owning the tracked
children. Repeated hits restart one shake from its original resting position,
so they cannot stack or drift. Completion, stopping, disabling, and destruction
restore the pivot. Scaled time is the default so paused gameplay also pauses
the effect; enable Use Unscaled Time when desired.

Small artificial view motion can still be uncomfortable in VR. Start with these
small values and evaluate in the headset. Connect a player comfort toggle to
`SetShakeEnabled(bool)` to disable the effect and stop any active shake.
