using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

/// <summary>
/// Open-ground level setup (Lvl 3): floor tracking, stick walking with gravity on uneven
/// ground, teleport onto the ground mesh only, and VR controller models (optional hands).
/// Same rules as the Lvl 2 elevator scene, without the elevator.
/// </summary>
public sealed class XrLevelPlaySetup : MonoBehaviour
{
    const float BodyHeight = 1.75f;
    const float SkinWidth = 0.02f;

    [Header("Hands / controllers")]
    [Tooltip("Off: show the normal VR controller models (recommended for Lvl 3).")]
    [SerializeField] bool showHandsOnControllers;
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("Wrist position on the controller (right hand; left is mirrored on X).")]
    [SerializeField] Vector3 handOnControllerPosition = new Vector3(0.025f, -0.015f, -0.06f);
    [Tooltip("Extra wrist rotation on the controller, degrees.")]
    [SerializeField] Vector3 handOnControllerEuler = Vector3.zero;

    [Header("Ground")]
    [Tooltip("Objects whose meshes are walkable and teleportable. Empty = Topology / Terrain / TerrainProxy.")]
    [SerializeField] Transform[] groundRoots;
    [Tooltip("Steeper ground than this blocks walking and turns the teleport reticle red.")]
    [Range(10f, 80f)] [SerializeField] float maxSlopeDegrees = 40f;
    [SerializeField] bool placePlayerOnGround = true;

    [Header("Guide")]
    [Tooltip("Objects that get the blue \"go here\" beacon (hidden once one of their launch buttons is pressed).")]
    [SerializeField] string[] beaconObjectNames = { "ShootMissile (1)" };
    [Tooltip("Persian font for the hint above the right controller.")]
    [SerializeField] TMPro.TMP_FontAsset handHintFont;
    [Tooltip("Hint above the right controller until the first launch button is pressed. Empty = no hint.")]
    [SerializeField] string launchHintMessage = "برو روی دکمه لانچر بزن";

    [Header("Music")]
    [Tooltip("Looped level music. Empty = Resources/Audio/Lvl3Music.")]
    [SerializeField] AudioClip music;
    [Range(0f, 1f)] [SerializeField] float musicVolume = 0.35f;
    [SerializeField] AudioClip missileLaunchSound;

    XROrigin origin;
    HandHintBanner launchHint;

    void Start()
    {
        PlayMusic();
        origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) return;

        // The first-scene auto setup pins the body to one height and adds a flat deck.
        // Uneven ground needs neither.
        BridgeXrPlaySetup.SuspendDeckLock(origin);
        var deck = GameObject.Find("Walkable Deck");
        if (deck != null) deck.SetActive(false);

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;
        LayerMask groundMask = 1 << groundLayer;

        UseOnlyHeadsetCamera();
        PrepareGround(groundLayer);
        SetupBody(groundMask);
        SetupTeleportRays(groundMask);
        if (placePlayerOnGround) PlantOnGround(groundMask);

        var rig = GetComponent<XrHandsRig>();
        if (rig == null) rig = gameObject.AddComponent<XrHandsRig>();
        rig.Configure(leftHandVisualPrefab, rightHandVisualPrefab, handOnControllerPosition, handOnControllerEuler);
        rig.UseControllerModels(!showHandsOnControllers);
        rig.Setup(origin);

        // Launch panels: any button still without a target gets the nearest missile / Shahed.
        MissileLaunchButton.AutoWireScene();
        LaserWeaponScope.SetupByName(missileLaunchSound, handHintFont, "LaserWeapon", "SM_LaserWeapon");

        if (beaconObjectNames != null)
        {
            foreach (var beaconName in beaconObjectNames)
            {
                if (string.IsNullOrWhiteSpace(beaconName)) continue;
                var target = GameObject.Find(beaconName);
                if (target == null)
                {
                    Debug.LogWarning("XrLevelPlaySetup: no '" + beaconName + "' for the go-here beacon.", this);
                    continue;
                }
                if (target.GetComponent<GoHereBeacon>() == null) target.AddComponent<GoHereBeacon>();
            }
        }

        ShowLaunchHint();
    }

    void PlayMusic()
    {
        var clip = music != null ? music : Resources.Load<AudioClip>("Audio/Lvl3Music");
        if (clip == null) return;
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = musicVolume;
        source.Play();
    }

    void ShowLaunchHint()
    {
        if (string.IsNullOrWhiteSpace(launchHintMessage) || origin.Camera == null) return;
        var hand = XrHandsRig.FindNamed(origin, "Right Controller");
        if (hand == null) return;

        launchHint = HandHintBanner.Create(origin.Camera, handHintFont, launchHintMessage);
        launchHint.SetHand(hand);

        // Gone as soon as any launch button is pressed.
        foreach (var button in FindObjectsByType<MissileLaunchButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            button.onPressed.AddListener(HideLaunchHint);
    }

    void HideLaunchHint()
    {
        if (launchHint != null) launchHint.Hide();
        launchHint = null;
        foreach (var button in FindObjectsByType<MissileLaunchButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            button.onPressed.RemoveListener(HideLaunchHint);
    }

    void OnValidate()
    {
        // Play mode: tune the hand position on the controller live from the inspector.
        if (!Application.isPlaying) return;
        var rig = GetComponent<XrHandsRig>();
        if (rig != null) rig.SetWristOffset(handOnControllerPosition, handOnControllerEuler);
    }

    void OnDestroy()
    {
        if (launchHint != null) launchHint.Hide();
    }

    /// <summary>
    /// Only the XR Origin headset camera may render to the HMD. Extra scene cameras tagged
    /// MainCamera, or with Target Eye = Both (e.g. Camera Lase), steal the VR view.
    /// Scope cameras are demoted to render-texture only (no stereo, no audio listener).
    /// </summary>
    void UseOnlyHeadsetCamera()
    {
        var headset = origin != null ? origin.Camera : null;
        if (headset == null)
        {
            Debug.LogWarning("XrLevelPlaySetup: XR Origin has no Camera — VR view will be wrong.", this);
            return;
        }

        headset.tag = "MainCamera";
        headset.stereoTargetEye = StereoTargetEyeMask.Both;
        headset.enabled = true;
        var headsetListener = headset.GetComponent<AudioListener>();
        if (headsetListener != null) headsetListener.enabled = true;

        var headsetData = headset.GetComponent<UniversalAdditionalCameraData>();
        if (headsetData != null) headsetData.allowXRRendering = true;

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == null || cam == headset) continue;
            if (!cam.gameObject.scene.IsValid()) continue;

            // Scope / weapon cameras: never write into the headset.
            var isScope = cam.name.IndexOf("Lase", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          cam.name.IndexOf("Laser", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          cam.name.IndexOf("Scope", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (isScope)
            {
                DemoteToRenderTextureCamera(cam);
                continue;
            }

            // Orphan editor Main Camera and any other stereo camera without a target texture.
            var stealsHeadset = cam.targetTexture == null &&
                                (cam.CompareTag("MainCamera") || cam.stereoTargetEye != StereoTargetEyeMask.None);
            if (!stealsHeadset) continue;

            cam.enabled = false;
            if (cam.CompareTag("MainCamera")) cam.tag = "Untagged";
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.allowXRRendering = false;
        }
    }

    static void DemoteToRenderTextureCamera(Camera cam)
    {
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.tag = "Untagged";
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            data.allowXRRendering = false;
            data.renderType = CameraRenderType.Base;
        }
        // LaserWeaponScope enables it and assigns the render texture when ready.
        if (cam.targetTexture == null) cam.enabled = false;
    }

    IEnumerable<Transform> GroundRoots()
    {
        if (groundRoots != null && groundRoots.Length > 0)
        {
            foreach (var root in groundRoots)
                if (root != null) yield return root;
            yield break;
        }

        foreach (var name in new[] { "Topology", "TerrainProxy", "Terrain" })
        {
            var found = GameObject.Find(name);
            if (found != null) yield return found.transform;
        }

        foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            yield return terrain.transform;
    }

    void PrepareGround(int groundLayer)
    {
        foreach (var root in GroundRoots())
        {
            var hasCollider = false;

            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain.GetComponent<TerrainCollider>() == null)
                {
                    var tc = terrain.gameObject.AddComponent<TerrainCollider>();
                    tc.terrainData = terrain.terrainData;
                }
            }

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;
                // Topology.fbx imports with colliders; this only covers meshes that still lack one.
                var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
            }

            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                col.gameObject.layer = groundLayer;
                hasCollider = true;
            }

            if (!hasCollider) continue;

            // One area per root collects every collider under it.
            var area = root.GetComponent<TeleportationArea>();
            if (area == null) area = root.gameObject.AddComponent<TeleportationArea>();
            area.enabled = true;
            area.interactionLayers = -1;
            area.filterSelectionByHitNormal = true;
            area.upNormalToleranceDegrees = maxSlopeDegrees;
        }
    }

    void SetupBody(LayerMask groundMask)
    {
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        if (origin.CameraFloorOffsetObject != null)
        {
            var offset = origin.CameraFloorOffsetObject.transform.localPosition;
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, 0f, offset.z);
        }

        var defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0) defaultLayer = 0;

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.enabled = true;
            body.height = BodyHeight;
            body.radius = 0.2f;
            body.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
            body.stepOffset = 0.3f;
            body.slopeLimit = maxSlopeDegrees;
            body.skinWidth = SkinWidth;
            body.includeLayers = groundMask | (1 << defaultLayer);
        }

        foreach (var gravity in origin.GetComponentsInChildren<GravityProvider>(true))
        {
            gravity.enabled = true;
            gravity.useGravity = true;
            gravity.useLocalSpaceGravity = false;
            gravity.sphereCastLayerMask = groundMask;
            gravity.sphereCastDistanceBuffer = 0.25f;
        }

        foreach (var jump in origin.GetComponentsInChildren<JumpProvider>(true))
            jump.enabled = false;

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move != null)
        {
            move.enabled = true;
            move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
            move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            move.enableFly = false;
        }
    }

    void SetupTeleportRays(LayerMask groundMask)
    {
        // Ground only: props and launchers in front of the ground would turn the reticle red.
        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport")) continue;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = groundMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }

        foreach (var provider in origin.GetComponentsInChildren<TeleportationProvider>(true))
            provider.enabled = true;
    }

    void PlantOnGround(LayerMask groundMask)
    {
        var start = origin.transform.position + Vector3.up * 50f;
        if (!Physics.Raycast(start, Vector3.down, out var hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        var body = origin.GetComponent<CharacterController>();
        var wasEnabled = body != null && body.enabled;
        if (body != null) body.enabled = false;
        var p = origin.transform.position;
        origin.transform.position = new Vector3(p.x, hit.point.y + SkinWidth, p.z);
        if (body != null) body.enabled = wasEnabled;
    }
}
