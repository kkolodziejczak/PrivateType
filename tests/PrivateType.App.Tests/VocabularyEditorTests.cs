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
        Assert.Equal("English dictation uses 1 of 200 phrases. 1 of 1,000 stored.", editor.BudgetText);
    }

    [Fact]
    public void Removing_a_row_only_affects_that_row()
    {
        var editor = new VocabularyEditor([new("Keep", "shared"), new("Drop", "shared")], "strong");

        editor.Remove(editor.Visible.Single(row => row.Phrase == "Drop"));

        Assert.Equal([new VocabularyEntry("Keep", "shared")], editor.Entries);
        Assert.Equal("strong", editor.Strength);
    }

    [Fact]
    public void Lists_packs_for_the_selected_language_unless_showing_all()
    {
        var editor = new VocabularyEditor([], "normal", [new("Dev", "en", true, ["Kubernetes"]), new("Names", "shared", false, ["PrivateType"])]);

        Assert.Equal(["Names"], editor.VisiblePacks.Select(pack => pack.Name));
        editor.Scope = "en";
        Assert.Equal(["Dev"], editor.VisiblePacks.Select(pack => pack.Name));
        editor.ShowAllPacks = true;
        Assert.Equal(["Dev", "Names"], editor.VisiblePacks.Select(pack => pack.Name));
        Assert.Equal("English · 1 phrase", editor.VisiblePacks[0].Summary);
    }

    [Fact]
    public void Enabling_a_pack_updates_the_budget_line()
    {
        var editor = new VocabularyEditor([new("MVVM", "shared")], "normal", [new("Names", "shared", false, ["PrivateType", "Nemotron"])]);
        Assert.StartsWith("Automatic dictation uses 1 of 200", editor.BudgetText);

        editor.VisiblePacks.Single().IsEnabled = true;

        Assert.StartsWith("Automatic dictation uses 3 of 200", editor.BudgetText);
        Assert.True(editor.Packs.Single().IsEnabled);
    }

    [Fact]
    public void Suggests_unique_names_from_file_names()
    {
        var editor = new VocabularyEditor([], "normal", [new("Software development", "en", true, ["a"])]);

        Assert.Equal("Software development", VocabularyEditor.NameFromFile(@"C:\packs\software-development.privatetype-vocabulary.json"));
        Assert.Equal("Product names", VocabularyEditor.NameFromFile("product_names.json"));
        Assert.Equal("Software development (2)", editor.SuggestName("Software development"));
        Assert.True(editor.IsNameTaken(" Software development "));
    }

    [Fact]
    public void Rejects_adding_a_pack_that_overflows_a_language_budget_and_names_it()
    {
        var english = Enumerable.Range(0, 150).Select(index => $"en {index}").ToArray();
        var editor = new VocabularyEditor([], "normal", [new("English", "en", true, english)]);

        var error = editor.ValidateAdding(new("More", "en", true, Enumerable.Range(0, 60).Select(index => $"more {index}").ToArray()));

        Assert.NotNull(error);
        Assert.Contains("English", error);
        Assert.Null(editor.ValidateAdding(new("Off", "en", false, ["x"])));
    }

    [Fact]
    public void Pack_edits_and_removal_change_only_the_local_copy()
    {
        var editor = new VocabularyEditor([], "normal", [new("Dev", "en", true, ["Kubernetes"])]) { ShowAllPacks = true };
        var item = editor.VisiblePacks.Single();

        editor.ReplacePack(item, new("Development", "pl", true, ["Kubernetes", "Nginx"]));
        Assert.Equal(("Development", "pl", 2), (editor.Packs.Single().Name, editor.Packs.Single().Scope, editor.Packs.Single().Phrases.Count));

        editor.RemovePack(item);
        Assert.Empty(editor.Packs);
    }
}
