import type { Inventory, SourceHit, EvidenceEntry, EntryComparison } from './evidence-engine.js';
export interface ReviewPair {baselineIndex:number;path:string;method:string;state:string;candidates:{index:number;score:number;samePath:boolean;responseSimilarity:number}[];currentIndex:number|null;sourceLines:number[];comparison:EntryComparison|null;observedDifferences:number;incomplete:boolean;advice:string;}
export interface Review {schemaVersion:number;sourceCoverage:{method:string;path:string;line:number;callLine:number|null;baselineCandidates:number;currentCandidates:number|null;advice:string}[];pairs:ReviewPair[];baselineCompared:number;comparisonCapped:boolean;counts:{matched:number;ambiguous:number;missing:number;differences:number};limitations:string[];}
export function rank(a:EvidenceEntry,entries:EvidenceEntry[]):unknown[];
export function review(first:Inventory,second:Inventory|null,sourceFirst:SourceHit[],sourceSecond:SourceHit[],compareEntries:(a:EvidenceEntry,b:EvidenceEntry)=>EntryComparison):Review;

export interface ParserCheck {sourceLine:number;hitLine:number;key:string;observed:number;inspected:number;status:string;limited:boolean;note:string;}
export function parserChecks(source:string,inv:Inventory,hits:SourceHit[]):ParserCheck[];
