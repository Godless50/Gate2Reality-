using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Ключевой инвариант граф-ассета: CreateRuntimeNodes() отдаёт ГЛУБОКУЮ копию,
    /// чтобы покадровые мутации рантайма (DwellAccumulator, LastSeenPose,
    /// runtimeTarget) никогда не пачкали ScriptableObject на диске.
    /// </summary>
    public sealed class NarrativeGraphAssetTests
    {
        private NarrativeGraphAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            _asset.SetNodes(new[]
            {
                new NarrativeNode
                {
                    nodeName = "A",
                    dwellTimeSeconds = 0.75f,
                    condition = new NarrativeCondition
                    {
                        type = ConditionType.SemanticDetection,
                        requiredLabel = NarrativeLabel.Chair,
                        minConfidence = 0.85f
                    },
                    nextNodeIndices = new[] { 1 }
                },
                new NarrativeNode
                {
                    nodeName = "B",
                    dwellTimeSeconds = 1.0f,
                    condition = new NarrativeCondition { type = ConditionType.Proximity },
                    nextNodeIndices = System.Array.Empty<int>()
                }
            });
            _asset.EntryNodeIndex = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        [Test]
        public void CreateRuntimeNodes_PreservesStructure()
        {
            NarrativeNode[] rt = _asset.CreateRuntimeNodes();
            Assert.AreEqual(2, rt.Length);
            Assert.AreEqual("A", rt[0].nodeName);
            Assert.AreEqual(0.75f, rt[0].dwellTimeSeconds, 1e-5f);
            Assert.AreEqual(ConditionType.SemanticDetection, rt[0].condition.type);
            Assert.AreEqual(NarrativeLabel.Chair, rt[0].condition.requiredLabel);
            Assert.AreEqual(1, rt[0].nextNodeIndices[0]);
        }

        [Test]
        public void CreateRuntimeNodes_ReturnsDistinctInstances()
        {
            NarrativeNode[] rt = _asset.CreateRuntimeNodes();
            Assert.AreNotSame(_asset.Nodes[0], rt[0], "узел должен быть новым экземпляром");
            Assert.AreNotSame(_asset.Nodes[0].condition, rt[0].condition, "условие должно быть склонировано");
            Assert.AreNotSame(_asset.Nodes[0].nextNodeIndices, rt[0].nextNodeIndices, "рёбра должны быть склонированы");
        }

        [Test]
        public void MutatingRuntimeNode_DoesNotTouchAsset()
        {
            NarrativeNode[] rt = _asset.CreateRuntimeNodes();

            rt[0].DwellAccumulator = 99f;
            rt[0].LastSeenPose = new Pose(Vector3.one, Quaternion.identity);
            rt[0].condition.minConfidence = 0.1f;
            rt[0].nextNodeIndices[0] = 42;

            // Ассет нетронут.
            Assert.AreEqual(0f, _asset.Nodes[0].DwellAccumulator, 1e-5f);
            Assert.AreEqual(0.85f, _asset.Nodes[0].condition.minConfidence, 1e-5f);
            Assert.AreEqual(1, _asset.Nodes[0].nextNodeIndices[0]);
        }

        [Test]
        public void CreateRuntimeNodes_LeavesTriggerablesEmptyForSceneBindings()
        {
            NarrativeNode[] rt = _asset.CreateRuntimeNodes();
            // Ссылки на эффекты сцены ассет не хранит — их доливает менеджер.
            Assert.IsNotNull(rt[0].triggerableBehaviours);
            Assert.AreEqual(0, rt[0].triggerableBehaviours.Length);
        }
    }
}
