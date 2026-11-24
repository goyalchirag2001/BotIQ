import { Component, Directive, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, Bot, Capture, Comparison, Fingerprint, History, Platform, Workflow, errorMessage } from './api';

@Component({selector:'app-root',imports:[RouterOutlet,RouterLink,RouterLinkActive],template:`
<div class="shell"><aside><div class="brand"><b>Bot<span>IQ</span></b><small>Integration intelligence</small></div><nav>
<a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}">Overview</a><a routerLink="/bots" routerLinkActive="active">Bot registry</a><a routerLink="/discovery" routerLinkActive="active">Browser discovery</a><a routerLink="/history" routerLinkActive="active">Investigation history</a></nav>
<div class="legacy"><small>Evidence tools</small><a href="/evidence.html" target="_blank" rel="noopener">External HAR &amp; C# evidence ↗</a><small>Existing lab tools</small><a href="/lab" target="_blank" rel="noopener">Three-way investigation ↗</a><a href="/intelligence.html" target="_blank" rel="noopener">Runtime & dependency analysis ↗</a></div><footer>v0.5.1 · Local synthetic lab</footer></aside><main><router-outlet /></main></div>`})
export class App {}

@Directive()
abstract class Page {
 protected readonly api=inject(Api); busy=signal(false); error=signal(''); notice=signal('');
 async run(action:()=>Promise<void>){if(this.busy())return;this.busy.set(true);this.error.set('');this.notice.set('');try{await action()}catch(e){this.error.set(errorMessage(e))}finally{this.busy.set(false)}}
}
@Component({imports:[CommonModule,RouterLink],template:`
<header><p class="eyebrow">CONTROL CENTER</p><h1>Integration overview</h1><p>Monitor bot snapshots, discover contract changes and review completed investigations.</p></header>
@if(error()){<p class="error">{{error()}}</p>}
<div class="metrics"><article><small>Registered snapshots</small><strong>{{bots().length}}</strong></article><article><small>Recent investigations</small><strong>{{history().length}}</strong></article><article><small>History storage</small><strong class="smaller">{{platform()?.historyStorage ?? '—'}}</strong></article></div>
<section><div class="section-title"><h2>Provider controls</h2><button (click)="refresh()" [disabled]="busy()">Refresh</button></div><p>Target: {{platform()?.providerUrl ?? 'http://127.0.0.1:5071/'}}</p><p>Active contract: <b>{{version() ?? 'Unavailable'}}</b></p><div class="actions"><button (click)="setVersion(1)" [disabled]="busy()">Restore v1</button><button class="danger" (click)="setVersion(2)" [disabled]="busy()">Switch to v2</button><a class="button primary" routerLink="/discovery">Open discovery</a></div>@if(notice()){<p role="status">{{notice()}}</p>}</section>
<section><h2>Latest investigation summaries</h2><div class="scroll"><table><thead><tr><th>Time</th><th>Type</th><th>Outcome</th><th>Differences</th></tr></thead><tbody>@for(h of history().slice(0,8);track h.id){<tr><td>{{h.createdAt|date:'medium'}}</td><td>{{h.kind}}</td><td>{{h.outcome}}</td><td>{{h.differenceCount}}</td></tr>}@empty{<tr><td colspan="4">No completed investigations yet.</td></tr>}</tbody></table></div></section>`})
export class Overview extends Page {
 bots=signal<Bot[]>([]);history=signal<History[]>([]);platform=signal<Platform|null>(null);version=signal<number|null>(null);
 constructor(){super();void this.refresh()}
 refresh(){return this.run(async()=>{const [p,b,h]=await Promise.all([this.api.get<Platform>('platform'),this.api.get<Bot[]>('bots'),this.api.get<History[]>('history')]);this.platform.set(p);this.bots.set(b);this.history.set(h);try{this.version.set((await this.api.get<{version:number}>('state')).version)}catch(e){this.version.set(null);this.notice.set(errorMessage(e))}})}
 setVersion(v:number){return this.run(async()=>{await this.api.post('provider/'+v);this.version.set(v);this.notice.set('Provider version changed.')})}
}
@Component({imports:[CommonModule,FormsModule],template:`
<header><p class="eyebrow">SOURCE INTELLIGENCE</p><h1>Bot registry</h1><p>Register versioned C# source snapshots. Uploaded source is analyzed, never executed.</p></header>
<section><h2>Register snapshot</h2><div class="form-grid"><label>Bot name<input [(ngModel)]="name" maxlength="100"></label><label>Version<input [(ngModel)]="version" maxlength="50"></label></div><label>C# source files (up to 100; 8 MiB total)<input type="file" multiple accept=".cs" (change)="select($event)"></label><button class="primary" (click)="register()" [disabled]="busy()">Register & analyze</button>@if(notice()){<p role="status">{{notice()}}</p>}@if(error()){<p class="error" role="alert">{{error()}}</p>}</section>
<section><div class="section-title"><h2>Source snapshots</h2><button (click)="refresh()" [disabled]="busy()">Refresh</button></div><div class="scroll"><table><thead><tr><th>Bot</th><th>Version</th><th>Files</th><th>Evidence</th><th>Action</th></tr></thead><tbody>@for(b of bots();track b.id){<tr><td>{{b.name}}</td><td>{{b.version}}</td><td>{{b.fileCount}}</td><td>{{b.evidenceCount}}</td><td><button (click)="inspect(b.id)" [disabled]="busy()">Inspect</button></td></tr>}@empty{<tr><td colspan="5">No snapshots registered.</td></tr>}</tbody></table></div>@if(report()){<h3>Source analysis</h3><pre>{{report()|json}}</pre>}</section>`})
export class Registry extends Page {
 name='HOTEL-DEMO-ANDROID';version='1.0.0';files:File[]=[];bots=signal<Bot[]>([]);report=signal<unknown>(null);
 constructor(){super();void this.refresh()}
 select(e:Event){this.files=Array.from((e.target as HTMLInputElement).files??[])}
 refresh(){return this.run(async()=>{this.bots.set(await this.api.get<Bot[]>('bots'))})}
 register(){return this.run(async()=>{if(!this.files.length||this.files.length>100||this.files.reduce((n,f)=>n+f.size,0)>8388608)throw Error('Select 1–100 C# files, up to 8 MiB total.');const files=await Promise.all(this.files.map(async f=>({name:f.name,content:await f.text()})));this.report.set(await this.api.post('bots',{name:this.name,version:this.version,files}));this.bots.set(await this.api.get<Bot[]>('bots'));this.notice.set('Source snapshot registered.')})}
 inspect(id:string){return this.run(async()=>{this.report.set(await this.api.get('bots/'+id))})}
}
@Component({imports:[CommonModule,FormsModule],template:`
<header><p class="eyebrow">NETWORK INTELLIGENCE</p><h1>Browser discovery</h1><p>Discover the live request through an independent Chromium session. Local synthetic provider only.</p></header>
<section><h2>1 / Workflow & baseline</h2><p>Browser target: <a href="http://127.0.0.1:5071/" target="_blank" rel="noopener">http://127.0.0.1:5071/</a></p><label>Workflow JSON<textarea [(ngModel)]="workflowText" rows="10"></textarea></label><div class="actions"><button (click)="setVersion(1)" [disabled]="busy()">Restore provider v1</button><button class="primary" (click)="capture()" [disabled]="busy()">Capture browser traffic</button></div><label>Or import a synthetic HAR (100 MiB)<input type="file" accept=".har,.json" (change)="selectHar($event)"></label><button (click)="importHar()" [disabled]="busy()">Import HAR</button><label>Known-good request<select [(ngModel)]="selected"><option [ngValue]="null">Select baseline</option>@for(r of baseline();track $index){<option [ngValue]="$index">{{r.method}} {{r.path}} · {{r.operation ?? 'no operation'}}</option>}</select></label></section>
<section><h2>2 / Investigate live contract</h2><label>Source snapshot (optional)<select [(ngModel)]="botId"><option value="">No source snapshot</option>@for(b of bots();track b.id){<option [value]="b.id">{{b.name}} · {{b.version}}</option>}</select></label><div class="actions"><button class="danger" (click)="setVersion(2)" [disabled]="busy()">Break provider v2</button><button class="primary" (click)="compare()" [disabled]="busy() || selected===null">Capture live & compare</button></div>@if(busy()){<p role="status">Working — follow the API terminal for browser stages.</p>}@if(notice()){<p role="status">{{notice()}}</p>}@if(error()){<p class="error" role="alert">{{error()}}</p>}@if(progress()){<p role="status">{{progress()}}</p>}@if(diagnostics().length){<pre>{{diagnostics().join('\\n')}}</pre>}</section>
@if(result();as r){<section><h2>{{r.match.outcome}}</h2><div class="scroll"><table><thead><tr><th>Candidate</th><th>Status</th><th>Score</th><th>Evidence</th></tr></thead><tbody>@for(c of r.match.candidates;track c.index){<tr><td>{{c.index===r.match.selectedIndex?'★ ':''}}{{c.request.method}} {{c.request.path}}</td><td>{{c.request.status}}</td><td>{{c.score}} / 100</td><td>{{c.reasons.join('; ')}}</td></tr>}</tbody></table></div><h3>Contract differences</h3><div class="scroll"><table><thead><tr><th>Area</th><th>Path</th><th>Before</th><th>After</th></tr></thead><tbody>@for(f of r.match.differences;track $index){<tr><td>{{f.area}}</td><td>{{f.path}}</td><td>{{f.before}}</td><td>{{f.after}}</td></tr>}</tbody></table></div><h3>Source impact</h3>@for(f of r.impact ?? [];track $index){<p>{{f.path}} — {{f.location.file}}:{{f.location.line}} · {{f.confidence}}</p>}<small>Similarity scores are heuristics, not probabilities.</small></section>}`})
export class DiscoveryPage extends Page {
 workflowText=JSON.stringify({name:'Hotel rate search',steps:[{action:'fill',selector:'#property',value:'HOTEL-101'},{action:'click',selector:'#search'},{action:'wait',selector:'#results:not(:empty)'}]},null,2);
 baseline=signal<Fingerprint[]>([]);selected:number|null=null;botId='';bots=signal<Bot[]>([]);result=signal<Comparison|null>(null);diagnostics=signal<string[]>([]);progress=signal('');harFile:File|null=null;
 constructor(){super();void this.run(async()=>{this.bots.set(await this.api.get<Bot[]>('bots'))})}
 selectHar(e:Event){this.harFile=(e.target as HTMLInputElement).files?.[0]??null}
 private async withProgress<T>(action:()=>Promise<T>):Promise<T>{
  this.progress.set('Starting browser capture…');let active=true;
  const poll=async()=>{try{const p=await this.api.get<{stage:string;running:boolean;elapsedMs:number}>('discovery/progress');if(active)this.progress.set(p.stage+' · '+Math.round(p.elapsedMs/1000)+'s');}catch{/* main request reports errors */}};
  const timer=setInterval(()=>void poll(),1000);try{return await action()}finally{active=false;clearInterval(timer);this.progress.set('')}
 }
 private workflow():Workflow{return JSON.parse(this.workflowText) as Workflow}
 private populate(requests:Fingerprint[]){this.baseline.set(requests.filter(r=>!r.incomplete&&r.status>=200&&r.status<300));this.selected=null;this.result.set(null);this.notice.set(this.baseline().length ? `${this.baseline().length} complete successful requests. Select the historical search request.` : 'No complete successful local synthetic requests found. Check the HAR origin and response bodies.')}
 setVersion(v:number){return this.run(async()=>{await this.api.post('provider/'+v);this.notice.set(`Provider v${v} active.`)})}
 capture(){return this.run(async()=>{this.diagnostics.set([]);this.baseline.set([]);this.selected=null;this.result.set(null);const r=await this.withProgress(()=>this.api.post<Capture>('discovery/capture',this.workflow()));this.diagnostics.set(r.diagnostics);if(r.truncated)throw Error('Incomplete capture. Retry.');this.populate(r.requests)})}
 importHar(){return this.run(async()=>{this.diagnostics.set([]);this.baseline.set([]);this.selected=null;this.result.set(null);if(!this.harFile||this.harFile.size>104857600)throw Error('Choose a HAR of up to 100 MiB.');this.populate(await this.api.har(this.harFile))})}
 compare(){return this.run(async()=>{this.result.set(null);this.diagnostics.set([]);if(this.selected===null)throw Error('Select baseline.');const r=await this.withProgress(()=>this.api.post<Comparison>('discovery/compare',{baseline:this.baseline()[this.selected!],workflow:this.workflow(),botId:this.botId||null}));this.result.set(r);this.diagnostics.set(r.capture.diagnostics);this.notice.set('Investigation completed and history recorded.')})}
}
@Component({imports:[CommonModule,FormsModule],template:`
<header><p class="eyebrow">OPERATIONAL EVIDENCE</p><h1>Investigation history</h1><p>Completed investigations, including request matching outcomes and contract differences. No raw response bodies stored here.</p></header><section><div class="section-title"><h2>Recent runs</h2><button (click)="refresh()" [disabled]="busy()">Refresh</button></div><label>Filter by source snapshot<select [(ngModel)]="botId" (ngModelChange)="refresh()" [disabled]="busy()"><option value="">All snapshots & unassigned runs</option>@for(b of bots();track b.id){<option [value]="b.id">{{b.name}} · {{b.version}}</option>}</select></label>@if(error()){<p class="error">{{error()}}</p>}<div class="scroll"><table><thead><tr><th>Time</th><th>Type</th><th>Outcome</th><th>Bot / Browser HTTP</th><th>Differences</th><th>Areas</th></tr></thead><tbody>@for(h of history();track h.id){<tr><td>{{h.createdAt|date:'medium'}}</td><td>{{h.kind}}</td><td>{{h.outcome}}</td><td>{{h.botStatus??'—'}} / {{h.browserStatus??'—'}}</td><td>{{h.differenceCount}}</td><td>{{h.details||'—'}}</td></tr>}@empty{<tr><td colspan="6">No completed investigations match this filter.</td></tr>}</tbody></table></div></section>`})
export class HistoryPage extends Page {
 botId='';bots=signal<Bot[]>([]);history=signal<History[]>([]);
 constructor(){super();void this.run(async()=>{this.bots.set(await this.api.get<Bot[]>('bots'));await this.load()})}
 private async load(){this.history.set(await this.api.get<History[]>('history?limit=100'+(this.botId?'&botId='+encodeURIComponent(this.botId):'')))}
 refresh(){return this.run(()=>this.load())}
}
