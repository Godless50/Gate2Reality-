using NUnit.Framework;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты ProgressStore: save/load/clear цикл.
    /// SetUp/TearDown очищают файл так, чтобы тесты не влияли друг на друга.
    /// </summary>
    [TestFixture]
    public sealed class ProgressStoreTests
    {
        [SetUp]
        public void SetUp() => ProgressStore.Clear();

        [TearDown]
        public void TearDown() => ProgressStore.Clear();

        [Test]
        public void AfterClear_HasSave_IsFalse()
        {
            Assert.IsFalse(ProgressStore.HasSave);
        }

        [Test]
        public void Save_ThenHasSave_IsTrue()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 1 });
            Assert.IsTrue(ProgressStore.HasSave);
        }

        [Test]
        public void Save_TryLoad_ReturnsSameNodeIndex()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 7, chapter = 1 });

            Assert.IsTrue(ProgressStore.TryLoad(out var loaded));
            Assert.AreEqual(7, loaded.nodeIndex);
        }

        [Test]
        public void Save_ThenClear_TryLoad_ReturnsFalse()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 3 });
            ProgressStore.Clear();
            Assert.IsFalse(ProgressStore.TryLoad(out _));
        }

        [Test]
        public void TryLoad_WhenNoFile_ReturnsFalse()
        {
            Assert.IsFalse(ProgressStore.TryLoad(out _));
        }

        [Test]
        public void Save_SetsVersionToCurrentVersion()
        {
            ProgressStore.Save(new ProgressData { version = 0 });
            Assert.IsTrue(ProgressStore.TryLoad(out var loaded));
            Assert.AreEqual(ProgressStore.CurrentVersion, loaded.version);
        }

        [Test]
        public void Save_SetsSavedAtUnixSeconds_Nonzero()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 1 });
            Assert.IsTrue(ProgressStore.TryLoad(out var loaded));
            Assert.Greater(loaded.savedAtUnixSeconds, 0L);
        }

        [Test]
        public void Save_NullData_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ProgressStore.Save(null));
        }

        [Test]
        public void RoundTrip_CrossedOver_PreservesFlag()
        {
            ProgressStore.Save(new ProgressData { crossedOver = true, chapter = 1 });
            Assert.IsTrue(ProgressStore.TryLoad(out var loaded));
            Assert.IsTrue(loaded.crossedOver);
        }
    }
}
