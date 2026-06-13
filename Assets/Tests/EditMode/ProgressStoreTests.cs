using NUnit.Framework;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Раунд-трип сейвов: запись → чтение → совпадение полей, очистка, версия.
    /// Использует реальный Application.persistentDataPath (доступен в тестах).
    /// </summary>
    public sealed class ProgressStoreTests
    {
        [SetUp]
        public void SetUp() => ProgressStore.Clear();

        [TearDown]
        public void TearDown() => ProgressStore.Clear();

        [Test]
        public void SaveThenLoad_RoundTripsAllFields()
        {
            var data = new ProgressData
            {
                chapter = 2,
                nodeIndex = 5,
                seenObjectsMask = 0b1010,
                crossedOver = true
            };

            ProgressStore.Save(data);
            Assert.IsTrue(ProgressStore.HasSave);

            Assert.IsTrue(ProgressStore.TryLoad(out ProgressData loaded));
            Assert.AreEqual(2, loaded.chapter);
            Assert.AreEqual(5, loaded.nodeIndex);
            Assert.AreEqual(0b1010, loaded.seenObjectsMask);
            Assert.IsTrue(loaded.crossedOver);
            Assert.AreEqual(ProgressStore.CurrentVersion, loaded.version);
            Assert.Greater(loaded.savedAtUnixSeconds, 0L);
        }

        [Test]
        public void Clear_RemovesSave()
        {
            ProgressStore.Save(new ProgressData { chapter = 1, nodeIndex = 0 });
            Assert.IsTrue(ProgressStore.HasSave);

            ProgressStore.Clear();
            Assert.IsFalse(ProgressStore.HasSave);
            Assert.IsFalse(ProgressStore.TryLoad(out _));
        }

        [Test]
        public void TryLoad_NoSave_ReturnsFalse()
        {
            Assert.IsFalse(ProgressStore.HasSave);
            Assert.IsFalse(ProgressStore.TryLoad(out ProgressData data));
            Assert.IsNull(data);
        }

        [Test]
        public void Save_OverwritesPrevious()
        {
            ProgressStore.Save(new ProgressData { chapter = 1, nodeIndex = 1 });
            ProgressStore.Save(new ProgressData { chapter = 1, nodeIndex = 7 });

            Assert.IsTrue(ProgressStore.TryLoad(out ProgressData loaded));
            Assert.AreEqual(7, loaded.nodeIndex);
        }
    }
}
