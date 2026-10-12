using LumoraGo.Core.Plugins;
using LumoraGo.Core.Search;
using LumoraGo.Plugin;

namespace LumoraGo.Core.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("10/4", 2.5)]
    [InlineData("2,5*2", 5)]
    [InlineData("2.5*2", 5)]
    [InlineData("2^3^2", 512)]
    [InlineData("-3+5", 2)]
    [InlineData("10 % 3", 1)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("= 1+1", 2)]
    [InlineData("3×4÷2", 6)]
    [InlineData("100-1", 99)]
    public void Evaluates(string input, double expected)
    {
        Assert.True(Calculator.TryEvaluate(input, out var value));
        Assert.Equal(expected, value, 10);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("2026")]
    [InlineData("-5")]
    [InlineData("1/0")]
    [InlineData("2+")]
    [InlineData("1.234.567+1")]
    [InlineData("notepad++")]
    [InlineData("foo(2)")]
    public void Ignores_non_math(string input)
    {
        Assert.False(Calculator.TryEvaluate(input, out _));
    }

    [Theory]
    [InlineData(1234.5, true, "1.234,5")]
    [InlineData(1234.5, false, "1234,5")]
    [InlineData(14, true, "14")]
    [InlineData(1.0 / 3, false, "0,3333333333")]
    public void Formats_in_turkish(double value, bool grouping, string expected)
    {
        Assert.Equal(expected, Calculator.Format(value, grouping));
    }

    [Fact]
    public async Task Enter_copies_result_without_grouping()
    {
        var host = new NullHost();
        var plugin = new CalculatorPlugin();
        plugin.Init(new PluginContext(CalculatorPlugin.Metadata, Path.GetTempPath(), host));

        var result = Assert.Single(await plugin.QueryAsync(new Query("1000+234,5", null, "1000+234,5"), CancellationToken.None));

        Assert.Equal("= 1.234,5", result.Title);
        Assert.True(result.Action(new ActionContext(ActionModifiers.None)));
        Assert.Equal("1234,5", Assert.Single(host.Copied));
    }
}
