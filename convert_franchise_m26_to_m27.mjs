import pkg from 'madden-franchise';
const FranchiseFile = pkg.FranchiseFile;
import fs from 'fs';
import os from 'os';
import path from 'path';
import crypto from 'crypto';

async function convert(srcPath, tplPath, outPath){
  console.log(`Loading src ${srcPath}`);
  const src = new FranchiseFile(srcPath);
  await src.parse();
  console.log(`src tables ${src.tables.length}`);

  // Work on a copy of the template so the original template file is never modified/corrupted.
  const tmpTpl = path.join(os.tmpdir(), `msc-franch-tpl-${crypto.randomBytes(6).toString('hex')}`);
  fs.copyFileSync(tplPath, tmpTpl);
  console.log(`Loading tpl ${tplPath} (working copy)`);
  let tpl;
  try {
    tpl = new FranchiseFile(tmpTpl);
    await tpl.parse();
    console.log(`tpl tables ${tpl.tables.length}`);

    let common=0, copied=0, skipped=0, skippedTables=0;
    const skipTablePatterns = [
      /Event$/i, /Reaction$/i, /Transaction/i, /Request$/i, /Manager$/i, /Director$/i,
      /Eval$/i, /Arc/i, /Quest/i, /Scenario/i, /Story/i, /Flow/i, /CareerFlow/i,
      /ThemeWeek/i, /Marketplace/i, /Notification/i, /Trophy/i, /History/i,
      /^Advance/i, /AutoSim/i, /Patch/i, /Version/i, /Schema/i
    ];
    for(let srcTable of src.tables){
      let name = srcTable.header.name;
      if(!name || name.startsWith('\0')) continue;
      if (skipTablePatterns.some(rx => rx.test(name))) { skippedTables++; continue; }
      let tplTable = tpl.tables.find(t=> t.header.name===name);
      if(!tplTable) continue;
      // Load records
      try{ await srcTable.readRecords(); }catch(e){ skipped++; continue; }
      try{ await tplTable.readRecords(); }catch(e){ skipped++; continue; }
      if(srcTable.records.length===0 || tplTable.records.length===0) continue;
      common++;
      let n = Math.min(srcTable.records.length, tplTable.records.length);
      for(let i=0;i<n;i++){
        let sRec = srcTable.records[i];
        let tRec = tplTable.records[i];
        if(sRec.isEmpty && tRec.isEmpty) continue;
        if(sRec.isEmpty || tRec.isEmpty) continue;
        let sFields = sRec.fields || {};
        let tFields = tRec.fields || {};
        let sKeys = Object.keys(sFields);
        for(let k of sKeys){
          if(!(k in tFields)) continue;
          try {
            const sField = sRec.getFieldByKey ? sRec.getFieldByKey(k) : sFields[k];
            const tField = tRec.getFieldByKey ? tRec.getFieldByKey(k) : tFields[k];
            // Skip reference/complex fields to reduce cross-version corruption/crashes
            if (sField && (sField.isReference || (sField.offset && sField.offset.isReference))) continue;
            if (tField && (tField.isReference || (tField.offset && tField.offset.isReference))) continue;
            tRec[k] = sRec[k];
            copied++;
          } catch(e) {}
        }
      }
    }
    console.log(`common tables ${common}, fields copied ${copied}, skipped system tables ${skippedTables}`);
    await tpl.save(outPath);
    console.log(`saved to ${outPath} size ${fs.statSync(outPath).size}`);
  } finally {
    try { fs.unlinkSync(tmpTpl); } catch {}
  }
}

const args = process.argv.slice(2);
if(args.length < 3){
  console.log("usage: node convert_franchise_m26_to_m27.mjs <src> <tpl> <out>");
  process.exit(1);
}
await convert(args[0], args[1], args[2]);
