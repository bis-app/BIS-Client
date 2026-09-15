using Microsoft.Web.WebView2.Core;

namespace BISClient.Services;

public static class WebViewRuntimeChecker
{
    public static bool TryGetVersion(out string? version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            version = null;
            return false;
        }
        catch (Exception)
        {
            version = null;
            return false;
        }
    }
}
