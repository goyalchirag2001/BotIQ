// Run with both lab servers started. Synthetic data only.
import assert from 'node:assert/strict';
const base='http://127.0.0.1:5070/api/';
const workflow={name:'Smoke test',steps:[{action:'fill',selector:'#property',value:'HOTEL-101'},{action:'click',selector:'#search'},{action:'wait',selector:'#results:not(:empty)'}]};
async function call(path,data,status=200){const r=await fetch(base+path,{method:'POST',headers:{'X-BotIQ-Lab':'1','content-type':'application/json'},body:data===undefined?undefined:JSON.stringify(data)});const x=await r.json();assert.equal(r.status,status,JSON.stringify(x));return x;}
try{
 await call('provider/1');
 const old=await call('discovery/capture',workflow);assert.equal(old.truncated,false);assert.ok(old.requests.length>=2);const baseline=old.requests.find(r=>r.operation==='HotelSearchV1');assert.ok(baseline);
 const har={log:{entries:[{request:{method:'POST',url:'http://127.0.0.1:5071/api/hotel/search?token=SECRET',headers:[{name:'Authorization',value:'SECRET'},{name:'content-type',value:'application/json'}],postData:{text:JSON.stringify({operationName:'HotelSearchV1',variables:{hotelId:'HOTEL-101',currency:'USD'}})}},response:{status:200,content:{text:JSON.stringify({data:{rates:[{price:100,translatedName:{value:'Synthetic'}}]}})}}},{request:{method:'GET',url:'https://example.com/private'},response:{status:200}}]}};
 const imported=await call('discovery/har',har);assert.equal(imported.length,1);assert.equal(imported[0].path,'/api/hotel/search');assert.ok(!JSON.stringify(imported).includes('SECRET'));assert.ok(!JSON.stringify(imported).includes('HOTEL-101'));
 await call('discovery/har',{},400);await call('discovery/capture',{...workflow,steps:[{action:'click',selector:'a'}]},400);
 await call('provider/2');const next=await call('discovery/compare',{baseline,workflow});assert.equal(next.match.outcome,'Candidate match');assert.equal(next.match.candidates[0].request.path,'/api/v2/availability');assert.ok(next.capture.requests.length>=2);
 for(const path of ['endpoint','x-demo-contract','$.operationName','$.variables.hotelId','$.variables.propertyId','$.data.rates[*].translatedName'])assert.ok(next.match.differences.some(d=>d.path===path),path);
 console.log('PASS: workflow, HAR import, sanitized fingerprints and ranked browser discovery');
}finally{await call('provider/1');}
