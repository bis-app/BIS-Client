using System.IO;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace BISClient.Services;

internal static class ShareTargetActivation
{
    public static async Task<IReadOnlyList<string>> CaptureFilesAsync()
    {
        ShareTargetActivatedEventArgs? share = null;
        try
        {
            var args = AppInstance.GetActivatedEventArgs();
            share = args as ShareTargetActivatedEventArgs;
            if (share is null)
            {
                return [];
            }

            var operation = share.ShareOperation;
            operation.ReportStarted();
            var files = await ReadStorageItemsAsync(operation);
            operation.ReportCompleted();
            return files;
        }
        catch (Exception)
        {
            try
            {
                share?.ShareOperation.ReportError("Die Dateien konnten nicht übernommen werden.");
            }
            catch
            {
            }

            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> ReadStorageItemsAsync(ShareOperation operation)
    {
        var data = operation.Data;
        if (!data.Contains(StandardDataFormats.StorageItems))
        {
            return [];
        }

        var items = await data.GetStorageItemsAsync();
        var files = new List<string>();
        foreach (var item in items)
        {
            if (item is StorageFile file
                && !string.IsNullOrWhiteSpace(file.Path)
                && File.Exists(file.Path))
            {
                files.Add(file.Path);
            }
        }

        return files;
    }
}
