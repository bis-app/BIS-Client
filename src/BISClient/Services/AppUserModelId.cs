using System.Runtime.InteropServices;

namespace BISClient.Services;

internal static class AppUserModelId
{
    public const string Value = "BIS.BISClient";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    public static void Apply()
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(Value);
        }
        catch
        {
            // Toasts funktionieren ohne AUMID eingeschränkt weiter.
        }
    }
}
