using NUnit.Framework;
using UnityEngine;
using Gate2Reality.SceneTwo;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты EchoZone — data-маркер зоны, только Init().
    /// </summary>
    [TestFixture]
    public sealed class EchoZoneTests
    {
        private GameObject _go;
        private EchoZone _zone;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("EchoZone");
            _zone = _go.AddComponent<EchoZone>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Init_SetsIndexCorrectly()
        {
            _zone.Init(2, EchoSurface.Wall, Vector3.forward);
            Assert.AreEqual(2, _zone.Index);
        }

        [Test]
        public void Init_SetsSurfaceCorrectly()
        {
            _zone.Init(0, EchoSurface.Table, Vector3.up);
            Assert.AreEqual(EchoSurface.Table, _zone.Surface);
        }

        [Test]
        public void Init_SetsSurfaceNormalCorrectly()
        {
            var normal = new Vector3(0f, 0f, 1f);
            _zone.Init(1, EchoSurface.Floor, normal);
            Assert.AreEqual(normal, _zone.SurfaceNormal);
        }

        [Test]
        public void Init_DefaultState_IndexIsZero()
        {
            // Before Init(): properties are default values
            Assert.AreEqual(0, _zone.Index);
            Assert.AreEqual(default(EchoSurface), _zone.Surface);
            Assert.AreEqual(Vector3.zero, _zone.SurfaceNormal);
        }

        [TestCase(0, EchoSurface.Wall)]
        [TestCase(1, EchoSurface.Table)]
        [TestCase(2, EchoSurface.Floor)]
        public void Init_AllSurfaceTypes_SetCorrectly(int index, EchoSurface surface)
        {
            _zone.Init(index, surface, Vector3.right);
            Assert.AreEqual(index, _zone.Index);
            Assert.AreEqual(surface, _zone.Surface);
        }
    }
}
