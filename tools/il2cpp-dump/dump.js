const fs=require("fs");
const MD="C:/Program Files (x86)/Steam/steamapps/common/Schedule I/Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat";
const b=fs.readFileSync(MD);
const H=(i)=>({off:b.readUInt32LE(i),size:b.readUInt32LE(i+4)});
const STR=H(24), EVT=H(32), PROP=H(40), METH=H(48), FLD=H(96), TYPE=H(160);

function s(idx){ if(idx<0) return "<null>"; let p=STR.off+idx,e=p; while(b[e]!==0)e++; return b.toString("utf8",p,e); }

const TSZ=88, MSZ=36, FSZ=12, ESZ=24, PSZ=20;
const nTypes=TYPE.size/TSZ;
const types=[];
for(let i=0;i<nTypes;i++){
  const o=TYPE.off+i*TSZ;
  types.push({
    name:s(b.readInt32LE(o)), ns:s(b.readInt32LE(o+4)),
    parent:b.readInt32LE(o+16),
    flags:b.readUInt32LE(o+28),
    fieldStart:b.readInt32LE(o+32), methodStart:b.readInt32LE(o+36),
    eventStart:b.readInt32LE(o+40), propStart:b.readInt32LE(o+44),
    methodCount:b.readUInt16LE(o+64), propCount:b.readUInt16LE(o+66),
    fieldCount:b.readUInt16LE(o+68), eventCount:b.readUInt16LE(o+70),
  });
}
function methName(i){ return s(b.readInt32LE(METH.off+i*MSZ)); }
function methParamCount(i){ return b.readUInt16LE(METH.off+i*MSZ+34); }
function fldName(i){ return s(b.readInt32LE(FLD.off+i*FSZ)); }
function evtName(i){ return s(b.readInt32LE(EVT.off+i*ESZ)); }
function propName(i){ return s(b.readInt32LE(PROP.off+i*PSZ)); }

const filter=process.argv[2]?new RegExp(process.argv[2]):null;
const nsFilter=process.argv[3]?new RegExp(process.argv[3]):null;
const out=[];
for(const t of types){
  const full=(t.ns?t.ns+".":"")+t.name;
  if(filter && !filter.test(full)) continue;
  if(nsFilter && !nsFilter.test(t.ns)) continue;
  out.push("=== "+full+" ===");
  if(t.eventCount>0){ const e=[]; for(let i=0;i<t.eventCount;i++) e.push(evtName(t.eventStart+i)); out.push("  EVENTS: "+e.join(", ")); }
  if(t.fieldCount>0){ const f=[]; for(let i=0;i<t.fieldCount;i++) f.push(fldName(t.fieldStart+i)); out.push("  FIELDS: "+f.join(", ")); }
  if(t.propCount>0){ const p=[]; for(let i=0;i<t.propCount;i++) p.push(propName(t.propStart+i)); out.push("  PROPS: "+p.join(", ")); }
  if(t.methodCount>0){ const m=[]; for(let i=0;i<t.methodCount;i++) m.push(methName(t.methodStart+i)+"/"+methParamCount(t.methodStart+i)); out.push("  METHODS: "+m.join(", ")); }
}
console.log(out.join("\n"));
console.error("types="+nTypes+" methods="+(METH.size/MSZ)+" matched="+out.filter(x=>x.startsWith("===")).length);
