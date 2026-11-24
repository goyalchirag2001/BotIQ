import http from 'node:http';
import { readFileSync } from 'node:fs';
const html = readFileSync(new URL('./index.html', import.meta.url), 'utf8');
let version = 1;
const port = 5071;
function json(res, status, value) {
  res.writeHead(status, {'content-type':'application/json', 'cache-control':'no-store'});
  res.end(JSON.stringify(value));
}
async function body(req) {
  let data = '';
  for await (const chunk of req) { data += chunk; if (data.length > 16384) throw Error('Request too large'); }
  return JSON.parse(data);
}
const server = http.createServer(async (req,res) => {
  try {
    const path = new URL(req.url, 'http://127.0.0.1').pathname;
    if (process.env.BOTIQ_PROVIDER_LOG === '1') console.log(`[provider] ${req.method} ${path}`);
    if (req.method === 'GET' && path === '/') {
      res.writeHead(200, {'content-type':'text/html; charset=utf-8','cache-control':'no-store'});
      return res.end(html.replaceAll('__VERSION__', String(version)));
    }
    if (req.method === 'GET' && path === '/api/state') return json(res,200,{version});
    if (req.method === 'GET' && path === '/api/ping') return json(res,200,{ok:true});
    if (req.method === 'POST' && path === '/admin/version') {
      const input = await body(req);
      if (![1,2].includes(input.version)) return json(res,400,{error:'Version must be 1 or 2'});
      version = input.version; return json(res,200,{version});
    }
    const endpoint = version === 1 ? '/api/hotel/search' : '/api/v2/availability';
    if (req.method !== 'POST' || path !== endpoint) return json(res,404,{error:'Unknown endpoint'});
    const input = await body(req);
    const expectedOperation = version === 1 ? 'HotelSearchV1' : 'PropertyAvailability';
    const idKey = version === 1 ? 'hotelId' : 'propertyId';
    if (req.headers['x-demo-contract'] !== String(version) || input.operationName !== expectedOperation || !input.variables?.[idKey] || input.variables.currency !== 'USD')
      return json(res,400,{error:'Invalid contract'});
    const rates = [{provider:'DemoStay',price:120,translatedName:{value:'Free cancellation'}},{provider:'ExampleRooms',price:135,translatedName:{value:'Room only'}}];
    if (version === 2) rates[0].translatedName = null;
    return json(res,200,{data:{rates}});
  } catch { return json(res,400,{error:'Invalid request'}); }
});
server.listen(port,'127.0.0.1',()=>console.log(`Synthetic provider: http://127.0.0.1:${port}`));
