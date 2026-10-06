# Security and privacy review / 安全与隐私审查

Second review: 2026-10-07. Decision: **launcher-only review passed for public publication; no unresolved release-blocking defect found in the reviewed wrapper scope**.
第二轮审查：2026-10-07。结论：**按启动器壳层范围通过公开发布检查，未发现范围内未解决的发布阻碍**。

## Scope and threat model / 范围

Reviewed launcher source, setup/build scripts, compiled binaries, reachable Git history, process invocation, proxy boundaries and wrapper-introduced privacy risks. Third-party internals and downstream user behavior are excluded under [DISCLAIMER.md](DISCLAIMER.md); the wrapper integration remains in scope. The Windows user account, installed official application, Node/npm installation, local proxy and vendor services are trusted. A malicious process already running as the same Windows user can replace user-writable launchers/runtime files; these wrappers are not a sandbox or an elevation boundary.

审查启动器源码、脚本、编译产物、可达 Git 历史、进程调用、代理范围与包装层引入的隐私风险。依据 [DISCLAIMER.md](DISCLAIMER.md)，第三方内部漏洞和下游用户行为排除在此次范围之外；包装层自己的集成安全仍在范围内。信任当前 Windows 用户、官方客户端、Node/npm、本地代理和厂商服务。程序不能防止同一用户权限下的恶意进程替换用户可写文件，也不提供权限隔离。

## Corrections / 已修正

- RDC updater and setup invoke Node's npm CLI directly, removing cmd.exe interpretation of paths containing shell metacharacters or environment expansion. Explicit npm registry and strict TLS verification are used; the updater also fixes the package's scoped registry and validates installed name/version.
- ChatGPT package handoff uses UTF-16LE PowerShell EncodedCommand and quotes the executable path as a literal. Package context checks include the expected publisher ID.
- PowerShell stdout/stderr are drained concurrently with bounded process waits; the previous read-to-EOF ordering could hang before reaching the timeout.
- Version validation uses ASCII digits and absolute string boundaries, rejecting trailing newlines and command/path separators.
- Documentation no longer implies that NO_PROXY covers every domestic destination or that all child networking libraries implement CIDR bypass identically.

消除 RDC 更新中的 shell 路径解释；ChatGPT 使用编码命令保护路径参数；补充包身份检查；修复输出管道阻塞与超时失效；收紧版本字符串验证，并说明代理边界。

## Historical upstream information (out of scope) / 上轮上游信息（不属于此次范围）

The broader review on 2026-10-06 recorded official npm latest as **Desktop Commander 0.2.52**. Both the existing runtime lock and a newly resolved production dependency lock reported **5 high and 6 moderate affected packages** via online `npm audit --omit=dev`. These counts include dependent packages and are not independent vulnerability counts. The fresh resolution used package-lock-only and ignore-scripts in an isolated directory; no Agent installation scripts or pairing were executed.

