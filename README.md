# AppProxyLauncher

**中文**
面向不希望使用 TUN 模式、或因管理员权限受限而不方便启用 TUN 的 Windows 用户，AppProxyLauncher 提供 ChatGPT/Codex 与 Remote Desktop Commander（Desktop Commander）的应用级本地代理启动器。通过已有的 HTTP/mixed 代理转发应用连接，减少对全局网络路径的影响；支持自定义代理端口、RDC 后台托盘运行与启动时检查更新。

**English**
AppProxyLauncher provides Windows launchers for ChatGPT/Codex and Remote Desktop Commander (Desktop Commander) that use an existing local HTTP/mixed proxy. It is intended for users who prefer to avoid TUN mode or cannot conveniently enable it because administrator access is restricted. It supports configurable proxy ports, an RDC system-tray launcher, and RDC update checks on launch.

## 责任范围 / Scope and disclaimer

本项目是面向不希望使用 TUN 或管理员权限受限用户的独立启动器壳层，不是 OpenAI、Desktop Commander 或代理软件的官方产品。它使用已有的本地 HTTP/mixed 代理，不提供代理服务器。
These are independent launcher wrappers for users who prefer to avoid TUN or have restricted administrator access. They use an existing local HTTP/mixed proxy and are not official vendor products or proxy servers.

安全审查和维护范围仅限本项目新增或修改的源码与功能。上游软件的内部安全与修复由各自作者负责，使用者自行负责其权限、配置和使用行为。启动器自身引入的漏洞或隐私泄露仍在本项目范围内。软件按现状提供，具体责任范围与限制见 [DISCLAIMER.md](DISCLAIMER.md)。
Security review and maintenance cover this project's own source and functionality. Upstream software remains the responsibility of its maintainers; users are responsible for their permissions, configuration and actions. Wrapper-introduced defects and privacy leaks remain in scope. Software is provided as is; see [DISCLAIMER.md](DISCLAIMER.md).

已完成启动器壳层的安全与隐私审查及隔离回归测试，结果见 [SECURITY-REVIEW.md](SECURITY-REVIEW.md)。这不表示上游组件已经审计通过。
A scoped launcher security/privacy review and isolated regression tests have been completed. See [SECURITY-REVIEW.md](SECURITY-REVIEW.md). This is not a certification of upstream components.

## 功能 / Features

- 默认代理地址：`http://127.0.0.1:7897`。使用 `--port=1080` 等参数更换本次启动的端口；无参数仍使用 7897。
- ChatGPT/Codex：通过 Windows 应用包上下文启动客户端，给该进程树设置 HTTP_PROXY、HTTPS_PROXY、ALL_PROXY；NO_PROXY 包括本机、常见私有网段、Tailscale 网段和 .cn 等域名。
- RDC：仅云端管理服务及其指定 Supabase 服务使用代理；其他 Agent 网络目标直接连接，工具子进程不继承代理环境变量。
- RDC 启动时查询官方 npm 最新版，失败时保留可用本地版本；后台托盘运行，右键退出，不设置开机启动。
- 不修改系统/WinHTTP 代理、系统环境变量、DNS、路由、VPN/TUN 或防火墙。其他应用的既有配置保留。

- Default proxy: `http://127.0.0.1:7897`; override it for one launch with `--port=1080`.
- ChatGPT/Codex: launch in the Windows package context with process-scoped proxy variables and local/private-network bypass rules.
- RDC: proxy the configured cloud-service hosts only; keep other Agent destinations direct and avoid passing proxy variables to tool subprocesses.
- RDC checks official npm updates on launch, runs in the system tray, and provides a right-click Exit action. No startup registration is created.
- The wrappers do not change system proxy settings, DNS, routes, VPN/TUN, firewall settings, or system environment variables.

进程树内的子进程可能继承 ChatGPT 的代理环境变量；这不等于逐个网络请求只代理 Remote Control。RDC 更新会从 npm 下载并执行第三方 Agent，npm 安装可能执行包的安装脚本。
Child processes in the ChatGPT process tree may inherit its proxy variables; this is not request-level isolation of Remote Control alone. RDC updates download and run a third-party Agent from npm, whose installation may execute package installation scripts.

