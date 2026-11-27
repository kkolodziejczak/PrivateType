using ParakeetRuntime.Net;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class RuntimeSpeechEngineTests
{
    // The in-process engine loads the Parakeet file PrivateType downloads, and ParakeetRuntime refuses
    // any file but its own pin. A package update that pins another file must update the download too.
    [Fact]
    public void Downloads_the_exact_Parakeet_file_the_runtime_package_loads()
    {
        var downloaded = SpeechModelCatalog.Parakeet.Manifest;
        var loaded = SpeechModel.Parakeet;

        Assert.Equal(loaded.Source, downloaded.DownloadUri);
        Assert.Equal(loaded.FileName, downloaded.FileName);
        Assert.Equal(loaded.Bytes, downloaded.ExpectedBytes);
        Assert.Equal(loaded.Sha256, downloaded.Sha256, ignoreCase: true);
    }
}
