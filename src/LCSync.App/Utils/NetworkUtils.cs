using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace LCSync.Utils;

public static class NetworkUtils
{
    public static List<string> GetLocalIPAddresses()
    {
        var ips = new List<string>();
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ips.Add(ip.ToString());
                }
            }
        }
        catch
        {
            ips.Add("127.0.0.1");
        }
        return ips.Count > 0 ? ips : new List<string> { "127.0.0.1" };
    }

    public static string GetPrimaryLocalIP()
    {
        var ips = GetLocalIPAddresses();
        return ips.FirstOrDefault(ip => !ip.StartsWith("127.") && !ip.StartsWith("169.254.")) 
               ?? ips.FirstOrDefault() 
               ?? "127.0.0.1";
    }
}
