using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscordActivity;

internal static class ForegroundAppDetector
{
    public static ForegroundApp? GetCurrent()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var title = GetWindowTitle(window);
            var path = "";
            var displayName = process.ProcessName;
            try
            {
                path = process.MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var version = FileVersionInfo.GetVersionInfo(path);
                    displayName = FirstNonEmpty(version.ProductName, version.FileDescription, process.ProcessName);
                }
            }
            catch
            {
                // Some protected processes do not expose their executable path.
            }

            return new ForegroundApp(process.ProcessName, title, path, displayName.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0) return "";
        var buffer = new StringBuilder(length + 1);
        return GetWindowText(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
