using NUnit.Framework;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public sealed class NarrativeContextTests
    {
        [Test]
        public void NarrativeContext_HasSeen_TrueForSeenLabel()
        {
            int mask = 1 << (int)NarrativeLabel.Chair;
            var ctx  = new NarrativeContext(NarrativeLabel.Chair, mask, 0.5f, 6500f, RoomType.LivingRoom);

            Assert.IsTrue(ctx.HasSeen(NarrativeLabel.Chair));
            Assert.IsFalse(ctx.HasSeen(NarrativeLabel.Book));
        }
    }
}
