using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Gate2Reality.Narrative;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// PlayMode тесты цепочки L1→L2→L3 relocalization.
    /// L2 (YOLO re-detection) требует реального детектора — не тестируем в CI.
    /// L1 и L3 полностью управляемы без AR-подсистемы.
    /// </summary>
    public sealed class OfflineAnchorRelocalizerTests
    {
        private GameObject _go;
        private OfflineAnchorRelocalizer _relocalizer;
        private AnchorRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Relocalizer");
            _go.SetActive(false);
            _relocalizer = _go.AddComponent<OfflineAnchorRelocalizer>();
            _registry = _go.AddComponent<AnchorRegistry>();

            // Инжект зависимостей через рефлексию (сериализованные поля)
            SetField(_relocalizer, "anchorRegistry", _registry);
            SetField(_relocalizer, "enableL2", false);   // L2 требует YOLO — отключаем

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;
            SetField(_relocalizer, "arCamera", cam);

            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            // Убрать камеру (создана отдельным GO в SetUp)
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null) Object.Destroy(cam.gameObject);
        }

        // ── L1: тёплый возврат ───────────────────────────────────────────────

        [UnityTest]
        public IEnumerator L1_WhenRegistryHasLiveTransforms_ReturnsLevel1()
        {
            var anchor = new GameObject("Chair").transform;
            _registry.Register(0, NarrativeLabel.Chair, anchor);

            RelocalizationResult? result = null;
            _relocalizer.Relocalize(null, r => result = r);

            yield return new WaitUntil(() => result.HasValue);

            Assert.AreEqual(1, result.Value.Level);
            Assert.IsTrue(result.Value.NodeAnchors.ContainsKey(0));
            Assert.AreSame(anchor, result.Value.NodeAnchors[0]);

            Object.Destroy(anchor.gameObject);
        }

        [UnityTest]
        public IEnumerator L1_MapContainsAllRegisteredAnchors()
        {
            var t0 = new GameObject("Chair").transform;
            var t1 = new GameObject("Book").transform;
            _registry.Register(0, NarrativeLabel.Chair, t0);
            _registry.Register(1, NarrativeLabel.Book, t1);

            RelocalizationResult? result = null;
            _relocalizer.Relocalize(null, r => result = r);
            yield return new WaitUntil(() => result.HasValue);

            Assert.AreEqual(1, result.Value.Level);
            Assert.AreEqual(2, result.Value.NodeAnchors.Count);

            Object.Destroy(t0.gameObject);
            Object.Destroy(t1.gameObject);
        }

        // ── L3: fallback без якорей ──────────────────────────────────────────

        [UnityTest]
        public IEnumerator L3_NullSave_ReturnsLevel3WithEmptyMap()
        {
            RelocalizationResult? result = null;
            _relocalizer.Relocalize(null, r => result = r);
            yield return new WaitUntil(() => result.HasValue);

            Assert.AreEqual(3, result.Value.Level);
            Assert.AreEqual(0, result.Value.NodeAnchors.Count);
        }

        [UnityTest]
        public IEnumerator L3_SaveWithAnchors_CreatesGameObjectsForEach()
        {
            var save = new ProgressData
            {
                anchors = new[]
                {
                    new AnchorRecord { nodeIndex = 0, label = (int)NarrativeLabel.Chair,
                        localPos = new Vector3(1f, 0f, 0f), localRot = Quaternion.identity },
                    new AnchorRecord { nodeIndex = 1, label = (int)NarrativeLabel.Book,
                        localPos = new Vector3(-1f, 0f, 0f), localRot = Quaternion.identity }
                }
            };

            RelocalizationResult? result = null;
            _relocalizer.Relocalize(save, r => result = r);
            yield return new WaitUntil(() => result.HasValue);

            Assert.AreEqual(3, result.Value.Level);
            Assert.AreEqual(2, result.Value.NodeAnchors.Count);
            Assert.IsTrue(result.Value.NodeAnchors.ContainsKey(0));
            Assert.IsTrue(result.Value.NodeAnchors.ContainsKey(1));
        }

        [UnityTest]
        public IEnumerator L3_AnchorPositions_AreRelativeToCamera()
        {
            // Камера смотрит по +Z, offset ref = 1.5м вперёд
            var save = new ProgressData
            {
                anchors = new[]
                {
                    new AnchorRecord { nodeIndex = 0, label = (int)NarrativeLabel.Chair,
                        localPos = Vector3.zero, localRot = Quaternion.identity }
                }
            };

            RelocalizationResult? result = null;
            _relocalizer.Relocalize(save, r => result = r);
            yield return new WaitUntil(() => result.HasValue);

            var pos = result.Value.NodeAnchors[0].position;
            // localPos = (0,0,0) → мировая поза = refOrigin = cam + 1.5f forward
            Assert.AreEqual(0f, pos.x, 0.01f);
            Assert.AreEqual(0f, pos.y, 0.01f);
            Assert.AreEqual(1.5f, pos.z, 0.01f);
        }

        // ── OnRelocalizationReported event ───────────────────────────────────

        [UnityTest]
        public IEnumerator L3_FiresReportedEvent_WithLevel3()
        {
            int reportedLevel = -1;
            int reportedCount = -1;

            // Сохраняем лямбду в переменную — только так можно корректно отписаться
            // от статического события. Создание новой лямбды при -= не работает.
            System.Action<int, int> handler = (lvl, cnt) =>
            {
                reportedLevel = lvl;
                reportedCount = cnt;
            };
            OfflineAnchorRelocalizer.OnRelocalizationReported += handler;

            try
            {
                _relocalizer.Relocalize(null, _ => { });
                yield return new WaitUntil(() => reportedLevel != -1);

                Assert.AreEqual(3, reportedLevel);
                Assert.AreEqual(0, reportedCount);
            }
            finally
            {
                OfflineAnchorRelocalizer.OnRelocalizationReported -= handler;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetField(object target, string name, object value)
        {
            var fi = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"поле '{name}' не найдено");
            fi.SetValue(target, value);
        }
    }
}
