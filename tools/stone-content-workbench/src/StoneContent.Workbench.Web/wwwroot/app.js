/* Stone Content Workbench — browser client (POC UI card t_e4d16b1c).
 *
 * AUTHORITY BOUNDARY: this script renders and shapes presentation ONLY. Every authoritative
 * decision — parse, validate, version-classify, generate — is made by the ASP.NET Core web
 * adapter calling the StoneContent.Workbench.Core deep module. The client never reimplements a
 * validation rule; it POSTs the current document and displays the diagnostics/artifacts the core
 * returns. Edits are enabled ONLY for Cooking nodes (the vertical slice); stable IDs are read-only;
 * version pins are manually editable so a semantic edit can be paired with an explicit bump.
 *
 * Palette: blue (#7e8cff / #5e9cff) for normal/emphasis, orange (#f0a85c) for attention. Meaning is
 * carried by text + shape (icons, "!" vs "✓", dashed borders), never by red-vs-green, and never by
 * cyan/magenta ambiguity.
 */
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];

let data = null;         // current edited document (plain JS object)
let orig = null;         // pristine document as loaded (for reset + diff base)
let origCanonical = '';  // canonical text of the pristine document (diff base)
let origArtifacts = {};  // baseline generated artifacts (name -> content), for narrow C# diff
let baselineHash = '';   // SHA-256 of the on-disk asset at load (stale-write guard)
let scratchRoot = '';

let curCanonical = '';       // canonical text of the current document (from /api/validate)
let curDiagnostics = [];     // diagnostics from the last core validate
let curArtifacts = null;     // generated artifacts from the last core preview, or null if blocked
let generationBlocked = false;

let tree = 'Cooking', sel = null, section = 'nodes';
let out = new URLSearchParams(location.search).get('tab') || 'problems';
let dirty = false;

const PIN_KEYS = ['contentRegistry', 'foundationalCatalog', 'facetPalette', 'treeTuning'];
const NODES_ARTIFACT = 'HomesteadProgressionCatalog.Data.g.cs';

const node = () => data.nodes.find(n => n.id === sel);
const tn = () => data.nodes.filter(n => n.treeId === tree);
const isEditable = () => { const n = node(); return n && n.treeId === 'Cooking'; };

function msg(s) { const x = $('#toast'); x.textContent = s; x.classList.add('on'); setTimeout(() => x.classList.remove('on'), 1600); }
function esc(s) { return String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;'); }

async function api(path, payload) {
  const opt = payload
    ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }
    : {};
  const r = await fetch(path, opt);
  if (!r.ok) throw new Error(path + ' → HTTP ' + r.status);
  return r.json();
}

// ── boot ────────────────────────────────────────────────────────────────────────────────────
async function boot() {
  const doc = await api('/api/document');
  if (!doc.ok) { $('#canvas').innerHTML = `<div class="cards"><h2>Load error</h2><p>${esc(doc.error || '')}</p></div>`; return; }
  origCanonical = doc.json;
  baselineHash = doc.baselineHash;
  scratchRoot = doc.scratchRoot;
  data = JSON.parse(doc.json);
  orig = JSON.parse(doc.json);
  sel = data.nodes.find(n => n.treeId === 'Cooking').id;

  // Baseline generated artifacts, so the "Generated C#" tab can show a narrow diff.
  const base = await api('/api/generate-preview', { document: origCanonical, baselineHash });
  origArtifacts = {};
  (base.artifacts || []).forEach(a => { origArtifacts[a.fileName] = a.content; });

  wireChrome();
  await refresh();
  render();
}

// Ask the core to re-validate + re-generate the current document. This is the single point where
// authoritative state is refreshed; the UI reads only from the returned data.
async function refresh() {
  const cur = JSON.stringify(data);
  const [val, gen] = await Promise.all([
    api('/api/validate', { document: cur, baselineHash }),
    api('/api/generate-preview', { document: cur, baselineHash }),
  ]);
  curDiagnostics = val.diagnostics || [];
  curCanonical = val.canonicalJson || '';
  generationBlocked = gen.blocked === true;
  curArtifacts = {};
  (gen.artifacts || []).forEach(a => { curArtifacts[a.fileName] = a.content; });
  $('#pc').textContent = curDiagnostics.length;
}

