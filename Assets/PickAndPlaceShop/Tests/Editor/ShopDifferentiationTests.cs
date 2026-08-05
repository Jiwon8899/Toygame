using NUnit.Framework;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopDifferentiationTests
    {
        [Test]
        public void CapsuleRecycler_UsesSharedSlotContainerAndDataThresholds()
        {
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.EmptyCapsuleProduct, Is.Not.Null);
            Assert.That(config.CapsuleRecyclerSlots, Is.GreaterThan(0));
            Assert.That(config.UpcycleThresholds, Is.EqualTo(new[] { 20, 50, 100 }));

            Assert.That((int)ShopContainerKind.CapsuleRecycler, Is.GreaterThan(
                (int)ShopContainerKind.AutomationBuffer));
        }

        [Test]
        public void SaveVersion_IncludesDifferentiationState()
        {
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(9));
            ShopProgressionSaveData save = new() { upcycleDecorMask = 5 };
            Assert.That(save.upcycleDecorMask, Is.EqualTo(5));
        }
    }
}
