using Microsoft.Extensions.Logging;

using Elysium.WorkStation.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Handlers;
#if WINDOWS
using Elysium.WorkStation.Controls;
using Microsoft.UI.Xaml.Controls;
#endif

namespace Elysium.WorkStation
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureMauiHandlers(handlers =>
                {
#if WINDOWS
                    ButtonHandler.Mapper.AppendToMapping("GlobalButtonAnimations", (handler, view) =>
                    {
                        if (handler.PlatformView is Microsoft.UI.Xaml.Controls.Button platformButton &&
                            view is Microsoft.Maui.Controls.VisualElement element)
                        {
                            GlobalButtonAnimations.Attach(platformButton, element);
                        }
                    });

                    ImageButtonHandler.Mapper.AppendToMapping("GlobalButtonAnimations", (handler, view) =>
                    {
                        if (handler.PlatformView is Microsoft.UI.Xaml.Controls.Button platformButton &&
                            view is Microsoft.Maui.Controls.VisualElement element)
                        {
                            GlobalButtonAnimations.Attach(platformButton, element);
                        }
                    });

                    WebViewHandler.Mapper.AppendToMapping("WebViewNoFlashBackground", (handler, view) =>
                    {
                        if (handler.PlatformView is WebView2 webView)
                        {
                            var isDark = (Application.Current?.UserAppTheme ?? AppTheme.Light) == AppTheme.Dark;
                            var bg = isDark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                            webView.DefaultBackgroundColor = bg;
                        }
                    });
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<Services.ISettingsService,      Services.SettingsService>();
            builder.Services.AddSingleton<Services.IClipboardSyncService,  Services.ClipboardSyncService>();
            builder.Services.AddSingleton<Services.IFileTransferService,   Services.FileTransferService>();
            builder.Services.AddSingleton<Services.IFolderSyncService, Services.FolderSyncService>();
            builder.Services.AddSingleton<Services.IRemoteToolPlugin, Services.GitRemoteToolPlugin>();
            builder.Services.AddSingleton<Services.IRemoteToolPlugin, Services.RemoteShellToolPlugin>();
            builder.Services.AddSingleton<Services.IRemoteToolCatalog, Services.RemoteToolCatalog>();
            builder.Services.AddSingleton<IDbContextFactory<AppDbContext>, Services.DynamicAppDbContextFactory>();
            builder.Services.AddSingleton<Services.INotificationRepository, Services.NotificationRepository>();
            builder.Services.AddSingleton<Services.IClipboardRepository,    Services.ClipboardRepository>();
            builder.Services.AddSingleton<Services.IFileRepository,         Services.FileRepository>();
            builder.Services.AddSingleton<Services.IFolderSyncRepository,   Services.FolderSyncRepository>();
            builder.Services.AddSingleton<Services.ICleanupService,        Services.CleanupService>();
            builder.Services.AddSingleton<Services.INoteRepository,         Services.NoteRepository>();
            builder.Services.AddSingleton<Services.IKanbanTaskRepository,   Services.KanbanTaskRepository>();
            builder.Services.AddSingleton<Services.IKanbanCleanupService,   Services.KanbanCleanupService>();
            builder.Services.AddSingleton<Services.IBrainstormNodeRepository, Services.BrainstormNodeRepository>();
            builder.Services.AddSingleton<Services.IVariableRepository,     Services.VariableRepository>();
            builder.Services.AddSingleton<Services.ISecretVaultService,     Services.SecretVaultService>();
            builder.Services.AddSingleton<Services.IToastService,            Services.ToastService>();
            builder.Services.AddTransient<Views.ClipboardHistoryPage>();
            builder.Services.AddTransient<Views.FilesPage>();
            builder.Services.AddTransient<Views.FolderSyncPage>();
            builder.Services.AddTransient<Views.FolderSyncDetailPage>();
            builder.Services.AddTransient<Views.RemoteToolsPage>();
            builder.Services.AddTransient<Views.FolderSyncEditorPage>();
            builder.Services.AddTransient<Views.IgnorePathPickerPage>();
            builder.Services.AddTransient<Views.NotificationsPage>();
            builder.Services.AddTransient<Views.NotesPage>();
            builder.Services.AddTransient<Views.NoteEditorPage>();
            builder.Services.AddTransient<Views.KanbanPage>();
            builder.Services.AddTransient<Views.BrainstormPage>();
            builder.Services.AddTransient<Views.SettingsPage>();
            builder.Services.AddTransient<Views.VariablesPage>();

#if WINDOWS
            builder.Services.AddSingleton<Services.IWebHostService, Services.WebHostService>();
            builder.Services.AddSingleton<Services.IMouseService,   Services.MouseService>();
            builder.Services.AddSingleton<Services.ITrayService,    Services.TrayService>();
            builder.Services.AddSingleton<Services.IRoleService,    Services.RoleService>();
            builder.Services.AddSingleton<Services.INotificationService, Services.NotificationService>();
            builder.Services.AddSingleton<Services.IStartupService, Services.StartupService>();
            builder.Services.AddSingleton<Services.IRemoteShellElevationService, Services.WindowsRemoteShellElevationService>();
#else
            builder.Services.AddSingleton<Services.IRoleService,    Services.DefaultRoleService>();
            builder.Services.AddSingleton<Services.INotificationService, Services.NullNotificationService>();
            builder.Services.AddSingleton<Services.IRemoteShellElevationService, Services.NullRemoteShellElevationService>();
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var mauiApp = builder.Build();

            using (var db = mauiApp.Services
                       .GetRequiredService<IDbContextFactory<AppDbContext>>()
                       .CreateDbContext())
                DatabaseInitializer.Initialize(db);

            return mauiApp;
        }
    }
}
