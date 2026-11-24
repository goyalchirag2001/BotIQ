// Starts the actual provider, checks both HTTP contracts, then stops it.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const base='http://127.0.0.1:5071';
const providerPath=new URL('../demo-provider/server.mjs',import.meta.url);
const child=spawn(process.execPath,[fileURLToPath(providerPath)],{stdio:['ignore','pipe','inherit']});
const ready=Promise.race([once(child.stdout,'data'),once(child,'exit').then(()=>{throw Error('Provider exited before startup');})]);
async function request(path,payload,version){
 const r=await fetch(base+path,payload===undefined?{}:{method:'POST',headers:{'content-type':'application/json','x-demo-contract':String(version)},body:JSON.stringify(payload)});
 return {status:r.status,data:await r.json()};
}
try{
 await ready;
 assert.equal((await request('/api/state')).data.version,1);
 const html1=await (await fetch(base)).text();assert.ok(html1.includes('const version = 1;'));
 const v1={operationName:'HotelSearchV1',variables:{hotelId:'HOTEL-101',currency:'USD'}};
 const v2={operationName:'PropertyAvailability',variables:{propertyId:'HOTEL-101',currency:'USD'}};
 const old=await request('/api/hotel/search',v1,1);assert.equal(old.status,200);assert.equal(old.data.data.rates.length,2);
 assert.equal((await request('/admin/version',{version:2})).status,200);
 const html2=await (await fetch(base)).text();assert.ok(html2.includes('const version = 2;'));
 assert.equal((await request('/api/hotel/search',v1,1)).status,404);
 assert.equal((await request('/api/v2/availability',v2,1)).status,400);
 assert.equal((await request('/api/v2/availability',v1,2)).status,400);
 const next=await request('/api/v2/availability',v2,2);assert.equal(next.status,200);assert.equal(next.data.data.rates.length,2);assert.equal(next.data.data.rates[0].translatedName,null);
 assert.equal((await request('/admin/version',{version:3})).status,400);
 console.log('PASS: synthetic provider HTTP contracts v1 and v2, rejection of obsolete requests and null response field.');
}finally{if(child.exitCode===null){child.kill();await once(child,'exit');}}
