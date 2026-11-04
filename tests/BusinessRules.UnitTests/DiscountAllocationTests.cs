namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class DiscountAllocationTests
{
    [TestMethod]
    public void DiscountAllocationDistributesRemainderCentsByLargestRemainder()
        => CollectionAssert.AreEqual(new[] { 0.04m, 0.03m, 0.03m }, DiscountAllocation.Allocate(0.10m, [1m, 1m, 1m]));

    [TestMethod]
    public void LeftoverCentsGoToTheLargestRemainder()
        => CollectionAssert.AreEqual(new[] { 0.01m, 0.01m, 0.03m }, DiscountAllocation.Allocate(0.05m, [1m, 2m, 4m]));

    [TestMethod]
    public void SharesAreProportionalToCapacity()
        => CollectionAssert.AreEqual(new[] { 7.50m, 2.50m }, DiscountAllocation.Allocate(10m, [30m, 10m]));

    [TestMethod]
    public void ZeroCapacityGetsNothing()
        => CollectionAssert.AreEqual(new[] { 0m, 5m, 0m }, DiscountAllocation.Allocate(5m, [0m, 5m, 0m]));

    [TestMethod]
    public void ZeroAmountAllocatesNothing()
        => CollectionAssert.AreEqual(new[] { 0m, 0m }, DiscountAllocation.Allocate(0m, [1m, 2m]));

    [TestMethod]
    public void FullAmountFillsEveryCapacity()
        => CollectionAssert.AreEqual(new[] { 0.33m, 0.01m, 0.10m }, DiscountAllocation.Allocate(0.44m, [0.33m, 0.01m, 0.10m]));

    [TestMethod]
    public void SharesSumToAmountAndStayWithinCapacity()
    {
        decimal[] capacities = [0.99m, 0.01m, 3.33m, 7.07m, 0.02m];

        for (var cents = 0; cents <= 1142; cents += 7)
        {
            var amount = cents / 100m;
            var shares = DiscountAllocation.Allocate(amount, capacities);

            Assert.AreEqual(amount, shares.Sum(), $"amount {amount}");
            for (var i = 0; i < shares.Length; i++)
            {
                Assert.IsTrue(shares[i] >= 0m && shares[i] <= capacities[i], $"amount {amount}, line {i}");
            }
        }
    }

    [TestMethod]
    public void DiscountAllocationRejectsAmountAboveCapacity()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiscountAllocation.Allocate(2.01m, [1m, 1m]));

    [TestMethod]
    public void RejectsNegativeAmount()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiscountAllocation.Allocate(-0.01m, [1m, 1m]));

    [TestMethod]
    public void RejectsAmountWithMoreThanTwoDecimals()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiscountAllocation.Allocate(0.005m, [1m, 1m]));
}
