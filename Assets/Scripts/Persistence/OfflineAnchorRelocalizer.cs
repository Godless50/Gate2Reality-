using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;
using Gate2Reality.Detection;

namespace Gate2Reality.Persistence
{
    /// <summary>
    /// Трёхуровневая релокализация якорей (L1→L2→L3, не может провалиться).
    ///
    ///   L1 — тёплый возврат: живые Transform в AnchorRegistry ещё валидны.
    ///        Стоимость: ноль. Применяется при свёрнутом приложении без сброса трекинга.
    ///   L2 — холодный старт + YOLO-окно: полный детектор на l2WindowSeconds,
    ///        fingerprint-сопоставление → новые GameObjects на мировых позах.
    ///        Requires enableL2 = true (ON по умолчанию, YOLO подтверждён на HONOR 90).
    ///   L3 — fallback: объекты не найдены / сейв без якорей. Создаём GameObjects
    ///        по запомненной относительной геометрии вокруг текущей камеры.
    ///        Не может провалиться; guard-подсказки доведут игрока.
    ///
    /// Инвариант privacy: L2 отключает person-only, собирает детекции, сразу
    /// возвращает person-only — окно минимально (≤ l2WindowSeconds).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OfflineAnchorRelocalizer : MonoBehaviour, IAnchorRelocalizer
    {
        /// <summary>
        /// Stage A диагностика: достигнутый уровень релокализации и число
        /// восстановленных якорей. Подписчик — отладочный HUD (dev-only).
        /// Срабатывает ровно один раз на каждый Relocalize.
        /// </summary>
        public static event System.Action<int, int> OnRelocalizationReported;

        [Header("Зависимости")]
        [SerializeField] private AnchorRegistry anchorRegistry;
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private Camera arCamera;

        [Header("Параметры L2")]
        [Tooltip("Включить YOLO re-detection (L2). HONOR 90 подтверждён, ON по умолчанию.")]
        [SerializeField] private bool enableL2 = true;
        [Tooltip("Максимальное окно сбора детекций, с. Ранний выход при нахождении всех объектов.")]
        [SerializeField] private float l2WindowSeconds = 3f;
        [Tooltip("Допуск fingerprint-матчинга, метры. Рекомендация: 0.20–0.40 по Stage A.")]
        [SerializeField] private float l2FingerprintTolerance = 0.35f;

        // ─── IAnchorRelocalizer ────────────────────────────────────────────

        public void Relocalize(ProgressData save, Action<RelocalizationResult> onComplete)
        {
            if (onComplete == null) return;
            StartCoroutine(RelocalizeCo(save, onComplete));
        }

        // ─── Core coroutine ────────────────────────────────────────────────

        private IEnumerator RelocalizeCo(ProgressData save, Action<RelocalizationResult> onComplete)
        {
            // L1: live transforms still valid (warm return / same session)
            if (anchorRegistry != null)
            {
                var all = anchorRegistry.All;
                if (all.Count > 0 && AllTransformsAlive(all))
                {
                    var map = BuildMapFromRegistry(anchorRegistry);
                    LogLevel(1, map.Count);
                    onComplete(new RelocalizationResult(1, map));
                    yield break;
                }
            }

            // L2: YOLO re-detection window
            if (enableL2 && detector != null &&
                save?.anchors != null && save.anchors.Length > 0)
            {
                var l2Output = new L2Result();
                yield return StartCoroutine(RunL2(save, l2Output));
                if (l2Output.Value.HasValue)
                {
                    onComplete(l2Output.Value.Value);
                    yield break;
                }
            }

            // L3: relative poses around camera — never fails
            onComplete(RunL3(save));
        }

        // ─── L2 ────────────────────────────────────────────────────────────

        private sealed class L2Result { public RelocalizationResult? Value; }

        private IEnumerator RunL2(ProgressData save, L2Result output)
        {
            // Best detection per label (confidence wins)
            var best = new Dictionary<NarrativeLabel, (Pose pose, float confidence)>(8);

            void OnDetection(DetectionEvent evt)
            {
                if (evt.Label == NarrativeLabel.None || evt.Label == NarrativeLabel.EchoZone)
                    return;
                if (!best.TryGetValue(evt.Label, out var prev) || evt.Confidence > prev.confidence)
                    best[evt.Label] = (evt.WorldPose, evt.Confidence);
            }

            detector.OnRawDetection += OnDetection;
            detector.SetPersonOnlyMode(false); // lift privacy guard for window

            float elapsed = 0f;
            while (elapsed < l2WindowSeconds)
            {
                if (save.anchors != null && AllExpectedLabelsFound(save.anchors, best))
                    break; // early exit
                elapsed += Time.deltaTime;
                yield return null;
            }

            detector.OnRawDetection -= OnDetection;
            detector.SetPersonOnlyMode(true); // restore privacy immediately

            if (best.Count == 0 || save.anchors == null) yield break;

            // Match saved records to detected poses by label
            var matched = new Dictionary<int, Pose>(save.anchors.Length); // nodeIndex → world pose
            for (int i = 0; i < save.anchors.Length; i++)
            {
                var rec = save.anchors[i];
                var lbl = (NarrativeLabel)rec.label;
                if (best.TryGetValue(lbl, out var hit))
                    matched[rec.nodeIndex] = hit.pose;
            }
            if (matched.Count == 0) yield break;

            // Fingerprint check: verify spatial layout is consistent
            if (save.fingerprint != null && save.fingerprint.anchorCount > 1 &&
                matched.Count == save.anchors.Length)
            {
                var livePos = new Vector3[save.anchors.Length];
                for (int i = 0; i < save.anchors.Length; i++)
                {
                    if (!matched.TryGetValue(save.anchors[i].nodeIndex, out Pose p))
                    { livePos = null; break; }
                    livePos[i] = p.position;
                }

                if (livePos != null)
                {
                    var liveFp = BuildFingerprintFromPositions(livePos);
                    if (!AnchorSerializer.FingerprintMatches(save.fingerprint, liveFp, l2FingerprintTolerance))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.Log("[Gate2Reality] L2: fingerprint mismatch — falling to L3.");
#endif
                        yield break;
                    }
                }
            }

            // Build result: create world-positioned GameObjects
            var map = new Dictionary<int, Transform>(matched.Count);
            foreach (var kvp in matched)
            {
                var go = CreateAnchorGO($"[L2Anchor_n{kvp.Key}]", kvp.Value.position, kvp.Value.rotation);
                map[kvp.Key] = go.transform;
                // Register so ChapterTwoDirector can read them
                RegisterInRegistry(kvp.Key, save, go.transform);
            }

            LogLevel(2, map.Count);
            output.Value = new RelocalizationResult(2, map);
        }

