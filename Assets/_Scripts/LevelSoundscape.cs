using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LevelSoundscape : MonoBehaviour
{
    [Header("State machine cues")]
    [SerializeField] Lvl1BuildLevelStateMachine firstExplosionSequence;
    [SerializeField] ElevatorDoorIntro elevator;
    [SerializeField] PersistentLevelMusic firstExplosionMusic;
    [SerializeField] PersistentLevelMusic nextLevelMusic;
    bool transitionStarted;

    [Header("Fighter jet reaction")]
    [SerializeField] AudioSource fighterJetReaction;
    [SerializeField, Min(0f)] float fighterJetReactionDelaySeconds = 1f;

    [Header("Level ambient")]
    [SerializeField] AudioSource ambient;
    [SerializeField, Min(0f)] float ambientFadeSeconds = 1.5f;

    [Header("Opening dialogue")]
    [SerializeField] AudioSource dialogue;
    [SerializeField, Min(0f)] float dialogueDelaySeconds = 10f;
    Coroutine openingDialogueRoutine;
    bool openingDialogueCancelled;
    bool openingDialogueStarted;
    Lvl1Tutorial openingTutorial;

    void OnEnable()
    {
        openingTutorial = firstExplosionSequence != null ? firstExplosionSequence.Tutorial : null;
        if (openingTutorial != null) openingTutorial.Started += BeginOpeningDialogue;
        if (firstExplosionSequence != null) firstExplosionSequence.FirstExplosionPlayed += OnFirstExplosion;
        if (firstExplosionSequence != null) firstExplosionSequence.FighterJetPlayed += OnFighterJet;
        if (elevator != null) elevator.DoorsOpened += OnDoorsOpened;
        if (elevator != null) elevator.TransitionStarted += OnTransitionStarted;
    }

    void Start()
    {
        if (ambient != null)
        {
            ambient.loop = true;
            ambient.Stop();
            if (elevator == null || elevator.AreDoorsOpenForPlayer) StartCoroutine(FadeInAmbient());
        }
        if (openingTutorial == null) BeginOpeningDialogue();
    }

    void BeginOpeningDialogue()
    {
        if (openingDialogueStarted || openingDialogueCancelled || dialogue == null || dialogue.clip == null) return;
        openingDialogueStarted = true;
        openingDialogueRoutine = StartCoroutine(PlayDialogue());
    }

    void OnFirstExplosion() => firstExplosionMusic?.Play();

    void OnFighterJet()
    {
        openingDialogueCancelled = true;
        if (openingDialogueRoutine != null) StopCoroutine(openingDialogueRoutine);
        openingDialogueRoutine = null;
        if (dialogue != null) dialogue.Stop();
        if (fighterJetReaction != null && fighterJetReaction.clip != null) StartCoroutine(PlayFighterJetReaction());
    }

    IEnumerator PlayFighterJetReaction()
    {
        if (fighterJetReactionDelaySeconds > 0f) yield return new WaitForSecondsRealtime(fighterJetReactionDelaySeconds);
        fighterJetReaction.Play();
    }

    void OnTransitionStarted()
    {
        if (transitionStarted || nextLevelMusic == null) return;
        transitionStarted = true;
        elevator.FadeOutTunnelMusic();
        nextLevelMusic.Play();
    }

    void OnDoorsOpened()
    {
        PersistentLevelMusic.FadeOutActive();
        if (ambient != null) StartCoroutine(FadeInAmbient());
    }

    IEnumerator FadeInAmbient()
    {
        if (ambient.clip == null) yield break;
        var target = ambient.volume;
        ambient.volume = 0f;
        ambient.Play();
        for (var elapsed = 0f; elapsed < ambientFadeSeconds; elapsed += Time.unscaledDeltaTime)
        {
            ambient.volume = Mathf.Lerp(0f, target, elapsed / ambientFadeSeconds);
            yield return null;
        }
        ambient.volume = target;
    }

    IEnumerator PlayDialogue()
    {
        if (dialogueDelaySeconds > 0f) yield return new WaitForSecondsRealtime(dialogueDelaySeconds);
        dialogue.loop = false;
        dialogue.Play();
        openingDialogueRoutine = null;
    }

    void OnDisable()
    {
        if (openingTutorial != null) openingTutorial.Started -= BeginOpeningDialogue;
        if (firstExplosionSequence != null) firstExplosionSequence.FirstExplosionPlayed -= OnFirstExplosion;
        if (firstExplosionSequence != null) firstExplosionSequence.FighterJetPlayed -= OnFighterJet;
        if (elevator != null) elevator.DoorsOpened -= OnDoorsOpened;
        if (elevator != null) elevator.TransitionStarted -= OnTransitionStarted;
        StopAllCoroutines();
        openingDialogueRoutine = null;
        if (ambient != null) ambient.Stop();
        if (dialogue != null) dialogue.Stop();
        if (fighterJetReaction != null) fighterJetReaction.Stop();
    }
}
