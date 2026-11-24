using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ControllerHandsPresentation : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float vibrationAmplitude = 0.5f;
    [SerializeField, Min(0f)] float vibrationSeconds = 0.3f;

    static bool handsUnlocked;
    XROrigin origin;
    XrHandsRig hands;
    bool completingTutorial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => handsUnlocked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != BuildLevelScenes.Lvl1 && scene.name != BuildLevelScenes.Lvl2 &&
            scene.name != BuildLevelScenes.Lvl3) return;

        if (scene.name == BuildLevelScenes.Lvl1) handsUnlocked = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            var sceneOrigin = root.GetComponentInChildren<XROrigin>();
            if (sceneOrigin == null) continue;
            if (sceneOrigin.GetComponent<ControllerHandsPresentation>() == null)
                sceneOrigin.gameObject.AddComponent<ControllerHandsPresentation>();
            break;
        }
    }

    IEnumerator Start()
    {
        origin = GetComponent<XROrigin>();
        yield return null;
        if (origin == null) yield break;

        var template = Resources.Load<XrHandsRig>("ControllerHandsRig");
        if (template == null)
        {
            Debug.LogError("ControllerHandsPresentation: missing Resources/ControllerHandsRig prefab.", this);
            yield break;
        }

        hands = Instantiate(template, origin.transform);
        hands.PreferHandTrackingWhenAvailable(false);
        hands.UseControllerModels(!handsUnlocked);
        hands.Setup(origin);
        foreach (var handName in new[] { "Left Hand", "Right Hand" })
        {
            var trackedHand = XrHandsRig.FindNamed(origin, handName);
            if (trackedHand != null) trackedHand.gameObject.SetActive(false);
        }
    }

    public void CompleteTutorialAfterDelay(float delaySeconds)
    {
        if (completingTutorial || handsUnlocked) return;
        completingTutorial = true;
        StartCoroutine(CompleteTutorial(delaySeconds));
    }

    IEnumerator CompleteTutorial(float delaySeconds)
    {
        if (delaySeconds > 0f) yield return new WaitForSecondsRealtime(delaySeconds);

        ControllerHaptics.Pulse(vibrationAmplitude, vibrationSeconds);

        if (vibrationSeconds > 0f) yield return new WaitForSecondsRealtime(vibrationSeconds);
        handsUnlocked = true;
        if (hands == null || origin == null) yield break;
        hands.UseControllerModels(false);
        hands.Setup(origin);
    }
}
