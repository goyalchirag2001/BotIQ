// Dependency-free loopback frontend gateway. Streams API requests, including large HARs.
import http from 'node:http';
import {createReadStream} from 'node:fs';
import {stat,readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const defaultRoot=fileURLToPath(new URL('../frontend/dist/botiq/browser/',import.meta.url));
const allowedRoutes=new Set(['/','/bots','/discovery','/history']);
const legacy=new Map([['/lab','/index.html'],['/bots.html','/bots.html'],['/discovery.html','/discovery.html'],['/intelligence.html','/intelligence.html'],['/evidence.html','/evidence.html'],['/evidence-engine.js','/evidence-engine.js']]);
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.mjs':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json','.svg':'image/svg+xml','.png':'image/png','.ico':'image/x-icon','.woff2':'font/woff2'};
function json(res,status,data){res.writeHead(status,{'content-type':'application/json','cache-control':'no-store'});res.end(JSON.stringify(data));}
const fallback=`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>BotIQ local lab</title><style>body{font:16px system-ui;background:#101827;color:#e5edf8;max-width:760px;margin:60px auto;padding:24px}a{color:#5eead4}section{border:1px solid #334155;border-radius:12px;padding:24px}li{margin:14px 0}</style><h1>BotIQ · Local lab</h1><section><p><strong>Lab-only mode — not the Angular application.</strong> The Angular bundle is not being served in this mode. Run the full startup command to build and serve Angular.</p><p id="status">Checking backend…</p><ul><li><a href="/lab">Three-way investigation</a></li><li><a href="/bots.html">Bot registry</a></li><li><a href="/evidence.html">External HAR &amp; C# evidence</a></li><li><a href="/discovery.html">Browser discovery and HAR import</a></li><li><a href="/intelligence.html">Runtime and dependency intelligence</a></li><li><a href="/api/history">Investigation history (JSON)</a></li><li><a href="http://127.0.0.1:5071/">Synthetic provider</a></li></ul></section><script>fetch('/api/health').then(r=>{if(!r.ok)throw Error('HTTP '+r.status);return r.json()}).then(x=>document.getElementById('status').textContent='API ready · '+x.version).catch(e=>document.getElementById('status').textContent='API unavailable: '+e.message)</script></html>`;
export async function createFrontend({root=defaultRoot,labOnly=false,apiPort=5070,port=4200}={}){
 root=path.resolve(root);
 if(!labOnly){try{const html=await readFile(path.join(root,'index.html'),'utf8');if(!html.includes('<app-root'))throw Error('Missing Angular root');const bundles=[...html.matchAll(/<script\b[^>]*\bsrc=["']([^"']+)["']/g)].map(m=>m[1]);if(!bundles.length)throw Error('No Angular bundle scripts');for(const src of bundles){const file=path.resolve(root,src.replace(/^\//,''));if(!file.startsWith(root+path.sep)||(await stat(file)).isFile()===false)throw Error('Missing Angular script '+src);}}catch(e){throw Error(`Angular build unavailable at ${root}: ${e.message}. Run npm install and npm run build, or use --lab-only explicitly.`);}}
 return http.createServer(async(req,res)=>{
  try{
   if(!new Set([`127.0.0.1:${port}`,`localhost:${port}`,`[::1]:${port}`]).has(req.headers.host)){return json(res,403,{detail:'Use the local BotIQ address.'});}
   const url=new URL(req.url,'http://127.0.0.1');const pathname=url.pathname;
   if(pathname==='/_botiq/health')return json(res,200,{service:'BotIQ.Frontend',mode:labOnly?'lab':'angular',version:'0.5.1'});
   const target=pathname.startsWith('/api/')?pathname+url.search:legacy.get(pathname);
   if(target){
    if(!['GET','HEAD','POST'].includes(req.method))return json(res,405,{detail:'Method not supported'});
    const headers={...req.headers,host:`127.0.0.1:${apiPort}`};delete headers.connection;delete headers['transfer-encoding'];delete headers.upgrade;
    const upstream=http.request({hostname:'127.0.0.1',port:apiPort,path:target,method:req.method,headers,timeout:600_000},r=>{if(res.destroyed){r.destroy();return;}res.writeHead(r.statusCode,r.headers);r.pipe(res);r.on('error',()=>res.destroy());});
    upstream.on('timeout',()=>upstream.destroy(Error('Backend timed out')));
    upstream.on('error',e=>{if(!res.headersSent&&!res.destroyed)json(res,502,{detail:'BotIQ API unavailable: '+e.message+'. Check API startup logs.'});else res.destroy();});
    req.on('aborted',()=>upstream.destroy());res.on('close',()=>{if(!res.writableEnded)upstream.destroy();});req.pipe(upstream);return;
   }
   if(!['GET','HEAD'].includes(req.method))return json(res,405,{detail:'Method not supported'});
   if(labOnly){if(pathname==='/'){res.writeHead(200,{'content-type':'text/html; charset=utf-8','cache-control':'no-store'});return res.end(req.method==='HEAD'?undefined:fallback);}return json(res,404,{detail:'Angular is not built in lab-only mode; open / for lab tools.'});}
   const decoded=decodeURIComponent(pathname);if(decoded.includes('\\')||decoded.includes('\0'))return json(res,400,{detail:'Invalid path'});
   const file=path.resolve(root,'.'+decoded);if(file!==root&&!file.startsWith(root+path.sep))return json(res,403,{detail:'Invalid path'});
   let chosen=allowedRoutes.has(pathname)?path.join(root,'index.html'):file;
   let info;try{info=await stat(chosen);}catch{return json(res,404,{detail:'Frontend resource not found'});}if(!info.isFile())return json(res,404,{detail:'Frontend resource not found'});
   res.writeHead(200,{'content-type':types[path.extname(chosen)]||'application/octet-stream','content-length':info.size,'cache-control':'no-store'});
   if(req.method==='HEAD')return res.end();const stream=createReadStream(chosen);stream.on('error',()=>res.destroy());stream.pipe(res);
  }catch(e){if(!res.headersSent)json(res,400,{detail:'Invalid frontend request'});else res.destroy();}
 });
}
export async function listenLocal(options={}){
 const port=options.port??4200;const primary=await createFrontend({...options,port});const listen=(s,host)=>new Promise((resolve,reject)=>{s.once('error',reject);s.listen({port,host,ipv6Only:host==='::1'},()=>{s.removeListener('error',reject);resolve();});});
 await listen(primary,'127.0.0.1');let secondary;
 try{secondary=await createFrontend({...options,port});await listen(secondary,'::1');}catch(e){if(!['EAFNOSUPPORT','EADDRNOTAVAIL','EPROTONOSUPPORT'].includes(e.code)){primary.close();throw e;}console.warn('IPv6 loopback unavailable; use http://127.0.0.1:'+port);secondary=undefined;}
 return [primary,...(secondary?[secondary]:[])];
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 const labOnly=process.argv.includes('--lab-only');try{await listenLocal({labOnly});console.log(`BotIQ ${labOnly?'local lab (NOT Angular)':'Angular'} ready: http://127.0.0.1:4200/ (localhost:4200 on supported loopback)`);}catch(e){console.error(e.message);process.exitCode=1;}
}
