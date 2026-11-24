// Run with both local servers started. Creates one immutable local synthetic snapshot.
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const url='http://127.0.0.1:5070/api/';
async function call(path,method='GET',body){
 const r=await fetch(url+path,{method,headers:{'X-BotIQ-Lab':'1','Content-Type':'application/json'},...(body?{body:JSON.stringify(body)}:{})});
 const x=await r.json();if(!r.ok)throw Error(`${r.status}: ${JSON.stringify(x)}`);return x;
}
const content=await readFile(new URL('../src/BotIQ.SampleBot/HotelBot.cs',import.meta.url),'utf8');
const b=await call('bots','POST',{name:'HOTEL-DEMO-ANDROID',version:'smoke-v1',files:[{name:'HotelBot.cs',content}]});
assert.ok(b.id);assert.equal(b.analysis.diagnostics.length,0);assert.ok(b.analysis.evidence.some(e=>e.kind==='EndpointLiteral'&&e.key==='/api/hotel/search'));
assert.equal((await call('bots/'+b.id)).sourceHash,b.sourceHash);assert.ok((await call('bots')).some(x=>x.id===b.id));
const invalid=await fetch(url+'bots',{method:'POST',headers:{'X-BotIQ-Lab':'1','Content-Type':'application/json'},body:JSON.stringify({name:'bad',version:'1',files:[{name:'../bad.cs',content:'class C {}'}]})});assert.equal(invalid.status,400);
const noMarker=await fetch(url+'bots',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});assert.equal(noMarker.status,403);
try{await call('provider/1','POST');await call('baseline','POST');await call('provider/2','POST');const report=await call(`bots/${b.id}/demo-investigation`,'POST');
 assert.equal(report.investigation.currentBot.exchange.status,404);assert.equal(report.sourceSnapshot.id,b.id);
 for(const path of ['endpoint','x-demo-contract','$.operationName','$.variables.hotelId','$.data.rates[*].translatedName'])assert.ok(report.impact.some(e=>e.path===path),path);
 console.log('PASS: registry, validation, source persistence and live source-impact mapping');
}finally{await call('provider/1','POST');}
