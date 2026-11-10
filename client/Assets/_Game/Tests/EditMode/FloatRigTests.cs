using Fishing.Core;
using NUnit.Framework;

namespace Fishing.Tests
{
    public sealed class FloatRigTests
    {
        [Test]
        public void MaxDepthIsRodLengthMinusHalfMetre()
        {
            Assert.AreEqual(350, FloatRig.MaxDepthCm(400));
            Assert.AreEqual(190, FloatRig.MaxDepthCm(240));
            Assert.AreEqual(190, FloatRig.MaxDepthCm(245), "кратно шагу 10 см");
            Assert.AreEqual(FloatRig.MinDepthCm, FloatRig.MaxDepthCm(30), "не меньше наименьшего спуска");
        }

        [Test]
        public void ClampSnapsToStepAndLimits()
        {
            Assert.AreEqual(120, FloatRig.Clamp(123, 400));
            Assert.AreEqual(130, FloatRig.Clamp(125, 400));
            Assert.AreEqual(FloatRig.MinDepthCm, FloatRig.Clamp(5, 400));
            Assert.AreEqual(350, FloatRig.Clamp(900, 400));
            Assert.AreEqual(FloatRig.DefaultDepthCm, FloatRig.Clamp(0, 400), "не задан — по умолчанию");
            Assert.AreEqual(FloatRig.DefaultDepthCm, FloatRig.Clamp(-40, 400));
        }

        [Test]
        public void BaitHangsAtFloatDepthOrLiesOnBottom()
        {
            Assert.AreEqual(100f, FloatRig.BaitDepthCm(100, 240f));
            Assert.IsFalse(FloatRig.OnBottom(100, 240f));
            Assert.AreEqual(240f, FloatRig.BaitDepthCm(300, 240f), "глубже дна не опустится");
            Assert.IsTrue(FloatRig.OnBottom(300, 240f));
            Assert.IsTrue(FloatRig.OnBottom(240, 240f));
        }

        [Test]
        public void ProfileRemembersFloatDepth()
        {
            var fish = TestFish.Database(TestFish.Species("roach"));
            var profile = new PlayerProfile { FloatDepthCm = 180 };
            Assert.AreEqual(180, PlayerProfile.FromState(profile.ToState(), fish).FloatDepthCm);
            Assert.AreEqual(0, PlayerProfile.FromState(new ProfileState { version = PlayerProfile.StateVersion }, fish).FloatDepthCm);
        }
    }
}
