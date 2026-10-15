# Missile launcher setup

1. Add `MissileLauncherController` to the vehicle or launcher root and assign its `container`.
2. Add one `MissileSlot` per missile prefab. Each slot can use its own `launchPoint`; if omitted, the container is used.
3. Assign the first VR input action to `rotateAction` and the second VR input action to `fireAction`. The same actions can instead call `RotateContainer()` and `LaunchMissiles()` through a UnityEvent.
4. Set `localRotationAxis`, `rotationDegrees`, and `rotationDuration` for phase one. Rotation raises `rotationCompleted` when finished.
5. Set `localSkyDirection`, `skyDistance`, `arcHeight`, and `flightCurve` for phase two. `requireRotationPhase` prevents firing before the first phase has completed.
6. Add `MissileProjectile` to every missile prefab. Assign launch particles, audio arrays, and an optional launch-light prefab. The script instantiates each light at the missile pivot plus `launchLightOffset`, without parenting it to the missile.
7. Add `MissileLaunchLight` to the light prefab and assign a child `Light` and `SpriteRenderer`. Its intensity and sprite alpha tremble between the configured thresholds while the sprite faces the main camera.

`ResetLauncher()` returns the container to its starting rotation and allows the rotation phase to run again.
