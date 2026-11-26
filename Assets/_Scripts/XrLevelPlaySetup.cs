using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
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

    [Header("Ground")]
    [Tooltip("Scene-authored walkable ground roots with colliders and teleportation areas.")]
    [SerializeField] Transform[] groundRoots;
    [Tooltip("Steeper ground than this blocks walking and turns the teleport reticle red.")]
    [Range(10f, 80f)] [SerializeField] float maxSlopeDegrees = 40f;
    [SerializeField] Transform playerSpawn;

    [Header("Guide")]
    [Tooltip("Persian font for the hint above the right controller.")]
    [SerializeField] TMPro.TMP_FontAsset handHintFont;
    [Tooltip("Hint above the right controller until both tutorial launch buttons are pressed. Empty = no hint.")]
    [SerializeField] string launchHintMessage = "برو روی دکمه لانچر بزن";

    [Header("Music")]
    [Tooltip("Optional clip override for the assigned scene music source.")]
    [SerializeField] AudioClip music;
    [Range(0f, 1f)] [SerializeField] float musicVolume = 0.35f;
    [SerializeField] AudioClip missileLaunchSound;
    [SerializeField] AudioClip missileImpactSound;

    [Header("Scene references")]
    [SerializeField] XROrigin origin;
    [SerializeField] Transform rightController;
    [SerializeField] XRRayInteractor[] teleportRays;
    [SerializeField] Camera[] scopeCameras;
    [SerializeField] Camera[] disabledCameras;
    [SerializeField] MissileLauncherButtons tutorialButtons;
    [SerializeField] MissileProjectile[] launchProjectiles;
    [SerializeField] FighterJetFlight[] flights;
    [SerializeField] ExplodeAndSwap[] strikes;
    [SerializeField] TrackerWeaponScope trackerScope;
    [SerializeField] AudioSource musicSource;
    HandHintBanner launchHint;

    void Start()
    {
        if (origin == null || groundRoots == null || groundRoots.Length == 0 || trackerScope == null)
        {
            Debug.LogError("XrLevelPlaySetup: assign the player, authored ground, tracker and interaction references in the scene.", this);
            enabled = false;
            return;
        }
        PlayMusic();
        BridgeXrPlaySetup.SuspendDeckLock(origin);
        var groundLayer = LayerMask.NameToLayer("Ground");
        LayerMask groundMask = 1 << Mathf.Max(0, groundLayer);
        UseOnlyHeadsetCamera();
        SetupBody(groundMask);
        SetupTeleportRays(groundMask);
        if (playerSpawn != null)
        {
            var body = origin.GetComponent<CharacterController>();
            var wasEnabled = body != null && body.enabled;
            if (body != null) body.enabled = false;
            origin.transform.SetPositionAndRotation(playerSpawn.position, playerSpawn.rotation);
            if (body != null) body.enabled = wasEnabled;
        }
        if (launchProjectiles != null)
            foreach (var projectile in launchProjectiles)
                if (projectile != null) projectile.ConfigureLaunchSound(missileLaunchSound);
        if (flights != null)
            foreach (var flight in flights)
                if (flight != null) flight.ConfigureMissileLaunchSound(missileLaunchSound);
        if (strikes != null)
            foreach (var strike in strikes)
                if (strike != null) strike.SetImpactSound(missileImpactSound);
        trackerScope.ConfigureFeedback(missileLaunchSound, missileImpactSound, handHintFont);
        ShowLaunchHint();
    }

    void PlayMusic()
    {
        if (musicSource == null) return;
        if (music != null) musicSource.clip = music;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    void ShowLaunchHint()
    {
        if (string.IsNullOrWhiteSpace(launchHintMessage) || origin.Camera == null) return;
        var hand = rightController;
        if (hand == null) return;

        launchHint = HandHintBanner.Create(origin.Camera, handHintFont, launchHintMessage);
        launchHint.SetHand(hand);

        if (tutorialButtons != null)
        {
            tutorialButtons.rotatePressed.AddListener(OnLaunchButtonPressed);
            tutorialButtons.firePressed.AddListener(OnLaunchButtonPressed);
        }
    }

    void OnLaunchButtonPressed()
    {
        if (tutorialButtons != null && !tutorialButtons.HasEnabledButtons) HideLaunchHint();
    }

    void HideLaunchHint()
    {
        if (launchHint != null)
        {
            try
            {
                launchHint.Hide();
            }
            catch (MissingReferenceException)
            {
                // The hint object can already be destroyed when the scene resets or a button fires late.
            }
        }

        launchHint = null;
        if (tutorialButtons != null)
        {
            tutorialButtons.rotatePressed.RemoveListener(OnLaunchButtonPressed);
            tutorialButtons.firePressed.RemoveListener(OnLaunchButtonPressed);
        }
    }


    void OnDestroy()
    {
        HideLaunchHint();
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

        if (scopeCameras != null)
            foreach (var camera in scopeCameras)
                if (camera != null) DemoteToRenderTextureCamera(camera);

        headset.tag = "MainCamera";
        headset.enabled = true;
        var headsetListener = headset.GetComponent<AudioListener>();
        if (headsetListener != null) headsetListener.enabled = true;

        var headsetData = headset.GetComponent<UniversalAdditionalCameraData>();
        if (headsetData != null)
            headsetData.allowXRRendering = true;
        else if (GraphicsSettings.currentRenderPipeline == null)
            headset.stereoTargetEye = StereoTargetEyeMask.Both;

        foreach (var cam in disabledCameras ?? System.Array.Empty<Camera>())
        {
            if (cam == null || cam == headset) continue;
            if (!cam.gameObject.scene.IsValid()) continue;

            // Scope / weapon cameras: never write into the headset.
            // Orphan editor Main Camera and any other stereo camera without a target texture.
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            var stealsHeadset = cam.targetTexture == null &&
                                (cam.CompareTag("MainCamera") ||
                                 (camData != null
                                     ? camData.allowXRRendering
                                     : cam.stereoTargetEye != StereoTargetEyeMask.None));
            if (!stealsHeadset) continue;

            cam.enabled = false;
            if (cam.CompareTag("MainCamera")) cam.tag = "Untagged";
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            if (camData != null)
                camData.allowXRRendering = false;
            else if (GraphicsSettings.currentRenderPipeline == null)
                cam.stereoTargetEye = StereoTargetEyeMask.None;
        }
    }

    static void DemoteToRenderTextureCamera(Camera cam)
    {
        cam.tag = "Untagged";
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            data.allowXRRendering = false;
            data.renderType = CameraRenderType.Base;
        }
        else if (GraphicsSettings.currentRenderPipeline == null)
            cam.stereoTargetEye = StereoTargetEyeMask.None;

        // Tracker weapon scope enables the camera and assigns the render texture when ready.
        if (cam.targetTexture == null) cam.enabled = false;
    }



    void SetupBody(LayerMask groundMask)
    {
        var settings = origin.GetComponent<XrOriginPlaySettings>();
        var bodyHeight = settings != null ? settings.BodyHeightMeters : BodyHeight;
        if (settings != null)
            settings.Apply();
        else
        {
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;
            if (origin.CameraFloorOffsetObject != null)
            {
                var offset = origin.CameraFloorOffsetObject.transform.localPosition;
                origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, 0f, offset.z);
            }
        }

        var defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0) defaultLayer = 0;

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.enabled = true;
            body.height = bodyHeight;
            body.radius = 0.2f;
            body.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
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

        if (settings == null)
        {
            var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
            if (move != null)
            {
                move.enabled = true;
                move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
                move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
                move.enableFly = false;
            }
        }
    }

    void SetupTeleportRays(LayerMask groundMask)
    {
        // Ground only: props and launchers in front of the ground would turn the reticle red.
        foreach (var ray in teleportRays ?? System.Array.Empty<XRRayInteractor>())
        {
            if (ray == null) continue;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = groundMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }

        foreach (var provider in origin.GetComponentsInChildren<TeleportationProvider>(true))
            provider.enabled = true;
    }

}
