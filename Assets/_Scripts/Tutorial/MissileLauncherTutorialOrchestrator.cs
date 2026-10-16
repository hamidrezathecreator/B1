using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Shows cards, gates physical buttons, and waits for the entire launcher group between steps.</summary>
public sealed class MissileLauncherTutorialOrchestrator : MonoBehaviour
{
    public enum TutorialPhase { Idle, ShowingRotateCard, AwaitingRotate, Rotating, ShowingFireCard, AwaitingFire, Firing, Completed, Failed }

    [SerializeField] MissileLauncherButtons buttons;
    [SerializeField] MissileLauncherActions actions;
    [Header("Existing world-space UI")]
    [Tooltip("CanvasGroup on your already configured rotation card. Only its alpha is changed.")]
    [SerializeField] CanvasGroup rotateCard;
    [Tooltip("CanvasGroup on your already configured fire card. Only its alpha is changed.")]
    [SerializeField] CanvasGroup fireCard;
    [SerializeField, Min(0f)] float fadeInDuration = 0.35f;
    [SerializeField, Min(0f)] float fadeOutDuration = 0.35f;
    [SerializeField] bool beginOnStart = true;
    public UnityEvent tutorialCompleted = new UnityEvent();
    public UnityEvent tutorialFailed = new UnityEvent();
    public TutorialPhase Phase { get; private set; }
    Coroutine cardRoutine;

    void Awake() => HideCardsImmediate();

    void OnEnable()
    {
        if (buttons != null)
        {
            buttons.DisableButtons();
            buttons.rotatePressed.AddListener(OnRotatePressed);
            buttons.firePressed.AddListener(OnFirePressed);
        }
        if (actions != null)
        {
            actions.allRotationsCompleted.AddListener(OnAllRotationsCompleted);
            actions.allLaunchSequencesCompleted.AddListener(OnAllLaunchSequencesCompleted);
            actions.actionFailed.AddListener(Fail);
        }
    }

    // Start runs after all scene buttons and guide components have initialized.
    void Start() { if (beginOnStart) Begin(); }

    [ContextMenu("Test/Begin or Restart Tutorial (Play Mode)")]
    public void Begin()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        if (buttons == null || !buttons.isActiveAndEnabled || !buttons.IsConfigured || actions == null || !actions.isActiveAndEnabled ||
            rotateCard == null || fireCard == null || rotateCard == fireCard ||
            !rotateCard.gameObject.activeInHierarchy || !fireCard.gameObject.activeInHierarchy)
        {
            Debug.LogError("Assign the button adapter, launcher actions, and two distinct, active card CanvasGroups from your existing world-space UI.", this);
            Fail();
            return;
        }
        buttons.DisableButtons();
        actions.ResetAll();
        StopCardFade();
        HideCardsImmediate();
        Phase = TutorialPhase.ShowingRotateCard;
        TransitionCards(rotateCard, () =>
        {
            if (!isActiveAndEnabled || Phase != TutorialPhase.ShowingRotateCard) return;
            Phase = TutorialPhase.AwaitingRotate;
            buttons.EnableRotate();
        });
    }

    void OnRotatePressed()
    {
        if (Phase != TutorialPhase.AwaitingRotate) return;
        Phase = TutorialPhase.Rotating;
        buttons.DisableButtons();
        TransitionCards(null);
        actions.RotateAll();
    }

    void OnAllRotationsCompleted()
    {
        if (Phase != TutorialPhase.Rotating) return;
        Phase = TutorialPhase.ShowingFireCard;
        TransitionCards(fireCard, () =>
        {
            if (!isActiveAndEnabled || Phase != TutorialPhase.ShowingFireCard) return;
            Phase = TutorialPhase.AwaitingFire;
            buttons.EnableFire();
        });
    }

    void OnFirePressed()
    {
        if (Phase != TutorialPhase.AwaitingFire) return;
        Phase = TutorialPhase.Firing;
        buttons.DisableButtons();
        TransitionCards(null);
        actions.FireAll();
    }

    void OnAllLaunchSequencesCompleted()
    {
        if (Phase != TutorialPhase.Firing) return;
        Phase = TutorialPhase.Completed;
        tutorialCompleted.Invoke();
    }

    void Fail()
    {
        Phase = TutorialPhase.Failed;
        if (buttons != null) buttons.DisableButtons();
        TransitionCards(null);
        tutorialFailed.Invoke();
    }

    void TransitionCards(CanvasGroup shownCard, System.Action onShown = null)
    {
        StopCardFade();
        if (isActiveAndEnabled) cardRoutine = StartCoroutine(FadeCards(shownCard, onShown));
        else HideCardsImmediate();
    }

    IEnumerator FadeCards(CanvasGroup shownCard, System.Action onShown)
    {
        float rotateStart = rotateCard != null ? rotateCard.alpha : 0f;
        float fireStart = fireCard != null ? fireCard.alpha : 0f;
        float rotateDuration = rotateCard == shownCard ? fadeInDuration : fadeOutDuration;
        float fireDuration = fireCard == shownCard ? fadeInDuration : fadeOutDuration;
        float elapsed = 0f;
        float duration = Mathf.Max(rotateDuration, fireDuration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            FadeCard(rotateCard, shownCard, rotateStart, elapsed, rotateDuration);
            FadeCard(fireCard, shownCard, fireStart, elapsed, fireDuration);
            yield return null;
        }
        if (rotateCard != null) rotateCard.alpha = rotateCard == shownCard ? 1f : 0f;
        if (fireCard != null) fireCard.alpha = fireCard == shownCard ? 1f : 0f;
        cardRoutine = null;
        onShown?.Invoke();
    }

    static void FadeCard(CanvasGroup card, CanvasGroup shownCard, float from, float elapsed, float duration)
    {
        if (card != null)
            card.alpha = Mathf.Lerp(from, card == shownCard ? 1f : 0f, duration <= 0f ? 1f : elapsed / duration);
    }

    void StopCardFade()
    {
        if (cardRoutine != null) StopCoroutine(cardRoutine);
        cardRoutine = null;
    }

    void HideCardsImmediate()
    {
        if (rotateCard != null) rotateCard.alpha = 0f;
        if (fireCard != null) fireCard.alpha = 0f;
    }

    void OnDisable()
    {
        if (buttons != null)
        {
            buttons.DisableButtons();
            buttons.rotatePressed.RemoveListener(OnRotatePressed);
            buttons.firePressed.RemoveListener(OnFirePressed);
        }
        if (actions != null)
        {
            actions.allRotationsCompleted.RemoveListener(OnAllRotationsCompleted);
            actions.allLaunchSequencesCompleted.RemoveListener(OnAllLaunchSequencesCompleted);
            actions.actionFailed.RemoveListener(Fail);
        }
        StopCardFade();
        HideCardsImmediate();
        Phase = TutorialPhase.Idle;
    }
}
