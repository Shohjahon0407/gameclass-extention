// Manajoy GameClass — «Hisobot»: the owner's reports from GameClass history (vanilla JS, inline SVG charts).
// Computed by this window's own process from GameClass's database (read-only). The service sends the same daily
// totals to Manajoy for ManaJoy Biznes (AnalyticsPushWorker; the owner can turn that off here). Locked with the
// owner's PIN; the unlock token lives only in this page's memory.
'use strict';

(() => {
  const DAY_MS = 86400000;
  const C = { pc: '#1b54e4', bar: '#eb6834', account: '#1baf7a', booked: '#eda100' };
  // Sequential blue (light → dark) for the heatmap; the lightest step means "nobody".
  const RAMP = ['#eef3fb', '#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#184f95', '#0d366b'];

  function create(ctx) {
    const { t, esc, fmtMoney, toast, errorText, dialogModal } = ctx;
    const R = {
      status: null,
      statusError: null,
      token: null,
      pinError: null,
      busy: false,
      tab: 'overview',
      preset: '7d',
      periodsOpen: false,
      filtersOpen: false,
      detailsOpen: false,
      syncOpen: false,
      from: null,
      to: null,
      group: 'auto',
      report: null,
      loading: false,
      error: null,
      day: null,
      dayData: null,
      dayLoading: false,
      dayError: null,
      sort: { key: 'busyHours', desc: true },
      push: null,
    };
    let seq = 0;

    const api = (method, path, body) => ctx.api(method, path, body, R.token ? { 'X-Analytics-Token': R.token } : {});
    const money = (v) => fmtMoney(v, R.report && R.report.currency);
    const num = (v, digits = 0) => Number(v || 0).toLocaleString('ru-RU', { maximumFractionDigits: digits, minimumFractionDigits: 0 });
    const hours = (v) => `${num(v, 1)} ${t('rp.h')}`;
    const pct = (v) => `${num((v || 0) * 100, 1)}%`;
    const pad = (n) => String(n).padStart(2, '0');
    const iso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
    const parse = (s) => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
    const addDays = (s, n) => { const d = parse(s); d.setDate(d.getDate() + n); return iso(d); };
    const hm = (value) => { const d = new Date(value); return `${pad(d.getHours())}:${pad(d.getMinutes())}`; };
    const dmy = (value) => { const d = typeof value === 'string' && value.length === 10 ? parse(value) : new Date(value); return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`; };
    const dayStart = () => (R.status ? R.status.dayStartHour : 6);
    /** The business day now: before 06:00 it is still yesterday's evening. */
    const businessToday = () => iso(new Date(Date.now() - dayStart() * 3600000));
    const weekdays = () => t('rp.weekdays').split(',');
    const months = () => t('rp.months').split(',');

    function duration(minutes) {
      const h = Math.floor(minutes / 60);
      const m = Math.round(minutes % 60);
      return h ? `${h} ${t('common.hour')}${m ? ` ${m} ${t('common.min')}` : ''}` : `${m} ${t('common.min')}`;
    }

    function short(v) {
      const a = Math.abs(v);
      if (a >= 1e6) return `${num(v / 1e6, 1)} ${t('rp.mln')}`;
      if (a >= 1e3) return `${num(v / 1e3, 0)} ${t('rp.k')}`;
      return num(v);
    }

    function rangeFor(preset) {
      const today = businessToday();
      const d = parse(today);
      switch (preset) {
        case 'today': return [today, today];
        case 'yesterday': return [addDays(today, -1), addDays(today, -1)];
        case '30d': return [addDays(today, -29), today];
        case 'month': return [iso(new Date(d.getFullYear(), d.getMonth(), 1)), today];
        case 'lastMonth': return [iso(new Date(d.getFullYear(), d.getMonth() - 1, 1)), iso(new Date(d.getFullYear(), d.getMonth(), 0))];
        case 'year': return [iso(new Date(d.getFullYear(), 0, 1)), today];
        default: return [addDays(today, -6), today];
      }
    }

    // ------------------------------------------------------------ loading

    function fail(error) {
      if (error && (error.code === 'pin_required' || error.code === 'pin_not_set')) {
        R.token = null;
        if (R.status) R.status.unlocked = false;
        R.pinError = error.code === 'pin_required' ? { code: 'pin_expired' } : null;
        return true;
      }
      return false;
    }

    async function loadStatus() {
      try {
        R.status = await api('GET', 'analytics/status');
        R.statusError = null;
        if (!R.status.unlocked) R.token = null;
        else loadPush();
      } catch (error) {
        R.statusError = error;
      }
      update();
    }

    /** Whether these reports also go to ManaJoy Biznes (on by default since 0.9.0) and how far that got. */
    async function loadPush() {
      try {
        R.push = await api('GET', 'analytics/push');
      } catch (error) {
        R.push = null;
      }
      const el = document.querySelector('[data-rp="push"]');
      if (el) el.innerHTML = pushLine();
    }

    function pushLine() {
      const p = R.push;
      if (!p || !p.paired || !p.enabled || (p.report && p.report.last_error)) return pushDetailLine();
      const ready = p.report && p.report.last_success_at;
      return `<span class="rp-sync ${ready ? 'ok' : 'wait'}"><i></i>${esc(t(ready ? 'rp.syncReady' : 'rp.syncPending'))}</span>
        <button class="rp-text-button" data-act="rp-sync-details" aria-expanded="${R.syncOpen}" aria-controls="rpSyncDetails">${esc(t('rp.detailsShort'))}</button>
        <div id="rpSyncDetails" class="small muted"${R.syncOpen ? '' : ' hidden'}>${pushDetailLine()}</div>`;
    }

    function pushDetailLine() {
      const p = R.push;
      if (!p) return '';
      if (!p.paired) return `<span class="rp-sync off"><i></i>${esc(t('rp.pushNotPaired'))}</span>`;
      if (!p.enabled) return `<span class="rp-sync off"><i></i>${esc(t('rp.pushOff'))}</span>`;
      const r = p.report;
      if (!r) return `<span class="rp-sync wait"><i></i>${esc(t('rp.pushOn'))}</span>`;
      if (r.last_error && !r.last_success_at) return `<span class="rp-sync fail"><i></i>${esc(t('rp.pushFail', { e: r.last_error }))}</span>`;
      const when = r.last_success_at ? t('rp.pushLast', { t: hmAgo(r.last_success_at) }) : t('rp.pushSoon');
      const history = r.backfill_done ? '' : ` · ${t('rp.pushHistory', { d: r.backfill_before ? dmy(r.backfill_before) : '…' })}`;
      const problem = r.last_error ? ` · ${t('rp.pushRetry')}` : '';
      return `<span class="rp-sync ${r.last_error ? 'wait' : 'ok'}"><i></i>${esc(t('rp.pushOn'))} · ${esc(when)}${esc(history)}${esc(problem)}</span>`;
    }

    function hmAgo(value) {
      const minutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
      return minutes < 1 ? t('common.now') : minutes < 60 ? `${minutes} ${t('common.min')}` : `${Math.round(minutes / 60)} ${t('common.hour')}`;
    }

    async function loadReport() {
      if (!R.from) [R.from, R.to] = rangeFor(R.preset);
      const mine = ++seq;
      R.loading = true;
      R.error = null;
      update();
      try {
        const report = await api('GET', `analytics?from=${R.from}&to=${R.to}&group=${R.group}`);
        if (mine !== seq) return;
        R.report = report;
      } catch (error) {
        if (mine !== seq) return;
        if (!fail(error)) R.error = error;
      }
      R.loading = false;
      update();
    }

    async function loadDay() {
      R.day = R.day || businessToday();
      const mine = ++seq;
      R.dayLoading = true;
      R.dayError = null;
      update();
      try {
        const data = await api('GET', `analytics/day?date=${R.day}`);
        if (mine !== seq) return;
        R.dayData = data;
      } catch (error) {
        if (mine !== seq) return;
        if (!fail(error)) R.dayError = error;
      }
      R.dayLoading = false;
      update();
    }

    const reloadTab = () => (R.tab === 'day' ? loadDay() : loadReport());

    function enter() {
      if (!R.status || !R.status.unlocked) { loadStatus(); return; }
      if (R.tab === 'day' ? !R.dayData : !R.report) reloadTab();
    }

    // Other PIN-locked pages (bar.js) share this lock and are told when it opens or closes.
    const listeners = [];

    function update() {
      for (const fn of listeners) fn();
      const el = document.querySelector('[data-rp="root"]');
      if (!el) return;
      const focused = document.activeElement && document.activeElement.id;
      el.innerHTML = body();
      const pin = document.getElementById(focused === 'rpPin2' ? 'rpPin2' : 'rpPin');
      if (pin) pin.focus();
    }

    // ------------------------------------------------------------ page

    function render() {
      return `<div class="rp" data-rp="root">${body()}</div>`;
    }

    function body() {
      if (R.statusError) return `<div class="page-head"><h1>${esc(t('rp.title'))}</h1></div>${errorNote(R.statusError)}`;
      if (!R.status) return `<div class="page-head"><h1>${esc(t('rp.title'))}</h1></div><div class="skel-page"><div class="skel skel-title"></div><div class="skel skel-block"></div></div>`;
      if (!R.status.unlocked) return lockScreen();
      return `${head()}${R.tab === 'day' ? dayPage() : reportPage()}`;
    }

    function errorNote(error) {
      const e = errorText(error);
      return `<div class="alert fail rp-error"><div class="grow"><b>${esc(e.message)}</b>${e.detail ? `<div class="small muted">${esc(e.detail)}</div>` : ''}</div>
        <button class="btn btn-secondary btn-xs" data-act="rp-retry">${esc(t('rp.retry'))}</button></div>`;
    }

    function lockScreen() {
      const setting = !R.status.pinSet;
      let error = '';
      if (R.status.lockedSeconds > 0) error = t('rp.pinLocked', { m: Math.ceil(R.status.lockedSeconds / 60) });
      else if (R.pinError) {
        const code = R.pinError.code;
        if (code === 'pin_wrong') error = t('rp.pinWrong', { n: R.pinError.detail || '' });
        else if (code === 'pin_locked') error = t('rp.pinLocked', { m: Math.ceil(Number(R.pinError.detail || 300) / 60) });
        else if (I18N_HAS('rp.' + code)) error = t('rp.' + code);
        else error = errorText(R.pinError).message;
      }
      return `<div class="rp-lock card">
        <div class="rp-lock-ic">${LOCK}</div>
        <h2>${esc(t(setting ? 'rp.setTitle' : 'rp.lockTitle'))}</h2>
        <p class="muted">${esc(t(setting ? 'rp.setText' : 'rp.lockText'))}</p>
        <div class="form">
          <div class="field"><label for="rpPin">${esc(t(setting ? 'rp.newPin' : 'rp.pin'))}</label>
            <input class="input rp-pin" id="rpPin" type="password" inputmode="numeric" maxlength="8" autocomplete="off"></div>
          ${setting ? `<div class="field"><label for="rpPin2">${esc(t('rp.newPin2'))}</label>
            <input class="input rp-pin" id="rpPin2" type="password" inputmode="numeric" maxlength="8" autocomplete="off">
            <div class="help">${esc(t('rp.pinHelp'))}</div></div>` : ''}
        </div>
        ${error ? `<p class="rp-pin-error" role="alert">${esc(error)}</p>` : ''}
        <button class="btn btn-primary" data-act="${setting ? 'rp-setpin' : 'rp-unlock'}" ${R.busy || R.status.lockedSeconds > 0 ? 'disabled' : ''}>
          ${R.busy ? '<span class="spinner"></span>' : ''}${esc(t(setting ? 'rp.setPin' : 'rp.unlock'))}</button>
        <p class="small muted">${esc(t('rp.localOnly'))}</p>
      </div>`;
    }

    const PRESETS = ['today', 'yesterday', '7d', '30d', 'month', 'lastMonth', 'year', 'custom'];
    const TABS = ['overview', 'pcs', 'day', 'bar', 'staff'];

    function head() {
      const loading = R.tab === 'day' ? R.dayLoading : R.loading;
      const tabs = TABS.map((id) => `<button role="tab" data-act="rp-tab" data-tab="${id}" class="${R.tab === id ? 'on' : ''}">${esc(t('rp.tab.' + id))}</button>`).join('');
      let controls = '';
      if (R.tab !== 'day') {
        const chips = PRESETS.slice(0, 3).map((p) => `<button class="chip${R.preset === p ? ' on' : ''}" data-act="rp-preset" data-preset="${p}">${esc(t('rp.p.' + p))}</button>`).join('');
        const days = R.from ? (parse(R.to) - parse(R.from)) / DAY_MS + 1 : 7;
        const groups = ['auto', 'hour', 'day', 'week', 'month'].map((g) => {
          const disabled = (g === 'hour' && days > 3) || (g === 'day' && days > 92);
          return `<button data-act="rp-group" data-group="${g}" class="${R.group === g ? 'on' : ''}" ${disabled ? 'disabled' : ''}>${esc(t('rp.g.' + g))}</button>`;
        }).join('');
        controls = `<div class="rp-bar">
          <div class="rp-chips">${chips}
            <button class="chip${PRESETS.indexOf(R.preset) > 2 ? ' on' : ''}" data-act="rp-periods" aria-expanded="${R.periodsOpen}" aria-controls="rpPeriods">${esc(t('rp.otherPeriod'))}${PRESETS.indexOf(R.preset) > 2 ? ` · ${esc(t('rp.p.' + R.preset))}` : ''}</button>
          </div>
          <span class="spacer"></span>
          <button class="btn btn-secondary btn-sm" data-act="rp-filters" aria-expanded="${R.filtersOpen}" aria-controls="rpFilters">${esc(t('rp.moreFilters'))}${R.group !== 'auto' ? ` · ${esc(t('rp.g.' + R.group))}` : ''}</button>
        </div>
        <div id="rpPeriods" class="rp-filter-panel"${R.periodsOpen ? '' : ' hidden'}>
          <div class="rp-chips">${PRESETS.slice(3).map((p) => `<button class="chip${R.preset === p ? ' on' : ''}" data-act="rp-preset" data-preset="${p}">${esc(t('rp.p.' + p))}</button>`).join('')}</div>
        </div>
        ${R.preset === 'custom' ? `<div class="rp-dates rp-custom-dates">
            <label for="rpFrom">${esc(t('rp.from'))}</label>
            <input class="input" type="date" id="rpFrom" value="${esc(R.from)}" max="${esc(businessToday())}" aria-label="${esc(t('rp.from'))}">
            <label for="rpTo">${esc(t('rp.to'))}</label>
            <input class="input" type="date" id="rpTo" value="${esc(R.to)}" max="${esc(businessToday())}" aria-label="${esc(t('rp.to'))}">
          </div>` : ''}
        <div id="rpFilters" class="rp-filter-panel"${R.filtersOpen ? '' : ' hidden'}>
          <span class="small muted">${esc(t('rp.groupBy'))}</span>
          <div class="tabs rp-groups" role="group" aria-label="${esc(t('rp.groupBy'))}">${groups}</div>
        </div>`;
      }
      return `<div class="page-head">
          <h1>${esc(t('rp.title'))}</h1>
          ${loading ? '<span class="spinner dark"></span>' : ''}
          <span class="spacer"></span>
          <button class="btn btn-secondary btn-sm" data-act="rp-refresh" ${loading ? 'disabled' : ''} title="${esc(t('prices.refresh'))}">${ctx.icon.refresh}<span class="lbl">${esc(t('prices.refresh'))}</span></button>
          <button class="btn btn-secondary btn-sm" data-act="rp-settings" title="${esc(t('rp.settings'))}">${ctx.icon.settings}<span class="lbl">${esc(t('rp.settings'))}</span></button>
          <button class="btn btn-secondary btn-sm" data-act="rp-lock" title="${esc(t('rp.lock'))}">${LOCK}<span class="lbl">${esc(t('rp.lock'))}</span></button>
        </div>
        <p class="page-lead">${esc(t('rp.lead', { h: pad(dayStart()) }))}</p>
        <div class="rp-push" data-rp="push">${pushLine()}</div>
        <div class="tabs rp-tabs" role="tablist">${tabs}</div>
        ${controls}`;
    }

    function reportPage() {
      const r = R.report;
      if (!r) return R.error ? errorNote(R.error) : `<div class="rp-kpis">${Array(4).fill('<div class="skel rp-skel-kpi"></div>').join('')}</div><div class="skel skel-block"></div>`;
      const notes = [];
      if (R.error) notes.push(errorNote(R.error));
      if (r.historyFrom && new Date(r.historyFrom) > new Date(r.windowStart)) {
        notes.push(`<div class="alert info compact"><div class="grow">${esc(t('rp.historyFrom', { d: dmy(r.historyFrom) }))}</div></div>`);
      }
      const period = r.from === r.to ? dmy(r.from) : `${dmy(r.from)} — ${dmy(r.to)}`;
      const top = `<div class="rp-period"><b>${esc(period)}</b><span class="muted">${esc(t('rp.window', { a: hm(r.windowStart), b: hm(r.windowEnd) }))}</span></div>${notes.join('')}`;
      switch (R.tab) {
        case 'pcs': return top + pcsPage(r);
        case 'bar': return top + barPage(r);
        case 'staff': return top + staffPage(r);
        default: return top + overview(r);
      }
    }

    // ------------------------------------------------------------ overview

    function delta(cur, prev, points) {
      if (points) {
        const d = (cur - prev) * 100;
        if (!prev && !cur) return '';
        return `<span class="rp-delta ${d >= 0 ? 'up' : 'down'}" title="${esc(t('rp.vsPrev'))}">${d >= 0 ? '▲' : '▼'} ${num(Math.abs(d), 1)} ${t('rp.pp')}</span>`;
      }
      if (!prev) return cur ? `<span class="rp-delta new" title="${esc(t('rp.vsPrev'))}">${esc(t('rp.new'))}</span>` : '';
      const d = (cur - prev) / Math.abs(prev) * 100;
      return `<span class="rp-delta ${d >= 0 ? 'up' : 'down'}" title="${esc(t('rp.vsPrev'))}">${d >= 0 ? '▲' : '▼'} ${num(Math.abs(d), Math.abs(d) < 10 ? 1 : 0)}%</span>`;
    }

    function kpi(label, value, sub, deltaHtml, accent) {
      return `<div class="rp-kpi${accent ? ' accent' : ''}"><div class="rp-kpi-l">${esc(label)}</div><div class="rp-kpi-v">${esc(value)}</div>
        <div class="rp-kpi-s">${deltaHtml || ''}${sub ? `<span>${esc(sub)}</span>` : ''}</div></div>`;
    }

    function overview(r) {
      const m = r.money;
      const p = r.previous;
      const u = r.usage;
      const s = r.sessions;
      const kpis = `<div class="rp-kpis">
        ${kpi(t('rp.kassa'), money(m.kassa), '', delta(m.kassa, p.kassa), true)}
        ${kpi(t('rp.pcTime'), money(m.pcTime), '', delta(m.pcTime, p.pcTime))}
        ${kpi(t('rp.bar'), money(m.bar), '', delta(m.bar, p.bar))}
        ${kpi(t('rp.occupancy'), pct(u.occupancy), t('rp.ofPcs', { n: u.pcCount }), delta(u.occupancy, p.occupancy, true))}
      </div>`;
      const extraKpis = `<div class="rp-kpis">
        ${kpi(t('rp.busyHours'), hours(u.busyHours), '', delta(u.busyHours, p.busyHours))}
        ${kpi(t('rp.sessions'), num(s.count), t('rp.avgLen', { t: duration(s.avgMinutes) }), delta(s.count, p.sessions))}
        ${kpi(t('rp.avgCheck'), money(s.avgCheck), '', '')}
        ${kpi(t('rp.peak'), `${u.peakBusy} / ${u.pcCount}`, u.peakAt ? `${dmy(u.peakAt)} ${hm(u.peakAt)}` : '', '')}
      </div>`;
      return `${kpis}
        <div class="rp-grid">
          <section class="card rp-wide rp-overview-chart"><div class="rp-card-head"><h2>${esc(t('rp.revenue'))}</h2>${legend([[C.pc, t('rp.pcTime')], [C.bar, t('rp.bar')]])}</div><div class="rp-chart-scroll">${revenueChart(r)}</div></section>
          <section class="card rp-wide rp-overview-chart"><div class="rp-card-head"><h2>${esc(t('rp.busyChart'))}</h2></div><div class="rp-chart-scroll">${busyChart(r)}</div></section>
        </div>
        <button class="btn btn-secondary rp-details-toggle" data-act="rp-details" aria-expanded="${R.detailsOpen}" aria-controls="rpDetails">${esc(t(R.detailsOpen ? 'rp.hideDetails' : 'rp.showDetails'))}<span aria-hidden="true">${R.detailsOpen ? '−' : '+'}</span></button>
        <div id="rpDetails"${R.detailsOpen ? '' : ' hidden'}>
          ${R.detailsOpen ? `${extraKpis}<div class="rp-grid">
          <section class="card rp-wide"><div class="rp-card-head"><h2>${esc(t('rp.heatmap'))}</h2><span class="small muted">${esc(t('rp.heatmapHint'))}</span></div>${heatmap(r)}</section>
          <section class="card"><h2>${esc(t('rp.moneyTitle'))}</h2>${moneyList(m)}</section>
          <section class="card"><h2>${esc(t('rp.zones'))}</h2>${zones(r)}</section>
          <section class="card"><h2>${esc(t('rp.sessionsTitle'))}</h2>${sessionsList(r)}</section>
          <section class="card"><div class="rp-card-head"><h2>${esc(t('rp.tariffs'))}</h2>${csvButton('tariffs')}</div>${tariffsTable(r)}</section>
          </div>` : ''}
        </div>`;
    }

    function legend(items) {
      return `<div class="rp-legend">${items.map(([color, label]) => `<span><i class="sw" data-c="${color}"></i>${esc(label)}</span>`).join('')}</div>`;
    }

    function bucketLabel(key, group, compact) {
      if (group === 'hour') return `${key.slice(11, 13)}:00`;
      if (group === 'month') { const [y, mo] = key.split('-').map(Number); return compact ? months()[mo - 1] : `${months()[mo - 1]} ${y}`; }
      const d = parse(key);
      if (group === 'week') return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}`;
      return compact ? `${pad(d.getDate())}.${pad(d.getMonth() + 1)}` : `${weekdays()[(d.getDay() + 6) % 7]}, ${dmy(key)}`;
    }

    /** Nice round axis maximum and 4 gridlines. */
    function axis(max) {
      if (max <= 0) return { top: 1, ticks: [0, 1] };
      const raw = max / 4;
      const mag = Math.pow(10, Math.floor(Math.log10(raw)));
      const step = [1, 2, 2.5, 5, 10].map((f) => f * mag).find((s) => s >= raw);
      const top = step * Math.ceil(max / step);
      const ticks = [];
      for (let v = 0; v <= top + step / 2; v += step) ticks.push(v);
      return { top, ticks };
    }

    const W = 960;
    const PAD = { l: 64, r: 12, t: 12, b: 28 };

    function frame(h, ax, fmt) {
      const ih = h - PAD.t - PAD.b;
      return ax.ticks.map((v) => {
        const y = PAD.t + ih - (v / ax.top) * ih;
        return `<line class="grid" x1="${PAD.l}" x2="${W - PAD.r}" y1="${y}" y2="${y}"/><text class="ax" x="${PAD.l - 8}" y="${y + 4}" text-anchor="end">${esc(fmt(v))}</text>`;
      }).join('');
    }

    function xLabels(series, group, h, slot) {
      const every = Math.max(1, Math.ceil(series.length / Math.floor((W - PAD.l) / 70)));
      return series.map((p, i) => i % every ? '' : `<text class="ax" x="${PAD.l + slot * i + slot / 2}" y="${h - 8}" text-anchor="middle">${esc(bucketLabel(p.key, group, true))}</text>`).join('');
    }

    function revenueChart(r) {
      const series = r.series;
      if (!series.length) return empty();
      const h = 180;
      const ih = h - PAD.t - PAD.b;
      const ax = axis(Math.max(...series.map((p) => p.pcTime + p.bar)));
      const slot = (W - PAD.l - PAD.r) / series.length;
      const bw = Math.max(2, Math.min(36, slot * 0.68));
      const y = (v) => (v / ax.top) * ih;
      const bars = series.map((p, i) => {
        const x = PAD.l + slot * i + (slot - bw) / 2;
        const base = PAD.t + ih;
        const hp = y(p.pcTime);
        const hb = y(p.bar);
        const gap = hp > 0 && hb > 0 ? 2 : 0;
        const tip = [bucketLabel(p.key, r.group, false), `${t('rp.pcTime')}: ${money(p.pcTime)}`, `${t('rp.bar')}: ${money(p.bar)}`,
          `${t('rp.kassa')}: ${money(p.kassa)}`, `${t('rp.sessions')}: ${num(p.sessions)}`].join('\n');
        return `<g>${hp > 0 ? `<path fill="${C.pc}" d="${barPath(x, base - hp, bw, hp, hb > 0 ? 0 : 4)}"/>` : ''}
          ${hb > 0 ? `<path fill="${C.bar}" d="${barPath(x, base - hp - gap - hb, bw, hb, 4)}"/>` : ''}
          <rect class="hit" x="${PAD.l + slot * i}" y="${PAD.t}" width="${slot}" height="${ih}" data-tip="${esc(tip)}"/></g>`;
      }).join('');
      return `<svg class="rp-chart" viewBox="0 0 ${W} ${h}" role="img" aria-label="${esc(t('rp.revenue'))}">${frame(h, ax, short)}${bars}
        <line class="base" x1="${PAD.l}" x2="${W - PAD.r}" y1="${PAD.t + ih}" y2="${PAD.t + ih}"/>${xLabels(series, r.group, h, slot)}</svg>`;
    }

    /** A bar with only its data end (top) rounded. */
    function barPath(x, top, w, h, radius) {
      const rr = Math.min(radius, w / 2, h);
      return `M${x},${top + h}V${top + rr}Q${x},${top} ${x + rr},${top}H${x + w - rr}Q${x + w},${top} ${x + w},${top + rr}V${top + h}Z`;
    }

    function busyChart(r) {
      const series = r.series;
      if (!series.length) return empty();
      const h = 160;
      const ih = h - PAD.t - PAD.b;
      const ax = axis(Math.max(...series.map((p) => p.busyHours)));
      const slot = (W - PAD.l - PAD.r) / series.length;
      const pt = (p, i) => [PAD.l + slot * i + slot / 2, PAD.t + ih - (p.busyHours / ax.top) * ih];
      const line = series.map((p, i) => `${i ? 'L' : 'M'}${pt(p, i).map((v) => v.toFixed(1)).join(',')}`).join('');
      const area = `${line}L${pt(series[series.length - 1], series.length - 1)[0].toFixed(1)},${PAD.t + ih}L${pt(series[0], 0)[0].toFixed(1)},${PAD.t + ih}Z`;
      const dots = series.length <= 40 ? series.map((p, i) => { const [x, yy] = pt(p, i); return `<circle class="dot" cx="${x}" cy="${yy}" r="4"/>`; }).join('') : '';
      const hits = series.map((p, i) => {
        const tip = [bucketLabel(p.key, r.group, false), `${t('rp.busyHours')}: ${hours(p.busyHours)}`, `${t('rp.sessions')}: ${num(p.sessions)}`].join('\n');
        return `<rect class="hit" x="${PAD.l + slot * i}" y="${PAD.t}" width="${slot}" height="${ih}" data-tip="${esc(tip)}"/>`;
      }).join('');
      return `<svg class="rp-chart" viewBox="0 0 ${W} ${h}" role="img" aria-label="${esc(t('rp.busyChart'))}">${frame(h, ax, (v) => num(v))}
        <path class="area" d="${area}"/><path class="line" d="${line}"/>${dots}
        <line class="base" x1="${PAD.l}" x2="${W - PAD.r}" y1="${PAD.t + ih}" y2="${PAD.t + ih}"/>${xLabels(series, r.group, h, slot)}${hits}</svg>`;
    }

    function heatmap(r) {
      const rows = r.heatmap;
      const max = Math.max(0, ...rows.flat());
      if (!max) return empty();
      const cw = 36;
      const ch = 26;
      const left = 40;
      const top = 20;
      const wd = weekdays();
      const order = [];
      for (let k = 0; k < 24; k++) order.push((dayStart() + k) % 24);
      let cells = '';
      rows.forEach((row, d) => {
        order.forEach((hour, k) => {
          const v = row[hour];
          const step = v <= 0 ? 0 : Math.min(RAMP.length - 1, 1 + Math.floor((v / max) * (RAMP.length - 1)));
          const tip = `${wd[d]} ${pad(hour)}:00–${pad((hour + 1) % 24)}:00\n${t('rp.heatTip', { n: num(v, 1), of: r.usage.pcCount })}`;
          cells += `<rect class="cell" x="${left + k * cw + 1}" y="${top + d * ch + 1}" width="${cw - 2}" height="${ch - 2}" rx="4" fill="${RAMP[step]}" data-tip="${esc(tip)}"/>`;
        });
        cells += `<text class="ax" x="${left - 8}" y="${top + d * ch + ch / 2 + 4}" text-anchor="end">${esc(wd[d])}</text>`;
      });
      order.forEach((hour, k) => { if (k % 2 === 0) cells += `<text class="ax" x="${left + k * cw + cw / 2}" y="${top - 6}" text-anchor="middle">${pad(hour)}</text>`; });
      const width = left + 24 * cw + 4;
      const scale = `<div class="rp-scale"><span>0</span>${RAMP.map((c) => `<i class="sw sq" data-c="${c}"></i>`).join('')}<span>${esc(t('rp.heatMax', { n: num(max, 1) }))}</span></div>`;
      return `<div class="rp-scroll"><svg class="rp-heat" viewBox="0 0 ${width} ${top + 7 * ch + 4}" role="img" aria-label="${esc(t('rp.heatmap'))}">${cells}</svg></div>${scale}`;
    }

    function moneyList(m) {
      const row = (label, value, cls = '') => `<div class="${cls}"><dt>${esc(label)}</dt><dd>${esc(value)}</dd></div>`;
      return `<dl class="kv rp-kv">
        ${row(t('rp.pcTime'), money(m.pcTime))}
        ${row(t('rp.barCash'), money(m.barCash))}
        ${row(t('rp.barInSessions'), money(m.barInSessions))}
        ${m.print ? row(t('rp.print'), money(m.print)) : ''}
        ${m.internet ? row(t('rp.internet'), money(m.internet)) : ''}
        ${row(t('rp.accountsAdded'), money(m.accountsAdded))}
        ${m.accountsReturned ? row(t('rp.accountsReturned'), `− ${money(m.accountsReturned)}`) : ''}
        ${row(t('rp.kassa'), money(m.kassa), 'total')}
        ${m.barFromAccounts ? row(t('rp.barFromAccounts'), money(m.barFromAccounts), 'aside') : ''}
      </dl><p class="small muted">${esc(t('rp.moneyNote'))}</p>`;
    }

    function zones(r) {
      if (!r.zones.length) return empty();
      return `<div class="rp-hbars">${r.zones.map((z) => `<div class="rp-hbar" data-tip="${esc(`${z.zone || t('zone.none')}\n${t('rp.busyHours')}: ${hours(z.busyHours)}\n${t('rp.pcTime')}: ${money(z.timeValue)}`)}">
        <div class="rp-hbar-l"><b>${esc(z.zone || t('zone.none'))}</b><span class="muted">${esc(t('rp.pcsN', { n: z.pcs }))}</span><span class="grow"></span><b>${esc(pct(z.occupancy))}</b></div>
        ${meter(z.occupancy)}</div>`).join('')}</div>`;
    }

    function meter(ratio) {
      const w = Math.max(0, Math.min(1, ratio)) * 100;
      return `<svg class="rp-meter" viewBox="0 0 100 8" preserveAspectRatio="none" aria-hidden="true"><rect class="track" width="100" height="8" rx="4"/>${w > 0 ? `<rect fill="${C.pc}" width="${Math.max(w, 2)}" height="8" rx="4"/>` : ''}</svg>`;
    }

    function sessionsList(r) {
      const s = r.sessions;
      const b = r.bookings;
      const row = (label, value) => `<div><dt>${esc(label)}</dt><dd>${esc(value)}</dd></div>`;
      return `<dl class="kv rp-kv">
        ${row(t('rp.sessions'), num(s.count))}
        ${row(t('rp.avgLenL'), duration(s.avgMinutes))}
        ${row(t('rp.withAccount'), num(s.withAccount))}
        ${row(t('rp.debts'), num(s.debts))}
        ${row(t('rp.bookedPast'), num(b.past))}
        ${row(t('rp.bookedUpcoming'), num(b.upcoming))}
        ${row(t('rp.cancelled'), num(b.cancelled))}
      </dl><p class="small muted">${esc(t('rp.bookingsNote'))}</p>`;
    }

    function empty() {
      return `<p class="muted rp-empty">${esc(t('rp.empty'))}</p>`;
    }

    // ------------------------------------------------------------ tables

    const TABLES = {
      tariffs: (r) => ({
        cols: [['name', t('rp.col.tariff')], ['sessions', t('rp.sessions'), 'n'], ['hours', t('rp.col.hours'), 'h'], ['timeValue', t('rp.col.value'), 'm']],
        rows: r.tariffs,
      }),
      pcs: (r) => ({
        cols: [['number', t('rp.col.pc'), 'pc'], ['zone', t('rp.col.zone')], ['busyHours', t('rp.col.hours'), 'h'], ['occupancy', t('rp.occupancy'), 'meter'],
          ['sessions', t('rp.sessions'), 'n'], ['timeValue', t('rp.col.value'), 'm'], ['lastUsed', t('rp.col.last'), 'dt']],
        rows: r.pcs,
      }),
      bar: (r) => ({
        cols: [['name', t('rp.col.item')], ['quantity', t('rp.col.qty'), 'n'], ['total', t('rp.col.total'), 'm'], ['cash', t('rp.col.cash'), 'm'],
          ['inSessions', t('rp.col.inSessions'), 'm'], ['fromAccounts', t('rp.col.fromAccounts'), 'm']],
        rows: r.bar,
      }),
      operators: (r) => ({
        cols: [['name', t('rp.col.operator')], ['sessions', t('rp.sessions'), 'n'], ['timeValue', t('rp.col.value'), 'm'], ['bar', t('rp.bar'), 'm']],
        rows: r.operators,
      }),
      shifts: (r) => ({
        cols: [['at', t('rp.col.closed'), 'dt'], ['operator', t('rp.col.operator')], ['total', t('rp.col.shiftTotal'), 'm'], ['removed', t('rp.col.removed'), 'm']],
        rows: r.shifts,
      }),
      debts: (r) => ({
        cols: [['pc', t('rp.col.pc'), 'pc'], ['at', t('rp.col.when'), 'dt'], ['sessionTotal', t('rp.col.sessionTotal'), 'm']],
        rows: r.debts,
      }),
    };

    function cell(value, kind) {
      switch (kind) {
        case 'm': return money(value);
        case 'h': return num(value, 1);
        case 'n': return num(value);
        case 'pc': return `PC ${value}`;
        case 'dt': return value ? `${dmy(value)} ${hm(value)}` : '—';
        case 'meter': return pct(value);
        default: return value || '—';
      }
    }

    function table(name, sortable) {
      const { cols, rows } = TABLES[name](R.report);
      if (!rows.length) return empty();
      let list = rows;
      if (sortable) {
        const { key, desc } = R.sort;
        list = [...rows].sort((a, b) => {
          const x = a[key] ?? '';
          const y = b[key] ?? '';
          const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), undefined, { numeric: true });
          return desc ? -c : c;
        });
      }
      const th = cols.map(([key, label, kind]) => {
        const right = kind && kind !== 'pc' ? ' class="r"' : '';
        if (!sortable) return `<th${right}>${esc(label)}</th>`;
        const on = R.sort.key === key;
        return `<th${right}><button data-act="rp-sort" data-key="${key}" class="${on ? 'on' : ''}">${esc(label)}${on ? (R.sort.desc ? ' ↓' : ' ↑') : ''}</button></th>`;
      }).join('');
      const tr = list.map((row) => `<tr>${cols.map(([key, , kind]) => kind === 'meter'
        ? `<td class="r rp-meter-cell">${meter(row[key])}<span>${esc(pct(row[key]))}</span></td>`
        : `<td${kind && kind !== 'pc' ? ' class="r"' : ''}>${esc(cell(row[key], kind))}</td>`).join('')}</tr>`).join('');
      return `<div class="rp-table-wrap"><table class="rp-table"><thead><tr>${th}</tr></thead><tbody>${tr}</tbody></table></div>`;
    }

    const csvButton = (name) => `<button class="btn btn-ghost btn-xs" data-act="rp-csv" data-table="${name}" title="${esc(t('rp.csvHint'))}">${DOWNLOAD}<span>CSV</span></button>`;

    function tariffsTable() { return table('tariffs', false); }

    function pcsPage(r) {
      const idle = r.pcs.filter((p) => p.busyHours === 0).length;
      const top = [...r.pcs].sort((a, b) => b.busyHours - a.busyHours)[0];
      return `<div class="rp-kpis three">
          ${kpi(t('rp.busyHours'), hours(r.usage.busyHours), t('rp.ofPcs', { n: r.usage.pcCount }), '')}
          ${kpi(t('rp.topPc'), top && top.busyHours ? `PC ${top.number}` : '—', top && top.busyHours ? `${hours(top.busyHours)} · ${pct(top.occupancy)}` : '', '')}
          ${kpi(t('rp.idlePcs'), num(idle), t('rp.idleHint'), '')}
        </div>
        <section class="card"><div class="rp-card-head"><h2>${esc(t('rp.tab.pcs'))}</h2>${csvButton('pcs')}</div>${table('pcs', true)}</section>`;
    }

    function barPage(r) {
      const m = r.money;
      return `<div class="rp-kpis three">
          ${kpi(t('rp.bar'), money(m.bar), '', delta(m.bar, r.previous.bar), true)}
          ${kpi(t('rp.barCash'), money(m.barCash), '', '')}
          ${kpi(t('rp.barInSessions'), money(m.barInSessions + m.barFromAccounts), t('rp.barInSessionsHint'), '')}
        </div>
        <section class="card"><div class="rp-card-head"><h2>${esc(t('rp.barItems'))}</h2>${csvButton('bar')}</div>${table('bar', false)}</section>`;
    }

    function staffPage() {
      return `<div class="rp-grid">
        <section class="card rp-wide"><div class="rp-card-head"><h2>${esc(t('rp.operators'))}</h2>${csvButton('operators')}</div>${table('operators', false)}</section>
        <section class="card rp-wide"><div class="rp-card-head"><h2>${esc(t('rp.shifts'))}</h2>${csvButton('shifts')}</div>${table('shifts', false)}</section>
        <section class="card rp-wide"><div class="rp-card-head"><h2>${esc(t('rp.debtsTitle'))}</h2>${csvButton('debts')}</div>${table('debts', false)}
          <p class="small muted">${esc(t('rp.debtsNote'))}</p></section>
      </div>`;
    }

    // ------------------------------------------------------------ one day: who sat where, when

    function dayPage() {
      const nav = `<div class="rp-bar">
        <button class="btn btn-secondary btn-sm" data-act="rp-day-step" data-step="-1" aria-label="${esc(t('rp.prevDay'))}">‹</button>
        <input class="input" type="date" id="rpDay" value="${esc(R.day || businessToday())}" max="${esc(addDays(businessToday(), 1))}" aria-label="${esc(t('rp.tab.day'))}">
        <button class="btn btn-secondary btn-sm" data-act="rp-day-step" data-step="1" aria-label="${esc(t('rp.nextDay'))}">›</button>
        <button class="chip${R.day === businessToday() ? ' on' : ''}" data-act="rp-day-today">${esc(t('rp.p.today'))}</button>
        <span class="spacer"></span>
        ${legend([[C.pc, t('rp.guest')], [C.account, t('rp.account')], [C.booked, t('rp.booked')]])}
      </div>`;
      if (R.dayError) return nav + errorNote(R.dayError);
      if (!R.dayData) return `${nav}<div class="skel skel-block"></div>`;
      return `${nav}<section class="card rp-day-card">${gantt(R.dayData)}</section><p class="small muted">${esc(t('rp.dayNote', { h: pad(dayStart()) }))}</p>`;
    }

    function gantt(d) {
      if (!d.pcs.length) return empty();
      const ws = new Date(d.windowStart).getTime();
      const span = new Date(d.windowEnd).getTime() - ws;
      const left = 70;
      const right = 74;
      const rowH = 26;
      const top = 24;
      const width = 1100;
      const plot = width - left - right;
      const x = (v) => left + ((new Date(v).getTime() - ws) / span) * plot;
      let out = '';
      for (let k = 0; k <= 24; k += 2) {
        const gx = left + (k / 24) * plot;
        out += `<line class="grid v" x1="${gx}" x2="${gx}" y1="${top - 4}" y2="${top + d.pcs.length * rowH}"/>`;
        out += `<text class="ax" x="${gx}" y="${top - 10}" text-anchor="middle">${pad((dayStart() + k) % 24)}:00</text>`;
      }
      d.pcs.forEach((pc, i) => {
        const y = top + i * rowH;
        out += `<rect class="row${i % 2 ? ' odd' : ''}" x="0" y="${y}" width="${width}" height="${rowH}"/>`;
        out += `<text class="lbl" x="10" y="${y + rowH / 2 + 4}">PC ${pc.number}</text>`;
        out += `<text class="ax" x="${width - 8}" y="${y + rowH / 2 + 4}" text-anchor="end">${esc(pc.busyHours ? hours(pc.busyHours) : '—')}</text>`;
        for (const b of pc.blocks) {
          const bx = x(b.start);
          const bw = Math.max(3, x(b.end) - bx - 1);
          const color = b.reserved || b.booked ? C.booked : b.account ? C.account : C.pc;
          const minutesLong = (new Date(b.end) - new Date(b.start)) / 60000;
          const tags = [b.reserved ? t('rp.reserved') : b.booked ? t('rp.booked') : '', b.account ? t('rp.account') : t('rp.guest'), b.active ? t('rp.activeNow') : ''].filter(Boolean).join(' · ');
          const tip = [`PC ${pc.number} · ${hm(b.start)}–${hm(b.end)} (${duration(minutesLong)})`, b.tariff, b.timeValue ? money(b.timeValue) : '', tags].filter(Boolean).join('\n');
          out += `<rect class="blk${b.reserved ? ' reserved' : ''}${b.active ? ' active' : ''}" x="${bx.toFixed(1)}" y="${y + 4}" width="${bw.toFixed(1)}" height="${rowH - 8}" rx="4" fill="${color}" data-tip="${esc(tip)}"/>`;
        }
      });
      const now = new Date(d.now).getTime();
      if (now > ws && now < ws + span) {
        const nx = x(d.now);
        out += `<line class="now" x1="${nx}" x2="${nx}" y1="${top - 4}" y2="${top + d.pcs.length * rowH}"/><text class="now-l" x="${nx}" y="${top + d.pcs.length * rowH + 14}" text-anchor="middle">${esc(hm(d.now))}</text>`;
      }
      return `<div class="rp-scroll"><svg class="rp-gantt" viewBox="0 0 ${width} ${top + d.pcs.length * rowH + 20}" role="img" aria-label="${esc(t('rp.tab.day'))}">${out}</svg></div>`;
    }

    // ------------------------------------------------------------ CSV (opens in Excel: UTF-8 BOM, ';' separated)

    function exportCsv(name) {
      const { cols, rows } = TABLES[name](R.report);
      const q = (v) => { const s = String(v ?? ''); return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
      const value = (row, key, kind) => {
        const v = row[key];
        if (kind === 'dt') return v ? `${dmy(v)} ${hm(v)}` : '';
        if (kind === 'meter') return num((v || 0) * 100, 1);
        if (kind === 'pc') return `PC ${v}`;
        return typeof v === 'number' ? String(v).replace('.', ',') : v;
      };
      const lines = [cols.map(([, label]) => q(label)).join(';')]
        .concat(rows.map((row) => cols.map(([key, , kind]) => q(value(row, key, kind))).join(';')));
      const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `manajoy-${name}-${R.report.from}_${R.report.to}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 5000);
      toast(t('rp.csvDone'));
    }

    // ------------------------------------------------------------ settings (day start, PIN change)

    function settingsModal() {
      const options = Array.from({ length: 13 }, (_, h) => `<option value="${h}"${h === dayStart() ? ' selected' : ''}>${pad(h)}:00</option>`).join('');
      dialogModal(`<h2>${esc(t('rp.settings'))}</h2>
        <div class="form">
          <div class="field"><label for="rpDayStart">${esc(t('rp.dayStart'))}</label>
            <select class="input" id="rpDayStart">${options}</select>
            <div class="help">${esc(t('rp.dayStartHelp'))}</div></div>
          ${R.push && R.push.paired ? `<label class="toggle"><input type="checkbox" id="rpPush"${R.push.enabled ? ' checked' : ''}>${esc(t('rp.pushToggle'))}</label>
            <div class="help">${esc(t('rp.pushToggleHelp'))}</div>` : ''}
          <div class="field"><label>${esc(t('rp.changePin'))}</label>
            <input class="input" id="rpOld" type="password" inputmode="numeric" maxlength="8" autocomplete="off" placeholder="${esc(t('rp.oldPin'))}">
            <input class="input rp-gap" id="rpNew" type="password" inputmode="numeric" maxlength="8" autocomplete="off" placeholder="${esc(t('rp.newPin'))}">
            <input class="input rp-gap" id="rpNew2" type="password" inputmode="numeric" maxlength="8" autocomplete="off" placeholder="${esc(t('rp.newPin2'))}">
            <div class="help">${esc(t('rp.changePinHelp'))}</div></div>
        </div>`, t('rp.save'), async (modal) => {
        const hour = Number(modal.querySelector('#rpDayStart').value);
        const oldPin = modal.querySelector('#rpOld').value;
        const newPin = modal.querySelector('#rpNew').value;
        const newPin2 = modal.querySelector('#rpNew2').value;
        const push = modal.querySelector('#rpPush');
        try {
          if (push && R.push && push.checked !== R.push.enabled) {
            toast(t('rp.pushSaving'));
            R.push = await api('POST', 'analytics/push', { enabled: push.checked });
            toast(t(push.checked ? 'rp.pushOnDone' : 'rp.pushOffDone'));
          }
          if (hour !== dayStart()) {
            R.status = await api('PUT', 'analytics/settings', { dayStartHour: hour });
            R.from = null;
            R.report = null;
            R.dayData = null;
            R.day = null;
            toast(t('rp.dayStartSaved', { h: pad(hour) }));
          }
          if (newPin || oldPin) {
            if (newPin !== newPin2) throw { code: 'pin_mismatch' };
            const unlocked = await api('POST', 'analytics/pin', { oldPin, newPin });
            R.token = unlocked.token;
            toast(t('rp.pinChanged'));
          }
        } catch (error) {
          if (!fail(error)) toast(pinMessage(error));
        }
        update();
        if (R.status && R.status.unlocked) reloadTab();
      });
    }

    function pinMessage(error) {
      if (error.code === 'pin_wrong') return t('rp.pinWrong', { n: error.detail || '' });
      if (error.code === 'pin_locked') return t('rp.pinLocked', { m: Math.ceil(Number(error.detail || 300) / 60) });
      return I18N_HAS('rp.' + error.code) ? t('rp.' + error.code) : errorText(error).message;
    }

    // ------------------------------------------------------------ actions

    async function unlock(setting) {
      const pin = (document.getElementById('rpPin') || {}).value || '';
      const pin2 = (document.getElementById('rpPin2') || {}).value || '';
      if (setting && pin !== pin2) { R.pinError = { code: 'pin_mismatch' }; update(); return; }
      if (!/^\d{4,8}$/.test(pin)) { R.pinError = { code: 'pin_format' }; update(); return; }
      R.busy = true;
      R.pinError = null;
      update();
      try {
        const result = setting ? await api('POST', 'analytics/pin', { newPin: pin }) : await api('POST', 'analytics/unlock', { pin });
        R.token = result.token;
        R.busy = false;
        await loadStatus();
        if (document.querySelector('[data-rp="root"]')) reloadTab();
        return;
      } catch (error) {
        R.pinError = error;
        if (error.code === 'pin_locked') await loadStatus();
      }
      R.busy = false;
      update();
    }

    const actions = {
      'rp-unlock': () => unlock(false),
      'rp-setpin': () => unlock(true),
      'rp-lock': () => {
        R.token = null;
        R.report = null;
        R.dayData = null;
        R.pinError = null;
        loadStatus();
      },
      'rp-retry': () => (R.statusError || !R.status ? loadStatus() : reloadTab()),
      'rp-refresh': () => reloadTab(),
      'rp-settings': () => settingsModal(),
      'rp-tab': (el) => {
        R.tab = TABS.includes(el.dataset.tab) ? el.dataset.tab : 'overview';
        update();
        if (R.tab === 'day' ? !R.dayData : !R.report) reloadTab();
      },
      'rp-preset': (el) => {
        R.preset = PRESETS.includes(el.dataset.preset) ? el.dataset.preset : '7d';
        if (R.preset === 'custom') {
          if (!R.from) [R.from, R.to] = rangeFor('7d');
          update();
          return;
        }
        [R.from, R.to] = rangeFor(R.preset);
        R.group = 'auto';
        loadReport();
      },
      'rp-group': (el) => { R.group = el.dataset.group; loadReport(); },
      'rp-periods': () => { R.periodsOpen = !R.periodsOpen; update(); },
      'rp-filters': () => { R.filtersOpen = !R.filtersOpen; update(); },
      'rp-details': () => { R.detailsOpen = !R.detailsOpen; update(); },
      'rp-sync-details': () => {
        R.syncOpen = !R.syncOpen;
        const el = document.querySelector('[data-rp="push"]');
        if (el) el.innerHTML = pushLine();
      },
      'rp-sort': (el) => {
        const key = el.dataset.key;
        R.sort = { key, desc: R.sort.key === key ? !R.sort.desc : key !== 'number' && key !== 'zone' };
        update();
      },
      'rp-csv': (el) => exportCsv(el.dataset.table),
      'rp-day-step': (el) => { R.day = addDays(R.day || businessToday(), Number(el.dataset.step)); loadDay(); },
      'rp-day-today': () => { R.day = businessToday(); loadDay(); },
    };

    document.addEventListener('change', (event) => {
      const id = event.target.id;
      if (id === 'rpFrom' || id === 'rpTo') {
        const from = (document.getElementById('rpFrom') || {}).value;
        const to = (document.getElementById('rpTo') || {}).value;
        if (!from || !to) return;
        if (from > to) { toast(t('rp.rangeOrder')); return; }
        if ((parse(to) - parse(from)) / DAY_MS + 1 > 366) { toast(t('error.analytics_range')); return; }
        R.from = from;
        R.to = to;
        R.group = 'auto';
        loadReport();
      } else if (id === 'rpDay' && event.target.value) {
        R.day = event.target.value;
        loadDay();
      }
    });

    document.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter') return;
      if (event.target.id === 'rpPin' && document.getElementById('rpPin2')) { document.getElementById('rpPin2').focus(); return; }
      if (event.target.id === 'rpPin' || event.target.id === 'rpPin2') unlock(!!document.getElementById('rpPin2'));
    });

    // One tooltip for every chart: follows the pointer over anything with data-tip (hit areas are larger than marks).
    const tip = document.createElement('div');
    tip.className = 'rp-tip';
    tip.hidden = true;
    document.body.appendChild(tip);
    document.addEventListener('mousemove', (event) => {
      const target = event.target.closest && event.target.closest('.rp [data-tip]');
      if (!target) { tip.hidden = true; return; }
      tip.textContent = target.getAttribute('data-tip');
      tip.hidden = false;
      const box = tip.getBoundingClientRect();
      const x = Math.min(window.innerWidth - box.width - 8, event.clientX + 14);
      const y = event.clientY + 16 + box.height > window.innerHeight ? event.clientY - box.height - 12 : event.clientY + 16;
      tip.style.left = `${Math.max(8, x)}px`;
      tip.style.top = `${Math.max(8, y)}px`;
    });

    // Legend swatches take their color from data-c (no inline style attributes under the CSP).
    new MutationObserver(() => {
      for (const sw of document.querySelectorAll('.rp .sw[data-c]:not([data-done])')) {
        sw.style.background = sw.dataset.c;
        sw.dataset.done = '1';
      }
    }).observe(document.body, { childList: true, subtree: true });

    const I18N_HAS = (key) => window.MJ_I18N.uz[key] !== undefined;

    return {
      render,
      enter,
      // The shared PIN lock, for bar.js.
      lock: {
        token: () => R.token,
        unlocked: () => !!(R.status && R.status.unlocked && R.token),
        ensure: () => { if (!R.status || !R.status.unlocked) loadStatus(); },
        html: () => (R.statusError ? errorNote(R.statusError) : R.status ? lockScreen() : '<div class="skel skel-block"></div>'),
        expired: (error) => { const gone = fail(error); if (gone) update(); return gone; },
        pushLine: () => pushLine(),
        onChange: (fn) => listeners.push(fn),
      },
      act: (name, el) => { if (actions[name]) { actions[name](el); return true; } return false; },
      settingsCard: () => `<section class="card settings-shortcut">
          <h2 class="settings-title"><span class="settings-icon">${ctx.icon.reports}</span>${esc(t('rp.title'))}</h2>
          <p class="muted small">${esc(t('rp.settingsCard', { h: pad(dayStart()) }))}</p>
          <div class="btns"><button class="btn btn-secondary btn-sm" data-act="nav" data-section="reports">${esc(t('rp.open'))}</button></div>
        </section>`,
    };
  }

  const LOCK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><rect x="4.5" y="10.5" width="15" height="10" rx="2.5"/><path d="M8 10.5V7.5a4 4 0 018 0v3"/></svg>';
  const DOWNLOAD = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>';

  window.MJReports = { create, LOCK };
})();
