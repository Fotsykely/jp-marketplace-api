using JP.Api.Entities;

namespace JP.Api.UnitTests.Entities;

public class ItemStateMachineTests
{
    private static Item NewItem(ItemStatus status) => new()
    {
        Title = "Robe",
        Description = "Robe d'été",
        Status = status
    };

    // [Theory] runs the same test once per [InlineData] line: one row = one case.
    [Theory]
    [InlineData(ItemStatus.Available, ItemStatus.Reserved)]
    [InlineData(ItemStatus.Reserved, ItemStatus.Sold)]
    [InlineData(ItemStatus.Reserved, ItemStatus.Available)]
    public void CanTransitionTo_AllowedTransition_ReturnsTrue(ItemStatus from, ItemStatus to)
    {
        var item = NewItem(from);

        Assert.True(item.CanTransitionTo(to));
    }

    [Theory]
    [InlineData(ItemStatus.Available, ItemStatus.Available)]
    [InlineData(ItemStatus.Available, ItemStatus.Sold)]   // no reservation = no lock
    [InlineData(ItemStatus.Reserved, ItemStatus.Reserved)]
    [InlineData(ItemStatus.Sold, ItemStatus.Available)]   // Sold is a dead end
    [InlineData(ItemStatus.Sold, ItemStatus.Reserved)]
    [InlineData(ItemStatus.Sold, ItemStatus.Sold)]
    public void CanTransitionTo_ForbiddenTransition_ReturnsFalse(ItemStatus from, ItemStatus to)
    {
        var item = NewItem(from);

        Assert.False(item.CanTransitionTo(to));
    }

    [Fact]
    public void TryTransitionTo_AllowedTransition_ChangesStatusAndSetsUpdatedAt()
    {
        var item = NewItem(ItemStatus.Available);
        var before = DateTime.UtcNow;

        var result = item.TryTransitionTo(ItemStatus.Reserved);

        Assert.True(result);
        Assert.Equal(ItemStatus.Reserved, item.Status);
        Assert.NotNull(item.UpdatedAt);
        Assert.True(item.UpdatedAt >= before);
    }

    [Fact]
    public void TryTransitionTo_ForbiddenTransition_ChangesNothing()
    {
        var item = NewItem(ItemStatus.Available);

        var result = item.TryTransitionTo(ItemStatus.Sold);

        Assert.False(result);
        Assert.Equal(ItemStatus.Available, item.Status);
        Assert.Null(item.UpdatedAt);
    }
}
