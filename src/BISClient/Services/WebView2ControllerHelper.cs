using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BISClient.Services;

internal static class WebView2ControllerHelper
{
    public static CoreWebView2Controller? TryGetController(WebView2 webView)
    {
        var type = webView.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        if (type.GetProperty("CoreWebView2Controller", flags)?.GetValue(webView) is CoreWebView2Controller fromProperty)
        {
            return fromProperty;
        }

        if (type.GetField("_coreWebView2Controller", flags)?.GetValue(webView) is CoreWebView2Controller fromField)
        {
            return fromField;
        }

        return null;
    }
}