| Component in fresh resolution | Finding |
| --- | --- |
| sharp 0.34.5 | High advisories in bundled image libraries: [libvips](https://github.com/advisories/GHSA-f88m-g3jw-g9cj), [libheif](https://github.com/advisories/GHSA-rgj7-g3m4-5g8c), [librsvg](https://github.com/advisories/GHSA-wq5f-xc86-pv6w). The librsvg advisory's possible RCE specifically concerns glibc Linux; this is not evidence of Windows RCE. |
| braces 3.0.3 | [Stack-exhaustion denial of service](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm), inherited through chokidar/md-to-pdf. |
| sprintf-js 1.0.3 | [Unbounded precision denial of service](https://github.com/advisories/GHSA-hp3w-g68c-fv3c), inherited through YAML/front-matter dependencies. |
| uuid 8.3.2 | [Missing buffer bounds check](https://github.com/advisories/GHSA-w5hq-g745-h8pq), inherited through exceljs. |
| undici 8.11.2, wrapper proxy runtime | Online npm audit reported zero known vulnerabilities at review time. |

The launcher does not bundle RDC dependencies, but downloads and executes them at runtime. No exploit against this launcher or live device was demonstrated. Reachability and platform applicability require further assessment. A clean privacy scan does not resolve these dependency findings.

RDC 依赖虽未随仓库打包，但会在运行时下载并执行。上述结果不表示已证明本机可被利用；仍需结合平台和调用路径进一步判断。这些历史结果保留为外部信息，不作为此次壳层发布阻碍；未声称上游问题已经修复。

Do not use `npm audit fix --force` or silently downgrade RDC to clear the report: the suggested older Agent and major dependency replacements need compatibility and remote-control tests. The owner subsequently limited release review to this wrapper on 2026-10-07; upstream fixes remain with their maintainers and are not a gate under that scope.

不能为了清除告警直接强制修复或降级 Agent。2026-10-07 所有者明确将此次发布审查限定为壳层，上游修复由相应作者负责，不作为壳层发布前置条件。

## Second-round wrapper checks / 第二轮壳层检查

- Re-read all project source and scripts for command injection, unintended network/proxy scope, global configuration writes, credential handling and telemetry. No new release-blocking wrapper finding was identified after the correction below.
- Tightened agent-host.mjs port matching to reject a trailing newline; its previous JavaScript regex end marker accepted that boundary on direct invocation. Normal tray input was already validated by the shared C# parser.
- Added Test-AgentHost.mjs with inert dispatcher/Agent fixtures. It verifies exact cloud-host routing, rejects lookalike hostnames, keeps local/private/Tailscale/.cn fixture destinations direct, and rejects invalid and newline-suffixed ports. It does not execute a third-party Agent, perform pairing or make network requests.
- Existing C# regression tests, fresh binary rebuild, checksum validation, syntax checks and repeat privacy/history scanning passed. No installed client restart was required. These are bounded review results, not a guarantee that every execution path is defect-free.

再次检查了命令传递、下载调用、代理边界、配置写入、凭据和遥测。修正直接调用 JS 包装层时换行结尾端口的校验边界，补充路由白名单的隔离测试。未发现新增壳层遥测、凭据上传、证书校验关闭或全局网络配置修改。第三方进程自己的行为不在本结论内。

## Privacy and validation / 隐私与验证

- Scanned reachable historical blobs and current release files in UTF-8 and both UTF-16LE byte alignments for known local identifiers, absolute user paths, credential patterns and private-key blocks; inspected Git commit identity and binary debug metadata. No matching personal information or credentials were found in the reviewed payload.
- Runtime logs, pairing credentials, device configuration and local audit artifacts are excluded from publication. Generic default 7897 and example --port=1080 are retained.
- Rebuilt both x64 executables without debug symbols and refreshed SHA256SUMS.txt.
- Passed scripts/Test.ps1: both binaries' port parsing, invalid/injection-shaped ports, strict version boundaries, literal special-character paths through a stub package API and direct Node invocation, concurrent output draining, and timeout enforcement.
- JavaScript/PowerShell syntax and Git whitespace checks passed. Tests did not start/stop the installed clients or change system networking; full live client restart and Agent lifecycle integration were not repeated during this review.

运行日志可能包含路径、设备标识或授权链接，属于敏感本地数据。源码/二进制扫描并不能证明不存在任何形式的秘密。普通公开 GitHub 仓库仍会显示仓库所有者的 GitHub 账号；通用 Git 提交身份不会隐藏所有权。

## Remaining trust and limitations / 信任边界

Automatic RDC updates trust the official npm publisher, registry and transitive dependencies. Integrity checks do not establish that an upstream update is benign; this version has no mandatory vulnerability gate for every future update. RDC installation scripts are permitted because upstream/native dependency setup may require them; the proxy runtime's setup disables scripts. See [npm installation documentation](https://docs.npmjs.com/cli/v11/commands/npm-install/).

Agent output remains complete as requested and may contain sensitive runtime data. No new telemetry is added by these wrappers, but upstream software has its own telemetry and service behavior. Executables are unsigned. This is a bounded code/privacy review, not an external penetration-test certification or a guarantee of vulnerability-free software.
