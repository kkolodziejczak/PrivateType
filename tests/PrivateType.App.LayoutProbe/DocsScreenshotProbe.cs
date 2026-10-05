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
        Render(PortableSettings.Default, page: "ModelTab", docsDirectory, "settings-model.png");
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

    private static void Render(PortableSettings settings, string? page, string directory, string name)
    {
        var window = new SettingsWindow(
            settings,
            [new MicrophoneOption("default", "System default microphone")],
            new ProbeModelStore(SpeechModelCatalog.DefaultId))
        {
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Manual,
            Width = 620,
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
                Capture(window, Path.Combine(directory, name), scroll);
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

    // Crops the empty space below the page so the image ends at the Save button row.
    private static void Capture(Window window, string path, ScrollViewer scroll)
    {
        var bitmap = Snapshot(window);
        var width = bitmap.PixelWidth;
        var fullHeight = bitmap.PixelHeight;

        var unusedHeight = (int)Math.Floor(scroll.ViewportHeight - scroll.ExtentHeight);
        var pageBottom = (int)Math.Ceiling(scroll.TranslatePoint(new Point(0, scroll.ExtentHeight), window).Y);
        var top = new CroppedBitmap(bitmap, new Int32Rect(0, 0, width, pageBottom));
        var bottom = new CroppedBitmap(bitmap, new Int32Rect(0, pageBottom + unusedHeight, width, fullHeight - pageBottom - unusedHeight));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(top, new Rect(0, 0, width, top.PixelHeight));
            context.DrawImage(bottom, new Rect(0, top.PixelHeight, width, bottom.PixelHeight));
        }

        var cropped = new RenderTargetBitmap(width, top.PixelHeight + bottom.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        cropped.Render(visual);
        Save(cropped, path);
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
