using System.Collections;
using UnityEngine;
using TMPro;
using Gate2Reality.Narrative;

namespace Gate2Reality.UI
{
    public class WhisperSubtitleController : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private OnDeviceNarrativeGenerator generator;
        [SerializeField] private float charRevealInterval = 0.04f;
        [SerializeField] private float holdDuration = 3f;
        [SerializeField] private float fadeOutDuration = 1f;

        private string _queued;
        private bool _playing;

        private void Awake()
        {
            generator.OnWhisperReady += Enqueue;
            if (subtitleText != null) subtitleText.maxVisibleCharacters = 0;
        }

        private void OnDestroy() => generator.OnWhisperReady -= Enqueue;

        private void Enqueue(string text)
        {
            _queued = text;
            if (!_playing) StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            _playing = true;
            string text = _queued;
            _queued = null;

            if (subtitleText == null) { _playing = false; yield break; }

            subtitleText.text = text;
            subtitleText.maxVisibleCharacters = 0;
            subtitleText.alpha = 1f;
            subtitleText.gameObject.SetActive(true);

            // Zero-GC typewriter via maxVisibleCharacters
            for (int i = 0; i <= text.Length; i++)
            {
                subtitleText.maxVisibleCharacters = i;
                yield return new WaitForSeconds(charRevealInterval);
            }

            yield return new WaitForSeconds(holdDuration);

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                subtitleText.alpha = 1f - elapsed / fadeOutDuration;
                yield return null;
            }

            subtitleText.gameObject.SetActive(false);
            _playing = false;

            if (_queued != null) StartCoroutine(PlayRoutine());
        }
    }
}
