using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace VRGallery
{
    /// <summary>
    /// Put this on a far board with world-space UI buttons. Each button opens
    /// a following VR frame with either its video or its two pictures.
    /// </summary>
    public class PictureFrameGallery : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Just for your own organization in the Inspector.")]
            public string label = "Frame";
            public Button openButton;
            [Tooltip("Frame for two pictures; also used for a single item if Single Frame is empty.")]
            public VRFollowFrame frame;
            [Tooltip("Optional separate one-panel frame for a single picture or video.")]
            public VRFollowFrame singleFrame;
            [InspectorName("Left Texture")]
            public Texture left_Picture;
            [InspectorName("Right Texture")]
            public Texture right_Picture;
            [Tooltip("Optional. When assigned, the video is shown instead of the pictures.")]
            public VideoClip videoClip;
#if UNITY_EDITOR
            [Tooltip("Editor Play Mode shortcut for this entry. None disables it. Use a different key for each entry.")]
            public Key editorTestKey = Key.None;
#endif
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        [Header("Behaviour")]
        [Tooltip("Opening a frame closes the others.")]
        [SerializeField] private bool onlyOneAtATime = true;
        [Tooltip("Pressing the button of an already open frame closes it.")]
        [SerializeField] private bool clickAgainToClose = true;
        [Tooltip("Optional button that closes every open frame.")]
        [SerializeField] private Button closeAllButton;

#if UNITY_EDITOR
        [Header("Editor Keyboard Test")]
        [Tooltip("In Play Mode, focus the Game view and press an entry's Editor Test Key to use its open/close behavior. Excluded from builds.")]
        [SerializeField] private bool enableEditorKeyboardTest = false;

        private void Update()
        {
            if (!enableEditorKeyboardTest || !Application.isFocused) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.editorTestKey == Key.None) continue;
                if (!keyboard[entry.editorTestKey].wasPressedThisFrame) continue;
                OnFrameButtonPressed(i);
                // Only one entry per press, including accidentally duplicated shortcuts.
                break;
            }
        }
#endif

        private struct ButtonBinding
        {
            public Button button;
            public UnityAction action;
        }

        private readonly List<ButtonBinding> bindings = new List<ButtonBinding>();
        private int activeEntryIndex = -1;

        private void OnEnable()
        {
            bindings.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.openButton == null) continue;
                int index = i;
                UnityAction handler = () => OnFrameButtonPressed(index);
                bindings.Add(new ButtonBinding { button = entry.openButton, action = handler });
                entry.openButton.onClick.AddListener(handler);
            }

            if (closeAllButton != null) closeAllButton.onClick.AddListener(CloseAll);
        }

        private void OnDisable()
        {
            foreach (ButtonBinding binding in bindings)
            {
                if (binding.button != null)
                    binding.button.onClick.RemoveListener(binding.action);
            }

            bindings.Clear();
            if (closeAllButton != null) closeAllButton.onClick.RemoveListener(CloseAll);
        }

        private void OnFrameButtonPressed(int index)
        {
            if (index < 0 || index >= entries.Count) return;

            Entry entry = entries[index];
            if (entry == null) return;
            bool singleContent = entry.videoClip != null ||
                (entry.left_Picture != null) != (entry.right_Picture != null);
            VRFollowFrame frame = singleContent && entry.singleFrame != null
                ? entry.singleFrame : entry.frame;
            if (frame == null)
            {
                Debug.LogWarning($"[{nameof(PictureFrameGallery)}] Entry {index} ('{entry.label}') needs a frame for its content.", this);
                return;
            }

            if (frame.IsOpen && activeEntryIndex == index)
            {
                if (clickAgainToClose)
                {
                    frame.Close();
                    activeEntryIndex = -1;
                }
                else frame.Recenter();
                return;
            }

            if (onlyOneAtATime) CloseAllExcept(frame);
            if (entry.videoClip != null)
                frame.SetVideo(entry.videoClip);
            else
                frame.SetPictures(entry.left_Picture, entry.right_Picture);
            frame.Open();
            activeEntryIndex = index;
        }

        // ───────────────────────── Public API (also usable from UnityEvents) ─────────────────────────

        public void OpenFrame(int index) => OnFrameButtonPressed(index);

        public void CloseAll()
        {
            CloseAllExcept(null);
            activeEntryIndex = -1;
        }

        private void CloseAllExcept(VRFollowFrame keepFrame)
        {
            var visited = new HashSet<VRFollowFrame>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null) continue;
                CloseIfDifferent(entry.frame, keepFrame, visited);
                CloseIfDifferent(entry.singleFrame, keepFrame, visited);
            }
        }

        private static void CloseIfDifferent(VRFollowFrame frame, VRFollowFrame keep,
            HashSet<VRFollowFrame> visited)
        {
            if (frame != null && frame != keep && visited.Add(frame)) frame.Close();
        }
    }
}