        // ─── L3 ────────────────────────────────────────────────────────────

        private RelocalizationResult RunL3(ProgressData save)
        {
            var map = new Dictionary<int, Transform>(8);

            if (save?.anchors == null || save.anchors.Length == 0)
            {
                LogLevel(3, 0);
                return new RelocalizationResult(3, map);
            }

            // Place anchors relative to camera: yaw-only rotation, 1.5 m forward ref origin.
            Transform cam = arCamera != null ? arCamera.transform : null;
            Vector3 camPos = cam != null ? cam.position : Vector3.zero;
            float camYaw = cam != null ? cam.eulerAngles.y : 0f;
            Quaternion yaw = Quaternion.Euler(0f, camYaw, 0f);
            Vector3 refOrigin = camPos + yaw * (Vector3.forward * 1.5f);

            foreach (var rec in save.anchors)
            {
                Vector3 worldPos = refOrigin + yaw * rec.localPos;
                Quaternion worldRot = yaw * rec.localRot;
                var go = CreateAnchorGO($"[L3Anchor_n{rec.nodeIndex}]", worldPos, worldRot);
                map[rec.nodeIndex] = go.transform;
                RegisterInRegistry(rec.nodeIndex, save, go.transform);
            }

            LogLevel(3, map.Count);
            return new RelocalizationResult(3, map);
        }

        // ─── Helpers ───────────────────────────────────────────────────────

        private GameObject CreateAnchorGO(string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.SetParent(transform, worldPositionStays: true);
            return go;
        }

        // Register a restored transform in the registry so that downstream
        // consumers (ChapterTwoDirector) don't need to receive the result explicitly.
        private void RegisterInRegistry(int nodeIndex, ProgressData save, Transform t)
        {
            if (anchorRegistry == null || save?.anchors == null) return;
            for (int i = 0; i < save.anchors.Length; i++)
            {
                if (save.anchors[i].nodeIndex == nodeIndex)
                {
                    anchorRegistry.Register(nodeIndex, (NarrativeLabel)save.anchors[i].label, t);
                    return;
                }
            }
        }

        private static Dictionary<int, Transform> BuildMapFromRegistry(AnchorRegistry reg)
        {
            var all = reg.All;
            var map = new Dictionary<int, Transform>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].t != null)
                    map[all[i].nodeIndex] = all[i].t;
            }
            return map;
        }

        private static bool AllTransformsAlive(
            IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> all)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].t == null) return false;
            return true;
        }

        private static bool AllExpectedLabelsFound(
            AnchorRecord[] records,
            Dictionary<NarrativeLabel, (Pose, float)> found)
        {
            foreach (var r in records)
            {
                var lbl = (NarrativeLabel)r.label;
                if (lbl == NarrativeLabel.None || lbl == NarrativeLabel.EchoZone) continue;
                if (!found.ContainsKey(lbl)) return false;
            }
            return true;
        }

        private static RoomFingerprint BuildFingerprintFromPositions(Vector3[] positions)
        {
            int n = positions.Length;
            int pairs = n * (n - 1) / 2;
            var dist = new float[pairs];
            int idx = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    dist[idx++] = Vector3.Distance(positions[i], positions[j]);
            return new RoomFingerprint { pairwiseDistances = dist, anchorCount = n };
        }

        private static void LogLevel(int level, int count)
        {
            OnRelocalizationReported?.Invoke(level, count);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Gate2Reality] Relocalization L{level}: {count} anchor(s) restored.");
#endif
        }
    }
}
