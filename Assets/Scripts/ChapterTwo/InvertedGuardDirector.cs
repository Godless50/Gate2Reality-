using UnityEngine;
using UnityEngine.Rendering;

namespace Gate2Reality.ChapterTwo
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// ПЕРЕВЁРНУТЫЙ Guard Node Главы II. В Главе I те же события NarrativeManager
    /// СПАСАЛИ застрявшего игрока (маяк → десатурация → партиклы-проводник). Здесь
    /// FSM ядра не меняется ни на строку — меняется ТОЛЬКО семантика реакций:
    /// простой игрока теперь работает ПРОТИВ него.
    ///
    ///   OnAudioBeaconRequested  -> ложный зов из НЕВЕРНОЙ точки (за спиной игрока,
    ///                              на дальней от цели стороне) — уводит не туда.
    ///   OnDesaturateRequested   -> мир СЖИМАЕТСЯ: виньетка наезжает, изнанка
    ///                              давит сильнее (комната смыкается вокруг
    ///                              медлящего игрока).
    ///   OnSaturationRestoreReq.  -> отпускает хватку, когда игрок снова движется
    ///                              к цели (узел сработал — прогресс).
    ///   OnGuideParticlesRequested-> «тянущиеся» партиклы ОТ цели К игроку —
    ///                              комната тянет к себе, а не указывает путь.
    ///
    /// Это и есть «guard помогает ПРОТИВ игрока» из дизайна Главы II — достигнуто
    /// чистым переиспользованием, без правок ядра графа.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvertedGuardDirector : MonoBehaviour
    {
        [Header("Ядро Главы II")]
        [SerializeField] private NarrativeManager chapterTwoNarrative;
        [SerializeField] private Transform arCameraTransform;

        [Header("Ложный зов")]
        [SerializeField] private AudioSource lureSource;  // 3D
        [Tooltip("На сколько метров за спину игрока уводить ложный источник")]
        [SerializeField] private float lureDistance = 3f;

        [Header("Сжатие мира")]
        [Tooltip("Виньетка/холодный профиль, наезжающий при простое")]
        [SerializeField] private Volume closingVolume;
        [SerializeField] private float closeLerpSeconds = 2f;
        [Tooltip("Доп. усиление веса изнанки при сжатии (0..1)")]
        [SerializeField] private float maxClosingWeight = 1f;

        [Header("Тянущиеся партиклы")]
        [SerializeField] private ParticleSystem reachingParticles; // эмиссия ОТ цели К игроку

        // -1 = отпускать, +1 = сжимать, 0 = покой.
        private float _closeDirection;

        private void OnEnable()
        {
            if (chapterTwoNarrative == null) return;
            chapterTwoNarrative.OnAudioBeaconRequested += HandleFalseLure;
            chapterTwoNarrative.OnDesaturateRequested += HandleWorldClosing;
            chapterTwoNarrative.OnSaturationRestoreRequested += HandleWorldRelease;
            chapterTwoNarrative.OnGuideParticlesRequested += HandleReaching;
        }

        private void OnDisable()
        {
            if (chapterTwoNarrative == null) return;
            chapterTwoNarrative.OnAudioBeaconRequested -= HandleFalseLure;
            chapterTwoNarrative.OnDesaturateRequested -= HandleWorldClosing;
            chapterTwoNarrative.OnSaturationRestoreRequested -= HandleWorldRelease;
            chapterTwoNarrative.OnGuideParticlesRequested -= HandleReaching;
        }

        // =====================================================================
        // АДВЕРСАРИАЛЬНЫЕ РЕАКЦИИ
        // =====================================================================
        private void HandleFalseLure(Pose targetPose)
        {
            if (lureSource == null || arCameraTransform == null) return;

            Vector3 playerPos = arCameraTransform.position;
            // Дальняя от цели сторона игрока: зов тянет ПРОЧЬ от настоящей цели.
            Vector3 away = (playerPos - targetPose.position);
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = -arCameraTransform.forward; // цель под ногами — за спину
            away.Normalize();

            lureSource.transform.position = playerPos + away * lureDistance;
            lureSource.spatialBlend = 1f;
            lureSource.Play();
        }

        private void HandleWorldClosing() => _closeDirection = 1f;
        private void HandleWorldRelease() => _closeDirection = -1f;

        private void HandleReaching(Pose targetPose)
        {
            if (reachingParticles == null || arCameraTransform == null) return;

            // Источник — у цели; направление — К игроку (комната тянется к нему).
            Vector3 origin = targetPose.position;
            Vector3 dir = (arCameraTransform.position - origin);
            if (dir.sqrMagnitude < 1e-4f) dir = arCameraTransform.forward;
            reachingParticles.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir.normalized));
            reachingParticles.Play();
        }

        private void Update()
        {
            if (_closeDirection == 0f || closingVolume == null) return;

            float w = closingVolume.weight +
                      _closeDirection * (Time.deltaTime / Mathf.Max(0.01f, closeLerpSeconds));
            closingVolume.weight = Mathf.Clamp(w, 0f, maxClosingWeight);

            if (closingVolume.weight <= 0f || closingVolume.weight >= maxClosingWeight)
                _closeDirection = 0f; // доехали — освобождаем Update-бюджет
        }
    }
}