// ── sidebar / trees ─────────────────────────────────────────────────────────────────────────
function counts() {
  $('#c-nodes').textContent = data.nodes.length;
  $('#c-facets').textContent = data.facets.length;
  $('#c-found').textContent = data.foundational.catalog.members.length + data.foundational.catalog.exclusions.length;
  $('#c-tuning').textContent = data.trees.length;
}

function renderTrees() {
  $('#trees').innerHTML = data.trees.map(t => `<button class="tree ${t.id === tree ? 'active' : ''}" data-tree="${t.id}"><span class="glyph">${t.id[0]}</span>${t.id}<span class="count">${data.nodes.filter(n => n.treeId === t.id).length}</span></button>`).join('');
  $('#treeTabs').innerHTML = data.trees.map(t => `<button class="tt ${t.id === tree ? 'active' : ''}" data-tree="${t.id}">${t.id}</button>`).join('');
  $$('[data-tree]').forEach(x => x.onclick = () => {
    tree = x.dataset.tree;
    sel = data.nodes.find(n => n.treeId === tree).id;
    section = 'nodes';
    $$('.nav').forEach(n => n.classList.toggle('active', n.dataset.section === 'nodes'));
    render();
  });
}

function renderPins() {
  PIN_KEYS.forEach(k => {
    const el = $('#pin-' + k);
    el.value = data.versions[k];
    el.classList.toggle('bumped', data.versions[k] > orig.versions[k]);
    el.onchange = async () => {
      const v = Math.max(1, parseInt(el.value, 10) || orig.versions[k]);
      data.versions[k] = v;
      markDirty();
      await refresh();
      renderPins(); output(); canvas();
      msg('Version pin updated');
    };
  });
}

// ── canvas ──────────────────────────────────────────────────────────────────────────────────
function card(n) {
  const unavailable = n.firstBuildStatus === 'Unavailable';
  const locked = n.treeId !== 'Cooking';
  const icon = n.outcomeType === 'LocalEffect' ? '⌂' : n.outcomeType === 'PermanentEffect' ? '◆' : '●';
  return `<button class="node ${n.id === sel ? 'selected' : ''} ${unavailable ? 'unavailable' : ''} ${locked ? 'locked-tree' : ''}" data-node="${n.id}"><div class="ntop"><div class="nicon">${icon}</div><div><div class="nname">${esc(n.displayLabel)}</div><div class="nid">${n.id} · v${n.version}</div></div></div><div class="chips"><span class="chip s">${n.firstBuildStatus}</span><span class="chip">BP ${n.pricing.developmentBp ?? '—'}</span><span class="chip">AP ${n.pricing.purchaseAp ?? '—'}</span></div></button>`;
}

function canvas() {
  const t = data.trees.find(x => x.id === tree);
  if (section === 'nodes') {
    $('#eyebrow').textContent = t.category + ' tree';
    $('#title').textContent = tree;
    const lv = [...new Set(tn().map(n => n.treeLevel))].sort();
    $('#canvas').innerHTML = `<div class="legend"><span>Authored graph · stable IDs locked</span><div><span>Executable</span><span>Unavailable</span></div></div>` +
      lv.map(l => `<div class="level"><div class="lev"><b>Level ${l}</b>${l === 1 ? 'initial' : t.tuning.cumulativeBpThresholds[0] + ' BP threshold'}</div><div class="grid ${tn().filter(n => n.treeLevel === l).length < 3 ? 'two' : ''}">${tn().filter(n => n.treeLevel === l).map(card).join('')}</div></div>`).join('');
    $$('[data-node]').forEach(x => x.onclick = () => { sel = x.dataset.node; canvas(); inspector(); output(); });
  } else {
    sectionCanvas();
  }
}

