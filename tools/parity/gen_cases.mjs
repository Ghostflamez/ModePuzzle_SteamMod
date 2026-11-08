/* Generates parity cases from the external tool's JavaScript engine (the reference implementation).
 *
 *   node tools/parity/gen_cases.mjs [out.json] [seed]
 *
 * MODEPUZZLE_DIR points at the ModePuzzle repository (default: ../ModePuzzle next to this repo).
 * Cases:
 *   eval:  random boards, items and non-overlapping layouts; expected per-row and total scores, and
 *          each item's placement list (count and cell sets) to check the geometry too.
 *   solve: small problems solved to proven optimality; expected objective values.
 */
import fs from 'fs';
import path from 'path';
import {fileURLToPath, pathToFileURL} from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const toolDir = process.env.MODEPUZZLE_DIR || path.resolve(here, '../../../ModePuzzle');
const engine = await import(pathToFileURL(path.join(toolDir, 'src/core/engine.js')).href);
const {DEFAULT_RULES} = await import(pathToFileURL(path.join(toolDir, 'src/config/game-rules.js')).href);

const out = process.argv[2] || path.resolve(here, '../../build/parity_cases.json');
let seed = Number(process.argv[3] || 20261108);
const rand = () => ((seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648);
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const pick = arr => arr[Math.floor(rand() * arr.length)];

// Shapes of the game's modules and nodes (cells), plus a few odd ones.
const SHAPES = [
  [[0, 0], [1, 0], [0, 1], [1, 1]], [[0, 0], [0, 1], [0, 2], [1, 2]], [[0, 0], [1, 0], [2, 0], [1, 1]],
  [[0, 0], [1, 0], [0, 1]], [[0, 0], [1, 0], [2, 0], [0, 1], [2, 1]], [[0, 0], [1, 0], [0, 1], [1, 1], [0, 2]],
  [[0, 0], [1, 0], [2, 0], [0, 1], [1, 1], [2, 1]], [[0, 0], [1, 0], [2, 0]], [[0, 0], [0, 1], [1, 1], [2, 1], [2, 2]],
];
const NODE_SHAPES = [[[0, 0], [1, 0]], [[0, 0]]];
const MODULE_TRAITS = ['left', 'top', 'negative', 'receiver', 'overvolted', 'degrading', 'premium'];
const STANDARD = ['10000010001000001110000010001000001', '10000010000000000000000000001000001', '00000000000000000000000000000000000'];

function board(kind) {
  if (kind === 'standard') {
    const s = pick(STANDARD);
    return {w: 7, h: 5, cells: [...s].map(c => c === '0')};
  }
  const w = int(2, 6), h = int(2, 5);
  const cells = Array.from({length: w * h}, () => rand() > 0.12);
  if (!cells.some(Boolean)) cells[0] = true;
  return {w, h, cells};
}

function item(id, small) {
  if (rand() < 0.25) return {id, name: id, type: 'node', shape: pick(NODE_SHAPES), base: [0, 0, 0], traits: [], qty: 1};
  const traits = [];
  for (let i = 0; i < 2; i++) if (rand() < 0.45) { const t = pick(MODULE_TRAITS); if (!traits.includes(t)) traits.push(t); }
  const span = small ? 30 : 120;
  return {id, name: id, type: 'module', shape: pick(SHAPES), base: [int(-span, span), int(-span, span), int(-span, span)], traits, qty: 1};
}

const config = (b, items, extra = {}) => ({version: 1, board: b, items, rules: {...DEFAULT_RULES}, optional: true, target: 2, ...extra});

const evalCases = [];
for (let c = 0; c < 300; c++) {
  const b = board(c % 2 ? 'standard' : 'random');
  const items = Array.from({length: int(2, 9)}, (_, i) => item(`i${i}`, false));
  const cfg = config(b, items);
  engine.validate(engine.migrateConfig(cfg));
  const placementsOf = new Map(items.map(it => [it.id, engine.generatePlacements(b, it)]));
  let mask = 0n;
  const layout = [];
  for (const it of items) {
    const free = placementsOf.get(it.id).filter(p => !(p.mask & mask));
    if (!free.length || rand() < 0.15) continue;
    const p = pick(free);
    mask |= p.mask;
    layout.push({item: it, p, instance: `${it.id}:1`});
  }
  const ev = engine.evaluate(layout, cfg.rules);
  evalCases.push({
    board: b, items,
    placements: items.map(it => ({id: it.id, count: placementsOf.get(it.id).length,
      cells: placementsOf.get(it.id).map(p => [...p.cells].sort((x, y) => x - y).join(',')).sort()})),
    layout: layout.map(a => ({id: a.item.id, cells: a.p.cells, rotation: a.p.rotation, flipped: a.p.flipped})),
    rows: ev.rows.map(r => ({id: r.itemId, self: r.self, absorbed: r.absorbed, receiver: r.receiver, total: r.total})),
    total: ev.total,
  });
}

const solveCases = [];
for (let c = 0; c < 60; c++) {
  const b = c % 3 === 0 ? board('standard') : board('random');
  const items = Array.from({length: int(2, 5)}, (_, i) => item(`s${i}`, true));
  for (const it of items) if (rand() < 0.3) it.qty = int(2, 3);
  for (const it of items) if (rand() < 0.15) it.required = true;
  let extra = {target: int(0, 2)};
  if (c % 4 === 1) extra.optional = false;
  if (c % 5 === 2) {
    const primary = int(0, 2);
    const secondary = (primary + int(1, 2)) % 3;
    const bounds = [0, 1, 2].map(k => (k !== primary && rand() < 0.5 ? {min: int(-40, 0), max: null} : {min: null, max: null}));
    extra = {target: 'custom', custom: {bounds, primary, secondary}};
  }
  const cfg = config(b, items, extra);
  const r = engine.solve(cfg, {limitMs: 60000, now: () => performance.now()});
  if (!r.proven) continue;
  solveCases.push({config: cfg, feasible: r.feasible, objective: r.objectiveValues, totals: r.layout?.total ?? null});
}

fs.mkdirSync(path.dirname(out), {recursive: true});
fs.writeFileSync(out, JSON.stringify({engine: 'ModePuzzle engine.js', evalCases, solveCases}));
console.log(`wrote ${evalCases.length} eval and ${solveCases.length} solve cases to ${out}`);
