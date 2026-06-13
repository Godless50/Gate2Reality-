using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.SceneTwo
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// Процедурное размещение эхо-зон Сцены 2 «Картограф» на реальных
    /// поверхностях комнаты игрока. Без геолокации: только плоскости ARCore
    /// и одометрия. Запускается активацией узла Чашки (к этому моменту
    /// ARPlaneManager успел отсканировать комнату за всю Сцену 1).
    ///
    /// АЛГОРИТМ:
    ///  1. Сбор кандидатов: трекаемые, не поглощённые плоскости с площадью
    ///     выше минимума. Стены — по alignment Vertical (работает на любом
    ///     устройстве, классификация лишь уточняет), поверхности — HorizontalUp.
    ///  2. Слоты: WallEcho (любая стена), SurfaceEcho (пол/стол), PortalWall
    ///     (САМАЯ БОЛЬШАЯ стена — финальной двери нужен размах).
    ///  3. Greedy-подбор с разносом minZoneSpacing и релаксацией порога
    ///     (1.5м -> 0.9м -> 0.4м): лучше тесные зоны, чем отказ.
    ///  4. Фолбэк-кольцо: комната без распознанных стен (бывает: зеркала,
    ///     однотонные обои) — зоны встают кольцом вокруг игрока на высоте
    ///     глаз. Та же философия, что в DepthPoseProjector: уровень 3 не
    ///     может провалиться.
    ///  5. ARAnchor на каждую зону: за минуты игры ARCore уточняет карту мира,
    ///     якорь «приклеивает» зону к реальной поверхности — без него зоны
    ///     уплывают на 10-20см и портал перестаёт лежать в стене.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EchoZonePlacer : MonoBehaviour
    {
        public enum ZoneKind : byte { WallEcho = 0, SurfaceEcho = 1, PortalWall = 2 }

        [Serializable]
        public struct ZoneSlot
        {
            public ZoneKind kind;
            [Tooltip("Индекс узла графа, который привяжется к этой зоне")]
            public int nodeIndex;
            [Tooltip("Визуал зоны (пульсирующий маркер/рамка портала). Опционален")]
            public GameObject visualPrefab;
        }

        [Header("Связи")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private Transform playerCamera;

        [Header("Конфигурация")]
        [Tooltip("Узел, активация которого запускает размещение (Чашка = 2)")]
        [SerializeField] private int triggerNodeIndex = 2;
        [SerializeField] private ZoneSlot[] slots =
        {
            // ПОРЯДОК = ПРИОРИТЕТ ВЫБОРА (не порядок прохождения игроком!).
            // PortalWall размещается ПЕРВЫМ и забирает самую большую стену;
            // остальные подбираются с разносом уже относительно него.
            // Прогон-симуляция поймала обратный порядок как баг: WallEcho
            // успевал украсть лучшую стену у финальной двери.
            new ZoneSlot { kind = ZoneKind.PortalWall,  nodeIndex = 5 },
            new ZoneSlot { kind = ZoneKind.WallEcho,    nodeIndex = 3 },
            new ZoneSlot { kind = ZoneKind.SurfaceEcho, nodeIndex = 4 },
        };

        [Header("Геометрия")]
        [SerializeField] private float minZoneSpacing = 1.5f;
        [SerializeField] private float minWallArea = 0.4f;     // м²
        [SerializeField] private float minSurfaceArea = 0.3f;  // м²
        [Tooltip("Полоса высоты для настенных зон (уровень глаз)")]
        [SerializeField] private float eyeMin = 1.0f, eyeMax = 1.8f;
        [SerializeField] private float fallbackRingRadius = 1.5f;

        public readonly struct PlacedZone
        {
            public readonly ZoneKind Kind;
            public readonly Transform Anchor;
            public PlacedZone(ZoneKind k, Transform a) { Kind = k; Anchor = a; }
        }

        /// <summary>Зоны размещены — подписчики: голо-карта, аудио-эмбиент.</summary>
        public event Action<IReadOnlyList<PlacedZone>> OnZonesPlaced;
        public IReadOnlyList<PlacedZone> Zones => _placed;

        private readonly List<PlacedZone> _placed = new List<PlacedZone>(3);
        private bool _done;

        private struct Candidate
        {
            public ARPlane Plane;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Area;
        }

        // Переиспользуемые списки — размещение одноразовое, но привычка важнее.
        private readonly List<Candidate> _walls = new List<Candidate>(16);
        private readonly List<Candidate> _surfaces = new List<Candidate>(16);

        private void OnEnable() => narrativeManager.OnNodeActivated += HandleNodeActivated;
        private void OnDisable() => narrativeManager.OnNodeActivated -= HandleNodeActivated;

        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            if (_done || nodeIndex != triggerNodeIndex) return;
            _done = true;
            PlaceZones();
        }

        // =====================================================================
        // РАЗМЕЩЕНИЕ
        // =====================================================================
        private void PlaceZones()
        {
            GatherCandidates();
            // Большие стены вперёд: слот PortalWall берёт _walls[0].
            _walls.Sort(static (a, b) => b.Area.CompareTo(a.Area));

            for (int i = 0; i < slots.Length; i++)
            {
                if (!TrySelect(slots[i].kind, out Candidate picked))
                {
                    picked = FallbackCandidate(i);
                }

                Transform anchor = CreateAnchor(in picked);

                // Маркер зоны (EchoZone): порядковый номер, тип поверхности и
                // нормаль — данные для эффектов, отладочного HUD и Сцены 3.
                EchoSurface surface = slots[i].kind == ZoneKind.SurfaceEcho
                    ? (picked.Pos.y > 0.35f ? EchoSurface.Table : EchoSurface.Floor)
                    : EchoSurface.Wall;
                anchor.gameObject.AddComponent<EchoZone>()
                      .Init(i, surface, picked.Rot * Vector3.forward);

                if (slots[i].visualPrefab != null)
                {
                    Instantiate(slots[i].visualPrefab, anchor, false);
                }

                narrativeManager.SetNodeRuntimeTarget(slots[i].nodeIndex, anchor);
                _placed.Add(new PlacedZone(slots[i].kind, anchor));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Gate2Reality] Зона {slots[i].kind} -> {picked.Pos} " +
                          $"(plane={(picked.Plane != null ? picked.Plane.trackableId.ToString() : "fallback")})");
#endif
            }

            OnZonesPlaced?.Invoke(_placed);

            // Плоскости отслужили: зоны заякорены (ARAnchor живут без
            // детекции), голо-карта построила контуры в обработчике события
            // выше. Гасим детекцию плоскостей до конца главы — ощутимая
            // экономия CPU/батареи на фоне и так горячего AR-трекинга.
            if (planeManager != null)
            {
                planeManager.requestedDetectionMode = PlaneDetectionMode.None;
            }
        }

        private void GatherCandidates()
        {
            _walls.Clear();
            _surfaces.Clear();
            Vector3 playerPos = playerCamera.position;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking) continue;
                if (plane.subsumedBy != null) continue; // поглощена более крупной

                float area = plane.size.x * plane.size.y;

                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    if (area < minWallArea) continue;

                    Vector3 pos = plane.center;
                    // Прижимаем к полосе глаз В ПРЕДЕЛАХ плоскости: выходить
                    // за её вертикальный размах нельзя — зона повиснет в воздухе.
                    float halfH = plane.size.y * 0.4f;
                    pos.y = Mathf.Clamp(pos.y,
                        Mathf.Max(eyeMin, plane.center.y - halfH),
                        Mathf.Min(eyeMax, plane.center.y + halfH));

                    // Нормаль стены — В КОМНАТУ (к игроку), иначе портал
                    // «смотрит» в соседскую квартиру.
                    Vector3 normal = plane.normal;
                    if (Vector3.Dot(normal, playerPos - pos) < 0f) normal = -normal;

                    _walls.Add(new Candidate
                    {
                        Plane = plane, Pos = pos,
                        Rot = Quaternion.LookRotation(normal), Area = area
                    });
                }
                else if (plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    if (area < minSurfaceArea) continue;

                    Vector3 pos = plane.center;
                    Vector3 toPlayer = playerPos - pos; toPlayer.y = 0f;
                    Quaternion rot = toPlayer.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(toPlayer.normalized)
                        : Quaternion.identity;

                    _surfaces.Add(new Candidate
                    {
                        Plane = plane, Pos = pos, Rot = rot, Area = area
                    });
                }
            }
        }

        /// <summary>Greedy с релаксацией разноса: 1.0x -> 0.6x -> 0.27x порога.</summary>
        private bool TrySelect(ZoneKind kind, out Candidate result)
        {
            List<Candidate> pool = kind == ZoneKind.SurfaceEcho ? _surfaces : _walls;
            // Поверхностей нет (пустая комната без стола, пол не пойман) —
            // SurfaceEcho деградирует до стены: лучше стена, чем фолбэк-кольцо.
            if (pool.Count == 0 && kind == ZoneKind.SurfaceEcho) pool = _walls;

            for (float spacing = minZoneSpacing; spacing >= minZoneSpacing * 0.25f; spacing *= 0.6f)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    // PortalWall обязан взять самую большую стену из допустимых —
                    // pool отсортирован по площади, первый проходной и есть лучший.
                    if (IsFarFromPlaced(pool[i].Pos, spacing))
                    {
                        result = pool[i];
                        return true;
                    }
                }
            }
            result = default;
            return false;
        }

        private bool IsFarFromPlaced(Vector3 pos, float spacing)
        {
            float sq = spacing * spacing;
            for (int i = 0; i < _placed.Count; i++)
            {
                if ((_placed[i].Anchor.position - pos).sqrMagnitude < sq) return false;
            }
            return true;
        }

        /// <summary>Уровень «не может провалиться»: кольцо вокруг игрока.</summary>
        private Candidate FallbackCandidate(int slotIndex)
        {
            float angle = slotIndex * 120f * Mathf.Deg2Rad; // 3 зоны через 120°
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            dir = playerCamera.rotation * dir; dir.y = 0f; dir.Normalize();

            Vector3 pos = playerCamera.position + dir * fallbackRingRadius;
            pos.y = Mathf.Clamp(playerCamera.position.y, eyeMin, eyeMax);

            return new Candidate
            {
                Plane = null, Pos = pos,
                Rot = Quaternion.LookRotation(-dir), Area = 0f
            };
        }

        private Transform CreateAnchor(in Candidate c)
        {
            var pose = new Pose(c.Pos, c.Rot);

            // Якорь к плоскости — максимум стабильности при уточнении карты.
            if (c.Plane != null && anchorManager != null)
            {
                ARAnchor anchor = anchorManager.AttachAnchor(c.Plane, pose);
                if (anchor != null) return anchor.transform;
            }

            // Фолбэк-зона / нет менеджера якорей: обычный GameObject.
            // Он не «приклеен» к миру, но для кольца вокруг игрока это и не нужно.
            var go = new GameObject("EchoZoneAnchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return go.transform;
        }
    }
}
