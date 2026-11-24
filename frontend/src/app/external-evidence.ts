import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { inventory, compareSource, compareEntries } from '../../../src/BotIQ.Api/wwwroot/evidence-engine.js';
import type { Inventory, SourceHit, EvidenceEntry, EntryComparison } from '../../../src/BotIQ.Api/wwwroot/evidence-engine.js';

@Component({selector:'botiq-external-evidence',imports:[CommonModule,FormsModule],templateUrl:'./external-evidence.html',styleUrl:'./external-evidence.css'})
export class ExternalEvidencePage {
 host=''; har:File|null=null; source:File|null=null; secondHar:File|null=null;
 busy=signal(false); error=signal(''); notice=signal('');
 first=signal<Inventory|null>(null); second=signal<Inventory|null>(null); hits=signal<SourceHit[]>([]);
 left=signal<number|null>(null); right=signal<number|null>(null); difference=signal<EntryComparison|null>(null);
 leftEntry=computed(()=>this.first()?.entries.find(e=>e.index===this.left())??null);
 rightEntries=computed(()=>this.second()?.entries??this.first()?.entries??[]);
 rightEntry=computed(()=>this.rightEntries().find(e=>e.index===this.right())??null);
 changeHost(host:string){this.host=host;this.clear();}
 selectFile(kind:'har'|'source'|'secondHar',event:Event){this[kind]=(event.target as HTMLInputElement).files?.[0]??null;this.clear();}
 clear(){this.first.set(null);this.second.set(null);this.hits.set([]);this.left.set(null);this.right.set(null);this.difference.set(null);this.error.set('');this.notice.set('');}
 private async read(file:File,max:number,label:string){if(file.size>max)throw Error(`${label} exceeds ${Math.floor(max/1048576)} MiB.`);return (await file.text()).replace(/^\uFEFF/,'');}
 private async readHar(file:File,host:string){let data:unknown;try{data=JSON.parse(await this.read(file,104857600,'HAR file'));}catch(e){if(e instanceof SyntaxError)throw Error('Invalid HAR JSON. Export the capture as a HAR file.');throw e;}return inventory(data,host);}
 async analyse(){
  if(this.busy())return;this.clear();this.busy.set(true);this.notice.set('Reading and analysing evidence locally…');
  try{
   if(!this.har)throw Error('Choose a HAR file first.');
   const host=this.host.trim().toLowerCase();if(!/^[a-z0-9.-]+$/.test(host))throw Error('Enter the exact hostname, for example szallas.hu (no scheme or path).');
   const first=await this.readHar(this.har,host);
   const hits=this.source?compareSource(await this.read(this.source,8388608,'C# source'),first):[];
   const second=this.secondHar?await this.readHar(this.secondHar,host):null;
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
