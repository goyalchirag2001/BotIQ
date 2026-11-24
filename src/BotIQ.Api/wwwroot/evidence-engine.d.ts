export interface NameDiff { missing: string[]; extra: string[]; }
export interface EvidenceEntry { index: number; method: string; path: string; query: string[]; headers: string[]; status: number; requestKind: string; requestFields: Record<string,string>; responseKind: string; responseFields: Record<string,string>; responseMarkers: string[]; bodyPresent: boolean; bodyLength: number; limited: boolean; }
export interface Inventory { total: number; scoped: number; skipped: number; host: string; entries: EvidenceEntry[]; capped: boolean; }
export interface Candidate { index: number; method: string; path: string; status: number; queryDiff: NameDiff; headerDiff: NameDiff; responseKind: string; sourcePayloadFieldsNotObserved: string[]; candidateOnly: boolean; }
export interface SourceHit { variable: string; path: string; line: number; query: string[]; method: string; callLine: number|null; headers: string[]; payload?: {line:number;fields:string[]}|null; candidates: Candidate[]; outcome: string; }
export interface FieldChange { field:string; before:string; after:string; }
export interface EntryComparison { method:string[]; path:string[]; status:number[]; query:NameDiff; headers:NameDiff; requestKind:string[]; requestFields:FieldChange[]; responseKind:string[]; responseFields:FieldChange[]; htmlMarkers:NameDiff; warning:string; }
export function inventory(har:unknown,host:string):Inventory;
export function compareSource(source:string,inv:Inventory):SourceHit[];
export function compareEntries(a:EvidenceEntry,b:EvidenceEntry):EntryComparison;
