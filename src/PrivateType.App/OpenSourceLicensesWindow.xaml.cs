using System.Windows;

namespace PrivateType.App;

public partial class OpenSourceLicensesWindow : Window
{
    public OpenSourceLicensesWindow()
    {
        InitializeComponent();
        Notices.Text = "PrivateType — MIT (maintainer-owned source and assets)\n\nBundled runtime and library notices\n\n• NeMo-Speech.cpp — Apache-2.0 with NVIDIA NOTICE\n• ggml — MIT\n• cpp-httplib — MIT\n• SentencePiece — Apache-2.0\n• Protocol Buffers — BSD 3-Clause\n• Abseil — Apache-2.0\n• utf8-range — MIT\n• NAudio 2.2.1 and companion assemblies — MIT\n• Self-contained .NET runtime — Microsoft .NET Library License and its accompanying notices\n\nThe Nemotron model is downloaded separately by the user under OpenMDW-1.1 and is not included in the application ZIP.\n\nBefore public release, this summary will be accompanied by the complete license texts and notices in the release licenses folder.";
    }

    private void Close(object sender, RoutedEventArgs e) => Close();
}
