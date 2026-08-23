using System.ComponentModel;
using System.Diagnostics;

namespace EasyAuthLocalEmulator.Hosting;

public static class BrowserLauncher
{
    public static bool TryOpen(string url, out string? error)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            error = null;
            return true;
        }
        catch (Win32Exception exception)
        {
            error = exception.Message;
            return false;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
