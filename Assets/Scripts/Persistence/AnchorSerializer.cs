using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Persistence
{
    /// <summary>
    /// Статическая утилита: преобразует живые Transform якорей в
    /// сериализуемые относительные позы и обратно. Вся математика
    /// в системе отсчёта опорного якоря (referenceLabel), инвариантной
    /// к тому, как ARCore выберет мировой ноль в новой сессии.
    /// </summary>
    public static class AnchorSerializer
    {
        /// <summary>
        /// Записывает относительные позы из регистра в ProgressData.
        /// Если опорный якорь не найден — берётся первый валидный.
        /// </summary>
        public static void Capture(IAnchorRegistry reg, NarrativeLabel referenceLabel,
                                   ProgressData into)
        {
            if (reg == null || into == null) return;
            var all = reg.All;
            if (all == null || all.Count == 0)
            {
                into.anchors = System.Array.Empty<AnchorRecord>();
                into.fingerprint = EmptyFingerprint();
                return;
            }

            // Find reference anchor by label; fall back to first valid
            Transform refT = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].label == referenceLabel && all[i].t != null)
                { refT = all[i].t; break; }
            }
            if (refT == null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].t != null)
                    { refT = all[i].t; referenceLabel = all[i].label; break; }
                }
            }
            if (refT == null)
            {
                into.anchors = System.Array.Empty<AnchorRecord>();
                into.fingerprint = EmptyFingerprint();
                return;
            }

            into.referenceFrameLabel = (int)referenceLabel;

            Vector3 refPos = refT.position;
            Quaternion refInv = Quaternion.Inverse(refT.rotation);

            var records = new AnchorRecord[all.Count];
            var worldPos = new Vector3[all.Count];

            for (int i = 0; i < all.Count; i++)
            {
                (int nodeIdx, NarrativeLabel lbl, Transform t) = all[i];
                Vector3 wp = t != null ? t.position : refPos;
                worldPos[i] = wp;
                records[i] = new AnchorRecord
                {
                    label = (int)lbl,
                    nodeIndex = nodeIdx,
                    localPos = refInv * (wp - refPos),
                    localRot = t != null ? refInv * t.rotation : Quaternion.identity,
                    confidence = 1f
                };
            }

            into.anchors = records;
            // Fingerprint uses only YOLO-detectable (semantic) anchors: Chair/Book/Cup.
            // Echo zones are procedural and can't be re-detected by YOLO in L2.
            into.fingerprint = ComputeSemanticFingerprint(all);
        }

        /// <summary>
        /// Computes a semantic fingerprint (Chair/Book/Cup only) from the registry.
        /// Useful for Stage A calibration logging. Matches what Capture() stores.
        /// </summary>
        public static RoomFingerprint Fingerprint(IAnchorRegistry reg)
        {
            if (reg == null) return EmptyFingerprint();
            return ComputeSemanticFingerprint(reg.All);
        }

        /// <summary>True for labels that YOLO can detect and that form the room fingerprint.</summary>
        public static bool IsSemanticLabel(NarrativeLabel lbl) =>
            lbl == NarrativeLabel.Chair ||
            lbl == NarrativeLabel.Book  ||
            lbl == NarrativeLabel.Cup;

        /// <summary>
        /// Checks whether two fingerprints describe the same room geometry
        /// within the given per-pair tolerance (metres).
        /// </summary>
        public static bool FingerprintMatches(RoomFingerprint saved, RoomFingerprint live,
                                              float toleranceMetres = 0.35f)
        {
            if (saved == null || live == null) return false;
            if (saved.anchorCount != live.anchorCount) return false;
            var sd = saved.pairwiseDistances;
            var ld = live.pairwiseDistances;
            if (sd == null || ld == null || sd.Length != ld.Length) return false;
            for (int i = 0; i < sd.Length; i++)
            {
                if (Mathf.Abs(sd[i] - ld[i]) > toleranceMetres) return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────

        private static RoomFingerprint ComputeSemanticFingerprint(
            IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> all)
        {
            // Pre-count to avoid a list allocation in the hot path.
            int count = 0;
            for (int i = 0; i < all.Count; i++)
                if (IsSemanticLabel(all[i].label) && all[i].t != null) count++;

            if (count == 0) return EmptyFingerprint();

            var positions = new Vector3[count];
            int idx = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsSemanticLabel(all[i].label) && all[i].t != null)
                    positions[idx++] = all[i].t.position;
            }
            return ComputeFingerprint(positions);
        }

        private static RoomFingerprint ComputeFingerprint(Vector3[] positions)
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

        private static RoomFingerprint EmptyFingerprint() =>
            new RoomFingerprint { pairwiseDistances = System.Array.Empty<float>(), anchorCount = 0 };
    }
}
