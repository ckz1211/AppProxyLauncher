import readline from 'node:readline';
import { pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
// Process-local dispatcher: only RDC's cloud services use the local proxy.
// No HTTP_PROXY environment variables are set or inherited by spawned tools.
const requireProxy = createRequire(new URL('./proxy-runtime/package.json', import.meta.url));
const { Agent, Pool, ProxyAgent, setGlobalDispatcher } = requireProxy('undici');
const portArgument = process.argv[3] ?? '';
const proxyPort = Number(portArgument.slice(7));
if (/^--port=[0-9]+$/.exec(portArgument)?.[0] !== portArgument || !Number.isInteger(proxyPort) || proxyPort < 1 || proxyPort > 65535)
  throw new Error('Agent host requires a validated --port=1..65535 from the tray launcher');
const proxyUrl = `http://127.0.0.1:${proxyPort}`;
const route = origin => {
  const host = new URL(origin).hostname.toLowerCase();
  return host === 'mcp.desktopcommander.app' || host === 'olvbkozcufcbptfogatw.supabase.co';
};
setGlobalDispatcher(new Agent({factory(origin, options) {
  return route(origin) ? new ProxyAgent(proxyUrl) : new Pool(origin, options);
}}));
console.error(`[RDCTray] Scoped proxy enabled for RDC cloud HTTPS/WebSocket via 127.0.0.1:${proxyPort}; other destinations direct.`);
const entry = process.argv[2];
if (!entry) throw new Error('Missing agent entry');
let stopping = false;
const stop = () => {
  if (stopping) return;
  stopping = true;
  process.emit('SIGTERM');
  setTimeout(() => process.exit(0), 8000).unref();
};
readline.createInterface({ input: process.stdin, terminal: false })
  .on('line', line => { if (line === 'RDC_TRAY_EXIT') stop(); })
  .on('close', stop);
process.argv = [process.execPath, entry, 'remote'];
await import(pathToFileURL(entry).href);
