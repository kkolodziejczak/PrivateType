using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PrivateType.Core;

namespace PrivateType.App;

// What the Model page needs from model storage; the layout probe supplies a fake.
internal interface IModelStore
{
    ModelStorageMode StorageMode { get; }
    bool IsPresent(SpeechModelDefinition model);
    Task DownloadAsync(SpeechModelDefinition model, IProgress<long> progress, CancellationToken cancellationToken);
    Task DeleteAsync(SpeechModelDefinition model, CancellationToken cancellationToken);
}

internal sealed class LibraryModelStore(SpeechModelLibrary library) : IModelStore
{
    public ModelStorageMode StorageMode => library.Storage(SpeechModelCatalog.Get(SpeechModelCatalog.DefaultId)).Mode;
    public bool IsPresent(SpeechModelDefinition model) => library.Provisioner(model).IsPresent();
    public Task DownloadAsync(SpeechModelDefinition model, IProgress<long> progress, CancellationToken cancellationToken)
        => library.Provisioner(model).EnsureAvailableAsync(progress, cancellationToken);
    public Task DeleteAsync(SpeechModelDefinition model, CancellationToken cancellationToken)
        => library.Provisioner(model).DeleteAsync(cancellationToken);
}

// The Model settings page: download, choose, and delete local speech models. Downloads and
// deletions happen immediately; the chosen model takes effect when settings are saved.
internal sealed class ModelLibraryEditor : INotifyPropertyChanged
{
    private readonly IModelStore store;
    private string selectedId;

    // Selected may differ from active while reviewing imported settings; only Save makes it active.
    public ModelLibraryEditor(IModelStore store, string activeId, string? selectedId = null)
    {
        this.store = store;
        ActiveId = activeId;
        this.selectedId = selectedId ?? activeId;
        Rows = SpeechModelCatalog.All.Select(model => new SpeechModelRow(this, model, store.IsPresent(model))).ToArray();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SpeechModelRow> Rows { get; }
    public string ActiveId { get; }

    public string SelectedId
    {
        get => selectedId;
        private set
        {
            selectedId = value;
            Notify();
            Refresh();
        }
    }

    public bool IsBusy => Rows.Any(row => row.IsDownloading || row.IsDeleting);

    public string StorageText => store.StorageMode == ModelStorageMode.Portable
        ? "Models are kept in this copy's app\\models folder."
        : "Models are shared by PrivateType versions for this Windows account. Deleting one here also removes it for those versions; they download it again when needed.";

    public void Use(SpeechModelRow row)
    {
        if (row.CanUse)
            SelectedId = row.Model.Id;
    }

    public async Task DownloadAsync(SpeechModelRow row)
    {
        if (row.IsPresent || row.IsDownloading)
            return;

        row.Error = null;
        row.BeginDownload();
        Notify(nameof(IsBusy));
        try
        {
            await store.DownloadAsync(row.Model, new Progress<long>(row.ReportProgress), row.Cancellation!.Token);
            row.IsPresent = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            row.Error = $"Download failed: {exception.Message}";
        }
        finally
        {
            row.EndDownload();
            Notify(nameof(IsBusy));
            Refresh();
        }
    }

    public void CancelDownload(SpeechModelRow row) => row.Cancellation?.Cancel();

    public void CancelAll()
    {
        foreach (var row in Rows)
            row.Cancellation?.Cancel();
    }

    public async Task DeleteAsync(SpeechModelRow row)
    {
        if (!row.CanDelete)
            return;

        row.Error = null;
        row.IsDeleting = true;
        Notify(nameof(IsBusy));
        try
        {
            await store.DeleteAsync(row.Model, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            row.Error = "Could not delete: another PrivateType copy may be using this model. Close it and try again.";
        }
        finally
        {
            row.IsDeleting = false;
            row.IsPresent = store.IsPresent(row.Model);
            Notify(nameof(IsBusy));
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (var row in Rows)
            row.Refresh();
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal sealed class SpeechModelRow(ModelLibraryEditor owner, SpeechModelDefinition model, bool isPresent) : INotifyPropertyChanged
    {
        private bool isPresent = isPresent;
        private string? error;
        private long downloadedBytes;
        private bool isDeleting;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SpeechModelDefinition Model { get; } = model;
        public string Name => Model.DisplayName;
        public string Summary => Model.Summary;
        public string Details => $"{SpeechModelCatalog.SizeLabel(Model)} · {Model.LicenseName} model terms";
        public Uri LicenseUri => Model.LicenseUri;
        public CancellationTokenSource? Cancellation { get; private set; }
        public bool IsDownloading => Cancellation is not null;
        public bool IsSelected => owner.SelectedId == Model.Id;
        public bool IsActive => owner.ActiveId == Model.Id;

        public bool IsDeleting
        {
            get => isDeleting;
            set
            {
                isDeleting = value;
                Refresh();
            }
        }

        public bool IsPresent
        {
            get => isPresent;
            set
            {
                isPresent = value;
                Refresh();
            }
        }

        public string? Error
        {
            get => error;
            set
            {
                error = value;
                Notify();
                Notify(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(error);
        public bool CanDownload => !IsPresent && !IsDownloading;
        public bool CanUse => IsPresent && !IsDownloading && !IsDeleting && !IsSelected;
        // The model in use and the one about to be used cannot be deleted.
        public bool CanDelete => IsPresent && !IsDownloading && !IsDeleting && !IsSelected && !IsActive;
        public double ProgressPercent => Math.Min(100, downloadedBytes * 100d / Model.Manifest.ExpectedBytes);

        public string StatusText => IsDeleting
            ? "Deleting…"
            : IsDownloading
            ? $"Downloading… {downloadedBytes / 1024d / 1024d:F0} of {Model.Manifest.ExpectedBytes / 1024d / 1024d:F0} MiB"
            : !IsPresent
                ? "Not downloaded"
                : IsSelected && IsActive
                    ? "In use"
                    : IsSelected
                        ? "Selected — used after you save"
                        : IsActive
                            ? "In use until you save"
                            : "Downloaded";

        public string DownloadName => $"Download {Name}";
        public string UseName => $"Use {Name}";
        public string DeleteName => $"Delete {Name}";
        public string CancelName => $"Cancel {Name} download";

        internal void BeginDownload()
        {
            downloadedBytes = 0;
            Cancellation = new CancellationTokenSource();
            Refresh();
        }

        internal void EndDownload()
        {
            Cancellation?.Dispose();
            Cancellation = null;
            Refresh();
        }

        internal void ReportProgress(long downloaded)
        {
            downloadedBytes = downloaded;
            Notify(nameof(ProgressPercent));
            Notify(nameof(StatusText));
        }

        internal void Refresh()
        {
            foreach (var name in new[] { nameof(IsPresent), nameof(IsDownloading), nameof(IsDeleting), nameof(IsSelected), nameof(CanDownload), nameof(CanUse), nameof(CanDelete), nameof(StatusText), nameof(ProgressPercent) })
                Notify(name);
        }

        private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
