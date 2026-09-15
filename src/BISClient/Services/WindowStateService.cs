using System.IO;
using System.Text.Json;
using System.Windows;
using BISClient.Configuration;
using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface IWindowStateService
{
    void Restore(Window window);

    void Save(Window window);
}

public sealed class WindowStateService : IWindowStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings _settings;
    private readonly ILogger<WindowStateService> _logger;

    public WindowStateService(AppSettings settings, ILogger<WindowStateService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Restore(Window window)
    {
        if (_settings.KioskMode)
        {
            ApplyKiosk(window);
            return;
        }

        var bounds = Load();
        if (bounds is null || bounds.Width < 200 || bounds.Height < 200)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Width = 1280;
            window.Height = 800;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;

        if (!IsOnVirtualScreen(window))
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Left = SystemParameters.WorkArea.Left + 40;
            window.Top = SystemParameters.WorkArea.Top + 40;
            window.Width = Math.Min(1280, SystemParameters.WorkArea.Width);
            window.Height = Math.Min(800, SystemParameters.WorkArea.Height);
        }

        if (Enum.TryParse<WindowState>(bounds.State, out var state) && state != WindowState.Minimized)
        {
            window.WindowState = state;
        }
    }

    public void Save(Window window)
    {
        if (_settings.KioskMode)
        {
            return;
        }

        try
        {
            var source = window.WindowState == WindowState.Normal ? window : null;
            var bounds = new WindowBounds
            {
                Left = window.RestoreBounds.Left,
                Top = window.RestoreBounds.Top,
                Width = window.RestoreBounds.Width,
                Height = window.RestoreBounds.Height,
                State = window.WindowState == WindowState.Minimized
                    ? WindowState.Normal.ToString()
                    : window.WindowState.ToString()
            };

            if (source is not null)
            {
                bounds.Left = window.Left;
                bounds.Top = window.Top;
                bounds.Width = window.Width;
                bounds.Height = window.Height;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_settings.WindowStateFile)!);
            File.WriteAllText(_settings.WindowStateFile, JsonSerializer.Serialize(bounds, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fensterzustand konnte nicht gespeichert werden");
        }
    }

    public static void ApplyKiosk(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowState = WindowState.Maximized;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = SystemParameters.VirtualScreenLeft;
        window.Top = SystemParameters.VirtualScreenTop;
        window.Width = SystemParameters.VirtualScreenWidth;
        window.Height = SystemParameters.VirtualScreenHeight;
    }

    private WindowBounds? Load()
    {
        try
        {
            if (!File.Exists(_settings.WindowStateFile))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WindowBounds>(
                File.ReadAllText(_settings.WindowStateFile),
                JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fensterzustand konnte nicht gelesen werden");
            return null;
        }
    }

    private static bool IsOnVirtualScreen(Window window)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var rect = new Rect(window.Left, window.Top, window.Width, window.Height);
        return screen.IntersectsWith(rect);
    }
}
