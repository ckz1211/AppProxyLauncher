using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Program
{
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDCTray");
    public static string Scope = "Local\\RDCTray_" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
    public static int Port;
    public static string ProxyUrl { get { return ProxyPortOptions.Url(Port); } }
    [STAThread]
    static void Main(string[] args)
    {
        try { Port = ProxyPortOptions.Parse(args); }
        catch (ArgumentException ex)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(Path.Combine(Root, "launcher-fatal.log"), DateTime.Now.ToString("o") + " " + ex.Message + Environment.NewLine, Encoding.UTF8);
            Environment.ExitCode = 2;
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, Scope + "_Instance", out created))
        {
            if (!created)
            {
                bool quit = Array.IndexOf(args, "--exit") >= 0;
                int activePort = ProxyPortOptions.DefaultPort;
                try
                {
                    var record = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "status.json")));
                    if (record.ContainsKey("proxyPort")) activePort = Convert.ToInt32(record["proxyPort"]);
                }
                catch { }
                bool replace = !quit && activePort != Port;
                try { using (var e = EventWaitHandle.OpenExisting(Scope + (quit || replace ? "_Exit" : "_Check"))) e.Set(); }
                catch { Environment.ExitCode = 3; return; }
                if (!replace) return;
                // Reuse graceful Exit before changing this process-local port.
                try { if (!mutex.WaitOne(20000)) { Environment.ExitCode = 3; return; } }
                catch (AbandonedMutexException) { }
            }
            if (Array.IndexOf(args, "--exit") >= 0) return;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(Path.Combine(Root, "logs"));
                Application.Run(new TrayContext());
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Root, "launcher-fatal.log"), DateTime.Now.ToString("o") + " " + ex + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly Control dispatch = new Control();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem versionItem = new ToolStripMenuItem("Desktop Commander");
    readonly ToolStripMenuItem statusItem = new ToolStripMenuItem("检查更新中…");
    readonly ToolStripMenuItem checkItem = new ToolStripMenuItem("检查更新并启动");
    readonly EventWaitHandle checkEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.Scope + "_Check");
    readonly EventWaitHandle exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.Scope + "_Exit");
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly object logLock = new object();
    readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
    readonly string node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
    readonly string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node_modules", "npm", "bin", "npm-cli.js");
    readonly IntPtr job;
    Process agent;
    Process installer;
    string currentVersion = "";
    string state = "检查更新中";
    string stdoutFile;
    string stderrFile;
    bool busy;
    volatile bool exiting;
    bool firstTick = true;

    public TrayContext()
    {
        var handle = dispatch.Handle;
        job = Native.NewKillOnCloseJob();
        tray = new NotifyIcon { Icon = MakeIcon(), Text = "RDC：检查更新中", Visible = true };
        var menu = new ContextMenuStrip();
        versionItem.Enabled = false;
        statusItem.Enabled = false;
        checkItem.Click += delegate { CheckAndStart(); };
        var logs = new ToolStripMenuItem("打开日志文件夹");
        logs.Click += delegate { Process.Start(new ProcessStartInfo("explorer.exe", Quote(Path.Combine(Program.Root, "logs"))) { UseShellExecute = true }); };
        var exit = new ToolStripMenuItem("退出");
        exit.Click += delegate { RequestExit(); };
        menu.Items.AddRange(new ToolStripItem[] { versionItem, statusItem, new ToolStripSeparator(), checkItem, logs, new ToolStripSeparator(), exit });
        tray.ContextMenuStrip = menu;
        timer.Interval = 500;
        timer.Tick += delegate
        {
            if (exitEvent.WaitOne(0)) { RequestExit(); return; }
            if (firstTick || checkEvent.WaitOne(0)) { firstTick = false; CheckAndStart(); }
        };
        timer.Start();
        Log("托盘启动器启动 PID=" + Process.GetCurrentProcess().Id + " proxy=" + Program.ProxyUrl);
        SaveState();
    }

    static string Quote(string text) { return "\"" + text.Replace("\"", "") + "\""; }
    static bool ValidVersion(string v) { return v != null && v.Length <= 128 && Regex.IsMatch(v, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?\z"); }
    string VersionDir(string v) { return Path.Combine(Program.Root, "versions", v); }
    string Entry(string v) { return Path.Combine(VersionDir(v), "node_modules", "@wonderwhy-er", "desktop-commander", "dist", "index.js"); }
    string CurrentFile { get { return Path.Combine(Program.Root, "current-version.txt"); } }

    void OnUI(Action action)
    {
        if (dispatch.IsDisposed) return;
        try { dispatch.BeginInvoke((MethodInvoker)delegate { if (!dispatch.IsDisposed) action(); }); } catch (InvalidOperationException) { }
    }

    void SetState(string value)
    {
        OnUI(delegate
        {
            state = value;
            statusItem.Text = "状态：" + value;
            versionItem.Text = "Desktop Commander " + currentVersion;
            string tip = "RDC " + currentVersion + "：" + value;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            SaveState();
        });
    }

    void SaveState()
    {
        int pid = 0;
        try { if (agent != null && !agent.HasExited) pid = agent.Id; } catch { }
        try
        {
            var record = new Dictionary<string, object> {
                {"trayPid", Process.GetCurrentProcess().Id}, {"agentPid", pid},
                {"version", currentVersion}, {"state", state}, {"time", DateTime.Now.ToString("o")},
                {"stdout", stdoutFile}, {"stderr", stderrFile}, {"checkingUpdate", busy}, {"proxyPort", Program.Port}
            };
            File.WriteAllText(Path.Combine(Program.Root, "status.json"), serializer.Serialize(record), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log("状态记录失败：" + ex.Message); }
    }

    void Log(string message)
    {
        lock (logLock)
            File.AppendAllText(Path.Combine(Program.Root, "logs", "launcher.log"), DateTime.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
    }

    string ReadInstalled()
    {
        if (!File.Exists(CurrentFile)) return "";
        string v = File.ReadAllText(CurrentFile).Trim();
        return ValidVersion(v) && File.Exists(Entry(v)) ? v : "";
    }

    string FetchLatest()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create("https://registry.npmjs.org/@wonderwhy-er%2fdesktop-commander/latest");
        request.Timeout = 12000;
        request.ReadWriteTimeout = 15000;
        request.UserAgent = "RDCTrayLauncher/1.0";
        request.Proxy = new WebProxy(Program.ProxyUrl, true);
        using (var response = request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            var data = serializer.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            string name = Convert.ToString(data["name"]);
            string v = Convert.ToString(data["version"]);
            if (name != "@wonderwhy-er/desktop-commander" || !ValidVersion(v)) throw new Exception("npm 返回了无效的软件版本");
            Log("官方 npm latest=" + v);
            return v;
        }
    }

    void InstallVersion(string v)
    {
        if (File.Exists(Entry(v))) return;
        string versions = Path.Combine(Program.Root, "versions");
        Directory.CreateDirectory(versions);
        string stage = Path.Combine(versions, ".staging-" + v + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "package.json"), "{\"name\":\"rdc-tray-runtime\",\"version\":\"1.0.0\",\"private\":true}", Encoding.UTF8);
        string logPath = Path.Combine(Program.Root, "logs", "update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        // Invoke the JavaScript CLI directly: user paths must never be interpreted by cmd.exe.
        string command = Quote(npm) + " install --prefix " + Quote(stage) + " --registry=https://registry.npmjs.org --@wonderwhy-er:registry=https://registry.npmjs.org --strict-ssl=true --no-audit --no-fund --no-update-notifier --omit=dev @wonderwhy-er/desktop-commander@" + v;
        var info = new ProcessStartInfo(node, command) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = stage
        };
        info.EnvironmentVariables["PUPPETEER_SKIP_DOWNLOAD"] = "true";
        info.EnvironmentVariables["NPM_CONFIG_UPDATE_NOTIFIER"] = "false";
        // Applies to this update subprocess only; no user/machine proxy changes.
        info.EnvironmentVariables["NPM_CONFIG_PROXY"] = Program.ProxyUrl;
        info.EnvironmentVariables["NPM_CONFIG_HTTPS_PROXY"] = Program.ProxyUrl;
        using (var process = new Process { StartInfo = info })
        {
            installer = process;
            Action<string> write = delegate(string line) { if (line == null) return; lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8); };
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { write(e.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { write(e.Data); };
            process.Start(); Native.Assign(job, process);
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(300000)) { process.Kill(); throw new Exception("更新超时，已有版本保持不变"); }
            process.WaitForExit();
            installer = null;
            if (process.ExitCode != 0) throw new Exception("npm 更新失败，详情见更新日志");
        }
        string package = Path.Combine(stage, "node_modules", "@wonderwhy-er", "desktop-commander", "package.json");
        var installed = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(package));
        if (Convert.ToString(installed["name"]) != "@wonderwhy-er/desktop-commander" || Convert.ToString(installed["version"]) != v || !File.Exists(Path.Combine(Path.GetDirectoryName(package), "dist", "index.js")))
            throw new Exception("新版本校验失败，已有版本保持不变");
        Directory.Move(stage, VersionDir(v));
        Log("已安装并校验 RDC " + v);
    }

    void SelectVersion(string v)
    {
        string temporary = CurrentFile + ".new";
        File.WriteAllText(temporary, v, new UTF8Encoding(false));
        if (File.Exists(CurrentFile)) File.Replace(temporary, CurrentFile, null); else File.Move(temporary, CurrentFile);
    }

    void CheckAndStart()
    {
        if (busy || exiting) return;
        busy = true; checkItem.Enabled = false;
        SetState("检查更新中");
        Task.Factory.StartNew(delegate
        {
            string selected = ReadInstalled();
            bool updateFailed = false;
            try
            {
                if (!File.Exists(node) || !File.Exists(npm)) throw new Exception("找不到已安装的 Node.js / npm");
                string latest = FetchLatest();
                if (selected != latest)
                {
                    if (selected != "" && new Version(selected.Split('-')[0]).CompareTo(new Version(latest.Split('-')[0])) > 0)
                        Log("保留较新已安装版本 " + selected + "，不自动降级");
                    else
                    {
                        SetState("更新到 " + latest);
                        InstallVersion(latest);
                        selected = latest;
                        SelectVersion(selected);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("更新检查/安装失败：" + ex);
                updateFailed = true;
            }
            try
            {
                if (exiting) return;
                if (selected == "") throw new Exception("没有可用的本地版本，请联网后重试");
                bool alive = agent != null && !agent.HasExited;
                if (!alive || currentVersion != selected)
                {
                    StopAgent();
                    currentVersion = selected;
                    StartAgent();
                }
                else SetState(updateFailed ? "运行中；更新检查失败" : "运行中；已是最新版");
            }
            catch (Exception ex) { Log("启动失败：" + ex); SetState("启动失败，请查看日志"); }
            finally
            {
                OnUI(delegate { busy = false; checkItem.Enabled = !exiting; SaveState(); });
            }
        });
    }

    void StartAgent()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        stdoutFile = Path.Combine(Program.Root, "logs", "agent-" + stamp + "-stdout.log");
        stderrFile = Path.Combine(Program.Root, "logs", "agent-" + stamp + "-stderr.log");
        var info = new ProcessStartInfo(node, Quote(Path.Combine(Program.Root, "agent-host.mjs")) + " " + Quote(Entry(currentVersion)) + " --port=" + Program.Port) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Program.Root
        };
        var child = new Process { StartInfo = info, EnableRaisingEvents = true };
        // Cloud routing lives inside agent-host; keep arbitrary tool children direct.
        foreach (string key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "NODE_USE_ENV_PROXY" })
            info.EnvironmentVariables.Remove(key);
        Action<string, string> output = delegate(string path, string line)
        {
            if (line == null) return;
            lock (logLock) File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            if (exiting) return;
            if (line.Contains("Device marked as online") || line.Contains("Presence tracked")) SetState("在线");
            else if (line.Contains("NOT reachable") || line.Contains("not ready") || line.Contains("Channel error")) SetState("连接重试中");
            else if (line.Contains("Authenticating") || line.Contains("verification")) SetState("等待浏览器授权");
        };
        child.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { output(stdoutFile, e.Data); };
        child.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { output(stderrFile, e.Data); };
        child.Exited += delegate
        {
            if (Object.ReferenceEquals(agent, child) && !exiting) { Log("Agent 已退出，退出码=" + child.ExitCode); SetState("已停止；点击快捷方式重试"); }
        };
        agent = child;
        child.Start(); Native.Assign(job, child);
        child.BeginOutputReadLine(); child.BeginErrorReadLine();
        Log("启动 RDC " + currentVersion + " Agent PID=" + child.Id);
        SetState("连接中");
    }

    void StopAgent()
    {
        var child = agent;
        agent = null;
        if (child == null) return;
        try
        {
            if (!child.HasExited)
            {
                Log("请求 Agent 正常退出 PID=" + child.Id);
                child.StandardInput.WriteLine("RDC_TRAY_EXIT"); child.StandardInput.Flush();
                if (!child.WaitForExit(10000)) { Log("正常退出超时，结束本启动器的 Agent"); child.Kill(); child.WaitForExit(3000); }
                child.WaitForExit();
                Log("Agent 退出码=" + child.ExitCode);
            }
        }
        catch (Exception ex) { Log("Agent 退出处理：" + ex.Message); }
        finally { child.Dispose(); }
    }

    void RequestExit()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); checkItem.Enabled = false;
        SetState("正在退出");
        Task.Factory.StartNew(delegate
        {
            try { if (installer != null && !installer.HasExited) installer.Kill(); } catch { }
            StopAgent();
            OnUI(delegate
            {
                Log("托盘启动器退出");
                state = "已退出"; SaveState();
                tray.Visible = false; tray.Dispose(); timer.Dispose();
                checkEvent.Dispose(); exitEvent.Dispose();
                if (job != IntPtr.Zero) Native.CloseHandle(job);
                dispatch.Dispose(); ExitThread();
            });
        });
    }

    static Icon MakeIcon()
    {
        using (var bitmap = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(28, 104, 218)))
        using (var font = new Font("Segoe UI", 13, FontStyle.Bold))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(brush, 1, 1, 30, 30);
            g.DrawString("D", font, Brushes.White, new RectangleF(1, 2, 30, 29), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            IntPtr icon = bitmap.GetHicon();
            try { return (Icon)Icon.FromHandle(icon).Clone(); } finally { Native.DestroyIcon(icon); }
        }
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct BasicLimit {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWS, MaxWS;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct IO {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimit {
        public BasicLimit Basic; public IO Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr data, uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    public static IntPtr NewKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        var info = new ExtendedLimit(); info.Basic.Flags = 0x2000;
        int size = Marshal.SizeOf(info); IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, 9, buffer, (uint)size)) { CloseHandle(job); return IntPtr.Zero; }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return job;
    }
    public static void Assign(IntPtr job, Process process) { if (job != IntPtr.Zero) AssignProcessToJobObject(job, process.Handle); }
}
