using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public sealed class TaskbarBadgeService : IDisposable
{
    private const int SwShowMinNoActive = 7;
    private const int SmCxSmIcon = 49;
    private const uint FlashTrayUntilForeground = 0x00000002 | 0x0000000C;
    private const uint FlashStop = 0;

    private readonly ILogger<TaskbarBadgeService> _logger;
    private ITaskbarList3? _taskbar;
    private int _shownCount;
    private bool _disposed;

    public TaskbarBadgeService(ILogger<TaskbarBadgeService> logger)
    {
        _logger = logger;
    }

    public void Show(IntPtr hwnd, int count)
    {
        if (_disposed || hwnd == IntPtr.Zero || count <= 0)
        {
            return;
        }

        var changed = count != _shownCount;
        _shownCount = count;

        try
        {
            ShowWindow(hwnd, SwShowMinNoActive);
            SetOverlay(hwnd, count);
            if (changed)
            {
                Flash(hwnd, FlashTrayUntilForeground);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Taskleisten-Hinweis für {Count} ungelesene Benachrichtigungen fehlgeschlagen", count);
        }
    }

    public void Clear(IntPtr hwnd)
    {
        _shownCount = 0;
        if (_disposed || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Flash(hwnd, FlashStop);
            Taskbar()?.SetOverlayIcon(hwnd, IntPtr.Zero, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Taskleisten-Hinweis konnte nicht entfernt werden");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_taskbar is not null)
        {
            Marshal.ReleaseComObject(_taskbar);
            _taskbar = null;
        }
    }

    private void SetOverlay(IntPtr hwnd, int count)
    {
        var icon = CreateCountIcon(IconSize(hwnd), count);
        try
        {
            var description = count == 1
                ? "1 ungelesene Benachrichtigung"
                : $"{count} ungelesene Benachrichtigungen";
            Taskbar()?.SetOverlayIcon(hwnd, icon, description);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private ITaskbarList3? Taskbar()
    {
        if (_taskbar is not null)
        {
            return _taskbar;
        }

        var taskbar = (ITaskbarList3)new CTaskbarList();
        var hr = taskbar.HrInit();
        if (hr < 0)
        {
            Marshal.ReleaseComObject(taskbar);
            Marshal.ThrowExceptionForHR(hr);
        }

        _taskbar = taskbar;
        return _taskbar;
    }

    private static void Flash(IntPtr hwnd, uint flags)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = flags,
            uCount = 0,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
    }

    private static int IconSize(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        var size = dpi == 0
            ? GetSystemMetrics(SmCxSmIcon)
            : GetSystemMetricsForDpi(SmCxSmIcon, dpi);
        return size < 16 ? 16 : size;
    }

    private static IntPtr CreateCountIcon(int size, int count)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(232, 17, 35));
            graphics.FillEllipse(fill, 0.5f, 0.5f, size - 1.5f, size - 1.5f);

            var text = count > 99 ? "99" : count.ToString();
            var fontSize = text.Length > 1 ? size * 0.46f : size * 0.62f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, Brushes.White, new RectangleF(0, -0.5f, size, size), format);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngBytes.Length);
            writer.Write(22);
            writer.Write(pngBytes);
        }

        ico.Position = 0;
        using var icon = new Icon(ico);
        var copy = CopyIcon(icon.Handle);
        if (copy == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return copy;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class CTaskbarList
    {
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig]
        int HrInit();

        [PreserveSig]
        int AddTab(IntPtr hwnd);

        [PreserveSig]
        int DeleteTab(IntPtr hwnd);

        [PreserveSig]
        int ActivateTab(IntPtr hwnd);

        [PreserveSig]
        int SetActiveAlt(IntPtr hwnd);

        [PreserveSig]
        int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        [PreserveSig]
        int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);

        [PreserveSig]
        int SetProgressState(IntPtr hwnd, int tbpFlags);

        [PreserveSig]
        int RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);

        [PreserveSig]
        int UnregisterTab(IntPtr hwndTab);

        [PreserveSig]
        int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

        [PreserveSig]
        int SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint dwReserved);

        [PreserveSig]
        int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);

        [PreserveSig]
        int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);

        [PreserveSig]
        int ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);

        [PreserveSig]
        int SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);

        [PreserveSig]
        int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);

        [PreserveSig]
        int SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }
}
