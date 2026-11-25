# CentralScope shake and fall settings

Select **CentralScopeSettings.asset** in this folder to edit both visual presets. The Android and Standalone renderer features share this asset, so the same settings apply in the headset and desktop preview.

- **Shake** starts subtle. Each XR launch, cloud or debris shake briefly enables this preset, then fades it back to off over the shake duration.
- **Fall** starts extreme and stays at full strength throughout the fall. Landing, stopping shake or disabling the rig turns it off.
- **Radial Blur Amount** controls blur strength; **Radial Blur Radius** controls streak length. **Vignette Amount** darkens the edges.
- **Feather Inner Radius** sets the clear central window; **Feather Outer Radius** and **Feather Curve** control the transition to the affected edges.

The effect starts off and only renders on the XR camera while shaking or falling. It uses Unity 6 Render Graph and stereo texture sampling. Scene views, tracker cameras and idle gameplay do not run the effect. Physical shake, fall speed and rotation remain controlled by XR Explosion Shake and XR Shock Fall Sequence.
