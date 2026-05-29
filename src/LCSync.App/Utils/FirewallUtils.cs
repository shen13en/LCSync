using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LCSync.Utils;

public static class FirewallUtils
{
    private const string RuleName = "LCSync";

    public static bool AddFirewallRule(int port)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var args = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}";
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoveFirewallRule()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var args = $"advfirewall firewall delete rule name=\"{RuleName}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
