using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace PrivateType.App;

// A page of credits: what PrivateType is built with, what each part does and under which license, from Credits.json.
// Whether the license texts are in this copy is read from the licenses folder and only decides the notice and the actions.
public partial class OpenSourceLicensesWindow : Window
{
    internal const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";
    private readonly string licensesDirectory;

    public OpenSourceLicensesWindow() : this(Path.Combine(AppContext.BaseDirectory, "licenses"))
    {
    }

    // A portable release ships its notices and license texts in the licenses folder beside the app.
    internal OpenSourceLicensesWindow(string licensesDirectory)
    {
        InitializeComponent();
        this.licensesDirectory = licensesDirectory;
        var hasFolder = Directory.Exists(licensesDirectory);
        WritePage(Credits.Declared, hasFolder);
        // An action that could open nothing is not offered.
        FullNoticesButton.Visibility = File.Exists(NoticesPath) ? Visibility.Visible : Visibility.Collapsed;
        OpenLicenseTextsButton.Visibility = hasFolder ? Visibility.Visible : Visibility.Collapsed;
        GetCompleteZipButton.Visibility = hasFolder ? Visibility.Collapsed : Visibility.Visible;
    }

    private string NoticesPath => Path.Combine(licensesDirectory, NoticesFileName);

    private void WritePage(Credits credits, bool hasFolder)
    {
        Page.Blocks.Add(Space(22));
        Page.Blocks.Add(new Paragraph(new Run("What PrivateType is built with")) { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });
        Page.Blocks.Add(Note("PrivateType is MIT-licensed. Each part below keeps its own license.", top: 6));
        if (!hasFolder)
            Page.Blocks.Add(MissingTextsNotice());
        foreach (var component in credits.Components)
        {
            Page.Blocks.Add(new Paragraph(new Run(component.Group.ToUpperInvariant()))
            {
                Margin = new Thickness(0, 22, 0, 6), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Themed("ColorNeutral400")
            });
            Page.Blocks.Add(Entry(component));
        }
        Page.Blocks.Add(Note($"Not part of PrivateType: the NVIDIA speech models. You download them separately, under their own terms: {credits.ModelTerms}.", top: 22));
        Page.Blocks.Add(Space(20));
    }

    // Fixed space at an edge of the page, where flow layout would drop a margin.
    private static BlockUIContainer Space(double height) => new(new Border { Height = height }) { Margin = new Thickness(0) };

    // Three lines: the name and its maker, what it does, its license (and, for the engine, what it carries under their own).
    private Paragraph Entry(CreditedComponent component)
    {
        var entry = Lines(new Paragraph { Margin = new Thickness(0) });
        entry.Inlines.Add(new Run(component.Name) { FontWeight = FontWeights.SemiBold });
        if (component.Maker is not null)
            entry.Inlines.Add(Quiet($" {component.Maker}"));
        entry.Inlines.Add(new LineBreak());
        entry.Inlines.Add(Quiet(component.Role));
        entry.Inlines.Add(new LineBreak());
        entry.Inlines.Add(new Run(component.License));
        if (component.Includes is not null)
            entry.Inlines.Add(Quiet($". {component.Includes}"));
        return entry;
    }

    private Paragraph Note(string text, double top) => Lines(new Paragraph(Quiet(text)) { Margin = new Thickness(0, top, 0, 0) });

    // The notice strip: no licenses folder beside this copy, so say what is missing and where the complete set comes from.
    private BlockUIContainer MissingTextsNotice()
    {
        var mark = new ContentControl { Style = (Style)FindResource("WarningMark"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = "The license texts aren't in this copy", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock { Text = "They come with the complete PrivateType portable ZIP.", Foreground = Themed("ColorNeutral400"), TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        row.Children.Add(mark);
        row.Children.Add(words);
        return new BlockUIContainer(new Border { Background = Themed("ColorNeutral900"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = row })
        {
            Margin = new Thickness(0, 18, 0, 0)
        };
    }

    private Run Quiet(string text) => new(text) { Foreground = Themed("ColorNeutral400") };

    private static Paragraph Lines(Paragraph paragraph)
    {
        paragraph.LineHeight = 19;
        paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        return paragraph;
    }

    private Brush Themed(string key) => (Brush)FindResource(key);

    // The notices open unmodified in the system's text viewer.
    private void OpenFullNotices(object sender, RoutedEventArgs e) => OpenIfPresent(NoticesPath, File.Exists(NoticesPath));

    private void OpenLicenseTexts(object sender, RoutedEventArgs e) => OpenIfPresent(licensesDirectory, Directory.Exists(licensesDirectory));

    private void GetCompleteZip(object sender, RoutedEventArgs e) => Process.Start(CompleteZipBrowserStartInfo());

    internal static ProcessStartInfo CompleteZipBrowserStartInfo() => new(ProjectLinks.Releases.AbsoluteUri) { UseShellExecute = true };

    private static void OpenIfPresent(string path, bool present)
    {
        if (present)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Close(object sender, RoutedEventArgs e) => Close();

    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
}
