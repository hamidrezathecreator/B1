using UnityEngine;
using System.Linq;
using UnityEngine.UI;

namespace Animation.TabSystem
{
    public class UITabsManager : MonoBehaviour
    {
        [SerializeField]
        private UITabToggle[] Tabs;
        [SerializeField]

        private Text titleText;
        private UITabToggle currentTab;
        private bool initialized = false;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                var defaultTab = Tabs.FirstOrDefault(f => f.defaultTab);
                if (defaultTab != null)
                {
                    SelectTab(defaultTab.Title, true);
                }
            }
        }

        public void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            foreach (var tab in Tabs)
            {
                tab.Initialize(OnSelectTab);
            }
        }

        public bool IsTabShown(string tabTitle)
        {
            return GetTab(tabTitle).isShowing;
        }

        public UITabToggle GetTab(string tabTitle)
        {
            var tab = Tabs.FirstOrDefault(x => x.Title == tabTitle);
            return tab;
        }

        public void SelectTab(string tabTitle, bool ignoreAnimation)
        {
            GetTab(tabTitle).SelectTab(ignoreAnimation);
        }

        private void OnSelectTab(UITabToggle tab, bool animationIgnored)
        {
            if (currentTab == tab)
            {
                return;
            }
            if (currentTab != null)
            {
                currentTab.DeselectTab(animationIgnored);
            }
            currentTab = tab;
            if (titleText != null)
            {
                titleText.text = tab.Title;
            }
        }
    }

}