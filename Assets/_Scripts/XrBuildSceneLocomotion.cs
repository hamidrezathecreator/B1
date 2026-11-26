using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Scenes in File → Build Settings: walking always on, play boundary enforcement when volumes exist.
/// </summary>
static class XrBuildSceneLocomotion
{
    static readonly HashSet<string> BuildScenes = new()
    {
        "Lvl_1",
        "Lvl2_MissileTunnel",
        "LVL3_MissileLaunch",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
            OnSceneLoaded(scene, LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!BuildScenes.Contains(scene.name)) return;

        XrAffordanceUtility.FixSceneAffordances();

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null) return;

        EnableWalking(origin);
        if (origin.GetComponent<XrAffordanceRendererFix>() == null)
            origin.gameObject.AddComponent<XrAffordanceRendererFix>();

        var enforcer = origin.GetComponent<XrPlayBoundaryEnforcer>();
        if (enforcer == null)
            enforcer = origin.gameObject.AddComponent<XrPlayBoundaryEnforcer>();
        else
            enforcer.RefreshVolumes();
    }

    public static void EnableWalking(XROrigin origin)
    {
        if (origin == null) return;
        if (HelicopterHover.IsMovementLocked(origin)) return;

        var settings = origin.GetComponent<XrOriginPlaySettings>();
        if (settings != null)
        {
            settings.SetStickWalkingEnabled(true);
            return;
        }

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move == null) return;

        move.enabled = true;
        move.enableStrafe = true;
        move.enableFly = false;
        move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
    }
}
