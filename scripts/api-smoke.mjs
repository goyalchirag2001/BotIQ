// Run with both servers started. Resets the lab to v1 at the end.
import assert from 'node:assert/strict';
const url='http://127.0.0.1:5070/api/';
async function call(path,method='POST'){
 const r=await fetch(url+path,{method,headers:{'X-BotIQ-Lab':'1'}});const x=await r.json();
 if(!r.ok)throw Error(`${r.status}: ${JSON.stringify(x)}`);return x;
}
try{
 await call('provider/1');const baseline=await call('baseline');assert.equal(baseline.bot.output.rateCount,2);
 const healthy=await call('investigate');assert.equal(healthy.historicalVsLive.length,0);assert.equal(healthy.currentBot.output.rateCount,2);
 await call('provider/2');const result=await call('investigate');
 assert.equal(result.currentBot.exchange.status,404);assert.equal(result.liveBrowser.status,200);assert.ok(result.liveResponseWithOldParser.error);
 for(const path of ['endpoint','x-demo-contract','$.operationName','$.variables.hotelId','$.variables.propertyId','$.data.rates[*].translatedName'])assert.ok(result.historicalVsLive.some(x=>x.path===path),path);
 console.log('PASS: .NET API three-way investigation');
}finally{await call('provider/1');}
