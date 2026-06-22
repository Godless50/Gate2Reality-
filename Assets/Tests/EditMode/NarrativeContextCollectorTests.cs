using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты NarrativeContextCollector.InferRoom() через рефлексию.
    /// GO создаётся неактивным — OnEnable не запускается, cameraManager/narrativeManager могут быть null.
    /// </summary>
    [TestFixture]
    public sealed class NarrativeContextCollectorTests
    {
        private static readonly FieldInfo MaskField =
            typeof(NarrativeContextCollector).GetField(
                "_seenMask", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo InferRoomMethod =
            typeof(NarrativeContextCollector).GetMethod(
                "InferRoom", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _go;
        private NarrativeContextCollector _collector;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Collector");
            _go.SetActive(false);
            _collector = _go.AddComponent<NarrativeContextCollector>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private void SetMask(int mask) => MaskField.SetValue(_collector, mask);
        private RoomType InferRoom() => (RoomType)InferRoomMethod.Invoke(_collector, null);

        private static int Bit(NarrativeLabel l) => 1 << (int)l;

        [Test]
        public void InferRoom_ChairAndBook_ReturnsOffice()
        {
            SetMask(Bit(NarrativeLabel.Chair) | Bit(NarrativeLabel.Book));
            Assert.AreEqual(RoomType.Office, InferRoom());
        }

        [Test]
        public void InferRoom_CupNoBook_ReturnsKitchen()
        {
            SetMask(Bit(NarrativeLabel.Cup));
            Assert.AreEqual(RoomType.Kitchen, InferRoom());
        }

        [Test]
        public void InferRoom_ChairOnly_ReturnsLivingRoom()
        {
            SetMask(Bit(NarrativeLabel.Chair));
            Assert.AreEqual(RoomType.LivingRoom, InferRoom());
        }

        [Test]
        public void InferRoom_EmptyMask_ReturnsUnknown()
        {
            SetMask(0);
            Assert.AreEqual(RoomType.Unknown, InferRoom());
        }
    }
}
