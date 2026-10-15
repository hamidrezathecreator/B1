using System.Collections;
using Animation.Base;
using UnityEngine;
using UnityEngine.Events;

namespace Animation.Misc
{
    [RequireComponent(typeof(BaseAnimation))]
    public class TogglePanel : MonoBehaviour, ITogglePanel
    {
        public PanelState initialState = PanelState.Default;
        public float autoCloseTimeout = 0f;
        private BaseAnimation anim;
        private bool _currentState = true;
        private UnityEvent onShow = new UnityEvent();
        private UnityEvent onHide = new UnityEvent();
        public TogglePanelWrapper Wrapper { get; set; }
        public bool currentState
        {
            get
            {
                if (initialized == false)
                {
                    return initialState == PanelState.Close ? false : true;
                }
                return _currentState;
            }
            private set
            {
                _currentState = value;
            }
        }
        public float toggleDuration
        {
            get
            {
                return anim.TotalDuration;
            }
        }
        private bool initialized = false;

        private void Awake()
        {
            if (!initialized)
                Initialize();
        }

        public void Show()
        {
            if (Wrapper != null)
            {
                Wrapper.Show();
                return;
            }
            Toggle(true);
        }

        public void Hide(bool force = false)
        {
            if (Wrapper != null)
            {
                Wrapper.Hide(false);
                return;
            }
            Toggle(false, false, force);
        }

        public void Toggle() => Toggle(!currentState);

        public void Toggle(bool state, bool forceSuddenly = false, bool force = false)
        {
            if (Wrapper != null)
            {
                Wrapper.Toggle(state, forceSuddenly, force);
                return;
            }
            var animation = GetAnimation();
            if (state == currentState && !force)
                return;
            currentState = state;
            if (state)
            {
                gameObject.SetActive(true);
            }
            var suddenly = forceSuddenly || !gameObject.activeInHierarchy;
            animation.Play(state ? AnimationDirection.Forward :
                AnimationDirection.Backward, suddenly,
                 () => {
                     if (state)
                     {
                         onShow.Invoke();
                         if (autoCloseTimeout > 0)
                             StartCoroutine(AutoClose());
                     }
                     else
                     {
                         onHide.Invoke();
                         gameObject.SetActive(false);
                     }
                 });
        }

        public void ManualInitialize()
        {
            if (!initialized)
                Initialize();
        }

        public void AddShowListener(UnityAction listener)
        {
            onShow.AddListener(listener);
        }

        public void RemoveShowListener(UnityAction listener)
        {
            onShow.RemoveListener(listener);
        }

        public void AddHideListener(UnityAction listener)
        {
            onHide.AddListener(listener);
        }

        public void RemoveHideListener(UnityAction listener)
        {
            onHide.RemoveListener(listener);
        }

        private void Initialize()
        {
            anim = GetComponent<BaseAnimation>();
            switch (initialState)
            {
                case PanelState.Open:
                    currentState = true;
                    anim.Play(AnimationDirection.Forward, true);
                    break;
                case PanelState.Close:
                    currentState = false;
                    anim.Play(AnimationDirection.Backward, true);
                    gameObject.SetActive(false);
                    break;
                default:
                    currentState = gameObject.activeSelf;
                    break;
            }
            initialized = true;
        }

        private BaseAnimation GetAnimation()
        {
            if (!initialized)
            {
                Initialize();
                return anim;
            }
            else
            {
                return anim;
            }
        }

        private IEnumerator AutoClose()
        {
            yield return new WaitForSeconds(autoCloseTimeout);
            Toggle(false);
        }
        public enum PanelState
        {
            Default = 0,
            Open = 1,
            Close = 2
        }
    }
}
