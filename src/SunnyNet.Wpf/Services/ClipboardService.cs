using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SunnyNet.Wpf.Services;

public static class ClipboardService
{
    private const int ClipboardCannotOpenHResult = unchecked((int)0x800401D0);
    private const int RetryCount = 12;
    private const int RetryDelayMs = 80;

    public static void SetText(string? text)
    {
        if (!TrySetText(text, out Exception? exception))
        {
            throw new InvalidOperationException(GetFriendlyErrorMessage(exception), exception);
        }
    }

    public static async Task SetTextAsync(string? text)
    {
        ClipboardSetResult result = await TrySetTextWithResultAsync(text).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(GetFriendlyErrorMessage(result.Exception), result.Exception);
        }
    }

    public static async Task<bool> TrySetTextAsync(string? text)
    {
        ClipboardSetResult result = await TrySetTextWithResultAsync(text).ConfigureAwait(false);
        return result.Success;
    }

    public static bool TrySetText(string? text, out Exception? exception)
    {
        string value = text ?? "";
        return TrySetTextCore(value, out exception);
    }

    private static Task<ClipboardSetResult> TrySetTextWithResultAsync(string? text)
    {
        string value = text ?? "";
        TaskCompletionSource<ClipboardSetResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            bool success = TrySetTextCore(value, out Exception? exception);
            completion.TrySetResult(new ClipboardSetResult(success, exception));
        })
        {
            IsBackground = true,
            Name = "SunnyNet Clipboard Writer"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static bool TrySetTextCore(string value, out Exception? exception)
    {
        exception = null;
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(value, copy: true);
                return true;
            }
            catch (Exception caught) when (IsClipboardException(caught))
            {
                exception = caught;
                if (attempt < RetryCount - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }

        return false;
    }

    public static string GetFriendlyErrorMessage(Exception? exception)
    {
        return IsClipboardBusy(exception)
            ? "剪贴板当前被其它程序占用，请稍后再试。"
            : $"复制失败：{exception?.Message ?? "未知错误"}";
    }

    private static bool IsClipboardException(Exception exception)
    {
        return exception is COMException or ExternalException or InvalidOperationException;
    }

    private static bool IsClipboardBusy(Exception? exception)
    {
        return exception is COMException { HResult: ClipboardCannotOpenHResult }
            || exception is ExternalException { HResult: ClipboardCannotOpenHResult }
            || (exception?.Message.Contains("OpenClipboard", StringComparison.OrdinalIgnoreCase) ?? false)
            || (exception?.Message.Contains("CLIPBRD_E_CANT_OPEN", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private readonly record struct ClipboardSetResult(bool Success, Exception? Exception);
}
