using Animation.Base;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Animation.TabSystem
{
    [System.Serializable]
    public class UITabToggle: MonoBehaviour//, IPointerEnterHandler, IPointerExitHandler
    {
        //public Button button;
        public Toggle toggle;
        public BaseAnimation panelToggleAnimation;
        public BaseAnimation selectAnimation;
        public string Title;
        public bool isShowing;
        public bool defaultTab;
        private SelectTabEvent onSelect = new SelectTabEvent();
        
        public virtual void Initialize(UnityAction<UITabToggle, bool> onSelect)
        {
            if (panelToggleAnimation != null)
            {
                panelToggleAnimation.gameObject.SetActive(true);
            }
            
            //button.gameObject.SetActive(true);
            toggle.onValueChanged.AddListener(ToggleValueChanged);
            this.onSelect.AddListener(onSelect);
            if (isShowing)
            {
                SelectTab(true);
            }
            else
            {
                DeselectTab(true);
            }
        }

        private void SelectTab()
        {
            SelectTab(false);
        }

        public void ToggleValueChanged(bool value)
        {
            if (toggle.isOn)
            {
                SelectTab();
            }
            else
            {
                DeselectTab();
            }
        }
        public void SelectTab(bool ignoreAnimation)
        {
            
            isShowing = true;
            //Hover();
            if (panelToggleAnimation != null)
            {
                panelToggleAnimation.gameObject.SetActive(true);
                panelToggleAnimation.Play(AnimationDirection.Forward, ignoreAnimation);
            }
            onSelect.Invoke(this, ignoreAnimation);
            //button.image.color = selectedColor;
            //var colors = button.colors;
            if (selectAnimation != null)
            {
                selectAnimation.Play(AnimationDirection.Forward, ignoreAnimation ? AnimPlayMode.Suddenly : AnimPlayMode.Lerp);
            }
            //button.colors = colors;
            
        }

        public void DeselectTab(bool ignoreAnimation = false)
        {
            isShowing = false;
            //Blur();
            if (panelToggleAnimation != null)
            {
                panelToggleAnimation.Play(AnimationDirection.Backward, ignoreAnimation, () => {
                    panelToggleAnimation.gameObject.SetActive(false);
                });
            }
            //button.image.color = normalColor;
            //var colors = button.colors;
            if (selectAnimation != null)
            {
                selectAnimation.Play(AnimationDirection.Backward, ignoreAnimation ? AnimPlayMode.Suddenly : AnimPlayMode.Lerp);
            }
            //button.colors = colors;
        }

        public void AddSelectListener(UnityAction<UITabToggle, bool> listener)
        {
            onSelect.AddListener(listener);
        }

        public void RemoveSelectListener(UnityAction<UITabToggle, bool> listener)
        {
            onSelect.RemoveListener(listener);
        }

        // public void OnPointerEnter(PointerEventData eventData)
        // {
        //     Hover();
        // }
        //
        // public void OnPointerExit(PointerEventData eventData)
        // {
        //     if (!isShowing)
        //     {
        //         Blur();
        //     }
        // }
        // private void Hover()
        // {
        //     button.GetComponentInChildren<Text>().color = selectColor;
        //
        // }
        // private void Blur()
        // {
        //     button.GetComponentInChildren<Text>().color = normalColor;
        //
        // }

        public class SelectTabEvent : UnityEvent<UITabToggle, bool> { }
    }

}