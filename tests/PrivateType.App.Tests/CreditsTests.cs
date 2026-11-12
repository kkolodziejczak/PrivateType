using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class CreditsTests
{
    [Fact]
    public void Credits_every_component_the_portable_release_ships_with_its_license()
    {
        var components = Credits.Declared.Components.ToDictionary(component => component.Name, component => component.License);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["NeMo-Speech.cpp"] = "Apache-2.0",
                ["Vulkan loader"] = "MIT and Apache-2.0",
                ["NAudio"] = "MIT",
                [".NET runtime"] = ".NET Library License",
                ["Kokoro-82M"] = "Apache-2.0"
            },
            components);
        Assert.All(Credits.Declared.Components, component => Assert.False(string.IsNullOrWhiteSpace(component.Role)));
    }

    [Fact]
    public void Names_the_engines_bundled_libraries_so_none_reads_as_Apache_licensed()
    {
        var engine = Credits.Declared.Components.Single(component => component.Name == "NeMo-Speech.cpp");

        Assert.Contains("ggml", engine.Includes);
        Assert.Contains("under their own licenses", engine.Includes);
    }

    [Fact]
    public void States_the_separately_downloaded_models_with_the_terms_the_model_catalog_links()
    {
        Assert.Equal(
            $"{SpeechModelCatalog.Parakeet.LicenseName} (Parakeet) and {SpeechModelCatalog.Nemotron.LicenseName} (Nemotron)",
            Credits.Declared.ModelTerms);
    }

    [Fact]
    public void Opens_the_releases_page_for_the_complete_zip_in_the_default_browser()
    {
        var startInfo = OpenSourceLicensesWindow.CompleteZipBrowserStartInfo();

        Assert.Equal("https://github.com/kkolodziejczak/privatetype/releases", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }
}
