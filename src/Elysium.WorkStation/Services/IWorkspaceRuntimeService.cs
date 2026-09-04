namespace Elysium.WorkStation.Services
{
    public interface IWorkspaceRuntimeService
    {
        bool IsStarted { get; }
        Task EnsureStartedAsync();
    }

    public sealed class WorkspaceRuntimeService : IWorkspaceRuntimeService
    {
        private readonly IRoleService _roleService;
        private readonly ISettingsService _settingsService;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly IFileTransferService _fileTransferService;
        private readonly IFolderSyncService _folderSyncService;
        private readonly ICleanupService _cleanupService;
        private readonly IKanbanCleanupService _kanbanCleanupService;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _isStarted;

        public bool IsStarted => _isStarted;

        public WorkspaceRuntimeService(
            IRoleService roleService,
            ISettingsService settingsService,
            IClipboardSyncService clipboardSyncService,
            IFileTransferService fileTransferService,
            IFolderSyncService folderSyncService,
            ICleanupService cleanupService,
            IKanbanCleanupService kanbanCleanupService)
        {
            _roleService = roleService;
            _settingsService = settingsService;
            _clipboardSyncService = clipboardSyncService;
            _fileTransferService = fileTransferService;
            _folderSyncService = folderSyncService;
            _cleanupService = cleanupService;
            _kanbanCleanupService = kanbanCleanupService;
        }

        public async Task EnsureStartedAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_isStarted &&
                    _clipboardSyncService.IsConnected &&
                    _fileTransferService.IsConnected &&
                    _folderSyncService.IsConnected)
                {
                    return;
                }

                if (!_settingsService.IsConfigured)
                {
                    throw new InvalidOperationException("La app no esta configurada. Abre Configuracion y define el servidor.");
                }

                if (_roleService.CurrentRole == Models.AppRole.Undetermined)
                {
                    throw new InvalidOperationException("El rol de esta instancia aun no esta definido. Abre la app para seleccionar Servidor o Cliente.");
                }

                var hubUrl = _roleService.CurrentRole == Models.AppRole.Server
                    ? $"http://localhost:{_settingsService.ServerPort}/hubs/workstation"
                    : _settingsService.HubUrl;

                await _clipboardSyncService.StartAsync(hubUrl);
                await _fileTransferService.StartAsync(hubUrl);
                await _folderSyncService.StartAsync(hubUrl);
                await _cleanupService.StartAsync();
                await _kanbanCleanupService.StartAsync();
                _isStarted = true;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
