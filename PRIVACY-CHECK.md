# Pre-upload privacy check / 上传前隐私检查

Date / 日期: 2026-10-06

## Scope / 范围

Reviewed the four source files, two setup/build scripts, documentation, and the freshly rebuilt Windows x64 executables. The uploaded files are selected with an explicit allowlist.

检查四个源码文件、构建/初始化脚本、说明及从源码重新编译的两个 Windows x64 exe；通过明确的文件清单上传。

## Findings / 结果

- No known personal username, personal computer name, device UUID, personal SID, personal peer IP, or absolute user-directory path found in the reviewed payload.
- Text and exe bytes were checked as UTF-8/ASCII and both UTF-16LE alignments.
- No GitHub-token pattern, JWT token pattern, or private-key block found.
- No PDB files or RSDS debug-path metadata are shipped.
- Executables are unsigned; no signing certificate containing a personal identity is included.
- Runtime logs, pairing/device credentials, node_modules, local status files, diagnostic records and local audit details are excluded.
- The default port 7897, example `--port=1080`, private-network bypass ranges, official package identity and public vendor service endpoints are retained as generic functionality.
- Git commits use a project-level author name and a reserved non-personal email address.

未发现上述已知个人标识或凭据模式。保留通用端口、私有网段绕过规则、官方应用包身份及公共厂商服务地址。没有上传运行数据或调试符号；提交元数据使用项目名称及非个人邮箱。

## Limits / 限制

This is a payload/privacy check, not a completed security audit. Pattern scans cannot prove that software has no vulnerabilities or that every possible secret format is absent. The repository remains private for the planned later audit.

这是上传内容检查，不能代替完整安全审计，也不能证明所有可能格式的秘密或漏洞都不存在。仓库保持私有，等待后续审计。
