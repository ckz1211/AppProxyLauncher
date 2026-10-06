using System;
using System.Globalization;

public static class ProxyPortOptions
{
    public const int DefaultPort = 7897;
    public static int Parse(string[] args)
    {
        int port = DefaultPort;
        bool found = false;
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--port", StringComparison.Ordinal)) continue;
            int parsed;
            if (found || !arg.StartsWith("--port=", StringComparison.Ordinal) ||
                !int.TryParse(arg.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out parsed) ||
                parsed < 1 || parsed > 65535)
                throw new ArgumentException("端口参数应为 --port=1 到 --port=65535，且只能提供一次。");
            port = parsed;
            found = true;
        }
        return port;
    }
    public static string Url(int port) { return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture); }
}
