using UnityEngine;

namespace Gate2Reality.Safety
{
    public class HorrorSafetyGovernor : MonoBehaviour
    {
        [SerializeField] private float scaleWhenHumanPresent = 0.25f;
        [SerializeField] private float transitionSpeed = 2f; // 1/0.5s = 2

        private float _targetScale = 1f;
        private float _currentScale = 1f;
        private static readonly int HorrorScaleId = Shader.PropertyToID("_HorrorScale");

        private void Update()
        {
            if (Mathf.Approximately(_currentScale, _targetScale)) return;
            _currentScale = Mathf.MoveTowards(_currentScale, _targetScale, transitionSpeed * Time.deltaTime);
            Shader.SetGlobalFloat(HorrorScaleId, _currentScale);
        }

        public void SetHumanPresent(bool present)
        {
            _targetScale = present ? scaleWhenHumanPresent : 1f;
        }

        public void ForceScale(float scale)
        {
            _targetScale = scale;
            _currentScale = scale;
            Shader.SetGlobalFloat(HorrorScaleId, scale);
        }
    }
}
