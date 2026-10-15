using System.Collections;
using Animation.Base;
using UnityEngine;

namespace Animation.Misc
{ 
    public class AnimationSfx : MonoBehaviour
    {
        [SerializeField]
        private BaseAnimation relatedAnimation;
        [SerializeField]
        private AudioSource audioSource;
        [SerializeField]
        private AudioClip startSfx;
        [SerializeField]
        private AudioClip loopSfx;
        [SerializeField]
        private AudioClip endSfx;
        private Coroutine playingRoutine;

        private void Start()
        {
            if (relatedAnimation == null || audioSource == null)
                return;
            relatedAnimation.onStartPlaying.AddListener(PlayStartSfx);
            relatedAnimation.onFinishPlaying.AddListener(PlayEndSfx);
        }

        private void PlayStartSfx()
        {
            playingRoutine = StartCoroutine(PlayStartSfxRoutine());
        }

        private void PlayEndSfx()
        {
            if (playingRoutine != null)
                StopCoroutine(playingRoutine);
            audioSource.Stop();
            if (endSfx != null)
            {
                audioSource.PlayOneShot(endSfx);
            }
        }

        private IEnumerator PlayStartSfxRoutine()
        {
            audioSource.Stop();
            if (startSfx != null)
            {
                audioSource.PlayOneShot(startSfx);
                yield return new WaitForSeconds(startSfx.length);
            }
            if (loopSfx != null)
            {
                audioSource.Stop();
                audioSource.clip = loopSfx;
                audioSource.loop = true;
                audioSource.Play();
            }
        }
    }
}
