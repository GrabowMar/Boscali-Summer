(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const data = window.CODE_ATLAS;
  const fmt = n => Number(n || 0).toLocaleString('en-US');
  const compact = n => n >= 1000 ? (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k' : String(n || 0);
  const node = (tag, cls, text) => {
    const result = document.createElement(tag);
    if (cls) result.className = cls;
    if (text !== undefined) result.textContent = text;
    return result;
  };
  const add = (parent, tag, cls, text) => { const result = node(tag, cls, text); parent.append(result); return result; };
  const button = (parent, text, cls, action) => {
    const result = add(parent, 'button', cls, text); result.type = 'button'; result.addEventListener('click', action); return result;
  };
  const fail = message => { $('fatal').hidden = false; $('fatal').textContent = message; };
  if (!data || !Array.isArray(data.modules) || !Array.isArray(data.files)) {
    fail('The source snapshot could not be loaded. Regenerate Code Atlas, then open index.html beside atlas-data.js.');
    return;
  }
  if (!window.cytoscape || !window.d3) {
    fail('A visualization library is missing. Keep the vendor folder beside index.html; Code Atlas runs entirely offline.');
    return;
  }

  const modules = new Map(data.modules.map(m => [m.id, m]));
  const files = new Map(data.files.map(f => [f.id, f]));
  const allEdges = data.edges || [];
  const fileEdges = data.fileEdges || [];
  const meta = data.meta || {};
  const state = { view: 'graph', scope: '', query: '', selected: null, minWeight: 5 };
  let cy;
  let resizeTimer;
  const nameOf = id => modules.get(id)?.name || files.get(id)?.name || id;
  const isShared = id => ['Core', 'AvionicsUi'].includes(modules.get(id)?.name || id);
  const isFile = id => files.has(id);
  const selectedId = () => state.selected && state.selected.kind !== 'edge' ? state.selected.id : null;

  function metric(parent, label, value) {
    const group = add(parent, 'div');
    add(group, 'dd', '', value);
    add(group, 'dt', '', label);
  }

  data.modules.slice().sort((a, b) => a.name.localeCompare(b.name)).forEach(m => {
    const option = add($('scope'), 'option', '', m.name); option.value = m.id;
  });
  metric($('summary'), 'COMPONENTS', fmt(data.modules.length));
  metric($('summary'), 'SOURCE FILES', fmt(meta.sourceFiles || data.files.length));
  metric($('summary'), 'NONBLANK LINES', fmt(meta.sourceLines || data.files.reduce((sum, f) => sum + f.lines, 0)));
  metric($('summary'), 'TYPES', fmt(meta.typeCount));
  const timestamp = new Date(meta.generatedAt);
  const date = Number.isNaN(timestamp.getTime()) ? 'Source snapshot' : timestamp.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  $('snapshot').textContent = date + ' · ' + String(meta.gitRevision || 'working tree').slice(0, 8) + (meta.dirtyFiles ? ' · modified checkout' : ' · checkout snapshot');
  $('snapshot').title = `${fmt(meta.dirtyFiles)} modified tracked paths at extraction time`;
  add($('method-content'), 'p', '', meta.analysis || 'Dependencies are resolved from C# source with Roslyn.');
  add($('method-content'), 'p', '', `${fmt(meta.dirtyFiles)} modified tracked paths at extraction time. This is a static source snapshot, not runtime telemetry.`);
  if (meta.diagnostics) add($('method-content'), 'p', '', `Analysis compilation diagnostics: ${fmt(meta.diagnostics.errors)} errors, ${fmt(meta.diagnostics.warnings)} warnings. Unresolved identifiers: ${fmt(meta.unresolvedIdentifiers)}. These counts describe the analysis model, not a build result.`);
  if (meta.preprocessorSymbols?.length) add($('method-content'), 'p', '', 'Active preprocessor symbols: ' + meta.preprocessorSymbols.join(', ') + '.');
  const notes = [
    'An arrow A → B means A uses a type declared in B. Matrix rows use columns. Coupling is the number of distinct resolved links reported by the extractor.',
    'Treemap area measures nonblank physical source lines, including nonblank comments. It does not measure complexity, test coverage, runtime cost, or quality.',
    ...(meta.scope || []), ...(meta.limitations || []), ...(meta.workspaceWarnings || [])
  ];
  const notesList = add($('method-content'), 'ul'); notes.forEach(text => add(notesList, 'li', '', text));

  function model() {
    const query = state.query.toLowerCase();
    let items = state.scope ? data.files.filter(f => f.module === state.scope) : data.modules;
    if (query) items = items.filter(item => {
      if (String(item.name + ' ' + (item.path || item.id)).toLowerCase().includes(query)) return true;
      return !state.scope && data.files.some(f => f.module === item.id && f.id.toLowerCase().includes(query));
    });
    const ids = new Set(items.map(item => item.id));
    const edges = (state.scope ? fileEdges : allEdges).filter(e => ids.has(e.source) && ids.has(e.target));
    return { items, edges, shownEdges: edges.filter(e => e.weight >= state.minWeight), ids };
  }

  function setScope(scope) {
    state.scope = scope; state.query = ''; state.selected = scope ? { kind: 'module', id: scope } : null;
    state.minWeight = scope ? 1 : 5; $('strength').value = String(state.minWeight);
    $('scope').value = scope; $('search').value = ''; render();
  }

  function select(id) {
    state.selected = { kind: isFile(id) ? 'file' : 'module', id };
    renderList(); renderInspector(); highlightGraph(); highlightTreemap();
  }

  function selectEdge(edge) {
    state.selected = { kind: 'edge', ...edge };
    renderInspector(); highlightGraph();
  }

  function renderList() {
    const content = $('source-list'); content.replaceChildren();
    const items = model().items.slice().sort((a, b) => a.name.localeCompare(b.name));
    $('list-title').textContent = state.scope ? 'SOURCE FILES' : 'COMPONENTS';
    $('list-count').textContent = fmt(items.length);
    items.forEach(item => {
      const entry = button(content, '', 'source-item' + (selectedId() === item.id ? ' selected' : ''), () => select(item.id));
      entry.title = `${item.path || item.id} · ${fmt(item.lines)} nonblank lines`;
      entry.setAttribute('aria-pressed', String(selectedId() === item.id));
      add(entry, 'span', 'source-marker' + (isShared(item.module || item.id) ? ' shared' : ''));
      add(entry, 'span', 'source-name', item.name);
      add(entry, 'span', 'source-value', compact(item.lines));
    });
  }

  function render() {
    renderList(); renderInspector();
    document.querySelectorAll('.view-tab').forEach(tab => {
      const active = tab.dataset.view === state.view;
      tab.classList.toggle('active', active); tab.setAttribute('aria-pressed', String(active));
    });
    ['graph', 'matrix', 'treemap'].forEach(view => { $(view).hidden = view !== state.view; });
    $('strength-wrap').hidden = state.view === 'treemap';
    $('fit').hidden = state.view !== 'graph';
    const { items, edges, shownEdges } = model();
    $('empty').hidden = items.length !== 0;
    const scopeName = state.scope ? nameOf(state.scope) : 'Component';
    $('view-title').textContent = state.view === 'graph' ? `${scopeName} dependencies` : state.view === 'matrix' ? `${state.scope ? scopeName + ' file' : 'Component'} coupling matrix` : `${state.scope ? scopeName + ' source' : 'Codebase'} footprint`;
    $('view-caption').textContent = state.view === 'graph' ? (state.scope ? 'File-to-file links inside this component. Select a file to inspect.' : 'Select a component to trace its dependencies. Double-click to explore files.') : state.view === 'matrix' ? 'Each row uses the column. Select a filled cell to inspect the coupling.' : 'Area = nonblank source lines. Select a file or a component heading.';
    $('view-count').textContent = state.view === 'treemap' ? `${fmt(items.length)} ${state.scope ? 'files' : 'components'}` : `${fmt(items.length)} nodes · ${fmt(shownEdges.length)} / ${fmt(edges.length)} links`;
    $('legend').textContent = state.view === 'treemap' ? 'AREA = NONBLANK SOURCE LINES' : state.view === 'matrix' ? 'ROW USES COLUMN · VALUE = DISTINCT LINKS' : state.scope ? 'NODE AREA ≈ SOURCE SIZE · ARROW = USES' : 'ARROW = USES · LINE WIDTH ≈ DISTINCT TYPE LINKS';
    if (state.view === 'graph') renderGraph(items, shownEdges);
    else if (state.view === 'matrix') renderMatrix(items, shownEdges);
    else renderTreemap(items);
  }

  function renderGraph(items, edges) {
    if (cy) cy.destroy();
    const maxLines = Math.max(1, ...items.map(item => item.lines));
    const degrees = new Map(items.map(item => [item.id, 0]));
    edges.forEach(edge => { degrees.set(edge.target, (degrees.get(edge.target) || 0) + 1); });
    const moduleOverview = !state.scope;
    const positions = new Map();
    if (moduleOverview) {
      const width = Math.max(280, $('graph').clientWidth);
      const height = Math.max(250, $('graph').clientHeight);
      const foundations = items.filter(item => isShared(item.id));
      const features = items.filter(item => !isShared(item.id)).sort((a, b) => a.name.localeCompare(b.name));
      const columns = Math.min(5, Math.max(2, Math.floor(width / 145)));
      const rows = Math.ceil(features.length / columns) + (foundations.length ? 1 : 0);
      const rowHeight = Math.max(36, (height - 35) / Math.max(1, rows));
      foundations.forEach((item, i) => positions.set(item.id, { x: width * (i + 1) / (foundations.length + 1), y: 20 + rowHeight / 2 }));
      features.forEach((item, i) => {
        const row = Math.floor(i / columns);
        const rowItems = Math.min(columns, features.length - row * columns);
        const column = i % columns;
        positions.set(item.id, { x: width * (column + (columns - rowItems) / 2 + .5) / columns, y: 20 + rowHeight * (row + (foundations.length ? 1 : 0) + .5) });
      });
    }
    cy = window.cytoscape({
      container: $('graph'),
      elements: [
        ...items.map(item => ({ data: { id: item.id, label: item.name.replace(/\.cs$/, ''), size: 25 + 45 * Math.sqrt(item.lines / maxLines), boxHeight: 30 + 16 * Math.sqrt(item.lines / maxLines), shared: isShared(item.module || item.id) ? 1 : 0, degree: degrees.get(item.id) }, position: positions.get(item.id) })),
        ...edges.map((edge, i) => ({ data: { id: 'edge-' + i, source: edge.source, target: edge.target, weight: edge.weight, thickness: Math.min(5, .6 + Math.log2(edge.weight + 1) * .32), edge } }))
      ],
      style: [
        { selector: 'node', style: { 'background-color': moduleOverview ? '#263d2d' : '#73ad8b', 'border-width': 1, 'border-color': '#9ccea7', 'border-opacity': moduleOverview ? .35 : .6, width: moduleOverview ? 136 : 'data(size)', height: moduleOverview ? 'data(boxHeight)' : 'data(size)', shape: moduleOverview ? 'round-rectangle' : 'ellipse', label: 'data(label)', color: '#e2ebe2', 'font-size': moduleOverview ? 13 : 10, 'font-family': 'Segoe UI, Arial, sans-serif', 'text-valign': moduleOverview ? 'center' : 'bottom', 'text-margin-y': moduleOverview ? 0 : 7, 'text-background-color': '#171d1b', 'text-background-opacity': moduleOverview ? 0 : .85, 'text-background-padding': '3px', 'text-background-shape': 'roundrectangle', 'min-zoomed-font-size': moduleOverview ? 0 : 6 } },
        { selector: 'node[shared = 1]', style: { 'background-color': moduleOverview ? '#453c28' : '#b09b65', 'border-color': '#d8ba78', shape: 'round-rectangle' } },
        { selector: 'edge', style: { width: 'data(thickness)', 'line-color': '#6d9581', 'target-arrow-color': '#8aad96', 'target-arrow-shape': 'triangle', 'arrow-scale': .65, 'curve-style': 'bezier', opacity: moduleOverview ? .14 : .22 } },
        { selector: '.dim', style: { opacity: .13 } },
        { selector: 'node.focus', style: { 'border-color': '#dcf8ce', 'border-width': 3, 'background-color': '#b6e3a2', color: moduleOverview ? '#172519' : '#eef7ea', 'z-index': 20 } },
        { selector: 'edge.outgoing', style: { 'line-color': '#b5e59b', 'target-arrow-color': '#b5e59b', opacity: .95, 'z-index': 10 } },
        { selector: 'edge.incoming', style: { 'line-color': '#d8ba78', 'target-arrow-color': '#d8ba78', opacity: .9, 'z-index': 10 } },
        { selector: 'edge.focus', style: { 'line-color': '#d9ffc2', 'target-arrow-color': '#d9ffc2', opacity: 1, width: 4, 'z-index': 30 } }
      ],
      layout: moduleOverview ? { name: 'preset', fit: true, padding: 20 } : {
        name: 'concentric', fit: true, padding: 43, avoidOverlap: true, minNodeSpacing: state.scope ? 40 : 50,
        concentric: n => n.data('degree'), levelWidth: () => Math.max(3, Math.ceil(Math.max(1, ...degrees.values()) / 4)),
        startAngle: -Math.PI / 2, clockwise: true, equidistant: false
      },
      minZoom: .05, maxZoom: 5, wheelSensitivity: .25, boxSelectionEnabled: false,
      userPanningEnabled: true, autoungrabify: false
    });
    cy.on('tap', 'node', event => select(event.target.id()));
    cy.on('dbltap', 'node', event => { const id = event.target.id(); if (modules.has(id)) setScope(id); });
    cy.on('tap', 'edge', event => selectEdge(event.target.data('edge')));
    cy.on('tap', event => { if (event.target === cy) { state.selected = null; renderList(); renderInspector(); highlightGraph(); } });
    highlightGraph();
  }

  function highlightGraph() {
    if (!cy) return;
    cy.elements().removeClass('dim focus incoming outgoing');
    if (!state.selected) return;
    if (state.selected.kind === 'edge') {
      const matches = cy.edges().filter(edge => edge.source().id() === state.selected.source && edge.target().id() === state.selected.target);
      if (!matches.length) return;
      cy.elements().addClass('dim'); matches.removeClass('dim').addClass('focus'); matches.connectedNodes().removeClass('dim').addClass('focus');
    } else {
      const selected = cy.getElementById(state.selected.id);
      if (!selected.length) return;
      cy.elements().addClass('dim'); selected.closedNeighborhood().removeClass('dim'); selected.addClass('focus');
      selected.outgoers('edge').addClass('outgoing'); selected.incomers('edge').addClass('incoming');
    }
  }

  function renderMatrix(items, edges) {
    const content = $('matrix'); content.replaceChildren();
    const sorted = items.slice().sort((a, b) => a.name.localeCompare(b.name));
    const edgeMap = new Map(edges.map(edge => [edge.source + '\0' + edge.target, edge]));
    const maxWeight = Math.max(1, ...edges.map(edge => edge.weight));
    const table = add(content, 'table', 'matrix-table');
    add(table, 'caption', '', `${state.scope ? nameOf(state.scope) + ': ' : ''}Rows use columns · ${fmt(edges.length)} directed dependencies`);
    const head = add(table, 'thead'); const header = add(head, 'tr'); add(header, 'th');
    sorted.forEach(item => { const th = add(header, 'th'); th.scope = 'col'; th.title = item.path || item.id; add(th, 'span', '', item.name); });
    const body = add(table, 'tbody');
    sorted.forEach(source => {
      const row = add(body, 'tr'); const label = add(row, 'th', '', source.name); label.scope = 'row'; label.title = source.path || source.id;
      sorted.forEach(target => {
        const cell = add(row, 'td');
        if (source.id === target.id) { cell.className = 'diagonal'; cell.textContent = '·'; return; }
        const edge = edgeMap.get(source.id + '\0' + target.id);
        if (!edge) return;
        const opacity = .2 + .7 * Math.log1p(edge.weight) / Math.log1p(maxWeight);
        cell.style.background = `rgba(129, 188, 122, ${opacity.toFixed(3)})`;
        const b = button(cell, edge.weight > 999 ? compact(edge.weight) : String(edge.weight), '', () => {
          content.querySelectorAll('.selected').forEach(element => element.classList.remove('selected')); cell.classList.add('selected'); selectEdge(edge);
        });
        b.title = `${source.name} uses ${target.name}: ${fmt(edge.weight)} distinct links`;
        b.setAttribute('aria-label', b.title);
      });
    });
  }

  function renderTreemap(items) {
    const content = $('treemap'); content.replaceChildren();
    const rect = content.getBoundingClientRect();
    const width = Math.max(200, rect.width - 40); const height = Math.max(200, rect.height - 30);
    const visibleIds = new Set(items.map(item => item.id));
    const query = state.query.toLowerCase();
    const selectedFiles = state.scope ? items : data.files.filter(f => visibleIds.has(f.module) && (!query || f.id.toLowerCase().includes(query) || nameOf(f.module).toLowerCase().includes(query)));
    const groups = state.scope ? [{ id: state.scope, name: nameOf(state.scope), children: selectedFiles.map(file => ({ name: file.name, file, value: file.lines })) }] : items.map(m => ({ id: m.id, name: m.name, children: selectedFiles.filter(f => f.module === m.id).map(file => ({ name: file.name, file, value: file.lines })) })).filter(m => m.children.length);
    const root = d3.hierarchy({ children: groups }).sum(item => item.value || 0).sort((a, b) => b.value - a.value);
    d3.treemap().size([width, height]).paddingOuter(2).paddingTop(item => item.depth === 1 ? 24 : 0).paddingInner(2).round(true)(root);
    const svg = d3.select(content).append('svg').attr('class', 'treemap-svg').attr('viewBox', `0 0 ${width} ${height}`).attr('role', 'img').attr('aria-label', 'Source size treemap. Each rectangle is a file, grouped by component; area represents nonblank lines. Use the source explorer to select with a keyboard.');
    const group = svg.selectAll('g.module').data(root.children || []).join('g').attr('class', 'module');
    group.append('rect').attr('class', m => 'tree-module' + (isShared(m.data.id) ? ' shared' : '')).attr('x', m => m.x0).attr('y', m => m.y0).attr('width', m => Math.max(0, m.x1 - m.x0)).attr('height', m => Math.max(0, m.y1 - m.y0));
    group.filter(m => m.x1 - m.x0 > 55 && m.y1 - m.y0 > 25).append('text').attr('class', 'tree-module-label').attr('x', m => m.x0 + 7).attr('y', m => m.y0 + 16).text(m => m.data.name.length * 7 > m.x1 - m.x0 - 15 ? m.data.name.slice(0, Math.max(3, Math.floor((m.x1 - m.x0 - 20) / 7))) + '…' : m.data.name).style('pointer-events', 'auto').style('cursor', 'pointer').on('click', (_, m) => setScope(m.data.id));
    const leaves = svg.selectAll('g.file').data(root.leaves().filter(m => m.data.file)).join('g').attr('class', 'file');
    leaves.append('rect').attr('class', m => 'tree-file' + (isShared(m.data.file.module) ? ' shared' : '')).attr('data-file', m => m.data.file.id).attr('x', m => m.x0).attr('y', m => m.y0).attr('width', m => Math.max(0, m.x1 - m.x0)).attr('height', m => Math.max(0, m.y1 - m.y0)).on('click', (_, m) => select(m.data.file.id)).append('title').text(m => `${m.data.file.id}\n${fmt(m.value)} nonblank lines · ${fmt(m.data.file.typeCount)} types`);
    leaves.filter(m => m.x1 - m.x0 > 68 && m.y1 - m.y0 > 30).append('text').attr('class', 'tree-label').attr('x', m => m.x0 + 5).attr('y', m => m.y0 + 15).text(m => {
      const name = m.data.name.replace(/\.cs$/, ''); const chars = Math.floor((m.x1 - m.x0 - 10) / 5.6); return name.length > chars ? name.slice(0, Math.max(1, chars - 1)) + '…' : name;
    });
    leaves.filter(m => m.x1 - m.x0 > 68 && m.y1 - m.y0 > 46).append('text').attr('class', 'tree-meta').attr('x', m => m.x0 + 5).attr('y', m => m.y0 + 30).text(m => `${fmt(m.value)} lines`);
    highlightTreemap();
  }

  function highlightTreemap() {
    $('treemap').querySelectorAll('[data-file]').forEach(rect => { rect.classList.toggle('selected', rect.dataset.file === selectedId()); });
  }

  function renderInspector() {
    const content = $('inspector-content'); content.replaceChildren();
    if (!state.selected) {
      add(content, 'p', 'inspection-kind', 'THE BIG PICTURE');
      add(content, 'h2', 'inspection-title', 'A map of the source.');
      add(content, 'p', 'inspect-intro', 'Select a component to see what it uses and who depends on it. Explore its files to move closer to the code.');
      const stats = add(content, 'dl', 'inspection-stats');
      metric(stats, 'Component links', fmt(allEdges.length)); metric(stats, 'File links', fmt(fileEdges.length));
      add(content, 'h3', '', 'LARGEST COMPONENTS');
      const largest = data.modules.slice().sort((a, b) => b.lines - a.lines).slice(0, 7);
      const max = largest[0]?.lines || 1;
      largest.forEach((m, i) => {
        const rank = add(content, 'div', 'module-rank'); add(rank, 'span', 'rank-number', String(i + 1).padStart(2, '0'));
        const body = add(rank, 'div', 'rank-body'); button(body, m.name, '', () => select(m.id));
        const bar = add(body, 'div', 'rank-bar'); add(bar, 'i').style.width = `${100 * m.lines / max}%`;
        add(rank, 'span', 'rank-lines', compact(m.lines));
      });
      add(content, 'p', 'detail-note', 'Size is a navigation aid. A large component is not automatically a problem.');
      return;
    }
    if (state.selected.kind === 'edge') { renderEdgeInspector(content, state.selected); return; }
    const item = modules.get(state.selected.id) || files.get(state.selected.id);
    if (!item) return;
    const module = modules.has(item.id);
    const edges = module ? allEdges : fileEdges;
    const outgoing = edges.filter(e => e.source === item.id).sort((a, b) => b.weight - a.weight);
    const incoming = edges.filter(e => e.target === item.id).sort((a, b) => b.weight - a.weight);
    add(content, 'p', 'inspection-kind', module ? (isShared(item.id) ? 'SHARED FOUNDATION' : 'FEATURE COMPONENT') : 'SOURCE FILE');
    add(content, 'h2', 'inspection-title', item.name);
    add(content, 'p', 'inspection-path', item.path || item.id);
    const stats = add(content, 'dl', 'inspection-stats');
    metric(stats, 'Nonblank lines', fmt(item.lines)); metric(stats, module ? 'Source files' : 'Declared types', fmt(module ? item.files : item.typeCount));
    metric(stats, 'Uses', fmt(outgoing.length)); metric(stats, 'Used by', fmt(incoming.length));
    if (module) button(content, `Explore ${fmt(item.files)} source files →`, 'action', () => setScope(item.id));
    else if (state.scope !== item.module) button(content, `Explore ${nameOf(item.module)} →`, 'action', () => { setScope(item.module); select(item.id); });
    if (!module && item.namespace) add(content, 'p', 'detail-note', 'Namespace: ' + item.namespace);
    connectionList(content, 'USES →', outgoing, true);
    connectionList(content, '← USED BY', incoming, false);
    if (!incoming.length && !outgoing.length) add(content, 'p', 'detail-note', 'No resolved cross-file type links were recorded for this selection. Reflection and runtime wiring are outside this view.');
  }

  function connectionList(parent, title, edges, outgoing) {
    if (!edges.length) return;
    add(parent, 'h3', '', `${title} ${fmt(edges.length)}`);
    const list = add(parent, 'div', 'connection-list');
    edges.slice(0, 30).forEach(edge => {
      const id = outgoing ? edge.target : edge.source;
      const b = button(list, '', 'connection-button', () => selectEdge(edge));
      add(b, 'span', '', nameOf(id)); add(b, 'small', '', fmt(edge.weight));
      b.title = `${nameOf(edge.source)} uses ${nameOf(edge.target)} · ${fmt(edge.weight)} links`;
    });
    if (edges.length > 30) add(parent, 'p', 'detail-note', `Showing the 30 strongest of ${fmt(edges.length)}. Use the matrix to inspect the remaining connections.`);
  }

  function renderEdgeInspector(content, edge) {
    add(content, 'p', 'inspection-kind', 'DIRECTED COUPLING');
    add(content, 'h2', 'inspection-title', `${nameOf(edge.source)} → ${nameOf(edge.target)}`);
    add(content, 'p', 'inspection-path', `${edge.source}\nuses\n${edge.target}`);
    add(content, 'span', 'weight-label', `${fmt(edge.weight)} DISTINCT LINKS`);
    let evidence = edge.evidence || [];
    if (!evidence.length && isFile(edge.source) && isFile(edge.target)) {
      const sourceModule = files.get(edge.source).module; const targetModule = files.get(edge.target).module;
      evidence = (allEdges.find(e => e.source === sourceModule && e.target === targetModule)?.evidence || []).filter(e => e.file === edge.source && e.targetFile === edge.target);
    }
    add(content, 'p', 'inspect-intro', 'The source on the left references types owned by the destination. Repeated mentions of the same dependency are deduplicated by the extractor.');
    if (evidence.length) {
      add(content, 'h3', '', 'SOURCE EVIDENCE');
      evidence.forEach(e => {
        const row = add(content, 'div', 'evidence');
        add(row, 'p', 'evidence-symbol', e.symbol);
        add(row, 'p', 'evidence-file', `${e.file}:${e.line}`);
        add(row, 'p', 'evidence-file', `→ ${e.targetFile}:${e.targetLine}`);
      });
      add(content, 'p', 'detail-note', `Up to ${fmt(evidence.length)} representative references shown; coupling weight counts all recorded links.`);
    } else {
      add(content, 'p', 'detail-note', 'This file pair has a recorded resolved type dependency. Representative symbol evidence is stored for component links; inspect the source files for the complete relationship.');
    }
    const actions = add(content, 'div', 'connection-list');
    button(actions, `Inspect ${nameOf(edge.source)}`, 'connection-button', () => select(edge.source));
    button(actions, `Inspect ${nameOf(edge.target)}`, 'connection-button', () => select(edge.target));
  }

  document.querySelectorAll('.view-tab').forEach(tab => tab.addEventListener('click', () => { state.view = tab.dataset.view; render(); }));
  $('scope').addEventListener('change', () => setScope($('scope').value));
  $('search').addEventListener('input', () => { state.query = $('search').value.trim(); render(); });
  $('strength').addEventListener('change', () => { state.minWeight = Number($('strength').value); render(); });
    $('fit').addEventListener('click', () => { if (cy) cy.fit(undefined, state.scope ? 43 : 20); });
  $('clear').addEventListener('click', () => { state.selected = null; renderList(); renderInspector(); highlightGraph(); highlightTreemap(); });
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if (state.view === 'graph' && cy) { if (!state.scope) renderGraph(model().items, model().shownEdges); else { cy.resize(); cy.fit(undefined, 43); } }
      else if (state.view === 'treemap') renderTreemap(model().items);
    }, 140);
  });
  render();
})();
