// Manajoy GameClass — club floor map: live view (dashboard / monitor) and the editor.
// Plain SVG built from strings; CSP-safe (no inline styles or handlers: presentation attributes,
// CSS classes and CSSOM only). Coordinates are Manajoy's club-map space 0..1000 (services.ResourceLayout):
// a PC point is the centre of its tile, an object is x/y (top-left) + w/h.
'use strict';

window.MJMap = (() => {
  const GRID = 1000;
  const SNAP = 25;
  const PC_W = 84;
  const PC_H = 66;
  const PC_MIN = 50;
  const PC_MAX = GRID - 50;
  const PAD = 40;
  const ZONE_PAD = 14;
  const ZONE_LABEL = 20;
  const STEP = 100;
  const MAX_UNDO = 100;
  const TEXT_KINDS = ['area', 'room', 'desk', 'entrance', 'label'];
  const BACKGROUND_KINDS = ['area', 'room'];
  // Named colours only: the saved map stores the name, never CSS.
  const AREA_COLORS = {
    blue: '#1b54e4', purple: '#7a2be2', orange: '#e08a1e', green: '#1e9e62',
    teal: '#0f9d9a', pink: '#db2777', red: '#e0463a', gray: '#7b8794',
  };
  // Ready-made places to mark on the floor (name comes from i18n, size in map units).
  const PRESETS = [
    { id: 'hall', kind: 'area', color: 'blue', w: 400, h: 300 },
    { id: 'vip', kind: 'area', color: 'purple', w: 300, h: 250 },
    { id: 'bar', kind: 'area', color: 'orange', w: 250, h: 150 },
    { id: 'cafe', kind: 'area', color: 'pink', w: 250, h: 200 },
    { id: 'console', kind: 'area', color: 'teal', w: 250, h: 200 },
    { id: 'lounge', kind: 'area', color: 'green', w: 250, h: 150 },
    { id: 'wc', kind: 'area', color: 'gray', w: 125, h: 125 },
    { id: 'smoke', kind: 'area', color: 'red', w: 150, h: 125 },
    { id: 'desk', kind: 'desk', w: 150, h: 75 },
    { id: 'entrance', kind: 'entrance', w: 125, h: 25 },
    { id: 'custom', kind: 'area', color: 'blue', w: 250, h: 200 },
  ];
  const ZONE_COLORS = ['#7a2be2', '#0ea5f0', '#e08a1e', '#1e9e62', '#e0463a', '#4324d6', '#0f9d9a', '#c2410c'];

  const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const snap = (v) => Math.round(v / SNAP) * SNAP;
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const num = (v) => Math.round(Number(v) || 0);
  const naturalName = (a, b) => String(a).localeCompare(String(b), undefined, { numeric: true });

  // ------------------------------------------------------------ geometry

  function zoneOrder(pcs) {
    const order = [];
    for (const pc of pcs) if (!order.includes(pc.zone || '')) order.push(pc.zone || '');
    return order;
  }

  function zoneColor(order, zone) {
    const i = order.indexOf(zone || '');
    return ZONE_COLORS[(i < 0 ? 0 : i) % ZONE_COLORS.length];
  }

  /** Frames around each zone's placed PCs (like the backend's _areas_from_stations): no drawing needed. */
  function zoneFrames(points, byName, order) {
    const groups = new Map();
    for (const [name, p] of Object.entries(points)) {
      const pc = byName.get(name);
      if (!pc || !pc.zone) continue;
      if (!groups.has(pc.zone)) groups.set(pc.zone, []);
      groups.get(pc.zone).push(p);
    }
    return [...groups.entries()].map(([zone, list]) => {
      const x = Math.min(...list.map((p) => p.x)) - PC_W / 2 - ZONE_PAD;
      const y = Math.min(...list.map((p) => p.y)) - PC_H / 2 - ZONE_PAD - ZONE_LABEL;
      const w = Math.max(...list.map((p) => p.x)) + PC_W / 2 + ZONE_PAD - x;
      const h = Math.max(...list.map((p) => p.y)) + PC_H / 2 + ZONE_PAD - y;
      return { zone, x, y, w, h, color: zoneColor(order, zone) };
    });
  }

  function bounds(points, objects, frames) {
    const xs = [];
    const ys = [];
    for (const p of Object.values(points)) { xs.push(p.x - PC_W / 2, p.x + PC_W / 2); ys.push(p.y - PC_H / 2, p.y + PC_H / 2); }
    for (const o of objects) { xs.push(o.x, o.x + o.w); ys.push(o.y, o.y + o.h); }
    for (const f of frames) { xs.push(f.x, f.x + f.w); ys.push(f.y, f.y + f.h); }
    if (!xs.length) return null;
    return { x: Math.min(...xs), y: Math.min(...ys), w: Math.max(...xs) - Math.min(...xs), h: Math.max(...ys) - Math.min(...ys) };
  }

  function padded(box, pad, minW = 320, minH = 220) {
    const w = Math.max(box.w + pad * 2, minW);
    const h = Math.max(box.h + pad * 2, minH);
    return { x: box.x + box.w / 2 - w / 2, y: box.y + box.h / 2 - h / 2, w, h };
  }

  /** Areas and rooms lie under everything else; the rest keep the order they were drawn in. */
  const ordered = (objects) => [...objects.filter((o) => BACKGROUND_KINDS.includes(o.kind)), ...objects.filter((o) => !BACKGROUND_KINDS.includes(o.kind))];

  /** Once the owner marks areas (hall, VIP…), they replace the automatic GameClass zone frames. */
  const hasAreas = (objects) => (objects || []).some((o) => o.kind === 'area');

  const vbAttr = (b) => `${b.x.toFixed(1)} ${b.y.toFixed(1)} ${b.w.toFixed(1)} ${b.h.toFixed(1)}`;

  // ------------------------------------------------------------ pieces

  function frameSvg(f) {
    return `<g class="mzone"><rect x="${f.x}" y="${f.y}" width="${f.w}" height="${f.h}" rx="16" fill="${f.color}" stroke="${f.color}"/>
      <text x="${f.x + 12}" y="${f.y + 15}" fill="${f.color}">${esc(f.zone.toUpperCase())}</text></g>`;
  }

  function objectSvg(o, t, extra = '', editor = false) {
    const key = editor ? ` data-key="obj:${esc(o.id)}"` : '';
    const cls = `mobj ${esc(o.kind)}${extra}`;
    const cx = o.x + o.w / 2;
    const cy = o.y + o.h / 2;
    const text = o.text || (o.kind === 'label' ? '' : t('edit.default.' + o.kind));
    const fit = (factor, lo, hi) => clamp(Math.round(Math.min(o.h * factor, (o.w / Math.max(1, text.length)) * 1.7)), lo, hi);
    switch (o.kind) {
      case 'area': {
        const c = AREA_COLORS[o.color] || AREA_COLORS.blue;
        // The name must fit the width: uppercase bold is about 0.72 em per letter.
        const size = clamp(Math.floor(Math.min(o.h * 0.14, (o.w - 26) / (Math.max(3, text.length) * 0.72))), 9, 24);
        return `<g class="${cls}"${key}><rect x="${o.x}" y="${o.y}" width="${o.w}" height="${o.h}" rx="18" fill="${c}" stroke="${c}"/>
          ${text ? `<text x="${o.x + 14}" y="${o.y + 12 + size}" fill="${c}" font-size="${size}">${esc(text.toUpperCase())}</text>` : ''}</g>`;
      }
      case 'wall':
        return `<g class="${cls}"${key}><rect x="${o.x}" y="${o.y}" width="${o.w}" height="${o.h}" rx="3"/></g>`;
      case 'room':
        return `<g class="${cls}"${key}><rect x="${o.x}" y="${o.y}" width="${o.w}" height="${o.h}" rx="14"/>
          ${text ? `<text x="${o.x + 14}" y="${o.y + 24}" font-size="16">${esc(text)}</text>` : ''}</g>`;
      case 'desk':
      case 'entrance':
        return `<g class="${cls}"${key}><rect x="${o.x}" y="${o.y}" width="${o.w}" height="${o.h}" rx="${Math.min(12, o.h / 2)}"/>
          <text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="central" font-size="${fit(0.5, 9, 20)}">${esc(o.kind === 'entrance' ? '⇩ ' + text : text)}</text></g>`;
      default:
        return `<g class="${cls}"${key}><rect class="hit" x="${o.x}" y="${o.y}" width="${o.w}" height="${o.h}"/>
          <text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="central" font-size="${fit(0.62, 10, 56)}">${esc(text || '…')}</text></g>`;
    }
  }

  // 12×12 marks drawn before the tile's small line: time left, a reservation, a postpaid session.
  const TILE_ICONS = {
    time: '<circle cx="6" cy="6" r="5"/><path d="M6 3.2V6l2 1.4"/>',
    resv: '<rect x="1" y="2" width="10" height="9" rx="2"/><path d="M1 5.5h10M4 0.8v2.4M8 0.8v2.4"/>',
    post: '<path d="M4 11.2V1h3.2a2.8 2.8 0 010 5.6H4"/>',
  };

  /** What a tile shows; also its data-sig, so a poll can tell when the tile must be redrawn. */
  function tileLook(pc, look, editor) {
    if (!pc) return { sub: '?', icon: '', badge: '', title: '' };
    if (editor) return { sub: '', icon: '', badge: '', title: '' };
    const l = look(pc);
    return { sub: l.short || '', icon: l.icon || '', badge: l.badge || '', title: l.title || '' };
  }

  /** One PC tile. View mode: clickable (data-act="pc"); editor: draggable (data-key). */
  function pcSvg(name, p, pc, look, opts = {}) {
    const state = pc ? pc.state : 'ghost';
    const l = tileLook(pc, look, opts.editor);
    const attrs = opts.editor ? `data-key="pc:${esc(name)}"` : `data-act="pc" data-pc="${esc(name)}" data-sig="${esc(sigOf(l))}"`;
    const cls = `mpc ${esc(state)}${opts.extra || ''}`;
    const label = String(pc ? pc.number : name);
    // The number is the first thing a cashier reads: as big as the 84-unit tile allows.
    const numSize = label.length <= 2 ? 28 : label.length === 3 ? 24 : 20;
    let sub = '<text class="s" x="0" y="0"></text>';
    if (l.sub) {
      // Bold digits and letters are about 0.6 em wide; the line must fit the tile, icon included.
      const room = l.icon ? 60 : 76;
      const size = clamp(Math.floor(room / (l.sub.length * 0.6)), 10, 14);
      if (l.icon) {
        const width = 12 + 3 + l.sub.length * 0.6 * size;
        const x = Math.max(4, (PC_W - width) / 2);
        sub = `<g class="ic" transform="translate(${x.toFixed(1)} 44)">${TILE_ICONS[l.icon] || ''}</g>
          <text class="s" x="${(x + 15).toFixed(1)}" y="55" font-size="${size}">${esc(l.sub)}</text>`;
      } else {
        sub = `<text class="s" x="${PC_W / 2}" y="55" text-anchor="middle" font-size="${size}">${esc(l.sub)}</text>`;
      }
    }
    const badge = l.badge ? `<g class="bdg"><circle cx="73" cy="17" r="8"/><g transform="translate(67.5 11.5) scale(0.92)">${TILE_ICONS[l.badge] || ''}</g></g>` : '';
    return `<g class="${cls}" ${attrs} transform="translate(${p.x - PC_W / 2} ${p.y - PC_H / 2})">
      ${l.title ? `<title>${esc(l.title)}</title>` : ''}
      <rect class="b" width="${PC_W}" height="${PC_H}" rx="10"/><rect class="bar" width="${PC_W}" height="5" rx="2.5"/>
      <text class="n" x="${PC_W / 2}" y="${l.sub ? 34 : 43}" text-anchor="middle" font-size="${numSize}">${esc(label)}</text>
      ${sub}${badge}
    </g>`;
  }

  const sigOf = (l) => `${l.sub}|${l.icon}|${l.badge}|${l.title}`;

  // ------------------------------------------------------------ live view

  /** The live map is fitted tighter than the editor (which needs room around the drawing). */
  const VIEW_PAD = 24;

  function fitBox(doc, pcs, pad = PAD) {
    const byName = new Map(pcs.map((pc) => [pc.name, pc]));
    const points = placedKnown(doc, byName);
    const frames = hasAreas(doc.objects) ? [] : zoneFrames(points, byName, zoneOrder(pcs));
    const box = bounds(points, doc.objects || [], frames);
    return box ? padded(box, pad) : { x: 0, y: 0, w: GRID, h: 625 };
  }

  const viewFit = (doc, pcs) => fitBox(doc, pcs, VIEW_PAD);

  function placedKnown(doc, byName) {
    const points = {};
    for (const [name, p] of Object.entries((doc && doc.pcs) || {})) if (byName.has(name)) points[name] = p;
    return points;
  }

  /** Names of GameClass PCs that are on the map. */
  function placedNames(doc, pcs) {
    return pcs.filter((pc) => doc && doc.pcs && doc.pcs[pc.name]).map((pc) => pc.name);
  }

  /**
   * o: { doc, pcs, look(pc) -> {short}, t, match(pc) -> bool, selected, viewBox, controls }
   */
  function viewSvg(o) {
    const byName = new Map(o.pcs.map((pc) => [pc.name, pc]));
    const points = placedKnown(o.doc, byName);
    const order = zoneOrder(o.pcs);
    const box = o.viewBox || viewFit(o.doc, o.pcs);
    const pcsSvg = Object.entries(points)
      .sort(([a], [b]) => naturalName(a, b))
      .map(([name, p]) => {
        const pc = byName.get(name);
        return pcSvg(name, p, pc, o.look, { extra: pcExtra(pc, o) });
      }).join('');
    return `<div class="map-wrap">
      <svg class="cmap" viewBox="${vbAttr(box)}" preserveAspectRatio="xMidYMid meet" role="img" aria-label="${esc(o.t('map.aria'))}">
        <g class="zones">${hasAreas(o.doc.objects) ? '' : zoneFrames(points, byName, order).map(frameSvg).join('')}</g>
        <g class="objs">${ordered(o.doc.objects || []).map((obj) => objectSvg(obj, o.t)).join('')}</g>
        <g class="pcs">${pcsSvg}</g>
      </svg>
      ${o.controls === false ? '' : `<div class="map-zoom">
        <button data-act="map-zoom" data-z="in" title="${esc(o.t('map.zoomIn'))}">+</button>
        <button data-act="map-zoom" data-z="out" title="${esc(o.t('map.zoomOut'))}">−</button>
        <button data-act="map-zoom" data-z="fit" title="${esc(o.t('map.fit'))}">⤢</button>
        ${o.fullButton ? `<button class="full" data-act="${o.fullButton}" title="${esc(o.t(o.fullButton === 'map-full' ? 'map.full' : 'map.fullExit'))}">${o.fullButton === 'map-full' ? ICONS.expand : ICONS.collapse}</button>` : ''}
      </div>`}
    </div>`;
  }

  function pcExtra(pc, o) {
    let extra = '';
    if (o.match && !o.match(pc)) extra += ' dim';
    if (o.selected && o.selected === pc.name) extra += ' sel';
    return extra;
  }

  /**
   * Poll update without re-rendering the map (zoom and selection stay): classes change in place,
   * and a tile whose text or marks changed is swapped for a freshly drawn one.
   */
  function patchView(root, o) {
    const byName = new Map(o.pcs.map((pc) => [pc.name, pc]));
    const points = (o.doc && o.doc.pcs) || {};
    for (const g of root.querySelectorAll('g.mpc[data-pc]')) {
      const pc = byName.get(g.dataset.pc);
      if (!pc) continue;
      const cls = `mpc ${pc.state}${pcExtra(pc, o)}`;
      const p = points[g.dataset.pc];
      if (p && g.dataset.sig !== sigOf(tileLook(pc, o.look, false))) {
        g.insertAdjacentHTML('afterend', pcSvg(g.dataset.pc, p, pc, o.look, { extra: pcExtra(pc, o) }));
        g.remove();
        continue;
      }
      if (g.getAttribute('class') !== cls) g.setAttribute('class', cls);
    }
  }

  function zoomBox(box, factor, cx, cy) {
    const w = clamp(box.w * factor, 160, 2600);
    const h = box.h * (w / box.w);
    return { x: cx - (cx - box.x) * (w / box.w), y: cy - (cy - box.y) * (h / box.h), w, h };
  }

  function svgPoint(svg, clientX, clientY) {
    const m = svg.getScreenCTM();
    return m ? new DOMPoint(clientX, clientY).matrixTransform(m.inverse()) : { x: 0, y: 0 };
  }

  function currentBox(svg) {
    const v = svg.viewBox.baseVal;
    return { x: v.x, y: v.y, w: v.width, h: v.height };
  }

  /**
   * Wheel zoom and drag-to-pan on a live map. store: { set(box|null) } keeps the view across re-renders.
   * A drag never counts as a click on a PC.
   */
  function attachViewer(svg, store) {
    let drag = null;
    let suppressClick = false;
    svg.addEventListener('wheel', (e) => {
      e.preventDefault();
      const p = svgPoint(svg, e.clientX, e.clientY);
      const box = zoomBox(currentBox(svg), e.deltaY > 0 ? 1.15 : 1 / 1.15, p.x, p.y);
      svg.setAttribute('viewBox', vbAttr(box));
      store.set(box);
    }, { passive: false });
    svg.addEventListener('pointerdown', (e) => {
      if (e.button !== 0 && e.button !== 1) return;
      drag = { x: e.clientX, y: e.clientY, box: currentBox(svg), moved: false, id: e.pointerId };
    });
    svg.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const dx = e.clientX - drag.x;
      const dy = e.clientY - drag.y;
      if (!drag.moved && Math.hypot(dx, dy) < 5) return;
      if (!drag.moved) { drag.moved = true; svg.setPointerCapture(drag.id); svg.classList.add('panning'); }
      const m = svg.getScreenCTM();
      const scale = m ? m.a : 1;
      const box = { ...drag.box, x: drag.box.x - dx / scale, y: drag.box.y - dy / scale };
      svg.setAttribute('viewBox', vbAttr(box));
      store.set(box);
    });
    const end = () => {
      if (drag && drag.moved) suppressClick = true;
      drag = null;
      svg.classList.remove('panning');
    };
    svg.addEventListener('pointerup', end);
    svg.addEventListener('pointercancel', end);
    svg.addEventListener('click', (e) => {
      if (suppressClick) { e.stopPropagation(); e.preventDefault(); suppressClick = false; }
    }, true);
  }

  function zoomButton(svg, store, how, fit) {
    if (how === 'fit') {
      svg.setAttribute('viewBox', vbAttr(fit));
      store.set(null);
      return;
    }
    const box = currentBox(svg);
    const next = zoomBox(box, how === 'in' ? 1 / 1.3 : 1.3, box.x + box.w / 2, box.y + box.h / 2);
    svg.setAttribute('viewBox', vbAttr(next));
    store.set(next);
  }

  // ------------------------------------------------------------ editor

  const ICONS = {
    select: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M5 3l14 8-6 2-2 6L5 3z"/></svg>',
    wall: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M4 20V4h16"/></svg>',
    room: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-dasharray="3 2"><rect x="3" y="5" width="18" height="14" rx="3"/></svg>',
    desk: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><rect x="3" y="8" width="18" height="8" rx="2"/><path d="M7 16v3M17 16v3"/></svg>',
    entrance: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 4h8v16h-8"/><path d="M3 12h10M9 8l4 4-4 4"/></svg>',
    label: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M5 6h14M12 6v13"/></svg>',
    undo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 7L4 12l5 5"/><path d="M4 12h11a5 5 0 010 10h-2"/></svg>',
    redo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 7l5 5-5 5"/><path d="M20 12H9a5 5 0 000 10h2"/></svg>',
    auto: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="5" height="5" rx="1"/><rect x="10" y="4" width="5" height="5" rx="1"/><rect x="17" y="4" width="4" height="5" rx="1"/><rect x="3" y="13" width="5" height="5" rx="1"/><rect x="10" y="13" width="5" height="5" rx="1"/></svg>',
    area: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="16" rx="4" fill="currentColor" fill-opacity=".15"/><path d="M7 9h6" stroke-linecap="round"/></svg>',
    copy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5a1 1 0 00-1-1H5a1 1 0 00-1 1v10a1 1 0 001 1h3"/></svg>',
    expand: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/></svg>',
    collapse: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5"/></svg>',
  };

  const DEFAULT_SIZE = { desk: [150, 75], entrance: [125, 25], room: [300, 200], label: [150, 40], area: [250, 200] };

  /**
   * o: { doc: {pcs, objects}, pcs, look, t, dirty, sync(): {cls, text},
   *      onSave(doc) -> Promise, onExit(), onError(e), confirm(title, text, ok, fn), dialog(html, ok, fn), toast(text) }
   */
  function createEditor(root, o) {
    const t = o.t;
    const byName = new Map(o.pcs.map((pc) => [pc.name, pc]));
    const order = zoneOrder(o.pcs);
    const E = {
      pcs: JSON.parse(JSON.stringify((o.doc && o.doc.pcs) || {})),
      objects: JSON.parse(JSON.stringify((o.doc && o.doc.objects) || [])),
      tool: 'select',
      preset: PRESETS[0],
      panel: 'pcs',
      sel: new Set(),
      undo: [],
      redo: [],
      dirty: !!o.dirty,
      box: null,
      drag: null,
      saving: false,
      space: false,
      textBefore: null,
    };
    const TOOLS = ['select', 'area', 'wall', 'desk', 'entrance', 'label'];

    root.innerHTML = `<div class="ed">
      <div class="ed-top">
        <div class="ed-tools" role="toolbar">
          ${TOOLS.map((tool) =>
            `<button class="ed-tool" data-ed-tool="${tool}" title="${esc(t('edit.tool.' + tool + '.hint'))}">${ICONS[tool]}<span>${esc(t('edit.tool.' + tool))}</span></button>`).join('')}
        </div>
        <div class="ed-tools">
          <button class="ed-tool" data-ed="undo" title="${esc(t('edit.undo'))} (Ctrl+Z)">${ICONS.undo}</button>
          <button class="ed-tool" data-ed="redo" title="${esc(t('edit.redo'))} (Ctrl+Y)">${ICONS.redo}</button>
          <button class="ed-tool" data-ed="auto">${ICONS.auto}<span>${esc(t('edit.auto'))}</span></button>
          <button class="ed-tool" data-ed="fullscreen" title="${esc(t('edit.fullscreen'))}">${ICONS.expand}</button>
        </div>
        <span class="spacer"></span>
        <span class="ed-status" id="edStatus"></span>
        <button class="btn btn-secondary btn-sm" data-ed="exit">${esc(t('edit.exit'))}</button>
        <button class="btn btn-primary btn-sm" data-ed="save">${esc(t('edit.save'))}</button>
      </div>
      <div class="ed-body">
        <aside class="ed-tray">
          <div class="ed-tabs">
            <button data-ed-panel="pcs">${esc(t('edit.tab.pcs'))}</button>
            <button data-ed-panel="areas">${esc(t('edit.tab.areas'))}</button>
          </div>
          <div id="edTray"></div>
        </aside>
        <div class="ed-canvas">
          <svg class="ed-svg tool-select" preserveAspectRatio="xMidYMid meet">
            <defs><pattern id="edDots" width="${SNAP}" height="${SNAP}" patternUnits="userSpaceOnUse"><circle cx="0" cy="0" r="1.7"/></pattern></defs>
            <rect class="ed-floor" x="0" y="0" width="${GRID}" height="${GRID}" rx="6"/>
            <rect class="ed-dots" x="0" y="0" width="${GRID}" height="${GRID}" fill="url(#edDots)"/>
            <g class="ed-layer"></g>
          </svg>
          <div class="ed-tip" id="edTip" hidden></div>
          <div class="map-zoom">
            <button data-ed="zoom-in" title="${esc(t('map.zoomIn'))}">+</button>
            <button data-ed="zoom-out" title="${esc(t('map.zoomOut'))}">−</button>
            <button data-ed="zoom-fit" title="${esc(t('map.fit'))}">⤢</button>
          </div>
        </div>
        <aside class="ed-props" id="edProps"></aside>
      </div>
    </div>`;

    const svg = root.querySelector('.ed-svg');
    const layer = svg.querySelector('.ed-layer');
    const tray = root.querySelector('#edTray');
    const props = root.querySelector('#edProps');
    const status = root.querySelector('#edStatus');
    const tip = root.querySelector('#edTip');
    let raf = 0;

    // ---------- state helpers

    const snapshot = () => JSON.stringify({ pcs: E.pcs, objects: E.objects });
    const restore = (json) => { const s = JSON.parse(json); E.pcs = s.pcs; E.objects = s.objects; E.sel.clear(); };
    const findObj = (id) => E.objects.find((obj) => obj.id === id);
    const selected = (prefix) => [...E.sel].filter((k) => k.startsWith(prefix)).map((k) => k.slice(prefix.length));
    const presetName = (preset) => t(preset.kind === 'area' ? 'area.' + preset.id : 'edit.default.' + preset.kind);

    function commit(before) {
      if (before === snapshot()) return;
      E.undo.push(before);
      if (E.undo.length > MAX_UNDO) E.undo.shift();
      E.redo = [];
      E.dirty = true;
    }

    function change(fn) {
      const before = snapshot();
      fn();
      commit(before);
      draw();
      panels();
    }

    function newId(kind) {
      let i = E.objects.length + 1;
      while (findObj(`${kind}${i}`)) i++;
      return `${kind}${i}`;
    }

    const clampPc = (p) => ({ x: clamp(snap(p.x), PC_MIN, PC_MAX), y: clamp(snap(p.y), PC_MIN, PC_MAX) });

    function clampObj(obj) {
      obj.w = clamp(num(obj.w), 6, GRID);
      obj.h = clamp(num(obj.h), 6, GRID);
      obj.x = clamp(num(obj.x), 0, GRID - obj.w);
      obj.y = clamp(num(obj.y), 0, GRID - obj.h);
      return obj;
    }

    const inside = (obj, x, y) => x >= obj.x && x <= obj.x + obj.w && y >= obj.y && y <= obj.y + obj.h;

    function occupied(x, y) {
      for (const p of Object.values(E.pcs)) if (Math.abs(p.x - x) < PC_W && Math.abs(p.y - y) < PC_H) return true;
      for (const obj of E.objects) {
        if (BACKGROUND_KINDS.includes(obj.kind)) continue;
        if (x > obj.x - PC_W / 2 && x < obj.x + obj.w + PC_W / 2 && y > obj.y - PC_H / 2 && y < obj.y + obj.h + PC_H / 2) return true;
      }
      return false;
    }

    /** Next free grid spot, preferably inside `area` (an area object) when given. */
    function freeSpot(area) {
      const xs = area ? [snap(area.x + PC_W / 2 + 10), snap(area.x + area.w - PC_W / 2 - 10)] : [STEP, PC_MAX];
      const ys = area ? [snap(area.y + PC_H / 2 + 30), snap(area.y + area.h - PC_H / 2 - 10)] : [STEP, PC_MAX];
      for (let y = ys[0]; y <= ys[1]; y += STEP) {
        for (let x = xs[0]; x <= xs[1]; x += STEP) if (!occupied(x, y)) return { x, y };
      }
      return area ? freeSpot(null) : null;
    }

    const unplaced = () => o.pcs.filter((pc) => !E.pcs[pc.name]).sort((a, b) => a.number - b.number);
    const ghosts = () => Object.keys(E.pcs).filter((name) => !byName.has(name)).sort(naturalName);

    /**
     * Rows of `cols` PCs per zone below everything already drawn, or, for an area, its free spots
     * row by row (PCs already standing there are never covered).
     */
    function autoPlace(names, cols, area) {
      if (area) {
        // Scan the area on the 25-unit grid so gaps between PCs already standing there are used too.
        const x0 = snap(area.x + PC_W / 2 + 10);
        const x1 = Math.min(area.x + area.w - PC_W / 2, PC_MAX);
        const y1 = Math.min(area.y + area.h - PC_H / 2, PC_MAX);
        let count = 0;
        for (const name of names) {
          let spot = null;
          for (let y = snap(area.y + PC_H / 2 + 35); y <= y1 && !spot; y += SNAP) {
            for (let x = x0; x <= x1; x += SNAP) if (!occupied(x, y)) { spot = { x, y }; break; }
          }
          if (!spot) break;
          E.pcs[name] = spot;
          count++;
        }
        return count;
      }
      let y = STEP;
      const placed = Object.values(E.pcs);
      if (placed.length || E.objects.length) {
        const bottom = Math.max(...placed.map((p) => p.y + PC_H / 2), ...E.objects.map((obj) => obj.y + obj.h), 0);
        y = snap(bottom + STEP);
      }
      const zones = new Map();
      for (const name of names) {
        const zone = (byName.get(name) || {}).zone || '';
        if (!zones.has(zone)) zones.set(zone, []);
        zones.get(zone).push(name);
      }
      let count = 0;
      for (const [, list] of zones) {
        list.forEach((name, i) => {
          const py = y + Math.floor(i / cols) * STEP;
          const px = STEP + (i % cols) * STEP;
          if (py <= PC_MAX && px <= PC_MAX) { E.pcs[name] = { x: px, y: py }; count++; }
        });
        y += Math.ceil(list.length / cols) * STEP + 50;
      }
      return count;
    }

    // ---------- drawing

    function fit() {
      const frames = hasAreas(E.objects) ? [] : zoneFrames(E.pcs, byName, order);
      const box = bounds(E.pcs, E.objects, frames);
      const base = { x: 0, y: 0, w: GRID, h: 650 };
      if (!box) return padded(base, 20);
      const x = Math.min(box.x, base.x);
      const y = Math.min(box.y, base.y);
      return padded({ x, y, w: Math.max(box.x + box.w, base.w) - x, h: Math.max(box.y + box.h, base.h) - y }, 20);
    }

    function handles(obj) {
      const r = 9;
      const corners = obj.kind === 'wall'
        ? (obj.w >= obj.h ? [['w', obj.x, obj.y + obj.h / 2], ['e', obj.x + obj.w, obj.y + obj.h / 2]]
          : [['n', obj.x + obj.w / 2, obj.y], ['s', obj.x + obj.w / 2, obj.y + obj.h]])
        : [['nw', obj.x, obj.y], ['ne', obj.x + obj.w, obj.y], ['sw', obj.x, obj.y + obj.h], ['se', obj.x + obj.w, obj.y + obj.h]];
      return corners.map(([c, x, y]) =>
        `<rect class="ed-handle h-${c}" data-handle="${esc(obj.id)}" data-corner="${c}" x="${x - r}" y="${y - r}" width="${r * 2}" height="${r * 2}" rx="5"/>`).join('');
    }

    function draw() {
      svg.setAttribute('viewBox', vbAttr(E.box || fit()));
      const d = E.drag;
      const frames = hasAreas(E.objects) ? '' : zoneFrames(E.pcs, byName, order).map(frameSvg).join('');
      const objs = ordered(E.objects).map((obj) => objectSvg(obj, t, E.sel.has('obj:' + obj.id) ? ' sel' : '', true)).join('');
      // Selected PCs are drawn last, so one dropped onto another stays on top.
      const onTop = ([name]) => (E.sel.has('pc:' + name) ? 1 : 0);
      const pcs = Object.entries(E.pcs).sort((a, b) => onTop(a) - onTop(b) || naturalName(a[0], b[0])).map(([name, p]) =>
        pcSvg(name, p, byName.get(name), o.look, { editor: true, extra: (E.sel.has('pc:' + name) ? ' sel' : '') + (byName.has(name) ? '' : ' ghost') })).join('');
      let overlay = '';
      const one = selected('obj:');
      if (one.length === 1 && E.sel.size === 1 && (!d || d.type === 'resize')) {
        const obj = findObj(one[0]);
        if (obj) overlay += handles(obj);
      }
      if (d && d.type === 'band' && d.active) {
        const b = rect(d.a, d.b);
        overlay += `<rect class="ed-band" x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}"/>`;
      }
      if (d && d.type === 'draw') overlay += objectSvg(draft(d), t, ' draft');
      layer.innerHTML = frames + objs + pcs + overlay;
      svg.setAttribute('class', `ed-svg tool-${E.tool}${E.space ? ' pan' : ''}`);
      for (const b of root.querySelectorAll('[data-ed-tool]')) b.classList.toggle('on', b.dataset.edTool === E.tool);
      for (const b of root.querySelectorAll('[data-ed-panel]')) b.classList.toggle('on', b.dataset.edPanel === E.panel);
      root.querySelector('[data-ed="undo"]').disabled = !E.undo.length;
      root.querySelector('[data-ed="redo"]').disabled = !E.redo.length;
      root.querySelector('[data-ed="save"]').disabled = E.saving;
      const s = E.dirty ? { cls: 'warn', text: t('edit.unsavedShort') } : o.sync();
      status.className = 'ed-status ' + s.cls;
      status.textContent = s.text;
      const hint = E.tool === 'select' ? '' : t(E.tool === 'area' || E.tool === 'desk' || E.tool === 'entrance'
        ? 'edit.drawHint' : 'edit.tool.' + E.tool + '.hint', { name: presetName(E.preset) });
      tip.hidden = !hint;
      tip.textContent = hint;
    }

    const redraw = () => { if (!raf) raf = requestAnimationFrame(() => { raf = 0; draw(); }); };

    function rect(a, b) {
      return { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), w: Math.abs(a.x - b.x), h: Math.abs(a.y - b.y) };
    }

    /** The object being drawn: walls are straight 12-unit lines, everything else a box (a click gives the preset size). */
    function draft(d) {
      const kind = E.tool;
      const r = rect(d.a, d.b);
      if (kind === 'wall') {
        const horizontal = Math.abs(d.b.x - d.a.x) >= Math.abs(d.b.y - d.a.y);
        return clampObj(horizontal
          ? { id: '_', kind, x: r.x, y: d.a.y - 6, w: Math.max(SNAP, r.w), h: 12 }
          : { id: '_', kind, x: d.a.x - 6, y: r.y, w: 12, h: Math.max(SNAP, r.h) });
      }
      const preset = kind === E.preset.kind ? E.preset : null;
      const extra = kind === 'area' ? { color: (preset && preset.color) || 'blue', text: presetName(preset || PRESETS[0]) }
        : kind === 'label' ? {} : { text: preset ? presetName(preset) : null };
      if (r.w < SNAP || r.h < SNAP) {
        const [w, h] = preset ? [preset.w, preset.h] : DEFAULT_SIZE[kind];
        return clampObj({ id: '_', kind, x: d.a.x, y: d.a.y, w, h, ...extra });
      }
      return clampObj({ id: '_', kind, x: r.x, y: r.y, w: r.w, h: r.h, ...extra });
    }

    function trayHtml() {
      if (E.panel === 'areas') {
        return `<p class="small muted">${esc(t('edit.areasHint'))}</p>
          <div class="presets">${PRESETS.map((preset) => {
            const on = (E.tool === preset.kind || (E.tool === 'area' && preset.kind === 'area')) && E.preset.id === preset.id;
            return `<button class="preset${on ? ' on' : ''}" data-preset="${preset.id}">
              <span class="pv ${preset.kind === 'area' ? 'c-' + preset.color : preset.kind}"></span>
              <span>${esc(presetName(preset))}</span></button>`;
          }).join('')}</div>`;
      }
      const list = unplaced();
      const groups = new Map();
      for (const pc of list) {
        if (!groups.has(pc.zone || '')) groups.set(pc.zone || '', []);
        groups.get(pc.zone || '').push(pc);
      }
      return `<h3>${esc(t('edit.tray'))} <span class="count">${list.length}</span></h3>
        ${list.length === 0 ? `<p class="small muted">${esc(t('edit.trayEmpty'))}</p>` : `<p class="small muted">${esc(t('edit.trayHint'))}</p>`}
        ${[...groups.entries()].map(([zone, pcs]) => `
          <div class="tray-zone"><span class="sw" data-color="${esc(zoneColor(order, zone))}"></span>${esc(zone || '—')}</div>
          <div class="tray-pcs">${pcs.map((pc) => `<button class="tray-pc ${esc(pc.state)}" data-tray="${esc(pc.name)}" title="${esc(pc.zone)}">${esc(pc.number)}</button>`).join('')}</div>`).join('')}`;
    }

    function panels() {
      tray.innerHTML = trayHtml();
      for (const sw of tray.querySelectorAll('.sw')) sw.style.background = sw.dataset.color;

      const pcs = selected('pc:');
      const objs = selected('obj:').map(findObj).filter(Boolean);
      if (!E.sel.size) {
        const gh = ghosts();
        const placed = o.pcs.length - unplaced().length;
        props.innerHTML = `<h3>${esc(t('edit.props'))}</h3>
          <div class="ed-stat"><b>${placed}</b> / ${o.pcs.length} <span>${esc(t('edit.placed'))}</span></div>
          ${gh.length ? `<div class="ed-ghosts"><b>${esc(t('edit.ghost'))}:</b> ${esc(gh.join(', '))}
            <button class="btn btn-danger-ghost btn-sm" data-ed="remove-ghosts">${esc(t('edit.removeGhosts'))}</button></div>` : ''}
          <ol class="ed-steps">
            <li>${esc(t('edit.step1'))}</li><li>${esc(t('edit.step2'))}</li><li>${esc(t('edit.step3'))}</li>
          </ol>
          <ul class="ed-hints">
            <li>${esc(t('edit.hint.area'))}</li><li>${esc(t('edit.hint.select'))}</li>
            <li>${esc(t('edit.hint.keys'))}</li><li>${esc(t('edit.hint.zoom'))}</li>
          </ul>
          ${Object.keys(E.pcs).length || E.objects.length ? `<button class="btn btn-danger-ghost btn-sm" data-ed="clear">${esc(t('edit.clear'))}</button>` : ''}`;
        return;
      }
      if (objs.length === 1 && !pcs.length) {
        const obj = objs[0];
        const inArea = obj.kind === 'area' ? Object.values(E.pcs).filter((p) => inside(obj, p.x, p.y)).length : 0;
        props.innerHTML = `<h3>${esc(t('edit.tool.' + (obj.kind === 'room' ? 'area' : obj.kind)))}</h3>
          ${TEXT_KINDS.includes(obj.kind) ? `<div class="field"><label for="edText">${esc(t(obj.kind === 'area' ? 'edit.areaName' : 'edit.text'))}</label>
            <input class="input" id="edText" maxlength="40" spellcheck="false" placeholder="${esc(obj.kind === 'label' ? '' : t('edit.default.' + obj.kind))}"></div>` : ''}
          ${obj.kind === 'area' ? `<div class="field"><label>${esc(t('edit.color'))}</label>
            <div class="swatches">${Object.keys(AREA_COLORS).map((c) =>
              `<button class="swatch c-${c}${obj.color === c ? ' on' : ''}" data-ed-color="${c}" title="${c}"></button>`).join('')}</div></div>
            <p class="small muted">${esc(t('edit.inArea', { n: inArea }))}</p>
            ${unplaced().length ? `<button class="btn btn-secondary btn-sm" data-ed="fill-area">${esc(t('edit.fillArea'))}</button>` : ''}` : ''}
          <p class="small muted">${esc(t('edit.size', { w: obj.w, h: obj.h }))}</p>
          <div class="btn-row">
            <button class="btn btn-secondary btn-sm" data-ed="duplicate">${ICONS.copy}${esc(t('edit.duplicate'))}</button>
            <button class="btn btn-danger-ghost btn-sm" data-ed="delete">${esc(t('edit.delete'))}</button>
          </div>`;
        const input = props.querySelector('#edText');
        if (input) input.value = obj.text || '';
        return;
      }
      props.innerHTML = `<h3>${esc(t('edit.selected', { n: E.sel.size }))}</h3>
        ${pcs.length >= 2 ? `<div class="btn-row">
          <button class="btn btn-secondary btn-sm" data-ed="align-row">${esc(t('edit.alignRow'))}</button>
          <button class="btn btn-secondary btn-sm" data-ed="align-col">${esc(t('edit.alignCol'))}</button></div>` : ''}
        ${objs.length ? `<button class="btn btn-secondary btn-sm" data-ed="duplicate">${ICONS.copy}${esc(t('edit.duplicate'))}</button>` : ''}
        <button class="btn btn-danger-ghost btn-sm" data-ed="delete">${esc(pcs.length && !objs.length ? t('edit.removePcs') : t('edit.delete'))}</button>
        <p class="small muted">${esc(t('edit.hint.keys'))}</p>`;
    }

    // ---------- actions

    function setTool(tool) {
      E.tool = tool;
      if (tool === 'area') {
        E.panel = 'areas';
        if (E.preset.kind !== 'area') E.preset = PRESETS[0];
      }
      if (tool !== 'select') E.sel.clear();
      draw();
      panels();
    }

    function pickPreset(id) {
      const preset = PRESETS.find((p) => p.id === id);
      if (!preset) return;
      E.preset = preset;
      E.tool = preset.kind;
      E.sel.clear();
      draw();
      panels();
    }

    function removeSelection() {
      if (!E.sel.size) return;
      change(() => {
        for (const name of selected('pc:')) delete E.pcs[name];
        const ids = new Set(selected('obj:'));
        E.objects = E.objects.filter((obj) => !ids.has(obj.id));
        E.sel.clear();
      });
    }

    function duplicate() {
      const objs = selected('obj:').map(findObj).filter(Boolean);
      if (!objs.length) return;
      change(() => {
        E.sel.clear();
        for (const obj of objs) {
          const copy = clampObj({ ...obj, id: newId(obj.kind), x: obj.x + SNAP * 2, y: obj.y + SNAP * 2 });
          E.objects.push(copy);
          E.sel.add('obj:' + copy.id);
        }
      });
    }

    function nudge(dx, dy) {
      if (!E.sel.size) return;
      change(() => {
        for (const name of selected('pc:')) if (E.pcs[name]) E.pcs[name] = clampPc({ x: E.pcs[name].x + dx, y: E.pcs[name].y + dy });
        for (const id of selected('obj:')) { const obj = findObj(id); if (obj) clampObj(Object.assign(obj, { x: obj.x + dx, y: obj.y + dy })); }
      });
    }

    function align(axis) {
      const names = selected('pc:').filter((name) => E.pcs[name]);
      if (names.length < 2) return;
      change(() => {
        names.sort((a, b) => (axis === 'row' ? E.pcs[a].x - E.pcs[b].x : E.pcs[a].y - E.pcs[b].y));
        const first = E.pcs[names[0]];
        names.forEach((name, i) => {
          E.pcs[name] = axis === 'row'
            ? clampPc({ x: first.x + i * STEP, y: first.y })
            : clampPc({ x: first.x, y: first.y + i * STEP });
        });
      });
    }

    function placeFromTray(name, point) {
      const area = point ? null : findObj(selected('obj:')[0]);
      const spot = point ? clampPc(point) : freeSpot(area && area.kind === 'area' ? area : null);
      if (!spot) { o.toast(t('edit.noSpace')); return; }
      change(() => { E.pcs[name] = spot; E.sel = new Set(['pc:' + name]); });
    }

    function autoDialog(area) {
      const list = unplaced();
      if (!list.length) { o.toast(t('edit.trayEmpty')); return; }
      const zones = zoneOrder(list);
      o.dialog(`<h2>${esc(t('edit.autoTitle'))}</h2><p>${esc(area ? t('edit.autoTextArea', { name: area.text || '' }) : t('edit.autoText'))}</p>
        <div class="form">
          <div class="field"><label for="autoZone">${esc(t('edit.autoZone'))}</label>
            <select class="input" id="autoZone"><option value="*">${esc(t('edit.autoAll', { n: list.length }))}</option>
              ${zones.map((z) => `<option value="${esc(z)}">${esc(z || '—')} (${list.filter((pc) => (pc.zone || '') === z).length})</option>`).join('')}</select></div>
          ${area ? '' : `<div class="field"><label for="autoCols">${esc(t('edit.autoCols'))}</label>
            <input class="input" id="autoCols" type="number" min="1" max="9" value="6"></div>`}
        </div>`, t('edit.autoRun'), (dialog) => {
        const zone = dialog.querySelector('#autoZone').value;
        const colsInput = dialog.querySelector('#autoCols');
        const cols = colsInput ? clamp(num(colsInput.value) || 6, 1, 9) : 9;
        const names = list.filter((pc) => zone === '*' || (pc.zone || '') === zone).map((pc) => pc.name);
        let count = 0;
        change(() => { count = autoPlace(names, cols, area); if (!area) E.box = null; });
        if (count < names.length) o.toast(t('edit.noSpace'));
      });
    }

    async function save() {
      if (E.saving) return;
      E.saving = true;
      draw();
      try {
        await o.onSave({ pcs: E.pcs, objects: E.objects });
        E.dirty = false;
      } catch (error) {
        o.onError(error);
      } finally {
        E.saving = false;
        draw();
      }
    }

    function exit() {
      if (E.dirty) o.confirm(t('edit.unsavedTitle'), t('edit.unsaved'), t('edit.leave'), () => { E.dirty = false; o.onExit(); });
      else o.onExit();
    }

    function zoom(how) {
      if (how === 'fit') { E.box = null; draw(); return; }
      const box = currentBox(svg);
      E.box = zoomBox(box, how === 'in' ? 1 / 1.3 : 1.3, box.x + box.w / 2, box.y + box.h / 2);
      draw();
    }

    function toggleFullscreen() {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else if (document.documentElement.requestFullscreen) document.documentElement.requestFullscreen().catch(() => {});
    }

    const buttons = {
      undo: () => { if (E.undo.length) { E.redo.push(snapshot()); restore(E.undo.pop()); E.dirty = true; draw(); panels(); } },
      redo: () => { if (E.redo.length) { E.undo.push(snapshot()); restore(E.redo.pop()); E.dirty = true; draw(); panels(); } },
      auto: () => autoDialog(null),
      'fill-area': () => autoDialog(findObj(selected('obj:')[0])),
      fullscreen: toggleFullscreen,
      save,
      exit,
      delete: removeSelection,
      duplicate,
      'align-row': () => align('row'),
      'align-col': () => align('col'),
      'remove-ghosts': () => change(() => { for (const name of ghosts()) delete E.pcs[name]; }),
      clear: () => o.confirm(t('edit.clear'), t('edit.clearConfirm'), t('edit.clear'), () => change(() => { E.pcs = {}; E.objects = []; E.sel.clear(); })),
      'zoom-in': () => zoom('in'),
      'zoom-out': () => zoom('out'),
      'zoom-fit': () => zoom('fit'),
    };

    // ---------- events

    const onClick = (e) => {
      const tool = e.target.closest('[data-ed-tool]');
      if (tool) { setTool(tool.dataset.edTool); return; }
      const panel = e.target.closest('[data-ed-panel]');
      if (panel) { E.panel = panel.dataset.edPanel; draw(); panels(); return; }
      const preset = e.target.closest('[data-preset]');
      if (preset) { pickPreset(preset.dataset.preset); return; }
      const color = e.target.closest('[data-ed-color]');
      if (color) {
        const obj = findObj(selected('obj:')[0]);
        if (obj) change(() => { obj.color = color.dataset.edColor; });
        return;
      }
      const b = e.target.closest('[data-ed]');
      if (b && !b.disabled && buttons[b.dataset.ed]) buttons[b.dataset.ed]();
    };
    root.addEventListener('click', onClick);

    props.addEventListener('focusin', (e) => { if (e.target.id === 'edText') E.textBefore = snapshot(); });
    props.addEventListener('focusout', (e) => { if (e.target.id === 'edText' && E.textBefore) { commit(E.textBefore); E.textBefore = null; draw(); } });
    props.addEventListener('input', (e) => {
      if (e.target.id !== 'edText') return;
      const obj = findObj(selected('obj:')[0]);
      if (!obj) return;
      obj.text = e.target.value.slice(0, 40) || null;
      E.dirty = true;
      redraw();
    });

    const toSvg = (e) => svgPoint(layer, e.clientX, e.clientY);

    svg.addEventListener('wheel', (e) => {
      e.preventDefault();
      const p = toSvg(e);
      E.box = zoomBox(currentBox(svg), e.deltaY > 0 ? 1.15 : 1 / 1.15, p.x, p.y);
      draw();
    }, { passive: false });

    svg.addEventListener('dblclick', (e) => {
      const hit = e.target.closest('[data-key^="obj:"]');
      if (!hit) return;
      E.sel = new Set([hit.dataset.key]);
      draw();
      panels();
      const input = props.querySelector('#edText');
      if (input) { input.focus(); input.select(); }
    });

    /** Start moving the selection; dragging one area carries the PCs and things standing inside it. */
    function startMove(p) {
      const orig = {};
      for (const k of E.sel) {
        if (k.startsWith('pc:') && E.pcs[k.slice(3)]) orig[k] = { ...E.pcs[k.slice(3)] };
        else if (k.startsWith('obj:') && findObj(k.slice(4))) orig[k] = { ...findObj(k.slice(4)) };
      }
      const areas = selected('obj:').map(findObj).filter((obj) => obj && obj.kind === 'area');
      if (areas.length === 1 && E.sel.size === 1) {
        const area = areas[0];
        for (const [name, pt] of Object.entries(E.pcs)) if (inside(area, pt.x, pt.y)) orig['pc:' + name] = { ...pt };
        for (const obj of E.objects) {
          if (obj !== area && obj.kind !== 'area' && inside(area, obj.x + obj.w / 2, obj.y + obj.h / 2)) orig['obj:' + obj.id] = { ...obj };
        }
      }
      E.drag = { type: 'move', p0: p, orig, before: snapshot() };
    }

    svg.addEventListener('pointerdown', (e) => {
      if (e.button === 2) return;
      const p = toSvg(e);
      svg.setPointerCapture(e.pointerId);
      if (e.button === 1 || E.space) {
        E.drag = { type: 'pan', x: e.clientX, y: e.clientY, box: currentBox(svg) };
        return;
      }
      if (E.tool !== 'select') {
        const a = { x: snap(p.x), y: snap(p.y) };
        E.drag = { type: 'draw', a, b: a };
        redraw();
        return;
      }
      const handle = e.target.closest('[data-handle]');
      if (handle) {
        const obj = findObj(handle.dataset.handle);
        if (obj) E.drag = { type: 'resize', obj, corner: handle.dataset.corner, orig: { ...obj }, p0: p, before: snapshot() };
        return;
      }
      const hit = e.target.closest('[data-key]');
      const key = hit && hit.dataset.key;
      const background = key && key.startsWith('obj:') && BACKGROUND_KINDS.includes((findObj(key.slice(4)) || {}).kind);
      if (hit && !(background && !E.sel.has(key))) {
        if (e.shiftKey || e.ctrlKey) { if (E.sel.has(key)) E.sel.delete(key); else E.sel.add(key); }
        else if (!E.sel.has(key)) E.sel = new Set([key]);
        startMove(p);
        draw();
        panels();
        return;
      }
      // Empty floor, or an area that is not selected yet: a rubber band; a plain click selects the area.
      if (!e.shiftKey) E.sel.clear();
      E.drag = { type: 'band', a: p, b: p, base: new Set(E.sel), area: background ? key : null, active: false };
      draw();
      panels();
    });

    svg.addEventListener('pointermove', (e) => {
      const d = E.drag;
      if (!d) return;
      if (d.type === 'pan') {
        const m = svg.getScreenCTM();
        const scale = m ? m.a : 1;
        E.box = { ...d.box, x: d.box.x - (e.clientX - d.x) / scale, y: d.box.y - (e.clientY - d.y) / scale };
        redraw();
        return;
      }
      const p = toSvg(e);
      if (d.type === 'draw') { d.b = { x: snap(p.x), y: snap(p.y) }; redraw(); return; }
      if (d.type === 'band') {
        d.b = p;
        if (!d.active && Math.hypot(p.x - d.a.x, p.y - d.a.y) > 8) d.active = true;
        redraw();
        return;
      }
      if (d.type === 'resize') {
        resize(d, snap(p.x - d.p0.x), snap(p.y - d.p0.y));
        redraw();
        return;
      }
      if (d.type === 'move') {
        const dx = snap(p.x - d.p0.x);
        const dy = snap(p.y - d.p0.y);
        for (const [k, orig] of Object.entries(d.orig)) {
          if (k.startsWith('pc:')) E.pcs[k.slice(3)] = clampPc({ x: orig.x + dx, y: orig.y + dy });
          else { const obj = findObj(k.slice(4)); if (obj) clampObj(Object.assign(obj, { x: orig.x + dx, y: orig.y + dy })); }
        }
        redraw();
      }
    });

    /** Corner handles; a wall only grows along its length. */
    function resize(d, dx, dy) {
      const o0 = d.orig;
      const min = d.obj.kind === 'wall' ? 12 : SNAP;
      let { x, y, w, h } = o0;
      if (d.corner.includes('w')) { x = Math.min(o0.x + dx, o0.x + o0.w - min); w = o0.x + o0.w - x; }
      if (d.corner.includes('e')) w = Math.max(min, o0.w + dx);
      if (d.corner.includes('n')) { y = Math.min(o0.y + dy, o0.y + o0.h - min); h = o0.y + o0.h - y; }
      if (d.corner.includes('s')) h = Math.max(min, o0.h + dy);
      Object.assign(d.obj, { x: Math.max(0, x), y: Math.max(0, y), w, h });
      clampObj(d.obj);
    }

    const endDrag = () => {
      const d = E.drag;
      E.drag = null;
      if (!d) return;
      if (d.type === 'move' || d.type === 'resize') commit(d.before);
      if (d.type === 'draw') {
        const obj = draft(d);
        const before = snapshot();
        obj.id = newId(E.tool);
        if (E.tool === 'label') obj.text = null;
        E.objects.push(obj);
        commit(before);
        E.sel = new Set(['obj:' + obj.id]);
        if (E.tool !== 'wall') E.tool = 'select';
      }
      if (d.type === 'band') {
        E.sel = new Set(d.base);
        if (d.active) {
          const r = rect(d.a, d.b);
          const within = (x, y) => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h;
          for (const [name, p] of Object.entries(E.pcs)) if (within(p.x, p.y)) E.sel.add('pc:' + name);
          // Objects only when wholly inside, so banding PCs in a hall does not grab the hall itself.
          for (const obj of E.objects) if (within(obj.x, obj.y) && within(obj.x + obj.w, obj.y + obj.h)) E.sel.add('obj:' + obj.id);
        } else if (d.area) {
          E.sel.add(d.area);
        }
      }
      draw();
      panels();
      const text = props.querySelector('#edText');
      if (d.type === 'draw' && text && E.objects.length && findObj(selected('obj:')[0]).kind === 'label') text.focus();
    };
    svg.addEventListener('pointerup', endDrag);
    svg.addEventListener('pointercancel', endDrag);

    // Tray: drag a PC onto the floor, or click it to put it on the next free spot (inside the selected area).
    tray.addEventListener('pointerdown', (e) => {
      const chip = e.target.closest('[data-tray]');
      if (!chip || e.button !== 0) return;
      e.preventDefault();
      const name = chip.dataset.tray;
      const start = { x: e.clientX, y: e.clientY };
      let ghost = null;
      const move = (ev) => {
        if (!ghost && Math.hypot(ev.clientX - start.x, ev.clientY - start.y) < 5) return;
        if (!ghost) {
          ghost = document.createElement('div');
          ghost.className = 'tray-ghost';
          ghost.textContent = chip.textContent;
          document.body.appendChild(ghost);
        }
        ghost.style.left = `${ev.clientX}px`;
        ghost.style.top = `${ev.clientY}px`;
      };
      const up = (ev) => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        if (!ghost) { placeFromTray(name, null); return; }
        ghost.remove();
        const r = svg.getBoundingClientRect();
        if (ev.clientX >= r.left && ev.clientX <= r.right && ev.clientY >= r.top && ev.clientY <= r.bottom) {
          placeFromTray(name, toSvg(ev));
        }
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
    });

    const typing = (e) => ['INPUT', 'SELECT', 'TEXTAREA'].includes(e.target.tagName);
    const onKeyDown = (e) => {
      if (document.getElementById('modal') && !document.getElementById('modal').hidden) return;
      const ctrl = e.ctrlKey || e.metaKey;
      if (ctrl && e.key.toLowerCase() === 's') { e.preventDefault(); save(); return; }
      if (typing(e)) return;
      if (ctrl && e.key.toLowerCase() === 'z') { e.preventDefault(); buttons[e.shiftKey ? 'redo' : 'undo'](); return; }
      if (ctrl && e.key.toLowerCase() === 'y') { e.preventDefault(); buttons.redo(); return; }
      if (ctrl && e.key.toLowerCase() === 'd') { e.preventDefault(); duplicate(); return; }
      if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); removeSelection(); return; }
      if (e.key === 'Escape') { E.sel.clear(); E.tool = 'select'; draw(); panels(); return; }
      if (e.key === ' ' && !E.space) { e.preventDefault(); E.space = true; draw(); return; }
      const arrows = { ArrowLeft: [-SNAP, 0], ArrowRight: [SNAP, 0], ArrowUp: [0, -SNAP], ArrowDown: [0, SNAP] };
      if (arrows[e.key]) { e.preventDefault(); nudge(...arrows[e.key]); }
    };
    const onKeyUp = (e) => { if (e.key === ' ') { E.space = false; draw(); } };
    const onBeforeUnload = (e) => { if (E.dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('beforeunload', onBeforeUnload);

    draw();
    panels();

    return {
      destroy() {
        window.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('keyup', onKeyUp);
        window.removeEventListener('beforeunload', onBeforeUnload);
        root.removeEventListener('click', onClick);
      },
      state: () => ({ doc: { pcs: E.pcs, objects: E.objects }, dirty: E.dirty }),
      refreshStatus: () => draw(),
    };
  }

  const tileIcon = (name) => TILE_ICONS[name] || '';

  return { viewSvg, patchView, attachViewer, zoomButton, fitBox, viewFit, tileIcon, placedNames, createEditor };
})();
