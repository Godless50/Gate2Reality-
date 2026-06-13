using System;
using UnityEngine;
using UnityEngine.UI;
using Gate2Reality.Effects;

namespace Gate2Reality.SceneTwo
{
    public class CrossingTransitionEffect : TriggerableEffectBase
    {
        [SerializeField] private Image flashOverlay;
        [SerializeField] private AudioSource stingerSource;
        [SerializeField] private GameObject normalWorld;
        [SerializeField] private GameObject invertedWorld;
        [SerializeField] private float flashDuration = 0.15f;
        [SerializeField] private float revealDelay = 0.3f;

        public event Action OnCrossedOver;

        private bool _worldSwapped;

        protected override void OnTriggered()
        {
            _worldSwapped = false;
            if (flashOverlay != null)
            {
                flashOverlay.gameObject.SetActive(true);
                flashOverlay.color = Color.white;
            }
            if (stingerSource != null) stingerSource.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (!_worldSwapped && TimeSinceTriggered >= flashDuration)
            {
                _worldSwapped = true;
                if (normalWorld != null) normalWorld.SetActive(false);
                if (invertedWorld != null) invertedWorld.SetActive(true);
                OnCrossedOver?.Invoke();
            }

            if (TimeSinceTriggered >= flashDuration)
            {
                float fade = Mathf.Clamp01(1f - (TimeSinceTriggered - flashDuration) / revealDelay);
                if (flashOverlay != null)
                    flashOverlay.color = new Color(1f, 1f, 1f, fade);
            }

            if (TimeSinceTriggered >= flashDuration + revealDelay)
            {
                if (flashOverlay != null) flashOverlay.gameObject.SetActive(false);
                MarkFinished();
            }
        }

        protected override void OnCancelled()
        {
            if (flashOverlay != null) flashOverlay.gameObject.SetActive(false);
        }
    }
}
