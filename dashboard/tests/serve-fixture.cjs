// Read-only, loopback fixture host for the actual dashboard assets.
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');

const web = path.resolve(__dirname, '../web');
const types = {'.html':'text/html', '.js':'text/javascript', '.css':'text/css',
  '.ico':'image/x-icon', '.png':'image/png', '.json':'application/json'};

function createFixtureServer() {
  return http.createServer(async (request, response) => {
    if (request.method !== 'GET') {
      response.writeHead(405, {Allow:'GET'}).end();
      return;
    }
    try {
      const route = decodeURIComponent(new URL(request.url, 'http://fixture.invalid').pathname);
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

module.exports = {createFixtureServer};
if (require.main === module) {
  const port = process.argv[2] === undefined ? 0 : Number(process.argv[2]);
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('Use a port from 0 to 65535.');
  const server = createFixtureServer();
  server.listen(port, '127.0.0.1', () => console.log('Dashboard fixture: http://127.0.0.1:' + server.address().port));
  const close = () => { server.closeAllConnections(); server.close(); };
  process.on('SIGINT', close);
  process.on('SIGTERM', close);
}
