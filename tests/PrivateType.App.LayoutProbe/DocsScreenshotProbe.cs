using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

// Renders each Settings page whole, with realistic content, for docs/SETTINGS.md.
internal static class DocsScreenshotProbe
{
    private const double PageHeight = 1500;

    public static void Run(string outputDirectory)
    {
        var docsDirectory = Path.Combine(outputDirectory, "docs");
        Directory.CreateDirectory(docsDirectory);
        Render(PortableSettings.Default, page: null, docsDirectory, "settings-general.png");
        Render(VocabularySample, page: "VocabularyTab", docsDirectory, "settings-vocabulary.png");
        var graphicsCard = new EngineDevice("vulkan:1", "NVIDIA GeForce MX450", EngineDeviceKind.GraphicsCard);
        Render(PortableSettings.Default, page: "ModelTab", docsDirectory, "settings-model.png", ModelDeviceStatus.Of(graphicsCard, fellBack: false, graphicsCard));
        Console.WriteLine($"Rendered settings documentation pages: {docsDirectory}");
    }

    private static PortableSettings VocabularySample => PortableSettings.Default with
    {
        Vocabulary =
        [
            new("PrivateType", VocabularyScopes.Shared),
            new("Kubernetes", VocabularyScopes.Shared),
            new("PostgreSQL", VocabularyScopes.Shared),
            new("WireGuard", VocabularyScopes.Shared)
        ],
        VocabularyCorrections =
        [
            new("three D printing", "3D printing", VocabularyScopes.Shared),
            new("fusion three sixty", "Fusion 360", VocabularyScopes.Shared)
        ],
        VocabularyPacks =
        [
            new("Software development", VocabularyScopes.Shared, IsEnabled: true, ["GitHub", "TypeScript", "pull request", "CI pipeline"])
        ]
    };

    private static void Render(PortableSettings settings, string? page, string directory, string name, ModelDeviceStatus? deviceStatus = null)
    {
        var window = new SettingsWindow(
            settings,
            [new MicrophoneOption("default", "System default microphone")],
            new ProbeModelStore(SpeechModelCatalog.DefaultId),
            deviceStatus: deviceStatus)
        {
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Manual,
            Width = 660,
            Height = PageHeight
        };
        window.Show();
        try
        {
            if (page is not null)
                ((RadioButton)window.FindName(page)).IsChecked = true;
            Flush(window);
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            if (scroll.ScrollableHeight < 1)
            {
                // Shrink the window to the page so the image ends at the Save button row, sidebar intact.
                window.Height -= Math.Floor(scroll.ViewportHeight - scroll.ExtentHeight);
                Flush(window);
                Save(Snapshot(window), Path.Combine(directory, name));
                return;
            }

            // Windows caps a window at the screen height, so a long page becomes a top and a bottom image.
            Save(Snapshot(window), Path.Combine(directory, name.Replace(".png", "-top.png")));
            scroll.ScrollToBottom();
            Flush(window);
            Save(Snapshot(window), Path.Combine(directory, name.Replace(".png", "-bottom.png")));
        }
        finally
        {
            window.Close();
        }
    }

    private static RenderTargetBitmap Snapshot(Window window)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
