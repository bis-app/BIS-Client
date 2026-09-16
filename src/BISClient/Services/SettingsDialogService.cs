using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace BISClient.Services;

public interface ISettingsDialogService
{
    bool? Show(Window? owner);
}

public sealed class SettingsDialogService : ISettingsDialogService
{
    private readonly IServiceProvider _services;

    public SettingsDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public bool? Show(Window? owner)
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Owner = owner ?? Application.Current?.MainWindow;
        return window.ShowDialog();
    }
}
