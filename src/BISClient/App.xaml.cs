using System.IO;
using System.Windows;
using BISClient.Configuration;
using BISClient.Hosting;
using Microsoft.Extensions.Configuration;
using BISClient.Services;
using BISClient.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using Serilog.Events;

namespace BISClient;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstanceService? _singleInstance;
    private string? _startupToastUrl;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppUserModelId.Apply();
        ToastNotificationManagerCompat.OnActivated += OnStartupToastActivated;

        IConfigurationRoot? configuration = null;
        AppSettings? settings = null;

        try
        {
            configuration = AppConfiguration.Load();
            settings = AppConfiguration.Bind(configuration);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Die Konfiguration konnte nicht geladen werden.\n\n" + ex.Message,
                "BIS Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var shareFiles = await ShareTargetActivation.CaptureFilesAsync();
        var startupFiles = ShellFileArguments.Merge(ShellFileArguments.Parse(e.Args), shareFiles);

        _singleInstance = new SingleInstanceService(settings);
        if (!_singleInstance.TryAcquire())
        {
            ToastNotificationManagerCompat.OnActivated -= OnStartupToastActivated;
            _singleInstance.SignalExisting(new InstanceActivationRequest
            {
                Url = _startupToastUrl,
                Files = startupFiles
            });
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        ToastNotificationManagerCompat.OnActivated -= OnStartupToastActivated;

        var logLevel = settings.DevelopmentMode ? LogEventLevel.Debug : LogEventLevel.Information;
        var logPath = Path.Combine(settings.LogDirectory, "bis-client-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("BIS Client wird gestartet");

            if (configuration is null || settings is null)
            {
                Shutdown(1);
                return;
            }

            var loadedConfiguration = configuration;
            var loadedSettings = settings;
            var singleInstance = _singleInstance;

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IConfiguration>(loadedConfiguration);
                    services.AddSingleton<ISingleInstanceService>(singleInstance);
                    services.AddBisClient(loadedSettings);
                })
                .Build();

            await _host.StartAsync();

            if (!WebViewRuntimeChecker.TryGetVersion(out var runtimeVersion))
            {
                Log.Error("Microsoft Edge WebView2 Runtime ist nicht installiert");
                MessageBox.Show(
                    "Die Microsoft Edge WebView2 Runtime ist nicht installiert.\n\n" +
                    "Bitte installieren Sie die Runtime und starten Sie den BIS Client erneut.\n" +
                    "Download: https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "BIS Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(2);
                return;
            }

            Log.Information("WebView2 Runtime gefunden: {Version}", runtimeVersion);

            var window = _host.Services.GetRequiredService<MainWindow>();
            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            window.Show();

            var traySession = _host.Services.GetRequiredService<TraySession>();
            singleInstance.StartWatching(request =>
            {
                traySession.RequestRestore(request.Url);
                if (request.Files.Count > 0)
                {
                    _ = viewModel.UploadShellFilesAsync(request.Files);
                }
            });

            await _host.Services.GetRequiredService<IExplorerUploadIntegration>().StartAsync();

            if (!string.IsNullOrWhiteSpace(_startupToastUrl))
            {
                traySession.RequestRestore(_startupToastUrl);
            }

            if (startupFiles.Count > 0)
            {
                _ = viewModel.UploadShellFilesAsync(startupFiles);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "BIS Client konnte nicht gestartet werden");
            MessageBox.Show(
                "Der BIS Client konnte nicht gestartet werden.\n\n" + ex.Message,
                "BIS Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("BIS Client wird beendet");

        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Host konnte nicht sauber gestoppt werden");
            }

            _host.Dispose();
        }

        _singleInstance?.Dispose();
        _singleInstance = null;

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private void OnStartupToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        _startupToastUrl = WindowsToastService.TryGetUrl(e) ?? _startupToastUrl;
    }
}
