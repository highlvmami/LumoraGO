namespace LumoraGo.Core.Tests;

public class QueryParserTests
{
    private static readonly IReadOnlySet<string> Keywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "=", "f", "c" };

    [Fact]
    public void Plain_text_has_no_keyword()
    {
        var q = QueryParser.Parse("  chrome ", Keywords);

        Assert.Null(q.ActionKeyword);
        Assert.Equal("chrome", q.Search);
        Assert.Equal("  chrome ", q.RawText);
    }

    [Fact]
    public void Keyword_followed_by_space_is_split_off()
    {
        var q = QueryParser.Parse("= 2+3*4", Keywords);

        Assert.Equal("=", q.ActionKeyword);
        Assert.Equal("2+3*4", q.Search);
    }

    [Fact]
    public void Keyword_alone_gives_empty_search()
    {
        var q = QueryParser.Parse("f", Keywords);

        Assert.Equal("f", q.ActionKeyword);
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void Word_starting_with_keyword_letter_is_not_a_keyword()
    {
        var q = QueryParser.Parse("firefox", Keywords);

        Assert.Null(q.ActionKeyword);
        Assert.Equal("firefox", q.Search);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_empty_query(string? input)
    {
        var q = QueryParser.Parse(input, Keywords);

        Assert.Null(q.ActionKeyword);
        Assert.True(q.IsEmpty);
    }
}
