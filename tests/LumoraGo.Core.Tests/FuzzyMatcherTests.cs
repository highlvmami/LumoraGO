using LumoraGo.Core.Search;

namespace LumoraGo.Core.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("chr", "Google Chrome")]
    [InlineData("not", "Notepad")]
    [InlineData("hesap", "Hesap Makinesi")]
    [InlineData("cmd", "Command Prompt")]
    [InlineData("calis", "Çalışma Alanı")]
    [InlineData("ISIK", "ışık")]
    public void Matches(string query, string target)
    {
        Assert.True(FuzzyMatcher.Score(query, target) > 0);
    }

    [Theory]
    [InlineData("xyz", "Visual Studio Code")]
    [InlineData("codev", "Visual Studio Code")]
    [InlineData("", "Notepad")]
    [InlineData("notepadplus", "Notepad")]
    public void Does_not_match(string query, string target)
    {
        Assert.Equal(0, FuzzyMatcher.Score(query, target));
    }

    [Fact]
    public void Acronym_beats_scattered_letters()
    {
        var vsCode = FuzzyMatcher.Score("vsc", "Visual Studio Code");
        var redistributable = FuzzyMatcher.Score("vsc", "Microsoft Visual C++ Redistributable");
        var installer = FuzzyMatcher.Score("vsc", "Visual Studio Installer");

        Assert.True(vsCode > redistributable, $"{vsCode} <= {redistributable}");
        Assert.True(vsCode > installer, $"{vsCode} <= {installer}");
    }

    [Fact]
    public void Prefix_beats_middle_match()
    {
        Assert.True(FuzzyMatcher.Score("note", "Notepad") > FuzzyMatcher.Score("note", "OneNote"));
    }

    [Fact]
    public void Consecutive_beats_gapped()
    {
        Assert.True(FuzzyMatcher.Score("pai", "Paint") > FuzzyMatcher.Score("pai", "Photos App Installer"));
    }

    [Fact]
    public void Camel_case_counts_as_word_start()
    {
        Assert.True(FuzzyMatcher.Score("pr", "PowerRename") > FuzzyMatcher.Score("pr", "Paperback"));
    }

    [Fact]
    public void Folding_keeps_length()
    {
        Assert.Equal("calisma alani", FuzzyMatcher.Fold("Çalışma Alanı"));
        Assert.Equal("Çalışma Alanı".Length, FuzzyMatcher.Fold("Çalışma Alanı").Length);
    }
}
