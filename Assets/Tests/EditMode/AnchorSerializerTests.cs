using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты сериализации якорей: roundtrip относительных поз,
    /// fingerprint-матчинг, миграция сейва v1→v2.
    /// TestAnchorRegistry — простой in-memory POCO, MonoBehaviour не нужен.
    /// </summary>
    public sealed class AnchorSerializerTests
    {
        // Simple non-MonoBehaviour IAnchorRegistry for testing
        private sealed class TestRegistry : IAnchorRegistry
        {
            private readonly List<(int nodeIndex, NarrativeLabel label, Transform t)> _list
                = new List<(int, NarrativeLabel, Transform)>();
            private readonly Dictionary<int, int> _map = new Dictionary<int, int>();

            public IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> All => _list;

            public void Register(int nodeIndex, NarrativeLabel label, Transform anchor)
            {
                if (_map.TryGetValue(nodeIndex, out int i)) _list[i] = (nodeIndex, label, anchor);
                else { _map[nodeIndex] = _list.Count; _list.Add((nodeIndex, label, anchor)); }
            }

            public bool TryGet(int nodeIndex, out Transform anchor)
            {
                if (_map.TryGetValue(nodeIndex, out int i)) { anchor = _list[i].t; return true; }
                anchor = null; return false;
            }

            public void Clear()
            {
                _list.Clear();
                _map.Clear();
            }
        }

        // ─── Fingerprint matching ─────────────────────────────────────────

        [Test]
        public void FingerprintMatches_SimilarGeometry_ReturnsTrue()
        {
            var saved = new RoomFingerprint
            { anchorCount = 2, pairwiseDistances = new[] { 1.50f } };
            var live = new RoomFingerprint
            { anchorCount = 2, pairwiseDistances = new[] { 1.65f } }; // 0.15 m < 0.35 tolerance

            Assert.IsTrue(AnchorSerializer.FingerprintMatches(saved, live, 0.35f));
        }

        [Test]
        public void FingerprintMatches_DisparateGeometry_ReturnsFalse()
        {
            var saved = new RoomFingerprint
            { anchorCount = 2, pairwiseDistances = new[] { 1.50f } };
            var live = new RoomFingerprint
            { anchorCount = 2, pairwiseDistances = new[] { 2.50f } }; // 1.0 m >> tolerance

            Assert.IsFalse(AnchorSerializer.FingerprintMatches(saved, live, 0.35f));
        }

        [Test]
        public void FingerprintMatches_DifferentAnchorCount_ReturnsFalse()
        {
            var a = new RoomFingerprint
            { anchorCount = 2, pairwiseDistances = new[] { 1.5f } };
            var b = new RoomFingerprint
            { anchorCount = 3, pairwiseDistances = new[] { 1.5f, 2.0f, 2.5f } };

            Assert.IsFalse(AnchorSerializer.FingerprintMatches(a, b));
        }

        [Test]
        public void FingerprintMatches_NullInputs_ReturnsFalse()
        {
            var fp = new RoomFingerprint { anchorCount = 1, pairwiseDistances = new[] { 1.0f } };
            Assert.IsFalse(AnchorSerializer.FingerprintMatches(null, fp));
            Assert.IsFalse(AnchorSerializer.FingerprintMatches(fp, null));
            Assert.IsFalse(AnchorSerializer.FingerprintMatches(null, null));
        }

        // ─── Capture round-trip ───────────────────────────────────────────

        [Test]
        public void Capture_RoundTrip_ReferenceAnchorAtOrigin()
        {
            var refGo = new GameObject("ref");
            refGo.transform.position = new Vector3(1f, 0f, 2f);
            refGo.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            var otherGo = new GameObject("other");
            otherGo.transform.position = new Vector3(3f, 0f, 2f); // 2 m away along world X

            var reg = new TestRegistry();
            reg.Register(0, NarrativeLabel.Chair, refGo.transform);
            reg.Register(1, NarrativeLabel.Book, otherGo.transform);

            var data = new ProgressData();
            AnchorSerializer.Capture(reg, NarrativeLabel.Chair, data);

            Assert.AreEqual(2, data.anchors.Length);
            Assert.AreEqual((int)NarrativeLabel.Chair, data.referenceFrameLabel);

            // Find records
            AnchorRecord refRec = null, otherRec = null;
            foreach (var r in data.anchors)
            {
                if (r.label == (int)NarrativeLabel.Chair) refRec = r;
                else otherRec = r;
            }
            Assert.IsNotNull(refRec, "Chair record should exist");
            Assert.IsNotNull(otherRec, "Book record should exist");

            // Reference anchor localPos must be (0,0,0)
            Assert.Less(refRec.localPos.magnitude, 0.001f,
                "reference anchor localPos should be zero");

            // Other anchor: 2 m from ref → localPos.magnitude ≈ 2
            Assert.Greater(otherRec.localPos.magnitude, 1.9f);
            Assert.Less(otherRec.localPos.magnitude, 2.1f);

            // Fingerprint: single pair ≈ 2 m
            Assert.AreEqual(1, data.fingerprint.pairwiseDistances.Length);
            Assert.AreEqual(2f, data.fingerprint.pairwiseDistances[0], 0.05f);

            UnityEngine.Object.DestroyImmediate(refGo);
            UnityEngine.Object.DestroyImmediate(otherGo);
        }

        [Test]
        public void Capture_MissingReferenceLabel_FallsBackToFirstAnchor()
        {
            var go = new GameObject("book");
            go.transform.position = Vector3.zero;

            var reg = new TestRegistry();
            reg.Register(1, NarrativeLabel.Book, go.transform);

            var data = new ProgressData();
            // Request Chair as reference but only Book is registered
            AnchorSerializer.Capture(reg, NarrativeLabel.Chair, data);

            Assert.AreEqual(1, data.anchors.Length);
            Assert.AreEqual((int)NarrativeLabel.Book, data.referenceFrameLabel,
                "fallback to first registered anchor");

            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Capture_EmptyRegistry_ProducesEmptyArrays()
        {
            var reg = new TestRegistry();
            var data = new ProgressData();
            AnchorSerializer.Capture(reg, NarrativeLabel.Chair, data);

            Assert.IsNotNull(data.anchors);
            Assert.AreEqual(0, data.anchors.Length);
            Assert.IsNotNull(data.fingerprint);
            Assert.AreEqual(0, data.fingerprint.anchorCount);
        }

        [Test]
        public void Capture_NullRegistry_DoesNotThrow()
        {
            var data = new ProgressData();
            Assert.DoesNotThrow(() => AnchorSerializer.Capture(null, NarrativeLabel.Chair, data));
        }

        // ─── TestRegistry.Clear ───────────────────────────────────────────

        [Test]
        public void TestRegistry_Clear_EmptiesAllData()
        {
            var go = new GameObject("t");
            var reg = new TestRegistry();
            reg.Register(0, NarrativeLabel.Chair, go.transform);
            reg.Register(1, NarrativeLabel.Book, go.transform);

            Assert.AreEqual(2, reg.All.Count);

            reg.Clear();

            Assert.AreEqual(0, reg.All.Count, "Clear should empty All list");
            Assert.IsFalse(reg.TryGet(0, out _), "Clear should remove nodeIndex 0");
            Assert.IsFalse(reg.TryGet(1, out _), "Clear should remove nodeIndex 1");

            UnityEngine.Object.DestroyImmediate(go);
        }

        // ─── v1 → v2 migration ────────────────────────────────────────────

        [Test]
        public void ProgressData_V1Json_HasNullAnchors()
        {
            // Simulates loading a v1 save that has no anchors field.
            const string v1Json =
                "{\"version\":1,\"chapter\":1,\"nodeIndex\":2," +
                "\"seenObjectsMask\":0,\"crossedOver\":false,\"savedAtUnixSeconds\":0}";

            var data = JsonUtility.FromJson<ProgressData>(v1Json);

            Assert.AreEqual(1, data.version);
            Assert.AreEqual(2, data.nodeIndex);
            Assert.IsNull(data.anchors,
                "v1 save: anchors field absent → null → triggers L3 fallback");
            Assert.IsNull(data.fingerprint);
        }

        [Test]
        public void ProgressData_V2Json_RoundTrips_Anchors()
        {
            var original = new ProgressData
            {
                version = 2,
                chapter = 1,
                nodeIndex = 3,
                anchors = new[]
                {
                    new AnchorRecord
                    { label = (int)NarrativeLabel.Chair, nodeIndex = 0,
                      localPos = Vector3.zero, localRot = Quaternion.identity, confidence = 0.95f }
                },
                fingerprint = new RoomFingerprint
                { anchorCount = 1, pairwiseDistances = System.Array.Empty<float>() },
                referenceFrameLabel = (int)NarrativeLabel.Chair
            };

            string json = JsonUtility.ToJson(original);
            var loaded = JsonUtility.FromJson<ProgressData>(json);

            Assert.AreEqual(2, loaded.version);
            Assert.IsNotNull(loaded.anchors);
            Assert.AreEqual(1, loaded.anchors.Length);
            Assert.AreEqual((int)NarrativeLabel.Chair, loaded.anchors[0].label);
            Assert.AreEqual(0.95f, loaded.anchors[0].confidence, 0.001f);
            Assert.AreEqual((int)NarrativeLabel.Chair, loaded.referenceFrameLabel);
        }

        // ─── IsSemanticLabel ──────────────────────────────────────────────

        [Test]
        public void IsSemanticLabel_RecognisesCorrectly()
        {
            Assert.IsTrue(AnchorSerializer.IsSemanticLabel(NarrativeLabel.Chair));
            Assert.IsTrue(AnchorSerializer.IsSemanticLabel(NarrativeLabel.Book));
            Assert.IsTrue(AnchorSerializer.IsSemanticLabel(NarrativeLabel.Cup));
            Assert.IsFalse(AnchorSerializer.IsSemanticLabel(NarrativeLabel.EchoZone));
            Assert.IsFalse(AnchorSerializer.IsSemanticLabel(NarrativeLabel.Portal));
            Assert.IsFalse(AnchorSerializer.IsSemanticLabel(NarrativeLabel.None));
        }

        // ─── EchoZone excluded from fingerprint ───────────────────────────

        [Test]
        public void Capture_EchoZone_ExcludedFromFingerprint_ButInAnchors()
        {
            var gos = new[] { CreateAt(0f, 0f), CreateAt(2f, 0f), CreateAt(1f, 2f) };
            var reg = new TestRegistry();
            reg.Register(0, NarrativeLabel.Chair, gos[0].transform);
            reg.Register(1, NarrativeLabel.Book, gos[1].transform);
            reg.Register(3, NarrativeLabel.EchoZone, gos[2].transform);

            var data = new ProgressData();
            AnchorSerializer.Capture(reg, NarrativeLabel.Chair, data);

            // EchoZone excluded from fingerprint: only Chair+Book form it (1 pair)
            Assert.AreEqual(2, data.fingerprint.anchorCount,
                "fingerprint counts only semantic anchors");
            Assert.AreEqual(1, data.fingerprint.pairwiseDistances.Length);

            // All 3 present in anchors[] for L3 relative fallback
            Assert.AreEqual(3, data.anchors.Length, "all anchors saved for L3");

            foreach (var go in gos) UnityEngine.Object.DestroyImmediate(go);
        }

        // ─── Three-anchor fingerprint ─────────────────────────────────────

        [Test]
        public void Fingerprint_ThreeAnchors_HasThreePairs()
        {
            var gos = new[] {
                CreateAt(0f, 0f), CreateAt(2f, 0f), CreateAt(1f, 2f)
            };
            var reg = new TestRegistry();
            reg.Register(0, NarrativeLabel.Chair, gos[0].transform);
            reg.Register(1, NarrativeLabel.Book, gos[1].transform);
            reg.Register(2, NarrativeLabel.Cup, gos[2].transform);

            var fp = AnchorSerializer.Fingerprint(reg);

            Assert.AreEqual(3, fp.anchorCount);
            Assert.AreEqual(3, fp.pairwiseDistances.Length, "3 anchors → 3 pairs");

            foreach (var go in gos) UnityEngine.Object.DestroyImmediate(go);
        }

        // ─────────────────────────────────────────────────────────────────────

        private static GameObject CreateAt(float x, float z)
        {
            var go = new GameObject();
            go.transform.position = new Vector3(x, 0f, z);
            return go;
        }
    }
}
