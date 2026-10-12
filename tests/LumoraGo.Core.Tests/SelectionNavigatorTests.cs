namespace LumoraGo.Core.Tests;

public class SelectionNavigatorTests
{
    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 0)]
    [InlineData(0, -1, 3, 2)]
    [InlineData(1, -1, 3, 0)]
    [InlineData(-1, 1, 3, 0)]
    [InlineData(-1, -1, 3, 2)]
    [InlineData(5, 1, 3, 0)]
    [InlineData(0, 1, 0, -1)]
    public void Moves_and_wraps(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, SelectionNavigator.Move(current, delta, count));
    }
}
