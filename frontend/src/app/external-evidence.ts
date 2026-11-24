import { Component, computed, signal, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { inventory, compareSource, compareEntries } from '../../../src/BotIQ.Api/wwwroot/evidence-engine.js';
import { Api, Bot, errorMessage } from './api';
import { review, parserChecks } from '../../../src/BotIQ.Api/wwwroot/review-engine.js';
import type { Review, ReviewPair, ParserCheck } from '../../../src/BotIQ.Api/wwwroot/review-engine.js';
import type { Inventory, SourceHit, EvidenceEntry, EntryComparison } from '../../../src/BotIQ.Api/wwwroot/evidence-engine.js';

interface CaptureJob {id?:string;host?:string;state:string;message:string;inventory:Inventory|null;}
@Component({selector:'botiq-external-evidence',imports:[CommonModule,FormsModule],templateUrl:'./external-evidence.html',styleUrl:'./external-evidence.css'})
export class ExternalEvidencePage implements OnDestroy {
 private readonly api=inject(Api);
 url=''; authorized=false; botId=''; bots=signal<Bot[]>([]); approved=signal<string[]>([]); recording=signal(false); captureMessage=signal(''); live=signal<Inventory|null>(null); report=signal<Review|null>(null); currentHits=signal<SourceHit[]>([]); baselineParsers=signal<ParserCheck[]>([]); currentParsers=signal<ParserCheck[]>([]); saved=signal(false); saving=signal(false); private timer:ReturnType<typeof setTimeout>|null=null; private destroyed=false; private jobId=''; private generation=0;
 constructor(){void this.loadConfig();}
 private async loadConfig(){try{const r=await this.captureApi<{approvedHosts:string[]}>('config');this.approved.set(r.approvedHosts);}catch(e){this.captureMessage.set(errorMessage(e));}try{this.bots.set(await this.api.get<Bot[]>('bots'));}catch{}}
 private async captureApi<T>(action:string,body?:unknown):Promise<T>{const r=await fetch('/api/external-capture/'+action,{method:body===undefined?'GET':'POST',headers:{'X-BotIQ-Lab':'1','Content-Type':'application/json'},...(body===undefined?{}:{body:JSON.stringify(body)}),cache:'no-store'});const data=await r.json();if(!r.ok)throw Error(data.detail||`HTTP ${r.status}`);return data as T;}
 async startCapture(){if(this.recording()||this.busy()||!this.authorized)return;this.recording.set(true);this.live.set(null);this.clear();this.captureMessage.set('Requesting browser launch…');const g=++this.generation;try{const job=await this.captureApi<CaptureJob>('start',{url:this.url.trim(),host:this.host.trim().toLowerCase(),authorized:this.authorized});if(this.destroyed||g!==this.generation){await this.captureApi('cancel',{});return;}this.jobId=job.id??'';this.captureMessage.set(job.message);this.poll(g);}catch(e){this.captureMessage.set(errorMessage(e));this.recording.set(false);}}
 private poll(g:number){this.timer=setTimeout(async()=>{if(this.destroyed||g!==this.generation)return;try{const job=await this.captureApi<CaptureJob>('status');if(this.destroyed||g!==this.generation)return;if(job.id!==this.jobId)throw Error('Recording session changed.');this.captureMessage.set(job.message);if(job.state==='recording'||job.state==='processing'){this.poll(g);return;}this.recording.set(false);if(job.state==='complete'&&job.inventory){this.live.set(job.inventory);this.secondHar=null;if(this.har)await this.analyse();}}catch(e){this.captureMessage.set(errorMessage(e));this.recording.set(false);}},1200);}
 async cancelCapture(){try{const job=await this.captureApi<CaptureJob>('cancel',{});this.captureMessage.set(job.message);}catch(e){this.captureMessage.set(errorMessage(e));}}
 ngOnDestroy(){this.destroyed=true;this.generation++;if(this.timer)clearTimeout(this.timer);if(this.recording())void this.captureApi('cancel',{}).catch(()=>{});}
 clearLive(){this.live.set(null);this.clear();}
 openPair(pair:ReviewPair){if(pair.currentIndex===null)return;this.selectLeft(pair.baselineIndex);this.selectRight(pair.currentIndex);this.compare();}
 download(){const report=this.report();if(!report)return;const blob=new Blob([JSON.stringify({generatedAt:new Date().toISOString(),host:this.host,report,sourceBaseline:this.hits(),sourceCurrent:this.currentHits(),parserBaseline:this.baselineParsers(),parserCurrent:this.currentParsers()},null,2)],{type:'application/json'});const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='botiq-evidence-review.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
 async save(){const r=this.report();if(!r||this.saving()||this.saved())return;this.saving.set(true);try{await this.api.post('history/external',{botId:this.botId||null,matched:r.counts.matched,ambiguous:r.counts.ambiguous,missing:r.counts.missing,differences:r.counts.differences,sourceHits:r.sourceCoverage.length,hasSecond:!!this.second()});this.saved.set(true);this.notice.set('Summary saved to investigation history.');}catch(e){this.error.set(errorMessage(e));}finally{this.saving.set(false);}}

 host=''; har:File|null=null; source:File|null=null; secondHar:File|null=null;
 busy=signal(false); error=signal(''); notice=signal('');
 first=signal<Inventory|null>(null); second=signal<Inventory|null>(null); hits=signal<SourceHit[]>([]);
 left=signal<number|null>(null); right=signal<number|null>(null); difference=signal<EntryComparison|null>(null);
 leftEntry=computed(()=>this.first()?.entries.find(e=>e.index===this.left())??null);
 rightEntries=computed(()=>this.second()?.entries??this.first()?.entries??[]);
 rightEntry=computed(()=>this.rightEntries().find(e=>e.index===this.right())??null);
 changeHost(host:string){this.host=host;this.live.set(null);this.clear();}
 selectFile(kind:'har'|'source'|'secondHar',event:Event){this[kind]=(event.target as HTMLInputElement).files?.[0]??null;if(kind==='secondHar')this.live.set(null);this.clear();}
 clear(){this.baselineParsers.set([]);this.currentParsers.set([]);this.report.set(null);this.currentHits.set([]);this.saved.set(false);this.first.set(null);this.second.set(null);this.hits.set([]);this.left.set(null);this.right.set(null);this.difference.set(null);this.error.set('');this.notice.set('');}
 private async read(file:File,max:number,label:string){if(file.size>max)throw Error(`${label} exceeds ${Math.floor(max/1048576)} MiB.`);return (await file.text()).replace(/^\uFEFF/,'');}
 private async readHar(file:File,host:string){let data:unknown;try{data=JSON.parse(await this.read(file,104857600,'HAR file'));}catch(e){if(e instanceof SyntaxError)throw Error('Invalid HAR JSON. Export the capture as a HAR file.');throw e;}return inventory(data,host);}
 async analyse(){
  if(this.busy())return;this.clear();this.busy.set(true);this.notice.set('Reading and analysing evidence locally…');
  try{
   if(!this.har)throw Error('Choose a HAR file first.');
   const host=this.host.trim().toLowerCase();if(!/^[a-z0-9.-]+$/.test(host))throw Error('Enter the exact hostname, for example szallas.hu (no scheme or path).');
   const first=await this.readHar(this.har,host);
   const code=this.source?await this.read(this.source,8388608,'C# source'):'';
   const hits=code?compareSource(code,first):[];
   const second=this.live()?.host===host?this.live():this.secondHar?await this.readHar(this.secondHar,host):null;
   const currentHits=code&&second?compareSource(code,second):[];
   this.baselineParsers.set(code?parserChecks(code,first,hits):[]);this.currentParsers.set(code&&second?parserChecks(code,second,currentHits):[]);this.currentHits.set(currentHits);this.report.set(review(first,second,hits,currentHits,compareEntries));
   this.first.set(first);this.second.set(second);this.hits.set(hits);
   this.left.set(first.entries[0]?.index??null);
   this.right.set(second?second.entries[0]?.index??null:first.entries[1]?.index??first.entries[0]?.index??null);
   this.notice.set(first.entries.length?`Analysis complete: ${first.entries.length} eligible requests and ${hits.length} source candidates.`:`No eligible requests for ${host}. Check the exact host and HAR contents.`);
  }catch(e){this.error.set(e instanceof Error?e.message:String(e));this.notice.set('');}finally{this.busy.set(false);}
 }
 selectLeft(index:number|null){this.left.set(index);this.difference.set(null);}
 selectRight(index:number|null){this.right.set(index);this.difference.set(null);}
 inspectCandidate(index:number){this.selectLeft(index);}
 compare(){const a=this.leftEntry(),b=this.rightEntry();if(!a||!b){this.error.set('Select two captured entries.');return;}this.error.set('');this.difference.set(compareEntries(a,b));}
 entryLabel(e:EvidenceEntry){return `#${e.index} ${e.method} ${e.path} · ${e.status} · ${e.responseKind}`;}
}
