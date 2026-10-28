using BISClient.Configuration;
using BISClient.Services;
using BISClient.Services.Native;
using BISClient.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BISClient.Hosting;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBisClient(this IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IWebViewEnvironmentService, WebViewEnvironmentService>();
        services.AddSingleton<INavigationGuard, NavigationGuard>();
        services.AddSingleton<IExternalBrowserLauncher, ExternalBrowserLauncher>();
        services.AddSingleton<IUserSettingsStore, UserSettingsStore>();
        services.AddSingleton<IImportUploadService, ImportUploadService>();
        services.AddSingleton<ISendToShortcutService, SendToShortcutService>();
        services.AddSingleton<IShareTargetRegistrationService, ShareTargetRegistrationService>();
        services.AddSingleton<IExplorerUploadIntegration, ExplorerUploadIntegration>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddSingleton<ISettingsDialogService, SettingsDialogService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IOutlookMailService, OutlookMailService>();
        services.AddSingleton<IWindowStateService, WindowStateService>();
        services.AddSingleton<IPrinterService, NullPrinterService>();
        services.AddSingleton<IScannerService, NullScannerService>();
        services.AddSingleton<INativeMessageBroker, NativeMessageBroker>();
        services.AddSingleton<TraySession>();
        services.AddSingleton<TaskbarBadgeService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ITrayIconService>(sp => sp.GetRequiredService<TrayIconService>());
        services.AddSingleton<IWindowsToastService, WindowsToastService>();
        services.AddSingleton<NotificationNavigationService>();
        services.AddTransient<WebViewTabSession>();
        services.AddTransient<BrowserTabViewModel>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<AppUpdateState>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddHostedService<BisNotificationPoller>();
        services.AddHostedService<AppUpdateChecker>();
        return services;
    }
}
