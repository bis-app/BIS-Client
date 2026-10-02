using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using BISClient.Services.Native;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface ISendToShortcutService
{
    void Sync(bool enabled);
}

public sealed class SendToShortcutService : ISendToShortcutService
{
    private readonly ILogger<SendToShortcutService> _logger;

    public SendToShortcutService(ILogger<SendToShortcutService> logger)
    {
        _logger = logger;
    }

    public void Sync(bool enabled)
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            if (!enabled)
            {
                DeleteShortcut(shortcutPath);
                return;
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                _logger.LogWarning("Senden-an-Verknüpfung konnte nicht angelegt werden: EXE nicht gefunden");
                return;
            }

            CreateShortcut(shortcutPath, exePath);
            _logger.LogDebug("Senden-an-Verknüpfung aktualisiert: {Path}", shortcutPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Senden-an-Verknüpfung konnte nicht aktualisiert werden");
        }
    }

    private static string GetShortcutPath()
    {
        var sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        return Path.Combine(sendTo, ExplorerUploadConstants.SendToShortcutFileName);
    }

    private static void DeleteShortcut(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static void CreateShortcut(string shortcutPath, string exePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory);
            link.SetDescription(ExplorerUploadConstants.ShortcutDescription);
            link.SetIconLocation(exePath, 0);

            var persist = (IPersistFile)link;
            persist.Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
