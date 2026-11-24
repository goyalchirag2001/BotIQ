// Run after start-local.mjs reports READY. --full also exercises all historical API suites.
import assert from 'node:assert/strict';import {spawn} from 'node:child_process';import {fileURLToPath} from 'node:url';import path from 'node:path';
const root=fileURLToPath(new URL('../',import.meta.url));const full=process.argv.includes('--full');const frontend='http://127.0.0.1:4200';
async function get(url){const r=await fetch(url,{signal:AbortSignal.timeout(10000)});assert.ok(r.ok,`${url}: HTTP ${r.status}`);return r;}
const health=await (await get(frontend+'/_botiq/health')).json();assert.equal(health.service,'BotIQ.Frontend');assert.equal(health.version,'0.5.1');
const api=await (await get(frontend+'/api/health')).json();assert.equal(api.service,'BotIQ.Api');assert.equal(api.version,'0.5.1');
const state=await (await get('http://127.0.0.1:5071/api/state')).json();assert.ok([1,2].includes(state.version));
const platform=await (await get(frontend+'/api/platform')).json();assert.equal(platform.version,'0.5.1');assert.ok(Array.isArray(await (await get(frontend+'/api/history')).json()));assert.ok(Array.isArray(await (await get(frontend+'/api/bots')).json()));
const home=await (await get(frontend+'/')).text();if(health.mode==='angular'){
 assert.match(home,/<app-root/);for(const route of ['/bots','/discovery','/history'])assert.match(await (await get(frontend+route)).text(),/<app-root/);
 const scripts=[...home.matchAll(/<script\b[^>]*\bsrc=["']([^"']+)["']/g)].map(m=>m[1]);assert.ok(scripts.length);for(const src of scripts)await get(new URL(src,frontend+'/').href);
}else assert.match(home,/not the Angular application/);
for(const route of ['/lab','/bots.html','/discovery.html','/intelligence.html'])await get(frontend+route);
console.log(`PASS: frontend (${health.mode}), API proxy, provider, registry and history (${platform.historyStorage}), routes and assets.`);
if(full){for(const script of ['scripts/batch05-smoke.mjs','scripts/discovery-smoke.mjs','scripts/api-smoke.mjs','scripts/registry-smoke.mjs','scripts/batch04-smoke.mjs']){
 await new Promise((resolve,reject)=>{const p=spawn(process.execPath,[path.join(root,script)],{cwd:root,stdio:'inherit'});p.on('error',reject);p.on('close',code=>code===0?resolve():reject(Error(`${script} exited ${code}`)));});
}console.log('PASS: all five API regression suites.');}
