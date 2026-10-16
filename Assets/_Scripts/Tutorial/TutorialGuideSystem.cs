using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Event-driven sequence of world-space UI sprite cards and scene actions.</summary>
public sealed class TutorialGuideSystem : MonoBehaviour
{
    [System.Serializable]
    public sealed class Step
    {
        public string name;
        [Tooltip("Event ID that starts this step. Leave empty for the first step when using Begin().")]
        public string startEventId;
        public Sprite guideSprite;
        [Tooltip("Event ID that dismisses this card and allows the sequence to continue.")]
        public string completionEventId;
        [Tooltip("Activated after the card has finished fading in.")]
        public GameObject enableAfterShown;
        [Tooltip("Optional objects to disable when this step starts.")]
        public GameObject[] disableWhenStarted;
    }

    [Header("World canvas")]
    [SerializeField] Image guideImage;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] float fadeInDuration = 0.35f;
    [SerializeField] float fadeOutDuration = 0.35f;
    [Header("Sequence")]
    [SerializeField] Step[] steps = new Step[0];
    [SerializeField] bool beginOnEnable;

    int currentIndex = -1;
    bool busy;
    Coroutine fadeRoutine;
    string listeningCompletionId;
    string dismissedByEventId;
    readonly HashSet<string> startIds = new HashSet<string>();
    readonly Dictionary<string, System.Action> startHandlers = new Dictionary<string, System.Action>();
    public bool CanShowCards => guideImage != null && isActiveAndEnabled;

    /// <summary>Displays one card under an external orchestrator's control; callback runs after fade-in.</summary>
    public void ShowCard(Sprite sprite, System.Action onShown)
    {
        if (!CanShowCards) return;
        StopCompletionListener();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        currentIndex = -1;
        busy = true;
        guideImage.sprite = sprite;
        guideImage.gameObject.SetActive(true);
        fadeRoutine = StartCoroutine(Fade(0f, 1f, fadeInDuration, onShown));
    }

    /// <summary>Dismisses an externally controlled card without advancing configured tutorial steps.</summary>
    public void HideCard()
    {
        StopCompletionListener();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        currentIndex = -1;
        busy = false;
        HideImmediate();
    }

    void Awake()
    {
        if (canvasGroup == null && guideImage != null) canvasGroup = guideImage.GetComponentInParent<CanvasGroup>();
        HideImmediate();
    }

    void OnEnable()
    {
        if (currentIndex < 0 && steps != null && steps.Length > 0) currentIndex = 0;
        startIds.Clear();
        if (steps != null)
            for (int i = 0; i < steps.Length; i++)
                if (!string.IsNullOrEmpty(steps[i].startEventId) && startIds.Add(steps[i].startEventId))
                {
                    string id = steps[i].startEventId;
                    System.Action handler = () => OnStartEvent(id);
                    startHandlers.Add(id, handler);
                    TutorialEventBus.Listen(id, handler);
                }
        if (beginOnEnable) Begin();
    }

    void OnDisable()
    {
        foreach (var pair in startHandlers) TutorialEventBus.StopListening(pair.Key, pair.Value);
        startHandlers.Clear();
        StopCompletionListener();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        busy = false;
        HideImmediate();
    }

    void OnStartEvent(string eventId)
    {
        if (busy || currentIndex < 0 || currentIndex >= steps.Length) return;
        if (string.Equals(eventId, steps[currentIndex].startEventId, System.StringComparison.Ordinal)) BeginCurrentStep();
    }

    /// <summary>Timeline/UnityEvent entry point for the first step.</summary>
    public void Begin()
    {
        if (steps == null || steps.Length == 0) return;
        currentIndex = 0;
        BeginCurrentStep();
    }

    /// <summary>Starts a specific zero-based step from Timeline or another system.</summary>
    public void BeginStep(int index)
    {
        if (steps == null || index < 0 || index >= steps.Length) return;
        currentIndex = index;
        BeginCurrentStep();
    }

    /// <summary>Starts the next matching step by its configured exact start ID.</summary>
    public void BeginById(string eventId)
    {
        if (busy || steps == null || string.IsNullOrEmpty(eventId)) return;
        for (int i = 0; i < steps.Length; i++)
        {
            if (!string.Equals(steps[i].startEventId, eventId, System.StringComparison.Ordinal)) continue;
            currentIndex = i;
            BeginCurrentStep();
            return;
        }
    }

    void BeginCurrentStep()
    {
        if (busy || currentIndex < 0 || currentIndex >= steps.Length) return;
        busy = true;
        Step step = steps[currentIndex];
        if (step.disableWhenStarted != null)
            foreach (GameObject target in step.disableWhenStarted) if (target != null) target.SetActive(false);
        if (guideImage != null) guideImage.sprite = step.guideSprite;
        if (guideImage != null) guideImage.gameObject.SetActive(true);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(0f, 1f, fadeInDuration, OnShown));
    }

    void OnShown()
    {
        Step step = steps[currentIndex];
        if (step.enableAfterShown != null) step.enableAfterShown.SetActive(true);
        listeningCompletionId = step.completionEventId;
        if (!string.IsNullOrEmpty(listeningCompletionId)) TutorialEventBus.Listen(listeningCompletionId, OnCompletionEvent);
        else busy = false;
    }

    void OnCompletionEvent()
    {
        if (!busy) return;
        dismissedByEventId = listeningCompletionId;
        StopCompletionListener();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(1f, 0f, fadeOutDuration, OnDismissed));
    }

    void OnDismissed()
    {
        if (guideImage != null) guideImage.gameObject.SetActive(false);
        currentIndex++;
        busy = false;
        if (currentIndex >= steps.Length) { currentIndex = -1; return; }
        // A single exact event may intentionally dismiss one card and start the next.
        if (!string.IsNullOrEmpty(dismissedByEventId) &&
            string.Equals(steps[currentIndex].startEventId, dismissedByEventId, System.StringComparison.Ordinal))
        {
            dismissedByEventId = null;
            BeginCurrentStep();
        }
    }

    void StopCompletionListener()
    {
        if (!string.IsNullOrEmpty(listeningCompletionId)) TutorialEventBus.StopListening(listeningCompletionId, OnCompletionEvent);
        listeningCompletionId = null;
    }

    IEnumerator Fade(float from, float to, float duration, System.Action completed)
    {
        if (canvasGroup == null) { completed?.Invoke(); yield break; }
        canvasGroup.alpha = from;
        if (duration <= 0f) { canvasGroup.alpha = to; completed?.Invoke(); yield break; }
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        canvasGroup.alpha = to;
        completed?.Invoke();
    }

    void HideImmediate()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (guideImage != null) guideImage.gameObject.SetActive(false);
    }
}

