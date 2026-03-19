using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class FilesPage : ContentPage
    {
        private readonly IFileTransferService _fileTransferService;

        public ObservableCollection<PendingFileItem> PendingFiles { get; } = [];
        public ObservableCollection<FileEntry> History => _fileTransferService.History;
        public bool HasPendingFiles => PendingFiles.Count > 0;

        public string StatusText => _fileTransferService.IsConnected
            ? "🟢  Sincronización activa"
            : "🔴  Sin conexión al servidor";

        public Color StatusColor => _fileTransferService.IsConnected
            ? Color.FromArgb("#1B5E20")
            : Color.FromArgb("#B71C1C");

        public Command AddFilesCommand { get; }
        public Command<PendingFileItem> RemovePendingCommand { get; }
        public Command SendCommand { get; }
        public Command<FileEntry> DownloadCommand { get; }

        public FilesPage(IFileTransferService fileTransferService)
        {
            _fileTransferService = fileTransferService;

            AddFilesCommand = new Command(async () =>
            {
                var picks = await FilePicker.Default.PickMultipleAsync();
                foreach (var f in picks)
                    if (PendingFiles.All(p => p.FullPath != f.FullPath))
                        PendingFiles.Add(new PendingFileItem(f.FullPath));
            });

            RemovePendingCommand = new Command<PendingFileItem>(item => PendingFiles.Remove(item));

            SendCommand = new Command(
                async () =>
                {
                    var paths = PendingFiles.Select(p => p.FullPath).ToList();
                    PendingFiles.Clear();
                    await _fileTransferService.SendFilesAsync(paths);
                },
                () => PendingFiles.Count > 0 && _fileTransferService.IsConnected);

            DownloadCommand = new Command<FileEntry>(async entry =>
            {
                if (entry is null) return;

                var destPath = await PickSavePathAsync(entry.FileName);
                if (destPath is null) return;

                try
                {
                    await _fileTransferService.DownloadFileAsync(entry, destPath);
                    await DisplayAlert("Descargado", $"Archivo guardado en:\n{destPath}", "Aceptar");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo descargar el archivo:\n{ex.Message}", "Aceptar");
                }
            });

            InitializeComponent();
            BindingContext = this;

            _fileTransferService.ConnectionStateChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                    ((Command)SendCommand).ChangeCanExecute();
                });

            PendingFiles.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasPendingFiles));
                ((Command)SendCommand).ChangeCanExecute();
            };

#if WINDOWS
            DropZone.Loaded += SetupWindowsDragDrop;
#endif
        }

#if WINDOWS
        private async Task<string> PickSavePathAsync(string fileName)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            var hwnd = ((Microsoft.Maui.MauiWinUIWindow)Window.Handler.PlatformView).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            return folder is not null ? Path.Combine(folder.Path, fileName) : null;
        }

        private void SetupWindowsDragDrop(object sender, EventArgs e)
        {
            DropZone.Loaded -= SetupWindowsDragDrop;
            if (DropZone.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement nativeView) return;

            nativeView.AllowDrop = true;
            nativeView.DragOver += (_, args) =>
            {
                args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                args.DragUIOverride.Caption = "Soltar para agregar";
            };
            nativeView.Drop += async (_, args) =>
            {
                var items = await args.DataView.GetStorageItemsAsync();
                var paths = items
                    .OfType<Windows.Storage.StorageFile>()
                    .Select(f => f.Path)
                    .ToList();
                if (paths.Count == 0) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var p in paths)
                        if (PendingFiles.All(x => x.FullPath != p))
                            PendingFiles.Add(new PendingFileItem(p));
                });
            };
        }
#else
        private Task<string> PickSavePathAsync(string fileName)
        {
            var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "Downloads");
            Directory.CreateDirectory(dir);
            return Task.FromResult(Path.Combine(dir, fileName));
        }
#endif
    }
}