function sectionCanvas() {
  const p = {
    overview: ['One canonical asset', 'The workbench edits declarative content and shows exactly what the core will generate. Runtime C# stays reviewable; CI — not the GUI — is authoritative.', [
      ['4 explicit version pins', 'Registry, foundation, palette, and tuning move independently.'],
      [data.nodes.length + ' authored nodes', 'Stable IDs, prices, requirements, ownership, and availability together.'],
      ['No live-world writes', 'Export a branch-ready JSON asset into the granted scratch root; runtime state is never touched.'],
      ['Parity before migration', 'Generated C# must match current catalogs and pass the suite.']]],
    facets: ['Facet palette', 'Facet positions and eligible candidate Trees are stable, versioned relationships.', data.facets.map(f => [f.id + ' · ' + f.category, f.candidateTreeIds.join(' → ')])],
    foundation: ['Foundational construction', 'Everyday pieces earn the low baseline source. Explicit exclusions win over membership.', [
      ['Eligible members · ' + data.foundational.catalog.members.length, data.foundational.catalog.members.join(', ')],
      ['Explicit exclusions · ' + data.foundational.catalog.exclusions.length, data.foundational.catalog.exclusions.join(', ')]]],
    tuning: ['Tree tuning', 'Held T012 values become first-class data rather than buried constants.', data.trees.map(t => [t.id, `Initial ${t.tuning.initialLevel} · +${t.tuning.unlockCostStep} BP per prior unlock · Level 2 at ${t.tuning.cumulativeBpThresholds[0]} cumulative BP`])],
  }[section];
  $('#eyebrow').textContent = 'Asset view';
  $('#title').textContent = p[0];
  $('#canvas').innerHTML = `<div class="cards"><div class="eyebrow">${esc(data.assetId)}</div><h2>${p[0]}</h2><p>${p[1]}</p><div class="cardgrid">${p[2].map(c => `<div class="card"><h3>${esc(c[0])}</h3><p>${esc(c[1])}</p><code>version pin: manual</code></div>`).join('')}</div></div>`;
}

// ── inspector ───────────────────────────────────────────────────────────────────────────────
function inspector() {
  if (section !== 'nodes') {
    $('#iname').textContent = 'Asset metadata';
    $('#iid').textContent = data.assetId;
    $('#ibody').innerHTML = `<div class="sect">Canonical source</div><div class="field"><label>Asset ID</label><input class="inp locked" value="${esc(data.assetId)}" readonly></div><div class="row"><div class="field"><label>Family</label><input class="inp locked" value="${esc(data.family)}" readonly></div><div class="field"><label>Variant</label><input class="inp locked" value="${esc(data.variant)}" readonly></div></div><div class="readonly-note"><b>Read-only view.</b><br>Only Cooking nodes are editable in this vertical slice. Export writes a JSON asset + generated C# to the scratch root.</div>`;
    return;
  }
  const n = node();
  const editable = isEditable();
  const dis = editable ? '' : 'disabled';
  $('#iname').textContent = n.displayLabel;
  $('#iid').textContent = `${n.id} · v${n.version}`;
  const bs = [['requiresCommittedTree', 'Committed Tree'], ['requiresCurrentContentVersion', 'Current content'], ['requiresActiveAttunement', 'Active Attunement'], ['requiresOfferedStatus', 'Offered status'], ['requiresDevelopmentAuthority', 'Development authority'], ['requiresResponsibilityRange', 'Responsibility Range']];
  const opts = (list, cur) => list.map(x => `<option ${x === cur ? 'selected' : ''}>${x}</option>`).join('');
  $('#ibody').innerHTML =
    `<div class="sect">Identity</div>` +
    `<div class="field"><label>Stable node ID · locked</label><input class="inp locked" value="${n.id}" readonly></div>` +
    `<div class="field"><label>Display label</label><input id="label" class="inp" value="${esc(n.displayLabel).replaceAll('"', '&quot;')}" ${dis}></div>` +
    `<div class="row"><div class="field"><label>Tree level</label><input id="level" type="number" min="1" class="inp" value="${n.treeLevel}" ${dis}></div><div class="field"><label>Node version</label><input id="nver" type="number" min="1" class="inp" value="${n.version}" ${dis}></div></div>` +
    `<div class="sect">Behavior</div>` +
    `<div class="field"><label>Outcome type</label><select id="outcome" class="inp" ${dis}>${opts(['LocalEffect', 'CharacterEffect', 'PermanentEffect'], n.outcomeType)}</select></div>` +
    `<div class="field"><label>Ownership</label><select id="owner" class="inp" ${dis}>${opts(['StoneCultivated', 'PersonalOffered', 'NoneWhileUnavailable'], n.ownership)}</select></div>` +
    `<div class="field"><label>First-build status</label><select id="status" class="inp" ${dis}>${opts(['Executable', 'Unavailable'], n.firstBuildStatus)}</select></div>` +
    `<div class="row"><div class="field"><label>Development BP</label><input id="bp" type="number" class="inp" placeholder="None" value="${n.pricing.developmentBp ?? ''}" ${dis}></div><div class="field"><label>Purchase AP</label><input id="ap" type="number" class="inp" placeholder="None" value="${n.pricing.purchaseAp ?? ''}" ${dis}></div></div>` +
    `<div class="sect">Requirements</div>` +
    bs.map(([k, l]) => `<div class="check"><span>${l}</span><span class="toggle ${n.requirements[k] ? 'on' : ''}" data-req="${editable ? k : ''}"></span></div>`).join('') +
    (editable
      ? `<div class="notice"><b>Semantic edits need pins</b><br>A semantic change refuses generation until the node version AND Registry pin are explicitly bumped.</div><button id="apply" class="btn primary apply">Apply draft in memory</button>`
      : `<div class="readonly-note"><b>${n.treeId} is read-only.</b><br>This vertical slice edits Cooking nodes only. Switch to the Cooking tree to author.</div>`);

  if (editable) {
    $$('.toggle[data-req]').forEach(t => { if (t.dataset.req) t.onclick = () => t.classList.toggle('on'); });
    $('#apply').onclick = async () => {
      n.displayLabel = $('#label').value.trim() || n.displayLabel;
      n.treeLevel = Math.max(1, +$('#level').value || 1);
      n.version = Math.max(1, +$('#nver').value || 1);
      n.outcomeType = $('#outcome').value;
      n.ownership = $('#owner').value;
      n.firstBuildStatus = $('#status').value;
      n.pricing.developmentBp = $('#bp').value === '' ? null : +$('#bp').value;
      n.pricing.purchaseAp = $('#ap').value === '' ? null : +$('#ap').value;
      bs.forEach(([k]) => { n.requirements[k] = $(`.toggle[data-req="${k}"]`).classList.contains('on'); });
      markDirty();
      await refresh();
      render();
      msg('Draft updated — core re-validated');
    };
  }
}

