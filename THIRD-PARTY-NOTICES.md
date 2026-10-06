# Third-party components

These wrappers are independent from OpenAI and Desktop Commander. Names and package identities are used only to identify the supported applications.

- Desktop Commander: official npm package `@wonderwhy-er/desktop-commander`; downloaded by the RDC launcher at runtime. Upstream: https://github.com/wonderwhy-er/DesktopCommanderMCP
- Undici: `undici@8.11.2`, installed from the official npm registry by the per-user setup script. Upstream: https://github.com/nodejs/undici
- Node.js, npm, Windows PowerShell and .NET Framework: external prerequisites; their binaries are not bundled.
- The Supabase hostname in `src/agent-host.mjs` is the RDC service's public backend address. It is not a personal project ID or account credential.

Third-party software is not included in this repository. Its own licensing, service terms, privacy practices, and installation behavior apply. Review these dependencies along with the launcher source before public distribution.
