using System;
using Localization.Base.Enums;
using UnityEngine;

namespace Localization.VoiceLocalization
{
    [Serializable]
    public class LocalizedAudioItem
    {
        public Languages Language;
        public AudioClip AudioClip;
    }
}