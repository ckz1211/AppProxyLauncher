import { strict as assert } from 'node:assert';
import { mkdtempSync, mkdirSync, writeFileSync, copyFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

// Test only the wrapper using an inert dispatcher and entry, without network or pairing.
const root = mkdtempSync(join(tmpdir(), 'appproxy-host-test-'));
try {
  copyFileSync(fileURLToPath(new URL('../src/agent-host.mjs', import.meta.url)), join(root, 'agent-host.mjs'));
  const modules = join(root, 'proxy-runtime', 'node_modules', 'undici');
  mkdirSync(modules, { recursive: true });
  writeFileSync(join(root, 'proxy-runtime', 'package.json'), '{"private":true}');
  writeFileSync(join(modules, 'index.js'), `
exports.Agent = class { constructor(options) { this.options = options; } };
exports.Pool = class { constructor(origin) { this.kind = 'direct'; this.origin = origin; } };
exports.ProxyAgent = class { constructor(proxy) { this.kind = 'proxy'; this.proxy = proxy; } };
exports.setGlobalDispatcher = dispatcher => { globalThis.wrapperDispatcher = dispatcher; };
`);
  const entry = join(root, 'fixture.mjs');
  writeFileSync(entry, `
import { strict as assert } from 'node:assert';
const factory = globalThis.wrapperDispatcher.options.factory;
for (const origin of ['https://mcp.desktopcommander.app','https://MCP.DESKTOPCOMMANDER.APP','https://olvbkozcufcbptfogatw.supabase.co']) {
  const result = factory(origin, {});
  assert.equal(result.kind, 'proxy');
  assert.equal(result.proxy, 'http://127.0.0.1:1080');
}
for (const origin of ['https://mcp.desktopcommander.app.attacker.invalid','https://other.supabase.co','http://127.0.0.1','http://192.168.1.1','https://example.cn','http://100.64.0.1']) {
  assert.equal(factory(origin, {}).kind, 'direct');
}
assert.equal(process.argv[2], 'remote');
console.log('ROUTING_PASS');
`);
  const run = port => spawnSync(process.execPath, [join(root, 'agent-host.mjs'), entry, port], { input: '', encoding: 'utf8', timeout: 10000 });
  const valid = run('--port=1080');
  assert.equal(valid.status, 0, valid.stderr);
  assert.match(valid.stdout, /ROUTING_PASS/);
  for (const port of ['--port=0','--port=65536','--port=1080\n','--port=1080;whoami','--port=+1080','--port=1.5']) {
    const invalid = run(port);
    assert.notEqual(invalid.status, 0);
    assert.match(invalid.stderr, /requires a validated/);
    assert.doesNotMatch(invalid.stdout, /ROUTING_PASS/);
  }
  console.log('PASS: wrapper routing allowlist and port boundaries; no network or third-party Agent executed.');
} finally {
  // root is the exact fresh directory returned by mkdtempSync above.
  rmSync(root, { recursive: true, force: true });
}