// ── output pane (problems / diff / selected JSON / generated C#) ──────────────────────────────
function output() {
  $$('.ot').forEach(x => x.classList.toggle('active', x.dataset.out === out));
  const el = $('#output');
  if (out === 'problems') { renderProblems(el); return; }
  if (out === 'json') { el.textContent = curCanonical || JSON.stringify(data, null, 2); return; }
  if (out === 'diff') { renderJsonDiff(el); return; }
  renderGeneratedCs(el);
}

function renderProblems(el) {
  if (curDiagnostics.length === 0) {
    el.innerHTML = `<div class="diaghead">Core validation clean — ${data.nodes.length} nodes, generation ${generationBlocked ? 'blocked' : 'ready'}.</div>` +
      `<div class="problem"><span class="ok">✓</span><b>Schema-shaped canonical asset</b><small>proposal</small></div>` +
      `<div class="problem"><span class="ok">✓</span><b>Roster arithmetic and level partition hold</b><small>roster</small></div>` +
      `<div class="problem"><span class="ok">✓</span><b>All stable references resolve</b><small>semantic integrity</small></div>` +
      `<div class="problem"><span class="ok">✓</span><b>Version pins satisfy the change policy</b><small>version gate</small></div>`;
    return;
  }
  el.innerHTML = `<div class="diaghead bad">${curDiagnostics.length} diagnostic${curDiagnostics.length > 1 ? 's' : ''} from the core — generation blocked. Click a row to jump to the field.</div>` +
    curDiagnostics.map((d, i) => `<div class="problem err" data-diag="${i}"><span class="ok">!</span><b>${esc(d.detail)}</b><span class="path">${esc(d.path)}</span><span class="code">${esc(d.code)}</span></div>`).join('');
  $$('.problem.err').forEach(row => row.onclick = () => navigateToDiagnostic(curDiagnostics[+row.dataset.diag]));
}

// Diagnostic navigation: parse the JSON-pointer-like path and select the referenced node/tree.
function navigateToDiagnostic(d) {
  const p = d.path || '';
  let m = p.match(/^\/nodes\/(\d+)/);           // e.g. /nodes/4/pricing/purchaseAp
  let target = null;
  if (m) target = data.nodes[+m[1]];
  if (!target) { m = p.match(/^\/nodes\[([^\]]+)\]/); if (m) target = data.nodes.find(n => n.id === m[1]); } // /nodes[FieldPrep]
  if (target) {
    tree = target.treeId; sel = target.id; section = 'nodes';
    $$('.nav').forEach(n => n.classList.toggle('active', n.dataset.section === 'nodes'));
    render();
    msg('Jumped to ' + target.id);
    return;
  }
  if (/^\/versions\//.test(p) || /^\/versions\[/.test(p)) { msg('See the manual version pins in the sidebar'); return; }
  msg('Diagnostic at ' + p);
}

