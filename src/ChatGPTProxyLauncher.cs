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
                location = p.StandardOutput.ReadToEnd().Trim();
                string error = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000) || p.ExitCode != 0) throw new Exception("无法读取已安装的 ChatGPT 客户端：" + error);
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
                string self = Process.GetCurrentProcess().MainModule.FileName.Replace("'", "''");
                string command = "Invoke-CommandInDesktopPackage -PackageFamilyName OpenAI.Codex_2p2nqsd0c76g0 -AppId App -Command '" + self + "' -Args '" + (probePackage ? "--in-package --probe-package" : "--in-package") + " --port=" + Port + "' -ErrorAction Stop";
                var enter = new ProcessStartInfo(query.FileName, "-NoLogo -NoProfile -NonInteractive -Command \"" + command + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(enter))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new Exception("应用包启动接口失败：" + error);
                }
                File.AppendAllText(log, DateTime.Now.ToString("o") + " Package-context handoff completed proxy=" + Proxy + Environment.NewLine, Encoding.UTF8);
                return;
            }
            uint packageLength = 0;
            if (GetCurrentPackageFullName(ref packageLength, null) != 122)
                throw new Exception("启动器没有获得预期的应用包上下文");
            var packageName = new StringBuilder((int)packageLength);
            if (GetCurrentPackageFullName(ref packageLength, packageName) != 0 || !packageName.ToString().StartsWith("OpenAI.Codex_"))
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref uint length, StringBuilder packageFullName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder packageFullName);
}
