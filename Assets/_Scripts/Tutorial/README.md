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
