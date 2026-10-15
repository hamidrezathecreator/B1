using System.Linq;
using UnityEngine;

namespace Localization.VoiceLocalization
{
    [RequireComponent(typeof(AudioSource))]
    public class LocalizeAudioSource : MonoBehaviour
    {
        [SerializeField] private LocalizedAudioItem[] audioClips;
        [SerializeField] private AudioClip fallbackClip;
        private AudioSource audioSource;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            OnLanguageChange(this);
            LocalizationAccessor.Localization.AddChangeListener(OnLanguageChange);
        }

        private void OnDisable()
        {
            LocalizationAccessor.Localization.RemoveChangeListener(OnLanguageChange);
        }

        private void OnLanguageChange(Object sender)
        {
            var clip = audioClips.FirstOrDefault(c => c.Language ==
                LocalizationAccessor.Localization.CurrentLanguage)?.AudioClip ? audioClips.FirstOrDefault(c => c.Language ==
                LocalizationAccessor.Localization.CurrentLanguage)?.AudioClip : fallbackClip;
            audioSource.clip = clip;
        }
    }
}
