const fs=require("fs");
const b=fs.readFileSync("C:/Program Files (x86)/Steam/steamapps/common/Schedule I/Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat");
const H=i=>({off:b.readUInt32LE(i),size:b.readUInt32LE(i+4)});
const STR=H(24),EVT=H(32),PROP=H(40),METH=H(48),FLD=H(96),TYPE=H(160);
const s=i=>{if(i<0)return"<null>";let p=STR.off+i,e=p;while(b[e]!==0)e++;return b.toString("utf8",p,e);};
const TSZ=88,MSZ=36,FSZ=12,ESZ=24,PSZ=20;
const re=new RegExp(process.argv[2]);
for(let i=0;i<TYPE.size/TSZ;i++){
  const o=TYPE.off+i*TSZ;
  const full=(s(b.readInt32LE(o+4))?s(b.readInt32LE(o+4))+".":"")+s(b.readInt32LE(o));
  const fs_=b.readInt32LE(o+32),ms=b.readInt32LE(o+36),es=b.readInt32LE(o+40),ps=b.readInt32LE(o+44);
  const mc=b.readUInt16LE(o+64),pc=b.readUInt16LE(o+66),fc=b.readUInt16LE(o+68),ec=b.readUInt16LE(o+70);
  const hits=[];
  for(let k=0;k<fc;k++){const n=s(b.readInt32LE(FLD.off+(fs_+k)*FSZ)); if(re.test(n))hits.push("field:"+n);}
  for(let k=0;k<mc;k++){const n=s(b.readInt32LE(METH.off+(ms+k)*MSZ)); if(re.test(n))hits.push("method:"+n+"/"+b.readUInt16LE(METH.off+(ms+k)*MSZ+34));}
  for(let k=0;k<ec;k++){const n=s(b.readInt32LE(EVT.off+(es+k)*ESZ)); if(re.test(n))hits.push("event:"+n);}
  for(let k=0;k<pc;k++){const n=s(b.readInt32LE(PROP.off+(ps+k)*PSZ)); if(re.test(n))hits.push("prop:"+n);}
  if(hits.length) console.log(full+"  ->  "+hits.join(", "));
}