// Exact JSON diff of the whole canonical document (pristine vs current). Positional line diff —
// deterministic and narrow, since a field edit or pin bump changes only its own line(s).
function renderJsonDiff(el) {
  if (curCanonical === origCanonical) { el.innerHTML = '<span class="cm">// No local changes. Edit a Cooking node and Apply draft.</span>'; return; }
  el.innerHTML = lineDiff(origCanonical, curCanonical);
}

function renderGeneratedCs(el) {
  if (generationBlocked) {
    el.innerHTML = `<div class="diaghead bad">Generation is blocked by validation. Resolve the Problems tab; the core refuses to generate an invalid asset.</div>`;
    return;
  }
  const before = origArtifacts[NODES_ARTIFACT] || '';
  const after = (curArtifacts && curArtifacts[NODES_ARTIFACT]) || '';
  if (before === after) {
    el.innerHTML = `<div class="diaghead">Deterministic core output · ${NODES_ARTIFACT} · no change vs baseline.</div>` +
      `<span class="cm">// ${esc(NODES_ARTIFACT)} — first lines of the core-generated artifact:</span>\n` +
      esc(after.split('\n').slice(0, 14).join('\n'));
    return;
  }
  el.innerHTML = `<div class="diaghead">Narrow generated-C# diff · ${NODES_ARTIFACT} (core output).</div>` + lineDiff(before, after);
}

function lineDiff(beforeText, afterText) {
  const a = beforeText.split('\n'), b = afterText.split('\n'), r = [];
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    if (a[i] === b[i]) continue;
    if (a[i] !== undefined) r.push(`<span class="del">- ${esc(a[i])}</span>`);
    if (b[i] !== undefined) r.push(`<span class="add">+ ${esc(b[i])}</span>`);
  }
  return r.join('\n');
}

// ── dirty / reset / chrome ────────────────────────────────────────────────────────────────────
function markDirty() { dirty = true; $('#dirty').classList.add('on'); }

function wireChrome() {
  $$('.nav').forEach(x => x.onclick = () => {
    section = x.dataset.section;
    $$('.nav').forEach(n => n.classList.toggle('active', n === x));
    canvas(); inspector();
  });
  $$('.ot').forEach(x => x.onclick = () => { out = x.dataset.out; output(); });
  $('#validate').onclick = async () => {
    await refresh(); out = 'problems'; output();
    msg(curDiagnostics.length ? `${curDiagnostics.length} diagnostic${curDiagnostics.length > 1 ? 's' : ''} from core` : 'Core validation passed');
  };
  $('#reset').onclick = async () => {
    data = JSON.parse(JSON.stringify(orig));
    sel = data.nodes.find(n => n.treeId === tree)?.id || data.nodes[0].id;
    dirty = false; $('#dirty').classList.remove('on');
    await refresh(); render();
    msg('Reverted to the clean asset');
  };
  $('#export').onclick = async () => {
    const res = await api('/api/export', { document: JSON.stringify(data), baselineHash });
    if (res.ok) {
      msg(`Exported ${res.files.length} file(s) to scratch`);
      $('#output').innerHTML = `<div class="diaghead">Atomic scratch export · ${esc(res.outputDirectory)}</div>` +
        res.files.map(f => `<div class="problem"><span class="ok">✓</span><b>${esc(f)}</b><small>written</small></div>`).join('');
      out = 'problems'; $$('.ot').forEach(x => x.classList.remove('active'));
    } else if (res.status === 'stale-baseline') {
      msg('Export refused — asset changed on disk'); out = 'problems'; output();
      $('#output').innerHTML = `<div class="diaghead bad">${esc(res.error)}</div>`;
    } else {
      msg('Export blocked — see Problems'); out = 'problems'; output();
    }
  };
}

function render() { counts(); renderTrees(); renderPins(); canvas(); inspector(); output(); }

boot().catch(e => { $('#canvas').innerHTML = `<div class="cards"><h2>Startup error</h2><p>${esc(e.message)}</p></div>`; });
