using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public class SeededRandomTests
    {
        // Эталон Mulberry32. Серверная реализация на TypeScript должна выдавать те же числа.
        [TestCase(42u, new uint[] { 2581720956u, 1925393290u, 3661312704u, 2876485805u })]
        [TestCase(12345u, new uint[] { 4207900869u, 1317490944u, 2079646450u, 3513001552u })]
        public void MatchesReferenceSequence(uint seed, uint[] expected)
        {
            var random = new SeededRandom(seed);
            foreach (uint value in expected)
                Assert.AreEqual(value, random.NextUInt());
        }

        [Test]
        public void SameSeedGivesSameSequence()
        {
            var a = new SeededRandom(777);
            var b = new SeededRandom(777);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextDouble(), b.NextDouble());
        }

        [Test]
        public void NextDoubleIsInUnitInterval()
        {
            var random = new SeededRandom(1);
            for (int i = 0; i < 100000; i++)
            {
                double value = random.NextDouble();
                Assert.That(value, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
            }
        }

        [Test]
        public void StateAllowsResumingSequence()
        {
            var original = new SeededRandom(99);
            original.NextUInt();
            original.NextUInt();
            var resumed = new SeededRandom(original.State);
            Assert.AreEqual(original.NextUInt(), resumed.NextUInt());
        }
    }
}
