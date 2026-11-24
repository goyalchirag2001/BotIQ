// Evidence-based, deterministic review. Never executes source or replays requests.
const overlap=(a,b)=>{const x=new Set(a),y=new Set(b);return x.size+y.size? [...x].filter(k=>y.has(k)).length/new Set([...x,...y]).size:0;};
const shapeKeys=e=>[...Object.keys(e.responseFields),...e.responseMarkers];
const countDiff=d=>Number(d.path[0]!==d.path[1])+Number(d.status[0]!==d.status[1])+Number(d.requestKind[0]!==d.requestKind[1])+Number(d.responseKind[0]!==d.responseKind[1])+d.query.missing.length+d.query.extra.length+d.headers.missing.length+d.headers.extra.length+d.requestFields.length+d.responseFields.length+d.htmlMarkers.missing.length+d.htmlMarkers.extra.length;
export function rank(a,entries){return entries.filter(b=>b.method===a.method).map(b=>{const same=a.path===b.path, response=overlap(shapeKeys(a),shapeKeys(b)), request=overlap(Object.keys(a.requestFields),Object.keys(b.requestFields)),query=overlap(a.query,b.query);const score=Math.round((same?45:0)+response*35+request*10+query*10);return {index:b.index,score,samePath:same,responseSimilarity:Math.round(response*100),entry:b};}).filter(x=>x.samePath||x.score>=35&&x.responseSimilarity>=75&&a.bodyPresent&&x.entry.bodyPresent).sort((a,b)=>b.score-a.score||a.index-b.index);}
export function review(first,second,sourceFirst,sourceSecond,compareEntries){
 const pairs=[]; const selected=new Set(sourceFirst.flatMap(h=>h.candidates.filter(c=>c.method===h.method).map(c=>c.index)));const base=selected.size?first.entries.filter(e=>selected.has(e.index)):first.entries;const limit=100;
 if(second)for(const a of base.slice(0,limit)){
  const candidates=rank(a,second.entries);const best=candidates[0];const gap=best?best.score-(candidates[1]?.score??-100):0;
  let state=!best?'not observed':gap<10?'ambiguous':!best.samePath?'possible replacement':'candidate';
  // A unique match is still a hypothesis, not evidence of a breaking change.
  const accepted=state==='candidate'||state==='possible replacement';const diff=accepted?compareEntries(a,best.entry):null;
  const incomplete=!!(a.limited||best?.entry.limited||!a.bodyPresent||!best?.entry.bodyPresent||first.capped||second.capped);
  pairs.push({baselineIndex:a.index,path:a.path,method:a.method,state,candidates:candidates.slice(0,5).map(({index,score,samePath,responseSimilarity})=>({index,score,samePath,responseSimilarity})),currentIndex:accepted?best.index:null,sourceLines:sourceFirst.filter(h=>h.candidates.some(c=>c.index===a.index&&c.method===h.method)).map(h=>h.line),comparison:diff,observedDifferences:diff?countDiff(diff):0,incomplete,advice:!best?'Capture the same workflow and verify request coverage; absence is not an endpoint change.':state==='ambiguous'?'Select the comparable request manually; repeated or similar requests cannot be disambiguated reliably.':incomplete?'Collect complete bodies and verify comparable inputs before interpreting differences.':state==='possible replacement'?'Review candidate replacement and validate against an authorised equivalent workflow.':'Inspect structural differences; confirm equivalent inputs and parser behavior before changing code.'});
 }
 const sourceCoverage=sourceFirst.map((h,i)=>({method:h.method,path:h.path,line:h.line,callLine:h.callLine,baselineCandidates:h.candidates.filter(c=>c.method===h.method).length,currentCandidates:second?(sourceSecond[i]?.candidates.filter(c=>c.method===h.method).length??0):null,advice:!h.candidates.some(c=>c.method===h.method)?'Expected call not observed in first capture; check workflow coverage.':second&&!sourceSecond[i]?.candidates.some(c=>c.method===h.method)?'Expected call not observed in second capture; do not infer removal without matching workflow.':'Candidate found; inspect header, query and payload field names at linked source lines.'}));
 return {schemaVersion:1,sourceCoverage,pairs,baselineCompared:pairs.length,comparisonCapped:!!second&&base.length>limit,counts:{matched:pairs.filter(p=>p.state==='candidate'||p.state==='possible replacement').length,ambiguous:pairs.filter(p=>p.state==='ambiguous').length,missing:pairs.filter(p=>p.state==='not observed').length,differences:pairs.reduce((n,p)=>n+p.observedDifferences,0)},limitations:['Static source heuristics; no source or DLL execution.','Similarity scores are not probabilities. Missing or extra fields may depend on inputs, sessions or recording coverage.','No credential values, raw bodies, business-value verification or confirmed fixes.']};
}

// Candidate literal parser keys linked to a directly assigned HTTP response; no regex execution.
export function parserChecks(source,inv,hits){
 const lines=source.split(/\r?\n/),checks=[];
 for(const [i,h] of hits.entries()){
  if(!h.callLine)continue;const assignment=lines[h.callLine-1]?.match(/\b([A-Za-z_]\w*)\s*=\s*(?:await\s+)?(?:this\.)?(?:tlsResponse|HttpGet|HttpPost|GetAsync|PostAsync)\s*\(/i);if(!assignment)continue;
  const variable=assignment[1],end=hits.slice(i+1).find(x=>x.callLine&&x.callLine>h.callLine)?.callLine??lines.length+1;
  const candidates=h.candidates.filter(c=>c.method===h.method).map(c=>inv.entries.find(e=>e.index===c.index)).filter(Boolean);const jsonObjects=new Set();
  for(let n=h.callLine;n<end-1&&checks.length<100;n++){
   const line=lines[n];if(/^\s*\/\//.test(line))continue;
   const parse=line.match(/\b(\w+)\s*=\s*JObject\.Parse\(\s*(\w+)\s*\)/);if(parse?.[2]===variable)jsonObjects.add(parse[1]);
   const extraction=line.match(/GetStringResult\(\s*(\w+)\s*,\s*(.*)/);const keys=[];
   if(extraction?.[1]===variable){const pattern=extraction[2];for(const m of pattern.matchAll(/""([A-Za-z_][\w.-]{0,79})""\s*:/g))keys.push(m[1]);}
   for(const name of jsonObjects)for(const m of line.matchAll(/\b(\w+)\s*\["([A-Za-z_][\w.-]{0,79})"\]/g))if(m[1]===name)keys.push(m[2]);
   for(const key of new Set(keys)){const eligible=candidates.filter(e=>e.bodyPresent&&['html','json'].includes(e.responseKind));const present=eligible.filter(e=>e.responseMarkers.includes(key));checks.push({sourceLine:n+1,hitLine:h.line,key,observed:present.length,inspected:eligible.length,status:!eligible.length?'No comparable body':present.length===eligible.length?'Marker observed':present.length?'Marker partially observed':'Marker not observed',limited:inv.capped||eligible.some(e=>e.limited),note:'Literal marker only; not a parser execution or proof of regression.'});}
  }
 }
 return checks;
}
