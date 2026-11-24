// One command for the full local platform. No npm packages needed by this launcher.
import {spawn} from 'node:child_process';
import {existsSync,mkdirSync,createWriteStream} from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import net from 'node:net';
import {setTimeout as delay} from 'node:timers/promises';
const root=fileURLToPath(new URL('../',import.meta.url));const front=path.join(root,'frontend');const api=path.join(root,'src','BotIQ.Api');const logDir=path.join(root,'.botiq-logs');
const labOnly=process.argv.includes('--lab-only');const children=[];let stopping=false;
export function nodeSupported(v){const [m,n,p]=v.replace(/^v/,'').split('.').map(Number);return (m===22&&(n>22||n===22&&p>=3))||(m===24&&n>=15)||m===26;}
function child(name,command,args,cwd){const log=createWriteStream(path.join(logDir,name+'.log'),{flags:'w'});const p=spawn(command,args,{cwd,env:{...process.env},windowsHide:true,shell:process.platform==='win32'&&command==='npm',stdio:['ignore','pipe','pipe']});children.push(p);for(const s of [p.stdout,p.stderr])s.on('data',d=>{log.write(d);process.stdout.write(`[${name}] ${d}`)});p.on('close',()=>log.end());p.on('error',e=>{log.write(e.message+'\n');console.error(`[${name}] ${e.message}`)});return p;}
function completed(p,name){return new Promise((resolve,reject)=>{p.once('error',reject);p.once('close',c=>c===0?resolve():reject(Error(`${name} exited ${c}. See ${logDir}.`)));});}
async function stop(){if(stopping)return;stopping=true;for(const p of [...children].reverse()){if(p.exitCode===null&&p.pid){if(process.platform==='win32'){await new Promise(r=>{const k=spawn('taskkill',['/PID',String(p.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});k.on('error',()=>{p.kill();r();});k.on('close',r);});}else p.kill('SIGTERM');}}}
async function portFree(port,host){const s=net.createServer();await new Promise((r,j)=>{s.once('error',e=>{if(['EAFNOSUPPORT','EADDRNOTAVAIL'].includes(e.code)&&host==='::1')r();else j(Error(`Port ${port} on ${host} is unavailable (${e.code}). Stop old BotIQ processes first.`));});s.listen({port,host,ipv6Only:host==='::1'},()=>s.close(r));});}
async function ready(name,p,url,check){for(let i=0;i<120;i++){if(p.exitCode!==null||p.signalCode)throw Error(`${name} stopped; see ${logDir}`);try{const r=await fetch(url,{signal:AbortSignal.timeout(1500)});if(r.ok&&check(await r.json()))return;}catch{}await delay(500);}throw Error(`${name} did not become ready at ${url}; see ${logDir}`);}
async function main(){
 mkdirSync(logDir,{recursive:true});
 if(!labOnly&&!nodeSupported(process.version))throw Error(`Angular 22 requires Node 22.22.3+, 24.15.0+ or 26.x (supported major versions only). Current: ${process.version}. Upgrade Node, reopen PowerShell, then retry. For legacy tools only: node scripts/start-local.mjs --lab-only`);
 for(const port of [5071,5070,4200])await portFree(port,'127.0.0.1');await portFree(4200,'::1');
 await completed(child('dotnet-version','dotnet',['--version'],root),'dotnet version');
 await completed(child('backend-build','dotnet',['build','BotIQ.slnx'],root),'Backend build');
 if(!labOnly){await completed(child('frontend-install','npm',[existsSync(path.join(front,'package-lock.json'))?'ci':'install','--no-audit','--no-fund'],front),'Frontend dependency install');await completed(child('frontend-build','npm',['run','build'],front),'Angular build');}
 const provider=child('provider',process.execPath,['demo-provider/server.mjs'],root);await ready('Provider',provider,'http://127.0.0.1:5071/api/state',x=>[1,2].includes(x.version));
 const backend=child('api','dotnet',[path.join(api,'bin','Debug','net10.0','BotIQ.Api.dll')],api);await ready('API',backend,'http://127.0.0.1:5070/api/health',x=>x.service==='BotIQ.Api');
 const frontend=child('frontend',process.execPath,['scripts/frontend-server.mjs',...(labOnly?['--lab-only']:[])],root);await ready('Frontend',frontend,'http://127.0.0.1:4200/_botiq/health',x=>x.service==='BotIQ.Frontend'&&x.mode===(labOnly?'lab':'angular'));
 const proxy=await fetch('http://127.0.0.1:4200/api/health',{signal:AbortSignal.timeout(5000)});if(!proxy.ok||(await proxy.json()).service!=='BotIQ.Api')throw Error('Frontend cannot reach API.');
 const page=await fetch('http://127.0.0.1:4200/',{signal:AbortSignal.timeout(5000)});if(!page.ok)throw Error('Frontend document unavailable.');
 console.log(`\nREADY: http://127.0.0.1:4200/ (${labOnly?'legacy lab, not Angular':'Angular'})\nAPI: http://127.0.0.1:5070/\nProvider: http://127.0.0.1:5071/\nLogs: ${logDir}\nKeep this terminal open. Ctrl+C stops the services.`);
 await Promise.race([provider,backend,frontend].map(p=>new Promise((_,reject)=>{p.once('close',c=>reject(Error(`A service exited (${c}); see ${logDir}`)));p.once('error',reject);})));}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 for(const sig of ['SIGINT','SIGTERM'])process.on(sig,()=>{void stop().then(()=>process.exit(0));});
 main().catch(async e=>{if(!stopping){console.error('\nStartup failed: '+e.message);await stop();process.exitCode=1;}});
}
