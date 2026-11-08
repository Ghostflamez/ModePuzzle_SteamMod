import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const external=process.env.MODEPUZZLE_DIR || path.resolve(root,'../ModePuzzle');
const {readSave,parseLenientJson}=await import(pathToFileURL(path.join(external,'src/core/save-import.js')));
const {generatePlacements}=await import(pathToFileURL(path.join(external,'src/core/engine.js')));
const text=fs.readFileSync(path.join(root,'build/save_7.es3'),'utf8');
const save=readSave(text);
const raw=parseLenientJson(text).playerStore.value;
const stuck=new Set();
for(const value of Object.values(raw)) {
 if(typeof value!=='string' || !value.includes('saveItems')) continue;
 try { for(const item of parseLenientJson(value).saveItems||[]) if(item._keys?.includes('MODULE_STUCK_TAG')) stuck.add(item.uniqueId); } catch {}
}
const item=m=>({...m,id:'u'+m.sources[0],name:'u'+m.sources[0],qty:1});
const cases=save.machines.map(m=>({name:m.name,key:m.key,board:m.board,
 items:[...m.installed.filter(p=>!stuck.has(p.module.sources[0])).map(p=>item(p.module)),
 ...save.free.filter(f=>!stuck.has(f.sources[0])&&(f.type==='node'||m.accepts.includes(f.moduleType))).map(item)].filter(a=>generatePlacements(m.board,a).length),
 fixed:m.installed.filter(p=>stuck.has(p.module.sources[0])).map(p=>({item:item(p.module),cells:p.cells.map(([x,y])=>y*m.board.w+x)})),
 current:m.installed.map(p=>({item:item(p.module),cells:p.cells.map(([x,y])=>y*m.board.w+x)}))}));
fs.writeFileSync(path.join(root,'build/real_cases.json'),JSON.stringify(cases));
console.log(JSON.stringify(cases.map(c=>({key:c.key,name:c.name,installed:c.current.length,candidates:c.items.length,fixed:c.fixed.length,cells:c.board.cells.filter(Boolean).length})),null,2));
