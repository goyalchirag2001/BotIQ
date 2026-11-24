import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
export interface Bot { id:string; name:string; version:string; createdAt:string; sourceHash:string; fileCount?:number; evidenceCount?:number; }
export interface Fingerprint { method:string; path:string; status:number; operation:string|null; headers:Record<string,string>; requestShape:Record<string,string>; responseShape:Record<string,string>; incomplete:boolean; }
export interface Capture { workflow:string; requests:Fingerprint[]; truncated:boolean; diagnostics:string[]; }
export interface Finding { area:string; path:string; before:string; after:string; severity:string; }
export interface Comparison { capture:Capture; match:{outcome:string;selectedIndex:number|null;candidates:{index:number;request:Fingerprint;score:number;reasons:string[]}[];differences:Finding[]}; impact:{path:string;location:{file:string;line:number;method:string};confidence:string}[]|null; }
export interface History { id:string;createdAt:string;botId:string|null;kind:string;outcome:string;differenceCount:number;botStatus:number|null;browserStatus:number|null;details:string; }
export interface Platform {version:string;historyStorage:string;providerUrl:string;}
export interface Workflow { name:string;steps:{action:string;selector:string;value?:string}[]; }
@Injectable({providedIn:'root'})
export class Api {
 private readonly http=inject(HttpClient);
 get<T>(path:string){return firstValueFrom(this.http.get<T>('/api/'+path));}
 post<T>(path:string,body:unknown=null){return firstValueFrom(this.http.post<T>('/api/'+path,body,{headers:{'X-BotIQ-Lab':'1'}}));}
 har(file:File){return firstValueFrom(this.http.post<Fingerprint[]>('/api/discovery/har',file,{headers:{'X-BotIQ-Lab':'1','Content-Type':'application/json'}}));}
}
export function errorMessage(e:unknown):string {
 if(e instanceof HttpErrorResponse) return e.error?.detail || e.error?.title || (e.status===0?'Cannot reach BotIQ API. Verify port 5070.':`HTTP ${e.status}`);
 return e instanceof Error?e.message:String(e);
}
