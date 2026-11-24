// Tests gateway, routes, real byte streaming and failure reporting without Angular or .NET installed.
import assert from 'node:assert/strict';import http from 'node:http';import {mkdtemp,writeFile,rm} from 'node:fs/promises';import {tmpdir} from 'node:os';import path from 'node:path';import {createFrontend} from './frontend-server.mjs';import {nodeSupported} from './start-local.mjs';
const listen=(s)=>new Promise(r=>s.listen(0,'127.0.0.1',()=>r(s.address().port)));const close=s=>new Promise(r=>{s.closeAllConnections();s.close(r)});
const root=await mkdtemp(path.join(tmpdir(),'botiq-gateway-'));let upstream,server,lab;
try{
 assert.equal(nodeSupported('v22.16.0'),false);assert.equal(nodeSupported('v22.22.3'),true);assert.equal(nodeSupported('v24.15.0'),true);assert.equal(nodeSupported('v25.0.0'),false);
 await assert.rejects(createFrontend({root}),/Angular build unavailable/);
 await writeFile(path.join(root,'index.html'),'<app-root></app-root><script src="main.js" type="module"></script>');await writeFile(path.join(root,'main.js'),'console.log("fixture")');
 upstream=http.createServer(async(req,res)=>{let bytes=0;for await(const chunk of req)bytes+=chunk.length;res.setHeader('content-type','application/json');res.end(JSON.stringify({path:req.url,bytes,marker:req.headers['x-botiq-lab']}));});const apiPort=await listen(upstream);
 // createFrontend needs the public Host port. Choose a free port, then bind it.
 const reserve=http.createServer();const port=await listen(reserve);await close(reserve);server=await createFrontend({root,apiPort,port});await new Promise(r=>server.listen(port,'127.0.0.1',r));const base=`http://127.0.0.1:${port}`;
 for(const route of ['/','/bots','/discovery','/history']){const r=await fetch(base+route);assert.equal(r.status,200);assert.match(await r.text(),/app-root/);}
 assert.match((await fetch(base+'/main.js')).headers.get('content-type'),/javascript/);assert.equal((await fetch(base+'/missing.js')).status,404);
 assert.equal((await (await fetch(base+'/_botiq/health')).json()).mode,'angular');
 const large='x'.repeat(6_000_000);const r=await fetch(base+'/api/discovery/har',{method:'POST',headers:{'X-BotIQ-Lab':'1','content-type':'application/json'},body:large});assert.deepEqual(await r.json(),{path:'/api/discovery/har',bytes:large.length,marker:'1'});
 assert.equal((await (await fetch(base+'/lab')).json()).path,'/index.html');assert.equal((await (await fetch(base+'/api/history?limit=2')).json()).path,'/api/history?limit=2');
 await close(upstream);upstream=null;assert.equal((await fetch(base+'/api/health')).status,502);
 await close(server);server=null;const spare=http.createServer();const labPort=await listen(spare);await close(spare);lab=await createFrontend({root,labOnly:true,port:labPort});await new Promise(r=>lab.listen(labPort,'127.0.0.1',r));const labBase=`http://127.0.0.1:${labPort}`;assert.match(await (await fetch(labBase+'/')).text(),/not the Angular application/);assert.equal((await (await fetch(labBase+'/_botiq/health')).json()).mode,'lab');
 console.log('PASS: Node compatibility, missing build detection, frontend routes/assets, 6 MB streaming proxy, legacy routes, backend failure and explicit lab mode.');
}finally{for(const s of [server,lab,upstream])if(s)await close(s);await rm(root,{recursive:true,force:true});}
