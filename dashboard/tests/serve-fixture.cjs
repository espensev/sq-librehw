// Read-only, loopback fixture host for the actual dashboard assets.
// Optional HTTPS peer fixtures stand in for tailnet machines during fleet
// acceptance: each peer serves only /data.json (plus CORS) from the recorded
// peer fixture with a distinct machine name, and a control route on the
// dashboard host switches a peer into a failure mode. Nothing here proxies or
// touches live machines.
const http = require('node:http');
const https = require('node:https');
const fs = require('node:fs/promises');
const path = require('node:path');

const web = path.resolve(__dirname, '../web');
const types = {'.html':'text/html', '.js':'text/javascript', '.css':'text/css',
  '.ico':'image/x-icon', '.png':'image/png', '.json':'application/json'};
const PEER_MODES = ['ok', 'error', 'html', 'invalid', 'slow', 'duplicate'];

function createFixtureServer(options = {}) {
  const peers = options.peers || [];
  return http.createServer(async (request, response) => {
    if (request.method !== 'GET') {
      response.writeHead(405, {Allow:'GET'}).end();
      return;
    }
    try {
      const url = new URL(request.url, 'http://fixture.invalid');
      const route = decodeURIComponent(url.pathname);
      if (route === '/__peers') {
        response.writeHead(200, {'Content-Type':'application/json', 'Cache-Control':'no-store'})
          .end(JSON.stringify(peers.map(peer => ({label:peer.label, url:peer.url, mode:peer.mode}))));
        return;
      }
      if (route === '/__peer') {
        const peer = peers[Number(url.searchParams.get('index'))];
        const mode = url.searchParams.get('mode');
        if (!peer || !PEER_MODES.includes(mode)) { response.writeHead(400).end(); return; }
        peer.mode = mode;
        response.writeHead(200, {'Content-Type':'application/json', 'Cache-Control':'no-store'})
          .end(JSON.stringify({label:peer.label, url:peer.url, mode}));
        return;
      }
      const telemetry = route === '/data.json';
      const file = telemetry ? path.join(__dirname, 'fixture.data.json')
        : path.resolve(web, '.' + (route === '/' ? '/index.html' : route));
      if (!telemetry && !file.startsWith(web + path.sep)) {
        response.writeHead(404).end();
        return;
      }
      const body = await fs.readFile(file);
      response.writeHead(200, {'Content-Type':types[path.extname(file)] || 'application/octet-stream',
        'Cache-Control':'no-store'}).end(body);
    } catch {
      response.writeHead(404).end();
    }
  });
}

// One HTTPS machine: GET /data.json only, CORS *, no other routes.
function createPeerServer(peer, tls) {
  return https.createServer(tls, async (request, response) => {
    const cors = {'Access-Control-Allow-Origin':'*', 'Cache-Control':'no-store'};
    if (request.method !== 'GET') { response.writeHead(405, {...cors, Allow:'GET'}).end(); return; }
    const route = new URL(request.url, 'https://fixture.invalid').pathname;
    if (route !== '/data.json') { response.writeHead(404, cors).end(); return; }
    const data = JSON.parse(await fs.readFile(path.join(__dirname, 'fixture.peer.data.json'), 'utf8'));
    data.Children[0].Text = peer.label;
    switch (peer.mode) {
      case 'error': response.writeHead(503, cors).end(); return;
      case 'html': response.writeHead(200, {...cors, 'Content-Type':'text/html'}).end('<!doctype html><title>login</title>'); return;
      case 'invalid': response.writeHead(200, {...cors, 'Content-Type':'application/json'}).end('{"Children":"nope"}'); return;
      case 'duplicate': {
        const first = data.Children[0].Children[0];
        data.Children[0].Children.push(JSON.parse(JSON.stringify(first)));
        break;
      }
      case 'slow': await new Promise(resolve => setTimeout(resolve, 20000)); break;
    }
    response.writeHead(200, {...cors, 'Content-Type':'application/json'}).end(JSON.stringify(data));
  });
}

function parseArgs(argv) {
  const opts = {port:0, peers:0, cert:null, key:null, peerPort:0};
  const positional = [];
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--peers') opts.peers = Number(argv[++i]);
    else if (arg === '--cert') opts.cert = argv[++i];
    else if (arg === '--key') opts.key = argv[++i];
    else if (arg === '--peer-port') opts.peerPort = Number(argv[++i]);
    else positional.push(arg);
  }
  if (positional[0] !== undefined) opts.port = Number(positional[0]);
  const validPort = value => Number.isInteger(value) && value >= 0 && value <= 65535;
  if (!validPort(opts.port) || !validPort(opts.peerPort)) throw new Error('Use a port from 0 to 65535.');
  if (!Number.isInteger(opts.peers) || opts.peers < 0 || opts.peers > 8) throw new Error('Use 0 to 8 peers.');
  if (opts.peers && (!opts.cert || !opts.key)) throw new Error('HTTPS peers need --cert and --key PEM files.');
  return opts;
}

module.exports = {createFixtureServer, createPeerServer, PEER_MODES};
if (require.main === module) {
  (async () => {
    const opts = parseArgs(process.argv.slice(2));
    const peers = [];
    const servers = [];
    if (opts.peers) {
      const tls = {cert:await fs.readFile(opts.cert), key:await fs.readFile(opts.key)};
      for (let i = 0; i < opts.peers; i++) {
        const peer = {label:'PEER-' + (i + 1), url:null, mode:'ok'};
        const server = createPeerServer(peer, tls);
        await new Promise(resolve => server.listen(opts.peerPort ? opts.peerPort + i : 0, '127.0.0.1', resolve));
        peer.url = 'https://127.0.0.1:' + server.address().port;
        peers.push(peer);
        servers.push(server);
        console.log(`Peer fixture ${peer.label}: ${peer.url} (GET /data.json only)`);
      }
    }
    const server = createFixtureServer({peers});
    servers.push(server);
    server.listen(opts.port, '127.0.0.1', () => console.log('Dashboard fixture: http://127.0.0.1:' + server.address().port));
    const close = () => servers.forEach(item => { item.closeAllConnections(); item.close(); });
    process.on('SIGINT', close);
    process.on('SIGTERM', close);
  })().catch(error => { console.error(error.message); process.exit(1); });
}
