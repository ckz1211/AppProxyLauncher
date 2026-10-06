using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

static class Program
{
    static int Port;
    static string Proxy { get { return ProxyPortOptions.Url(Port); } }
    const string Bypass = "localhost,127.0.0.0/8,::1,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,100.64.0.0/10,fd7a:115c:a1e0::/48,.local,.lan,.ts.net,.cn";
    [STAThread]
    static void Main(string[] args)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTScopedProxy");
        Directory.CreateDirectory(root);
        string log = Path.Combine(root, "launcher.log");
        string stage = "query installed package";
        bool diagnose = Array.IndexOf(args, "--diagnose") >= 0;
        bool inPackage = Array.IndexOf(args, "--in-package") >= 0;
        bool probePackage = Array.IndexOf(args, "--probe-package") >= 0;
        try
        {
            Port = ProxyPortOptions.Parse(args);
            // Resolve the installed Store package every time, so client updates remain usable.
            var query = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -Command \"Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            string location;
            using (var p = Process.Start(query))
            {
                location = ReadProcess(p, 15000).Trim();
            }
            string exe = Path.Combine(location, "app", "ChatGPT.exe");
            stage = "validate package path";
            if (!Directory.Exists(location) || !File.Exists(exe) || !location.Contains("\\WindowsApps\\OpenAI.Codex_"))
                throw new Exception("找不到已安装的官方 ChatGPT 客户端");
            if (diagnose)
            {
                File.AppendAllText(log, DateTime.Now.ToString("o") + " Diagnosis proxy=" + Proxy + " resolved exe=" + exe + Environment.NewLine, Encoding.UTF8);
                return;
            }
            var running = Process.GetProcessesByName("ChatGPT");
            if (running.Length != 0 && !probePackage)
            {
                MessageBox.Show("ChatGPT 仍在运行。独立代理需要在启动时生效。请先从 ChatGPT 托盘完全退出，再点击此快捷方式。", "ChatGPT 独立代理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!inPackage)
            {
                // The official Appx API grants package context without installing/repackaging
                // the application or changing package debugging settings. It drops caller env,
                // so our inner launcher must set the proxy AFTER entering package context.
                stage = "enter package context";
                string command = PackageCommand(Process.GetCurrentProcess().MainModule.FileName, Port, probePackage);
                var enter = new ProcessStartInfo(query.FileName, EncodedArguments(command))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(enter))
                {
                    ReadProcess(p, 30000);
                }
                File.AppendAllText(log, DateTime.Now.ToString("o") + " Package-context handoff completed proxy=" + Proxy + Environment.NewLine, Encoding.UTF8);
                return;
            }
            uint packageLength = 0;
            if (GetCurrentPackageFullName(ref packageLength, null) != 122)
                throw new Exception("启动器没有获得预期的应用包上下文");
            var packageName = new StringBuilder((int)packageLength);
            if (GetCurrentPackageFullName(ref packageLength, packageName) != 0 || !packageName.ToString().StartsWith("OpenAI.Codex_", StringComparison.Ordinal) || !packageName.ToString().EndsWith("_2p2nqsd0c76g0", StringComparison.Ordinal))
                throw new Exception("启动器的应用包身份不匹配");
            var info = new ProcessStartInfo(exe, probePackage ? "--version" : "") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
            // Windows environment keys are case-insensitive. Only this process tree inherits them.
            info.EnvironmentVariables["HTTP_PROXY"] = Proxy;
            info.EnvironmentVariables["HTTPS_PROXY"] = Proxy;
            info.EnvironmentVariables["ALL_PROXY"] = Proxy;
            info.EnvironmentVariables["NO_PROXY"] = Bypass;
            // Do not affect browsers, Tailscale, user/machine environment or system proxy settings.
            stage = "start packaged ChatGPT executable";
            using (var app = Process.Start(info))
            {
                uint childLength = 0;
                string childPackage = "none";
                if (GetPackageFullName(app.Handle, ref childLength, null) == 122)
                {
                    var childName = new StringBuilder((int)childLength);
                    if (GetPackageFullName(app.Handle, ref childLength, childName) == 0) childPackage = childName.ToString();
                }
                File.AppendAllText(log, DateTime.Now.ToString("o") + " " + (probePackage ? "Probe" : "Started") + " PID=" + app.Id + " proxy=" + Proxy + " package=" + childPackage + " exe=" + exe + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(log, DateTime.Now.ToString("o") + " Stage=" + stage + " " + ex.ToString() + Environment.NewLine, Encoding.UTF8);
            Environment.ExitCode = ex is ArgumentException ? 2 : 1;
            if (!diagnose && !probePackage) MessageBox.Show(ex.Message, "ChatGPT 独立代理启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    static string PackageCommand(string self, int port, bool probe)
    {
        return "Invoke-CommandInDesktopPackage -PackageFamilyName OpenAI.Codex_2p2nqsd0c76g0 -AppId App -Command '" + self.Replace("'", "''") + "' -Args '" + (probe ? "--in-package --probe-package" : "--in-package") + " --port=" + port + "' -ErrorAction Stop";
    }
    static string EncodedArguments(string command)
    {
        return "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }
    // Drain both pipes concurrently; reading to EOF before WaitForExit defeats its timeout.
    static string ReadProcess(Process process, int timeout)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("Windows 应用包接口响应超时");
        }
        if (!System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, timeout))
            throw new TimeoutException("Windows 应用包接口输出超时");
        if (process.ExitCode != 0) throw new Exception("Windows 应用包接口失败：" + error.Result);
        return output.Result;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref uint length, StringBuilder packageFullName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder packageFullName);
}
