// Manajoy GameClass — «Bar»: the owner's stock book for GameClass's bar (vanilla JS).
// GameClass has a price list and a sales log but no stock. The owner writes counts, deliveries and write-offs here
// (PIN, the same lock as «Hisobot»); every GameClass sale is subtracted automatically. GameClass is only read.
'use strict';

(() => {
  const QUICK = [6, 12, 24, 48];

  function create(ctx) {
    const { t, esc, fmtMoney, toast, errorText, dialogModal, lock } = ctx;
    const B = { overview: null, loading: false, error: null, filter: 'all', q: '' };

    const api = (method, path, body) => ctx.api(method, path, body, lock.token() ? { 'X-Analytics-Token': lock.token() } : {});
    const num = (v) => Number(v || 0).toLocaleString('ru-RU');
    const pad = (n) => String(n).padStart(2, '0');
    const when = (value) => {
      if (!value) return '—';
      const d = new Date(value);
      return `${pad(d.getDate())}.${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
    };
    const money = (v) => fmtMoney(v, B.overview && B.overview.currency);

    // ------------------------------------------------------------ loading

    async function load() {
      B.loading = true;
      B.error = null;
      update();
      try {
        B.overview = await api('GET', 'bar');
      } catch (error) {
        if (!lock.expired(error)) B.error = error;
      }
      B.loading = false;
      update();
    }

    function enter() {
      lock.ensure();
      if (lock.unlocked() && !B.overview && !B.loading) load();
      update();
    }

    lock.onChange(() => {
      if (!document.querySelector('[data-bar="root"]')) return;
      if (lock.unlocked() && !B.overview && !B.loading) load();
      else if (!lock.unlocked()) B.overview = null;
      update();
    });

    function update() {
      const el = document.querySelector('[data-bar="root"]');
      if (!el) return;
      const search = document.activeElement && document.activeElement.id === 'barSearch';
      el.innerHTML = body();
      if (search) {
        const input = document.getElementById('barSearch');
        input.focus();
        input.setSelectionRange(input.value.length, input.value.length);
      }
    }

    // ------------------------------------------------------------ page

    const render = () => `<div class="bar-page" data-bar="root">${body()}</div>`;

    function body() {
      const head = `<div class="page-head"><h1>${esc(t('bar.title'))}</h1>
          ${B.loading ? '<span class="spinner dark"></span>' : ''}
          <span class="spacer"></span>
          ${lock.unlocked() ? `<button class="btn btn-secondary btn-sm" data-act="bar-refresh" ${B.loading ? 'disabled' : ''}>${ctx.icon.refresh}<span class="lbl">${esc(t('prices.refresh'))}</span></button>` : ''}
        </div>
        <p class="page-lead">${esc(t('bar.lead'))}</p>`;
      if (!lock.unlocked()) return `${head}<div class="rp">${lock.html()}</div>`;
      if (B.error && !B.overview) return `${head}${errorNote(B.error)}`;
      if (!B.overview) return `${head}<div class="skel skel-block"></div>`;
      return `${head}<div class="rp-push">${lock.pushLine()}</div>${B.error ? errorNote(B.error) : ''}${summary()}${list()}`;
    }

    function errorNote(error) {
      const e = errorText(error);
      return `<div class="alert fail"><div class="grow"><b>${esc(e.message)}</b>${e.detail ? `<div class="small muted">${esc(e.detail)}</div>` : ''}</div>
        <button class="btn btn-secondary btn-xs" data-act="bar-refresh">${esc(t('rp.retry'))}</button></div>`;
    }

    function summary() {
      const o = B.overview;
      const chip = (id, label, n, cls) => `<button class="chip${B.filter === id ? ' on' : ''}${cls ? ' ' + cls : ''}" data-act="bar-filter" data-filter="${id}">${esc(label)}<b>${n}</b></button>`;
      const untracked = o.items.filter((i) => !i.tracked).length;
      return `<div class="bar-sum">
          <div class="bar-kpi"><span>${esc(t('bar.tracked'))}</span><b>${num(o.tracked)}</b></div>
          <div class="bar-kpi warn"><span>${esc(t('bar.low'))}</span><b>${num(o.low)}</b></div>
          <div class="bar-kpi fail"><span>${esc(t('bar.out'))}</span><b>${num(o.out)}</b></div>
        </div>
        <div class="bar-tools">
          <div class="bar-chips">
            ${chip('all', t('bar.f.all'), o.items.length)}
            ${chip('alert', t('bar.f.alert'), o.low + o.out, o.low + o.out ? 'warn' : '')}
            ${chip('untracked', t('bar.f.untracked'), untracked)}
          </div>
          <input class="input bar-search" id="barSearch" type="search" placeholder="${esc(t('bar.search'))}" value="${esc(B.q)}">
        </div>
        ${o.tracked === 0 ? `<div class="alert info"><div class="grow"><b>${esc(t('bar.startTitle'))}</b><div class="small">${esc(t('bar.startText'))}</div></div>
          <button class="btn btn-secondary btn-xs" data-act="bar-restore">${esc(t('bar.restore'))}</button></div>` : ''}`;
    }

    function visible() {
      const q = B.q.toLowerCase();
      return B.overview.items.filter((i) => {
        if (q && !i.name.toLowerCase().includes(q)) return false;
        if (B.filter === 'alert') return ['low', 'out', 'negative'].includes(i.state);
        if (B.filter === 'untracked') return !i.tracked;
        return true;
      });
    }

    function list() {
      const items = visible();
      if (!items.length) return `<p class="muted rp-empty">${esc(t('bar.empty'))}</p>`;
      return `<div class="bar-list">${items.map(row).join('')}</div>`;
    }

    function stateBadge(i) {
      if (!i.tracked) return `<span class="bar-badge none">${esc(t('bar.s.untracked'))}</span>`;
      return `<span class="bar-badge ${i.state}">${esc(t('bar.s.' + i.state))}</span>`;
    }

    function row(i) {
      const remaining = i.tracked
        ? `<div class="bar-rem ${i.state}"><b>${num(i.remaining)}</b><span>${esc(t('bar.pcs'))}</span></div>`
        : '<div class="bar-rem none"><b>—</b></div>';
      const meta = [
        `${t('bar.soldToday')}: ${num(i.soldToday)}`,
        `${t('bar.sold7')}: ${num(i.sold7Days)}`,
        i.tracked && i.lowThreshold ? `${t('bar.threshold')}: ${num(i.lowThreshold)}` : '',
        i.lastReceiveAt ? t('bar.lastReceive', { n: num(i.lastReceiveQty), t: when(i.lastReceiveAt) }) : '',
      ].filter(Boolean).join(' · ');
      const actions = i.tracked
        ? `<button class="btn btn-primary btn-xs" data-act="bar-receive" data-id="${i.itemId}">${esc(t('bar.receive'))}</button>
           <button class="btn btn-secondary btn-xs bar-more" data-act="bar-more" data-id="${i.itemId}" aria-label="${esc(t('bar.more'))}">⋯</button>`
        : `<button class="btn btn-secondary btn-xs" data-act="bar-start" data-id="${i.itemId}">${esc(t('bar.start'))}</button>`;
      return `<div class="bar-row ${i.tracked ? i.state : 'untracked'}">
          <div class="bar-name"><b>${esc(i.name)}</b><span class="muted">${esc(money(i.price))}</span>${stateBadge(i)}</div>
          ${remaining}
          <div class="bar-meta small muted">${esc(meta)}${i.state === 'negative' ? `<div class="bar-neg">${esc(t('bar.negativeHint'))}</div>` : ''}</div>
          <div class="bar-acts">${actions}</div>
        </div>`;
    }

    const item = (id) => B.overview && B.overview.items.find((i) => i.itemId === Number(id));

    // ------------------------------------------------------------ modals

    function qtyModal(i, kind) {
      const counting = kind === 'count';
      const title = t(counting ? (i.tracked ? 'bar.countTitle' : 'bar.startTitleItem') : kind === 'receive' ? 'bar.receiveTitle' : 'bar.writeoffTitle', { name: i.name });
      const quick = kind === 'receive' ? `<div class="bar-quick">${QUICK.map((n) => `<button type="button" class="chip" data-quick="${n}">+${n}</button>`).join('')}</div>` : '';
      const help = counting
        ? (i.tracked ? t('bar.countHelp', { n: num(i.remaining) }) : t('bar.startHelp'))
        : t(kind === 'receive' ? 'bar.receiveHelp' : 'bar.writeoffHelp');
      dialogModal(`<h2>${esc(title)}</h2>
        <div class="form">
          <div class="field"><label for="barQty">${esc(t(counting ? 'bar.qtyNow' : 'bar.qty'))}</label>
            <input class="input bar-qty" id="barQty" type="number" inputmode="numeric" min="${counting ? 0 : 1}" max="100000" step="1">
            ${quick}
            <div class="help" id="barQtyHelp">${esc(help)}</div></div>
          <div class="field"><label for="barNote">${esc(t('bar.note'))}</label>
            <input class="input" id="barNote" maxlength="120" placeholder="${esc(t(kind === 'writeoff' ? 'bar.notePhWriteoff' : 'bar.notePh'))}"></div>
        </div>`, t('bar.save'), async (modal) => {
        const qty = Number(modal.querySelector('#barQty').value);
        if (!Number.isInteger(qty) || qty < (counting ? 0 : 1) || qty > 100000) { toast(t('error.bar_qty')); return; }
        await save('POST', 'bar/entry', { itemId: i.itemId, kind, qty, note: modal.querySelector('#barNote').value || null },
          t(counting ? 'bar.countSaved' : kind === 'receive' ? 'bar.receiveSaved' : 'bar.writeoffSaved', { name: i.name, n: num(qty) }));
      });
      const input = document.getElementById('barQty');
      if (input) {
        input.focus();
        if (counting && i.tracked) {
          input.addEventListener('input', () => {
            const value = Number(input.value);
            const helpEl = document.getElementById('barQtyHelp');
            if (!helpEl) return;
            helpEl.textContent = input.value === '' ? help : t('bar.countDiff', { a: num(i.remaining), b: num(value), d: `${value - i.remaining > 0 ? '+' : ''}${num(value - i.remaining)}` });
          });
        }
      }
      for (const btn of document.querySelectorAll('[data-quick]')) {
        btn.addEventListener('click', () => { input.value = String(Number(input.value || 0) + Number(btn.dataset.quick)); input.focus(); });
      }
    }

    function thresholdModal(i) {
      dialogModal(`<h2>${esc(t('bar.thresholdTitle', { name: i.name }))}</h2>
        <div class="form"><div class="field"><label for="barLow">${esc(t('bar.threshold'))}</label>
          <input class="input bar-qty" id="barLow" type="number" inputmode="numeric" min="0" max="100000" value="${i.lowThreshold || ''}">
          <div class="help">${esc(t('bar.thresholdHelp'))}</div></div></div>`, t('bar.save'), async (modal) => {
        const low = Number(modal.querySelector('#barLow').value || 0);
        if (!Number.isInteger(low) || low < 0) { toast(t('error.bar_qty')); return; }
        await save('PUT', 'bar/item', { itemId: i.itemId, lowThreshold: low }, t('bar.thresholdSaved'));
      });
    }

    function moreModal(i) {
      dialogModal(`<h2>${esc(i.name)}</h2>
        <div class="bar-menu">
          <button class="btn btn-secondary" data-act="bar-count" data-id="${i.itemId}">${esc(t('bar.count'))}<small>${esc(t('bar.countSub'))}</small></button>
          <button class="btn btn-secondary" data-act="bar-writeoff" data-id="${i.itemId}">${esc(t('bar.writeoff'))}<small>${esc(t('bar.writeoffSub'))}</small></button>
          <button class="btn btn-secondary" data-act="bar-threshold" data-id="${i.itemId}">${esc(t('bar.thresholdBtn'))}<small>${esc(i.lowThreshold ? t('bar.thresholdNow', { n: num(i.lowThreshold) }) : t('bar.thresholdNone'))}</small></button>
          <button class="btn btn-secondary" data-act="bar-history" data-id="${i.itemId}">${esc(t('bar.history'))}<small>${esc(t('bar.historySub'))}</small></button>
          <button class="btn btn-danger-ghost" data-act="bar-stop" data-id="${i.itemId}">${esc(t('bar.stop'))}<small>${esc(t('bar.stopSub'))}</small></button>
        </div>`, t('common.close'), () => {});
    }

    async function historyModal(i) {
      let h;
      try {
        h = await api('GET', `bar/history?itemId=${i.itemId}`);
      } catch (error) {
        if (!lock.expired(error)) toast(errorText(error).message);
        return;
      }
      const kind = (k) => t('bar.k.' + k);
      const sign = (e) => (e.kind === 'receive' ? '+' : e.kind === 'writeoff' ? '−' : '=');
      const entries = h.entries.length ? `<table class="rp-table bar-hist"><thead><tr><th>${esc(t('bar.when'))}</th><th>${esc(t('bar.what'))}</th><th class="r">${esc(t('bar.qty'))}</th></tr></thead>
        <tbody>${h.entries.map((e) => `<tr><td>${esc(when(e.at))}</td><td>${esc(kind(e.kind))}${e.note ? `<div class="small muted">${esc(e.note)}</div>` : ''}</td><td class="r"><b>${sign(e)}${num(e.qty)}</b></td></tr>`).join('')}</tbody></table>`
        : `<p class="muted">${esc(t('bar.noEntries'))}</p>`;
      const sales = h.sales.length ? `<table class="rp-table bar-hist"><thead><tr><th>${esc(t('bar.day'))}</th><th class="r">${esc(t('bar.sold'))}</th></tr></thead>
        <tbody>${h.sales.slice(0, 31).map((s) => `<tr><td>${esc(s.date.split('-').reverse().join('.'))}</td><td class="r">−${num(s.quantity)}</td></tr>`).join('')}</tbody></table>`
        : `<p class="muted">${esc(t('bar.noSales'))}</p>`;
      dialogModal(`<h2>${esc(t('bar.historyTitle', { name: i.name }))}</h2>
        <div class="bar-hist-wrap"><h3>${esc(t('bar.book'))}</h3>${entries}<h3>${esc(t('bar.salesByDay'))}</h3>${sales}</div>`, t('common.close'), () => {});
    }

    async function save(method, path, payload, done) {
      try {
        B.overview = await api(method, path, payload);
        toast(done);
      } catch (error) {
        if (!lock.expired(error)) toast(errorText(error).message);
      }
      update();
    }

    // ------------------------------------------------------------ actions

    const actions = {
      'bar-refresh': () => load(),
      'bar-filter': (el) => { B.filter = el.dataset.filter; update(); },
      'bar-receive': (el) => qtyModal(item(el.dataset.id), 'receive'),
      'bar-start': (el) => qtyModal(item(el.dataset.id), 'count'),
      'bar-more': (el) => moreModal(item(el.dataset.id)),
      'bar-count': (el) => qtyModal(item(el.dataset.id), 'count'),
      'bar-writeoff': (el) => qtyModal(item(el.dataset.id), 'writeoff'),
      'bar-threshold': (el) => thresholdModal(item(el.dataset.id)),
      'bar-history': (el) => historyModal(item(el.dataset.id)),
      'bar-stop': (el) => {
        const i = item(el.dataset.id);
        save('PUT', 'bar/item', { itemId: i.itemId, tracked: false }, t('bar.stopped', { name: i.name }));
      },
      'bar-restore': async () => {
        try {
          const result = await api('POST', 'bar/restore');
          toast(result.restored ? t('bar.restored', { n: result.restored }) : t('bar.restoredNone'));
          await load();
        } catch (error) {
          if (!lock.expired(error)) toast(errorText(error).message);
        }
      },
    };

    document.addEventListener('input', (event) => {
      if (event.target.id !== 'barSearch') return;
      B.q = event.target.value.trim();
      const listEl = document.querySelector('[data-bar="root"] .bar-list, [data-bar="root"] .rp-empty');
      if (listEl) listEl.outerHTML = list();
    });

    return {
      render,
      enter,
      act: (name, el) => {
        if (!actions[name]) return false;
        // Menu buttons live inside the modal: close it first (dialogModal closes only on its own buttons).
        const modal = el.closest && el.closest('.modal-backdrop');
        if (modal && name !== 'bar-refresh') modal.hidden = true;
        actions[name](el);
        return true;
      },
    };
  }

  window.MJBar = { create };
})();
