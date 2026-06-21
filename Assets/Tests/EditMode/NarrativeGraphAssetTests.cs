using Gate2Reality.Narrative;
using NUnit.Framework;
using UnityEngine;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public class NarrativeGraphAssetTests
    {
        [Test]
        public void SetNodes_Empty_NodeCountIsZero()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.SetNodes(new NarrativeNode[] { });
                Assert.AreEqual(0, asset.NodeCount);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void SetNodes_ThreeNodes_NodeCountIsThree()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.SetNodes(new NarrativeNode[]
                    { new NarrativeNode(), new NarrativeNode(), new NarrativeNode() });
                Assert.AreEqual(3, asset.NodeCount);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void SetNodes_Null_ProducesEmptyNodes()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.SetNodes(null);
                Assert.IsNotNull(asset.Nodes);
                Assert.AreEqual(0, asset.NodeCount);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void EntryNodeIndex_DefaultIsZero()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try { Assert.AreEqual(0, asset.EntryNodeIndex); }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void EntryNodeIndex_CanBeSet()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.EntryNodeIndex = 2;
                Assert.AreEqual(2, asset.EntryNodeIndex);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void CreateRuntimeNodes_ReturnsNewArrayNotSameRef()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.SetNodes(new NarrativeNode[] { new NarrativeNode() });
                var runtime = asset.CreateRuntimeNodes();
                Assert.IsFalse(ReferenceEquals(runtime, asset.Nodes));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void CreateRuntimeNodes_PreservesNodeName()
        {
            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            try
            {
                asset.SetNodes(new NarrativeNode[] { new NarrativeNode { nodeName = "Chair" } });
                var runtime = asset.CreateRuntimeNodes();
                Assert.AreEqual("Chair", runtime[0].nodeName);
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
