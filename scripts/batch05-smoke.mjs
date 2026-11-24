// Both servers must run. Run with --history-only after restart to verify persistence.
import assert from 'node:assert/strict';
const root='http://127.0.0.1:5070/api/';
const workflow={name:'Batch 05 navigation smoke',steps:[{action:'fill',selector:'#property',value:'HOTEL-101'},{action:'click',selector:'#search'},{action:'wait',selector:'#results:not(:empty)'}]};
async function get(path){const r=await fetch(root+path);const x=await r.json();assert.equal(r.status,200,JSON.stringify(x));return x;}
async function post(path,body){const r=await fetch(root+path,{method:'POST',headers:{'X-BotIQ-Lab':'1','content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});const x=await r.json();assert.equal(r.status,200,JSON.stringify(x));return x;}
const platform=await get('platform');assert.match(platform.version,/^0\.5\.[01]$/);console.log('History storage:',platform.historyStorage);
if(process.argv.includes('--history-only')){const h=await get('history?limit=1');assert.equal(h.length,1,'No history: first run full smoke, then restart with same storage');console.log('PASS: persisted history',h[0].id);process.exit(0);}
const previous=await get('history?limit=100');
try{
 const doc=await fetch('http://127.0.0.1:5071/');assert.equal(doc.status,200);assert.ok((await doc.text()).includes('id="property"'));
 await post('provider/1');
 const old=await post('discovery/capture',workflow);assert.equal(old.truncated,false);assert.ok(old.diagnostics.some(x=>x.includes('Chromium navigation succeeded: http://127.0.0.1:5071/')),old.diagnostics.join(' '));
 const baseline=old.requests.find(x=>x.operation==='HotelSearchV1');assert.ok(baseline);assert.equal(baseline.status,200);
 await post('provider/2');
 const result=await post('discovery/compare',{baseline,workflow});assert.equal(result.match.outcome,'Candidate match');assert.ok(result.match.differences.length>0);
 const history=await get('history?limit=100');const fresh=history.find(x=>!previous.some(p=>p.id===x.id));assert.ok(fresh);assert.equal(fresh.kind,'discovery');assert.equal(fresh.outcome,result.match.outcome);assert.equal(fresh.differenceCount,result.match.differences.length);assert.equal(fresh.browserStatus,200);assert.ok(!('requestBody' in fresh));assert.ok(!('responseBody' in fresh));
 assert.deepEqual(await get('history?botId=00000000-0000-0000-0000-000000000000'),[]);
 assert.equal((await get('history?limit=1')).length,1);
 console.log('PASS: browser navigation, independent v1/v2 discovery and persisted history');
}finally{await post('provider/1');}
