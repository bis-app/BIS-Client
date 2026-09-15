using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BISClient.Services;

public interface IWindowsToastService
{
    event EventHandler<string?>? Activated;

    void Show(string title, string body, string? zielUrl);
}

public sealed class WindowsToastService : IWindowsToastService, IDisposable
{
    private readonly TraySession _session;
    private readonly ILogger<WindowsToastService> _logger;
    private bool _disposed;

    public WindowsToastService(TraySession session, ILogger<WindowsToastService> logger)
    {
        _session = session;
        _logger = logger;
        ToastNotificationManagerCompat.OnActivated += OnActivated;
    }

    public event EventHandler<string?>? Activated;

    public void Show(string title, string body, string? zielUrl)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(string.IsNullOrWhiteSpace(title) ? "BIS" : title);

            if (!string.IsNullOrWhiteSpace(body))
            {
                builder.AddText(body);
            }

            builder.AddArgument("action", "open");
            if (!string.IsNullOrWhiteSpace(zielUrl))
            {
                builder.AddArgument("url", zielUrl);
            }

            builder.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows-Toast konnte nicht angezeigt werden");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ToastNotificationManagerCompat.OnActivated -= OnActivated;
    }

    public static string? TryGetUrl(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("url", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
        }

        return null;
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var url = TryGetUrl(e);
        Activated?.Invoke(this, url);
        _session.RequestRestore(url);
    }
}
