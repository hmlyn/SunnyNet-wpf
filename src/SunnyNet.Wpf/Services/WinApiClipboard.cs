using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SunnyNet.Wpf.Services;

public static class WinApiClipboard
{
    private const int ClipboardCannotOpenHResult = unchecked((int)0x800401D0);
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    public static void SetText(string? text)
    {
        if (!TrySetText(text, out Exception? exception))
        {
            throw new InvalidOperationException(GetFriendlyErrorMessage(exception), exception);
        }
    }

    public static async Task SetTextAsync(string? text)
    {
        ClipboardSetResult result = await Task.Run(() =>
        {
            bool success = TrySetText(text, out Exception? exception);
            return new ClipboardSetResult(success, exception);
        }).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException(GetFriendlyErrorMessage(result.Exception), result.Exception);
        }
    }

    public static async Task<bool> TrySetTextAsync(string? text)
    {
        ClipboardSetResult result = await Task.Run(() =>
        {
            bool success = TrySetText(text, out Exception? exception);
            return new ClipboardSetResult(success, exception);
        }).ConfigureAwait(false);

        return result.Success;
    }

    public static bool TrySetText(string? text, out Exception? exception)
    {
        string value = text ?? "";
        return TrySetTextCore(value, out exception);
    }

    private static bool TrySetTextCore(string value, out Exception? exception)
    {
        exception = null;
        try
        {
            SetUnicodeTextByWinApi(value);
            return true;
        }
        catch (Exception caught) when (IsClipboardException(caught))
        {
            exception = caught;
            return false;
        }
    }

    private static void SetUnicodeTextByWinApi(string value)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw CreateWin32Exception("OpenClipboard");
        }

        IntPtr memoryHandle = IntPtr.Zero;
        bool memoryTransferred = false;
        try
        {
            if (!EmptyClipboard())
            {
                throw CreateWin32Exception("EmptyClipboard");
            }

            byte[] bytes = Encoding.Unicode.GetBytes(value + '\0');
            memoryHandle = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)bytes.Length);
            if (memoryHandle == IntPtr.Zero)
            {
                throw CreateWin32Exception("GlobalAlloc");
            }

            IntPtr memoryPointer = GlobalLock(memoryHandle);
            if (memoryPointer == IntPtr.Zero)
            {
                throw CreateWin32Exception("GlobalLock");
            }

            try
            {
                Marshal.Copy(bytes, 0, memoryPointer, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(memoryHandle);
            }

            if (SetClipboardData(CfUnicodeText, memoryHandle) == IntPtr.Zero)
            {
                throw CreateWin32Exception("SetClipboardData");
            }

            memoryTransferred = true;
        }
        finally
        {
            _ = CloseClipboard();
            if (!memoryTransferred && memoryHandle != IntPtr.Zero)
            {
                _ = GlobalFree(memoryHandle);
            }
        }
    }

    private static Win32Exception CreateWin32Exception(string apiName)
    {
        int error = Marshal.GetLastWin32Error();
        Win32Exception exception = new(error);
        return new Win32Exception(error, $"{apiName} 失败：{exception.Message}");
    }

    public static string GetFriendlyErrorMessage(Exception? exception)
    {
        return IsClipboardBusy(exception)
            ? "剪贴板当前被其它程序占用，请稍后再试。"
            : $"复制失败：{exception?.Message ?? "未知错误"}";
    }

    private static bool IsClipboardException(Exception exception)
    {
        return exception is COMException or ExternalException or InvalidOperationException or Win32Exception;
    }

    private static bool IsClipboardBusy(Exception? exception)
    {
        return exception is COMException { HResult: ClipboardCannotOpenHResult }
            || exception is ExternalException { HResult: ClipboardCannotOpenHResult }
            || exception is Win32Exception { NativeErrorCode: 5 or 1418 }
            || (exception?.Message.Contains("OpenClipboard", StringComparison.OrdinalIgnoreCase) ?? false)
            || (exception?.Message.Contains("CLIPBRD_E_CANT_OPEN", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private readonly record struct ClipboardSetResult(bool Success, Exception? Exception);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
