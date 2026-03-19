using Microsoft.Extensions.Logging;

using Elysium.WorkStation.Data;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
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
            builder.Services.AddDbContextFactory<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={Path.Combine(FileSystem.AppDataDirectory, "elysium.db")}"));
            builder.Services.AddSingleton<Services.INotificationRepository, Services.NotificationRepository>();
            builder.Services.AddTransient<Views.ClipboardHistoryPage>();
            builder.Services.AddTransient<Views.FilesPage>();
            builder.Services.AddTransient<Views.NotificationsPage>();
            builder.Services.AddTransient<Views.SettingsPage>();

#if WINDOWS
            builder.Services.AddSingleton<Services.IWebHostService, Services.WebHostService>();
            builder.Services.AddSingleton<Services.IMouseService,   Services.MouseService>();
            builder.Services.AddSingleton<Services.ITrayService,    Services.TrayService>();
            builder.Services.AddSingleton<Services.IRoleService,    Services.RoleService>();
            builder.Services.AddSingleton<Services.INotificationService, Services.NotificationService>();
#else
            builder.Services.AddSingleton<Services.IRoleService,    Services.DefaultRoleService>();
            builder.Services.AddSingleton<Services.INotificationService, Services.NullNotificationService>();
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var mauiApp = builder.Build();

            using (var db = mauiApp.Services
                       .GetRequiredService<IDbContextFactory<AppDbContext>>()
                       .CreateDbContext())
                db.Database.EnsureCreated();

            return mauiApp;
        }
    }
}
