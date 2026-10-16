# Tutorial guide and VR press button

## Scene setup

1. Create a world-space Canvas with an `Image` and a `CanvasGroup`. Assign the Image to `TutorialGuideSystem.guideImage` and the CanvasGroup to `canvasGroup`.
2. Add `TutorialGuideSystem` to the Canvas (or another scene object). Add one `Step` per card:
   - `startEventId`: exact ID that starts this step. The first step can be started with `Begin()` from a Timeline Signal Receiver.
   - `guideSprite`: sprite displayed by the world-space Image.
   - `completionEventId`: exact ID that dismisses the card after its fade-in completes.
   - `enableAfterShown`: button or other object to activate only after the card is fully visible.
3. Add `VRPressButton` to a physical button with a Collider. Set its `exactEventId`, press direction, distance, and child/visual `pressTransform`. Leave `interactableAtStart` off for tutorial-controlled buttons.
4. Make sure the hand has an XR Poke Interactor and the button has a suitable collider/interaction layer. `VRPressButton` latches down on the first select and publishes its exact ID.

## Wiring the sequence

- Timeline Signal Receiver -> `TutorialGuideSystem.Begin()` starts step 0.
- Step 0's `enableAfterShown` -> button with ID `button.one`.
- Step 0's `completionEventId` -> `button.one`.
- Step 1's `startEventId` can be a different event ID, or the same `button.one` ID when that press should dismiss step 0 and immediately start step 1.
- A button may also call `PublishEvent()` from a UnityEvent when an event should be emitted without XR selection.

IDs are ordinal strings, so `button.one` and `Button.One` are different events. The bus is process-local and components subscribe/unsubscribe automatically with their Unity lifecycle.


## Timeline by ID

Add TutorialEventEmitter to a scene object, set its exactEventId, and bind Raise() to a Timeline Signal Receiver. The signal publishes that ID and starts the matching tutorial step. You can also call TutorialGuideSystem.BeginById(string) from code or a string UnityEvent.

## Missile launcher tutorial orchestration

Use `MissileLauncherTutorialOrchestrator` for rotation/fire tutorials involving one or many launchers. It fades CanvasGroups on your existing rotation/fire cards, uses `MissileLauncherButtons` for gated press events, and `MissileLauncherActions` for group commands and completion barriers. The next card waits for every launcher to finish rotation; it does not advance on the initial button press.

A launcher Inspector's **Create VR Tutorial Setup** command creates the physical buttons, actions, and orchestrator references. Assign CanvasGroups from the two cards in your existing world-space canvas and populate the actions' launcher array. Keep the card objects active: only their alpha is changed, with configurable fade-in/out durations. See `../MissileLauncher/README.md` for setup and event semantics.

The missile orchestrator owns card fades and enables the relevant physical button only after the card is fully visible. It does not require a TutorialGuideSystem. Do not let another guide, animation, or Timeline track control these same card alphas during this sequence. Timeline signals can call the orchestrator's Begin() to start the delayed sequence (turn off Begin After Delay for Timeline-only initiation). Begin Delay defaults to 3 seconds, runs on unscaled time, and applies before the first sound/card.

Assign **Rotate Intro Sound** and **Fire Intro Sound** on the orchestrator to play audio before the corresponding card. The first sound starts after Begin Delay elapses; the second starts only after every assigned launcher finishes rotation. Each sound must finish before its card fades in, and only after the fade does its physical button become interactable. Use a dedicated **Tutorial Audio Source** (defaults to the source on the orchestrator). Empty clips skip playback. Restarting or disabling cancels pending playback and progression.


