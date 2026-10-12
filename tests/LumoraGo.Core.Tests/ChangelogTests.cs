namespace LumoraGo.Core.Tests;

public class ChangelogTests
{
    private const string Markdown = """
        # Sürüm notları

        ## [0.2.0] - 2026-10-12
        - Otomatik güncelleme
        * /version komutu

        Açıklama satırı madde değil.

        ## 0.1.0
        - İlk sürüm
        """;

    [Fact]
    public void Reads_bullets_of_requested_version_only()
    {
        Assert.Equal(["Otomatik güncelleme", "/version komutu"], Changelog.GetNotes(Markdown, "0.2.0"));
        Assert.Equal(["İlk sürüm"], Changelog.GetNotes(Markdown, "0.1.0"));
    }

    [Fact]
    public void Unknown_version_has_no_notes()
    {
        Assert.Empty(Changelog.GetNotes(Markdown, "0.3.0"));
    }

    [Fact]
    public void Version_prefix_must_match_exactly()
    {
        Assert.Empty(Changelog.GetNotes("## 0.10.0\n- on\n", "0.1.0"));
    }

    [Fact]
    public void Handles_windows_line_endings()
    {
        Assert.Equal(["a", "b"], Changelog.GetNotes("## 1.0.0\r\n- a\r\n- b\r\n", "1.0.0"));
    }
}
