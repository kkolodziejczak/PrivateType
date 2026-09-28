using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class VocabularyEditorTests
{
    [Fact]
    public void Changing_scope_filters_rows_without_dropping_other_scopes()
    {
        var editor = new VocabularyEditor([new("Shared term", "shared"), new("Polski termin", "pl"), new("English term", "en")], "normal");

        Assert.Equal(["Shared term"], editor.Visible.Select(row => row.Phrase));
        editor.Scope = "pl";
        Assert.Equal(["Polski termin"], editor.Visible.Select(row => row.Phrase));
        Assert.Equal(3, editor.Entries.Count);
        Assert.Contains("Polish", editor.ScopeHint);
    }

    [Fact]
    public void New_rows_join_the_visible_scope_and_blank_rows_are_not_saved()
    {
        var editor = new VocabularyEditor([], "normal") { Scope = "en" };
        Assert.True(editor.IsEmpty);

        var typed = editor.Add();
        typed.Phrase = "  Kubernetes ";
        editor.Add();

        Assert.False(editor.IsEmpty);
        Assert.Equal([new VocabularyEntry("Kubernetes", "en")], editor.Entries);
        Assert.Equal("1 of 200 phrases across all languages", editor.CountText);
    }

    [Fact]
    public void Removing_a_row_only_affects_that_row()
    {
        var editor = new VocabularyEditor([new("Keep", "shared"), new("Drop", "shared")], "strong");

        editor.Remove(editor.Visible.Single(row => row.Phrase == "Drop"));

        Assert.Equal([new VocabularyEntry("Keep", "shared")], editor.Entries);
        Assert.Equal("strong", editor.Strength);
    }
}