NO_PROXY 的 CIDR、域名后缀支持取决于应用使用的网络库；`.cn` 不代表所有国内流量。本工具不是强制网络隔离或防泄漏工具，无法保证所有客户端请求都遵循环境变量。已有的系统代理也可能继续影响应用。
CIDR and domain-suffix support in NO_PROXY depends on each networking library. `.cn` does not cover all domestic traffic. This is not an enforced network isolation or leak-prevention tool: clients may ignore proxy variables, and existing system proxy settings can still affect applications.

## 环境 / Requirements

- Windows 10/11 x64；现有官方 ChatGPT/Codex Windows 应用包（OpenAI.Codex）。
- Windows PowerShell 与 .NET Framework 4.x。
- RDC：Node.js 24.19 or newer compatible Node.js，npm，目前启动器在 `%ProgramFiles%\nodejs` 查找它们。
- 已运行的本地 HTTP/mixed 代理。仅 SOCKS 的监听端口不能用于 HTTP 代理地址。
- 已满足上述前提时，日常启动器本身不请求 UAC。软件首次安装及组织策略可能仍需要管理员权限。

## 使用 / Usage

`bin/` 提供从本仓库源码重新编译的 Windows x64 程序。

**ChatGPT**
1. 完全退出现有客户端，包括托盘实例。
2. 启动 `bin\ChatGPTProxyLauncher.exe`，或创建指向它的快捷方式。
3. 可在快捷方式“目标”末尾添加端口参数：
```text
"…\ChatGPTProxyLauncher.exe" --port=1080
```

**RDC**
1. 在项目目录运行初始化脚本，将 Agent 包装文件和代理依赖安装到当前用户的 LocalAppData：
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-RDC.ps1 -Port 1080
```
2. 启动 `bin\RDCTray.exe --port=1080`。第一次使用由官方 Agent 完成浏览器配对。
3. 右键托盘图标可检查更新、查看本地日志、退出。相同端口的重复启动复用实例；端口变化时正常退出旧实例后再启动。

For ChatGPT, fully exit the existing client before launching the wrapper. For RDC, run the per-user setup script first. Add `--port=1080` to a shortcut target to use that local HTTP/mixed proxy port. Omitting the argument uses 7897. The wrappers do not change the proxy server's listening port.

## 构建 / Build

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
node .\scripts\Test-AgentHost.mjs
```

使用 Windows 自带的 .NET Framework C# 编译器，输出到 `bin/`；不生成 PDB 调试文件。默认端口只在 `src\ProxyPortOptions.cs` 中定义，两个 exe 共用这份源码。
Build with the Windows .NET Framework C# compiler. Output goes to `bin/` without PDB debug files. Both executables compile the shared default-port source.

## 隐私与审计 / Privacy and review

仓库仅包含整理后的源码、构建/初始化脚本、说明与重新编译的 exe。没有上传个人用户名、个人绝对路径、设备 ID、个人邮箱、配对文件、账户凭据、运行日志或本机诊断记录。7897 和示例 `--port=1080` 是通用参数。

程序运行后会在当前用户的 LocalAppData 写入日志与状态；第三方 Agent 还会管理自己的配对凭据。运行日志可能包含本地路径、账户/设备标识，**不要把运行目录、日志或凭据文件加入 Git**。文件扫描不替代完整的安全审计。

The repository includes reviewed source, build/setup scripts, documentation, and freshly rebuilt executables. No personal paths, device IDs, personal emails, pairing files, credentials, runtime logs, or machine-specific diagnostic records are included. Runtime logs and third-party pairing data remain local and may contain sensitive information; do not commit them. File scanning is not a complete security audit.

参见 [PRIVACY-CHECK.md](PRIVACY-CHECK.md)、[SECURITY-REVIEW.md](SECURITY-REVIEW.md)、[SHA256SUMS.txt](SHA256SUMS.txt) 与 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
