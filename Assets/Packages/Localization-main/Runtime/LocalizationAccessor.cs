using System;
using Localization.Base.Enums;
using UnityEngine;

namespace Localization
{
    [DefaultExecutionOrder(-1000)][ExecuteAlways]
    public class LocalizationAccessor : MonoBehaviour
    {
        private static LocalizationAccessor instance { get; set; }
        [SerializeField] private Base.Localization data;
        public static Base.Localization Localization => instance == null ?  null: instance.data;
        [SerializeField] private bool isInitialized = false; 

        
        private void Awake()
        {
            int o = 0;
            if (instance != null)
            {
                Debug.LogWarning("There could be only one instance of localization");
                return;
            }
            Initialize();
        }

        private void Initialize()
        {
            if (isInitialized)
            {
                Debug.LogWarning("The localization has already been initialized");
                //return;
            }
            instance = this;
            Localization.Initialize();
            isInitialized = true;
        }
        [ContextMenu("Force Initialize")]
        public void ForceInit()
        {
            instance = this;
            Localization.Initialize();
            isInitialized = true;
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            Initialize();
        }
#endif
        [ContextMenu(nameof(ChangeToEnglish))]
        public void ChangeToEnglish()
        {
            data.ChangeLanguage(Languages.English, this);
        }
        
        [ContextMenu(nameof(ChangeToPersian))]
        public void ChangeToPersian()
        {
            data.ChangeLanguage(Languages.Persian, this);
        }
    }
}
