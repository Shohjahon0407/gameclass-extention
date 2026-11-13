// Manajoy GameClass — setup wizard and dashboard (vanilla JS, no build step, CSP: no inline code).
// Talks only to this window's own loopback API (/api/setup/*) with the one-time token from the URL fragment.
'use strict';

(() => {
  const I18N = window.MJ_I18N;
  const WIZARD = ['preflight', 'database', 'install', 'pair', 'done'];
  const CODE_ALPHABET = /[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]/;
  const POLL_MS = 4000;
  const MONITOR_POLL_MS = 3000;
  const PING_MS = 10000;
  const MAP = window.MJMap;

  const S = {
    token: null,
    lang: 'uz',
    state: null,
    view: 'loading',
    busy: false,
    error: null,
    preflight: null,
    db: null,
    install: null,
    pair: null,
    pairFromDashboard: false,
    dash: null,
    uninstall: null,
    release: null,
    prefs: {},
    layout: null,
    tab: null,
    filter: { q: '', zone: '', state: '' },
    selected: null,
    mapView: null,
    fullView: null,
    editDraft: null,
    editReturn: 'dashboard',
    section: 'hall',
    alertsOpen: false,
    form: { adminUser: 'sa', adminPassword: '', windowsAuth: false, server: '', database: '', code: '', dropLogin: false },
  };

  const $view = document.getElementById('view');
  const $rail = document.getElementById('rail');
  const $toast = document.getElementById('toast');
  const $modal = document.getElementById('modal');
  const $demo = document.getElementById('demo');
  let pollTimer = null;
  let toastTimer = null;
  let editor = null;
  let regionCache = {};

  // ---------------------------------------------------------------- helpers

  function t(key, vars) {
    let text = (I18N[S.lang] && I18N[S.lang][key]) ?? I18N.uz[key] ?? key;
    if (vars) {
      for (const [name, value] of Object.entries(vars)) text = text.split('{' + name + '}').join(String(value));
    }
    return text;
  }

  const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  async function api(method, path, body, extraHeaders) {
    let response;
    try {
      response = await fetch('/api/setup/' + path, {
        method,
        headers: Object.assign({ 'X-Setup-Token': S.token || '' }, body ? { 'Content-Type': 'application/json' } : {}, extraHeaders || {}),
        body: body ? JSON.stringify(body) : undefined,
      });
    } catch {
      throw { code: 'network' };
    }
    if (response.status === 204) return null;
    let data = null;
    try { data = await response.json(); } catch { /* empty body */ }
    if (!response.ok) throw (data && data.error) || { code: response.status === 403 ? 'network' : 'unexpected', detail: 'HTTP ' + response.status };
    return data;
  }

  function errorText(error) {
    const code = (error && error.code) || 'unexpected';
    const key = 'error.' + code;
    const known = (I18N.uz[key] !== undefined);
    return { message: known ? t(key) : t('error.unexpected'), detail: (error && error.detail) || (known ? '' : code) };
  }

  function minutes(total) {
    if (total == null) return '';
    const h = Math.floor(total / 60);
    const m = total % 60;
    if (h > 0) return m ? `${h} ${t('common.hour')} ${m} ${t('common.min')}` : `${h} ${t('common.hour')}`;
    return `${m} ${t('common.min')}`;
  }

  function ago(iso) {
    if (!iso) return '';
    const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
    if (seconds < 5) return t('common.now');
    if (seconds < 60) return t('common.ago', { t: `${seconds} ${t('common.sec')}` });
    if (seconds < 3600) return t('common.ago', { t: `${Math.round(seconds / 60)} ${t('common.min')}` });
    return t('common.ago', { t: `${Math.round(seconds / 3600)} ${t('common.hour')}` });
  }

  function clock(iso) {
    const d = new Date(iso);
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }

  /** GameClass stores the currency as typed by the club ("so'm", "сум", "UZS"…): show it in the window's language. */
  function cur(currency) {
    if (!currency) return '';
    const key = String(currency).replace(/[’ʻʼ‘`]/g, "'").toLowerCase().trim();
    return ["so'm", 'som', 'sum', 'сум', 'uzs'].includes(key) ? t('money.som') : currency;
  }

  function fmtMoney(value, currency) {
    if (value == null) return '—';
    // Space-grouped thousands ("20 000"), the way prices are written in Uzbekistan.
    const text = Number(value).toLocaleString('ru-RU', { maximumFractionDigits: 2 });
    const unit = cur(currency);
    return unit ? `${text} ${unit}` : text;
  }

  function toast(text) {
    $toast.textContent = text;
    $toast.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { $toast.hidden = true; }, 5000);
  }

  const ICON = {
    check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.2 4.2L19 7"/></svg>',
    warn: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M12 7v6M12 17h.01"/></svg>',
    fail: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M7 7l10 10M17 7L7 17"/></svg>',
    info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M12 11v6M12 7h.01"/></svg>',
    shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M12 3l7 3v5c0 4.5-3 8.3-7 10-4-1.7-7-5.5-7-10V6l7-3z"/><path d="M9 12l2 2 4-4" stroke-linecap="round"/></svg>',
    bolt: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M13 2L4 14h7l-1 8 9-12h-7l1-8z"/></svg>',
    send: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M22 2L11 13"/><path d="M22 2l-7 20-4-9-9-4 20-7z"/></svg>',
    hall: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><rect x="3" y="3" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="2"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="2"/></svg>',
    prices: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M3 12.6V4a1 1 0 011-1h8.6l8.7 8.7a1.5 1.5 0 010 2.1l-7.5 7.5a1.5 1.5 0 01-2.1 0L3 12.6z"/><circle cx="8" cy="8" r="1.6"/></svg>',
    settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 00.3 1.8l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-1.8-.3 1.7 1.7 0 00-1 1.5V21a2 2 0 11-4 0v-.1a1.7 1.7 0 00-1.1-1.5 1.7 1.7 0 00-1.8.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00.3-1.8 1.7 1.7 0 00-1.5-1H3a2 2 0 110-4h.1a1.7 1.7 0 001.5-1.1 1.7 1.7 0 00-.3-1.8l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 001.8.3H9a1.7 1.7 0 001-1.5V3a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.8-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.8V9a1.7 1.7 0 001.5 1H21a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z"/></svg>',
    monitor: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8 21h8M12 17v4"/></svg>',
    pencil: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M4 20h4L19 9a2.8 2.8 0 00-4-4L4 16v4z"/><path d="M13.5 6.5l4 4"/></svg>',
    refresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M20 11a8 8 0 10-2.3 5.7"/><path d="M20 4v7h-7"/></svg>',
    bar: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M7 3h10l-1 18H8L7 3z"/><path d="M7.4 9h9.2"/></svg>',
    reports: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/></svg>',
    pointer: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M6 3l12 7-5.2 1.6L10 17 6 3z"/><rect x="11" y="15" width="10" height="6" rx="1.5" stroke-dasharray="2 2"/></svg>',
  };

  // «Hisobot» lives in reports.js; it shares the helpers and its own data-act names start with "rp-".
  const REPORTS = window.MJReports.create({ t, esc, api, fmtMoney, toast, errorText, dialogModal, icon: ICON });
  // «Bar» (bar.js): the owner's stock book, behind the same PIN as «Hisobot»; its actions start with "bar-".
  const BAR = window.MJBar.create({ t, esc, api, fmtMoney, toast, errorText, dialogModal, icon: ICON, lock: REPORTS.lock });

  const badge = (level) => level === 'pending' ? '<span class="spinner"></span>' : ICON[level === 'ok' ? 'check' : level] || ICON.info;

  function checkItem(check, extraHint) {
    if (check.id === 'stop_old' && check.detail === 'new') check = { ...check, id: 'stop_old_new', detail: '' };
    const level = check.level;
    const hintKey = `check.${check.id}.${level}`;
    const hint = I18N.uz[hintKey] !== undefined ? t(hintKey) : '';
    return `<li class="check ${esc(level)}">
      <span class="badge">${badge(level)}</span>
      <div>
        <div class="title">${esc(t('check.' + check.id))}</div>
        ${hint ? `<div class="hint">${esc(hint)}</div>` : ''}
        ${extraHint ? `<div class="hint">${esc(extraHint)}</div>` : ''}
        ${check.detail ? `<div class="detail">${esc(check.detail)}</div>` : ''}
      </div>
    </li>`;
  }

  // Planned / running steps use the future-tense "plan.*" texts when there are some.
  const planTitle = (id) => (I18N.uz['plan.' + id] !== undefined ? t('plan.' + id) : t('check.' + id));

  const plannedList = (ids) => `<ul class="checks">${ids.map((id, i) => `<li class="check planned">
      <span class="badge">${i + 1}</span><div><div class="title">${esc(planTitle(id))}</div></div></li>`).join('')}</ul>`;

  const pendingList = (ids) => `<ul class="checks">${ids.map((id) => `<li class="check pending">
      <span class="badge">${badge('pending')}</span><div><div class="title">${esc(planTitle(id))}</div></div></li>`).join('')}</ul>`;

  const errorBox = (error) => {
    if (!error) return '';
    const { message, detail } = errorText(error);
    return `<div class="error-box" role="alert">${esc(message)}${detail ? `<span class="tech">${esc(detail)}</span>` : ''}</div>`;
  };

  const button = (act, label, cls = 'btn-primary', opts = {}) =>
    `<button class="btn ${cls}" data-act="${act}" ${opts.disabled ? 'disabled' : ''}>${opts.spinner ? '<span class="spinner"></span>' : ''}${esc(label)}</button>`;

  // ---------------------------------------------------------------- rail

  let railHtml = '';

  /** The rail (club status) is redrawn only when its text changes, so polls do not flicker it. */
  function renderRail(force = true) {
    const html = railMarkup();
    if (!force && html === railHtml) return;
    railHtml = html;
    $rail.innerHTML = html;
  }

  /** Outside the wizard the rail can be tucked in to a narrow strip (remembered in prefs; tucked by default). */
  const railCollapsible = () => !(WIZARD.includes(S.view) && !S.pairFromDashboard) && S.view !== 'welcome';
  const railCollapsed = () => railCollapsible() && S.prefs.rail !== 'open';

  const localStatus = () => (S.dash && S.dash.local ? S.dash.local.status : 'starting');
  const clubName = () => (S.state && (S.state.clubName || S.state.machineName)) || '';

  /** Something on the settings page wants the owner's attention (shown as a dot on its menu item). */
  function settingsAttention() {
    const d = S.dash;
    if (!S.state || !S.state.paired || updateAvailable() || (S.release && S.release.available)) return true;
    if (d && d.local && !d.local.reachable) return true;
    if (d && d.push && d.push.enabled && d.push.lastError) return true;
    return !!(d && d.manajoy && d.manajoy.reachable && d.manajoy.stationsUnmapped > 0);
  }

  /** Zal / Narxlar / Sozlamalar and the monitor, in the full rail (mini = false) or the 76 px strip. */
  function railNav(mini) {
    const here = S.view === 'dashboard' || (S.view === 'pair' && S.pairFromDashboard) ? S.section : null;
    const status = localStatus();
    const item = (id, icon, label, badge, act = 'nav') => `<button class="${here === id && act === 'nav' ? 'on' : ''}" data-act="${act}" data-section="${id}"
        title="${esc(id === 'hall' && mini ? `${label} · ${clubName()} · ${t('dash.status.' + status)}` : label)}" aria-label="${esc(label)}"${here === id ? ' aria-current="page"' : ''}>
        ${icon}<span class="lbl">${esc(label)}</span>${badge ? `<i class="nav-badge ${badge}"></i>` : ''}</button>`;
    return `<nav class="rail-nav${mini ? ' mini' : ''}">
        ${item('hall', ICON.hall, t('nav.hall'), mini ? statusLevel(status) : '')}
        ${item('prices', ICON.prices, t('nav.prices'), '')}
        ${item('reports', ICON.reports, t('nav.reports'), '')}
        ${item('bar', ICON.bar, t('nav.bar'), S.barAlerts && S.barAlerts.length ? 'warn' : '')}
        ${item('settings', ICON.settings, t('nav.settings'), settingsAttention() ? 'warn' : '')}
        <span class="nav-sep"></span>
        ${item('monitor', ICON.monitor, t(mini ? 'mon.title' : 'mon.open'), '', 'monitor')}
      </nav>`;
  }

  function railMarkup() {
    document.body.classList.toggle('rail-collapsed', railCollapsed());
    if (railCollapsed()) {
      return `
      <div class="rail-mini">
        <button class="rail-toggle" data-act="rail-toggle" title="${esc(t('rail.expand'))}" aria-label="${esc(t('rail.expand'))}">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9 6l6 6-6 6"/></svg>
        </button>
        <img class="mini-logo" src="logo-white.png" alt="">
        ${S.state && S.state.installed ? railNav(true) : ''}
        <span class="spacer"></span>
        <div class="lang vertical" role="group" aria-label="Language">
          <button data-act="lang" data-lang="uz" class="${S.lang === 'uz' ? 'on' : ''}">UZ</button>
          <button data-act="lang" data-lang="ru" class="${S.lang === 'ru' ? 'on' : ''}">RU</button>
        </div>
      </div>`;
    }
    return railFull();
  }

  function railFull() {
    const inWizard = WIZARD.includes(S.view) && !S.pairFromDashboard;
    const index = WIZARD.indexOf(S.view);
    const steps = WIZARD.map((id, i) => {
      const cls = i < index ? 'done' : i === index ? 'active' : '';
      const dot = i < index ? ICON.check.replace('stroke-width="3"', 'stroke-width="3" width="14" height="14"') : String(i + 1);
      return `<li class="step ${cls}"><span class="dot">${dot}</span><span class="label">${esc(t('steps.' + id))}</span></li>`;
    }).join('');

    let middle = '';
    if (inWizard) {
      middle = `<ol class="steps">${steps}</ol>`;
    } else if (S.state && S.state.installed) {
      const status = localStatus();
      middle = `<div class="rail-card">
        <div class="label">${esc(t('dash.club'))}</div>
        <div class="value">${esc(clubName())}</div>
        <div class="meta"><i class="nav-badge ${statusLevel(status)}"></i>${esc(t('dash.status.' + status))}</div>
      </div>
      ${railNav(false)}`;
    }

    return `
      <div class="brand">
        <img src="logo-white.png" alt="">
        <div class="grow"><div class="brand-name">${esc(t('app.title'))}</div><div class="brand-sub">${esc(t('brand.biznes'))}</div></div>
        ${railCollapsible() ? `<button class="rail-toggle" data-act="rail-toggle" title="${esc(t('rail.collapse'))}" aria-label="${esc(t('rail.collapse'))}">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M15 6l-6 6 6 6"/></svg></button>` : ''}
      </div>
      ${middle}
      <div class="rail-foot">
        <div class="lang" role="group" aria-label="Language">
          <button data-act="lang" data-lang="uz" class="${S.lang === 'uz' ? 'on' : ''}">UZ</button>
          <button data-act="lang" data-lang="ru" class="${S.lang === 'ru' ? 'on' : ''}">RU</button>
        </div>
        <div class="version">${S.state ? esc(`v${S.state.version} · ${S.state.machineName}`) : ''}</div>
      </div>`;
  }

  // ---------------------------------------------------------------- views

  const views = {
    loading: () => `<div class="skel-page" aria-busy="true"><div class="skel skel-title"></div><div class="skel skel-line"></div><div class="skel skel-block"></div></div>`,

    welcome: () => `
      <div class="kicker">${esc(t('welcome.kicker'))}</div>
      <h1>${esc(t('welcome.title'))}</h1>
      <p class="lead">${esc(t('welcome.lead'))}</p>
      <div class="features">
        ${[['shield', 'f1'], ['bolt', 'f2'], ['send', 'f3']].map(([icon, key]) => `
          <div class="card feature">
            <div class="icon">${ICON[icon]}</div>
            <h3>${esc(t(`welcome.${key}.title`))}</h3>
            <p>${esc(t(`welcome.${key}.text`))}</p>
          </div>`).join('')}
      </div>
      <div class="card">
        <h2>${esc(t('welcome.need'))}</h2>
        <ul class="needs"><li>${esc(t('welcome.need1'))}</li><li>${esc(t('welcome.need2'))}</li></ul>
      </div>
      <div class="actions">${button('start', t('welcome.start'))}</div>`,

    preflight: () => {
      const report = S.preflight;
      let body = pendingList(['windows', 'sql_service', 'gameclass_server', 'internet']);
      let verdict = `<div class="verdict info"><span class="spinner"></span>${esc(t('preflight.running'))}</div>`;
      if (report) {
        const level = report.verdict;
        verdict = `<div class="verdict ${esc(level)}">${badge(level)}${esc(t('preflight.' + level))}</div>`;
        const order = { fail: 0, warn: 1, ok: 2, info: 3 };
        body = `<ul class="checks">${[...report.checks].sort((a, b) => order[a.level] - order[b.level]).map((c) => checkItem(c)).join('')}</ul>`;
      }
      return `
        <h1>${esc(t('preflight.title'))}</h1>
        <p class="lead">${esc(t('preflight.lead'))}</p>
        ${S.error ? errorBox(S.error) : verdict}
        ${body}
        <div class="actions footer">
          ${button('back-welcome', t('common.back'), 'btn-secondary')}
          <span class="spacer"></span>
          ${button('preflight', t('preflight.again'), 'btn-secondary', { disabled: S.busy })}
          ${button('to-database', t('common.continue'), 'btn-primary', { disabled: S.busy || !report || report.verdict === 'fail' })}
        </div>`;
    },

    database: () => {
      const f = S.form;
      const result = S.db;
      let resultHtml = '';
      if (S.busy) {
        resultHtml = `<div class="verdict info"><span class="spinner"></span>${esc(t('database.running'))}</div>`;
      } else if (result) {
        resultHtml = `<div class="verdict ${result.success ? 'ok' : 'fail'}">${badge(result.success ? 'ok' : 'fail')}${esc(t(result.success ? 'database.success' : 'database.failed'))}</div>
          <ul class="checks">${result.checks.map((c) => checkItem(c)).join('')}</ul>`;
      }
      return `
        <h1>${esc(t('database.title'))}</h1>
        <p class="lead">${esc(t('database.lead'))}</p>
        <div class="card">
          <div class="form">
            <div class="row-2">
              <div class="field">
                <label for="adminUser">${esc(t('database.adminUser'))}</label>
                <input class="input" id="adminUser" autocomplete="off" spellcheck="false" ${f.windowsAuth || S.busy ? 'disabled' : ''}>
              </div>
              <div class="field">
                <label for="adminPassword">${esc(t('database.adminPassword'))}</label>
                <input class="input" id="adminPassword" type="password" autocomplete="new-password" ${f.windowsAuth || S.busy ? 'disabled' : ''}>
              </div>
            </div>
            <div class="field help">${esc(t('database.passwordHint'))}</div>
            <label class="toggle"><input type="checkbox" id="windowsAuth" ${f.windowsAuth ? 'checked' : ''} ${S.busy ? 'disabled' : ''}>${esc(t('database.windowsAuth'))}</label>
            <details class="advanced">
              <summary>${esc(t('database.advanced'))}</summary>
              <div class="row-2">
                <div class="field"><label for="server">${esc(t('database.server'))}</label><input class="input" id="server" placeholder="(local)" spellcheck="false"></div>
                <div class="field"><label for="database">${esc(t('database.database'))}</label><input class="input" id="database" placeholder="GameClass" spellcheck="false"></div>
              </div>
            </details>
          </div>
        </div>
        ${S.error ? `<div class="form-error">${errorBox(S.error)}</div>` : ''}
        ${resultHtml}
        <div class="actions footer">
          ${button('to-preflight', t('common.back'), 'btn-secondary', { disabled: S.busy })}
          <span class="spacer"></span>
          ${result && result.success
            ? button('to-install', t('common.continue'))
            : button('database', t('database.connect'), 'btn-primary', { disabled: S.busy, spinner: S.busy })}
        </div>`;
    },

    install: () => {
      const result = S.install;
      let body = '';
      if (S.busy) {
        body = `<div class="verdict info"><span class="spinner"></span>${esc(t('install.running'))}</div>`
          + pendingList(['copy_files', 'store_secret', 'register_service', 'start_service', 'read_gameclass']);
      } else if (result) {
        const count = result.local ? result.local.pcs.length : 0;
        body = `<div class="verdict ${result.success ? 'ok' : 'fail'}">${badge(result.success ? 'ok' : 'fail')}${esc(result.success ? t('install.success', { n: count }) : t('install.failed'))}</div>
          <ul class="checks">${result.steps.map((c) => checkItem(c)).join('')}</ul>`;
      } else {
        body = plannedList(['copy_files', 'store_secret', 'register_service', 'start_service', 'read_gameclass', 'shortcut']);
      }
      return `
        <h1>${esc(t('install.title'))}</h1>
        <p class="lead">${esc(t('install.lead'))}</p>
        ${S.error ? errorBox(S.error) : ''}
        ${body}
        <div class="actions footer">
          ${button('to-database', t('common.back'), 'btn-secondary', { disabled: S.busy || (result && result.success) })}
          <span class="spacer"></span>
          ${result && result.success
            ? button('to-pair', t('common.continue'))
            : button('install', result ? t('common.retry') : t('install.run'), 'btn-primary', { disabled: S.busy, spinner: S.busy })}
        </div>`;
    },

    pair: () => {
      const result = S.pair;
      if (result) {
        const pushed = result.push && result.push.lastSuccessAt;
        const pushError = result.push && result.push.lastError;
        return `
          <div class="hero-ok">
            <div class="big-check">${ICON.check}</div>
            <h1>${esc(t('pair.success', { club: result.clubName }))}</h1>
            <p class="lead">${esc(pushed ? t('pair.pushOk') : pushError ? pushText(pushError) : t('pair.pushWait'))}</p>
          </div>
          <div class="actions center">${button(S.pairFromDashboard ? 'to-dashboard' : 'to-done', t('common.continue'))}</div>`;
      }

      const shown = S.form.code || 'ABCD-2345';
      return `
        <h1>${esc(t('pair.title'))}</h1>
        <p class="lead">${esc(t('pair.lead'))}</p>
        <div class="pair-grid">
          <div class="card">
            <ol class="howto">
              <li>${esc(t('pair.how1'))}</li>
              <li>${esc(t('pair.how2'))}</li>
              <li>${esc(t('pair.how3'))}</li>
            </ol>
            <div class="field">
              <label for="code" class="small muted">${esc(t('pair.code'))}</label>
              <input class="code-input" id="code" maxlength="9" placeholder="XXXX-XXXX" autocomplete="off" spellcheck="false" ${S.busy ? 'disabled' : ''}>
            </div>
            ${S.state && S.state.paired ? `<p class="small muted">${esc(t('pair.repairNote'))}</p>` : ''}
          </div>
          <div class="phone" aria-hidden="true">
            <div class="screen">
              <div class="cap">${esc(t('pair.code'))}</div>
              <div class="code">${esc(shown)}</div>
              <div class="timer">15:00</div>
            </div>
            <div class="caption">${esc(t('brand.biznes'))}</div>
          </div>
        </div>
        ${S.error ? `<div class="form-error">${errorBox(S.error)}</div>` : ''}
        <div class="actions footer">
          ${S.pairFromDashboard
            ? button('to-dashboard', t('common.back'), 'btn-secondary', { disabled: S.busy })
            : button('skip-pair', t(S.state && S.state.paired ? 'pair.keep' : 'pair.later'), 'btn-secondary', { disabled: S.busy })}
          <span class="spacer"></span>
          ${button('pair', S.busy ? t('pair.running') : t('pair.connect'), 'btn-primary', { disabled: S.busy || S.form.code.replace('-', '').length !== 8, spinner: S.busy })}
        </div>`;
    },

    done: () => `
      <div class="hero-ok">
        <div class="big-check">${ICON.check}</div>
        <h1>${esc(t('done.title'))}</h1>
        <p class="lead">${esc(t('done.lead'))}</p>
        <p class="muted">${esc(t('done.tip'))}</p>
      </div>
      <div class="actions center">${button('to-dashboard', t('done.dashboard'))}</div>`,

    dashboard: () => renderDashboard(),

    monitor: () => renderMonitor(),

    mapFull: () => renderMapFull(),

    mapEdit: () => `
      <div class="ed-head">
        <h1>${esc(t('edit.title'))}</h1>
        <p class="lead">${esc(t('edit.lead'))}</p>
      </div>
      <div id="editorRoot"></div>`,

    uninstalled: () => `
      <div class="hero-ok">
        <div class="big-check">${ICON.check}</div>
        <h1>${esc(t('uninstall.done'))}</h1>
        <p class="lead">${esc(t('uninstall.doneText'))}</p>
        ${S.uninstall ? `<ul class="checks">${S.uninstall.steps.map((c) => checkItem(c)).join('')}</ul>` : ''}
      </div>
      <div class="actions center">${button('close', t('common.close'))}</div>`,
  };

  function pushText(error) {
    if (!error) return t('push.ok');
    if (error.startsWith('rejected: bridge key')) return t('push.keyWrong');
    if (error.startsWith('rejected: live status')) return t('push.disabled');
    if (error.startsWith('rejected: snapshot')) return t('push.format');
    if (error.startsWith('timeout')) return t('push.timeout');
    if (error.startsWith('network')) return t('push.network');
    return error;
  }

  function statusLevel(status) {
    if (status === 'ok') return 'ok';
    if (status === 'degraded' || status === 'stale' || status === 'starting') return 'warn';
    return 'fail';
  }

  // ---------------------------------------------------------------- dashboard (regions patched on every poll)

  const MAP_FULL_ICON = '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/></svg>';

  const pcsOf = () => (S.dash && S.dash.local && S.dash.local.pcs) || [];
  const layoutDoc = () => (S.layout && S.layout.layout) || { pcs: {}, objects: [] };
  const hasMap = () => MAP.placedNames(layoutDoc(), pcsOf()).length > 0;
  const currentTab = () => S.tab || S.prefs.dashTab || (hasMap() ? 'map' : 'list');

  function hm(total) {
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
  }

  /**
   * Texts shown for a PC: `sub` in lists and details; on the map tile `short` with a mark (`icon`:
   * time / resv / post), a corner `badge` for a busy PC that is booked next, and the full `title` on hover.
   */
  function pcLook(pc) {
    const state = t('state.' + pc.state);
    let look;
    if (pc.state === 'busy') {
      if (pc.postpay) look = { sub: t('pc.postpay'), short: t('pc.postpay'), icon: 'post' };
      else if (pc.timeLeftMinutes != null) look = { sub: t('pc.left', { t: minutes(pc.timeLeftMinutes) }), short: hm(pc.timeLeftMinutes), icon: 'time' };
      else look = { sub: '', short: state };
      if (pc.nextReservationAt) look.badge = 'resv';
    } else if (pc.nextReservationAt) {
      look = { sub: t('pc.reserved', { t: clock(pc.nextReservationAt) }), short: clock(pc.nextReservationAt), icon: 'resv' };
    } else {
      look = { sub: '', short: state };
    }
    const next = pc.state === 'busy' && pc.nextReservationAt ? `${t('pcd.next')}: ${clock(pc.nextReservationAt)}` : '';
    look.title = [`PC ${pc.number}`, state, look.sub, next].filter(Boolean).join(' · ');
    return look;
  }

  const NO_ZONE = '__none__';

  function matches(pc) {
    const f = S.filter;
    if (S.view === 'monitor') return true;
    if (f.zone && (f.zone === NO_ZONE ? !!pc.zone : (pc.zone || '') !== f.zone)) return false;
    if (f.state === 'other' ? !['disabled', 'unknown'].includes(pc.state) : f.state && pc.state !== f.state) return false;
    if (f.q) {
      const q = f.q.toLowerCase();
      return String(pc.number).startsWith(q) || pc.name.toLowerCase() === q || (pc.zone || '').toLowerCase().includes(q);
    }
    return true;
  }

  const filtering = () => !!(S.filter.q || S.filter.zone || S.filter.state);

  function syncInfo() {
    const sync = S.layout && S.layout.sync;
    if (!sync) return { state: 'none', cls: 'info', text: '' };
    const cls = { synced: 'ok', local: 'info', pending: 'warn', offline: 'warn', conflict: 'fail', error: 'fail' }[sync.state] || 'info';
    let text = t('sync.' + sync.state);
    if (sync.state === 'error' && sync.detail === 'key_rejected') text = t('sync.keyRejected');
    return { state: sync.state, cls, text };
  }

  /** The dashboard has three pages in the rail: the live hall, prices and settings. */
  function renderDashboard() {
    if (S.section === 'prices') return renderPrices();
    if (S.section === 'settings') return renderSettings();
    if (S.section === 'reports') return REPORTS.render();
    if (S.section === 'bar') return BAR.render();
    return `<div class="hall">
      <div class="hall-head" data-region="head">${dashHead()}</div>
      <div class="hall-alerts" data-region="alerts">${dashAlerts()}</div>
      <div class="hall-body">
        <div class="hall-main">
          <div class="hall-bar">
            <div data-region="stats">${dashStats()}</div>
            <div class="hall-tools" data-region="tools">${pcsTools()}</div>
          </div>
          <div class="hall-map" data-region="pcs">${pcsBody()}</div>
        </div>
        <div class="hall-side-slot" data-region="detail">${pcDetail()}</div>
      </div>
    </div>`;
  }

  /** One line: club, live status and the map's own controls (other pages live in the rail). */
  function dashHead() {
    const d = S.dash;
    const status = localStatus();
    const sync = syncInfo();
    const any = pcsOf().length > 0;
    const tab = currentTab();
    return `<div class="hh-title">
        <h1 title="${esc(clubName())}">${esc(clubName())}</h1>
        <span class="pill sm ${statusLevel(status)}"><span class="led"></span>${esc(t('dash.status.' + status))}</span>
        <span class="updated">${esc(d ? t('dash.updated', { t: ago(d.generatedAt) }) : '')}</span>
      </div>
      <div class="hh-actions">
        ${sync.text ? `<span class="sync ${sync.cls}" title="${esc(t('sync.hint'))}"><span class="led"></span>${esc(sync.text)}</span>` : ''}
        ${any ? `<div class="tabs" role="tablist">
          <button role="tab" data-act="tab" data-tab="map" class="${tab === 'map' ? 'on' : ''}">${esc(t('map.tab.map'))}</button>
          <button role="tab" data-act="tab" data-tab="list" class="${tab === 'list' ? 'on' : ''}">${esc(t('map.tab.list'))}</button>
        </div>` : ''}
        ${any ? `<button class="btn btn-secondary btn-sm" data-act="map-edit" title="${esc(t(hasMap() ? 'map.edit' : 'map.draw'))}">${ICON.pencil}<span class="lbl">${esc(t(hasMap() ? 'map.edit' : 'map.draw'))}</span></button>` : ''}
        ${any ? `<button class="btn btn-secondary btn-sm" data-act="map-full" title="${esc(t('map.full'))}">${MAP_FULL_ICON}<span class="lbl">${esc(t('map.fullShort'))}</span></button>` : ''}
      </div>`;
  }

  /** 0.7.0 vs 0.6.12 by numbers (build metadata after '+' ignored); >0 when a is newer. */
  function compareVersions(a, b) {
    const pa = String(a).split('+')[0].split('.').map(Number);
    const pb = String(b).split('+')[0].split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
      const d = (pa[i] || 0) - (pb[i] || 0);
      if (d) return d;
    }
    return 0;
  }

  /** Offered only when THIS window is newer than the running service (never a downgrade). */
  function updateAvailable() {
    const local = S.dash && S.dash.local;
    const installed = local && local.reachable ? local.bridgeVersion : null;
    return installed && S.state && compareVersions(S.state.version, installed) > 0 && !S.state.runningFromInstallDir ? installed : null;
  }

  /** Everything that needs a reaction, most serious first. `hall: false` ones are shown only in Settings. */
  function alertList() {
    const d = S.dash;
    const local = d && d.local;
    const push = d && d.push;
    const manajoy = d && d.manajoy;
    const list = [];
    if (S.error) {
      const { message, detail } = errorText(S.error);
      list.push({ level: 'fail', text: message, detail });
    }
    const old = updateAvailable();
    if (old) {
      list.push({ level: 'info', text: t('dash.updateAvailable', { new: S.state.version, old }), acts: button('update', t('dash.update'), 'btn-primary btn-xs', { disabled: S.busy, spinner: S.busy }) });
    }
    if (S.state && !S.state.paired) {
      list.push({ level: 'info', bold: t('dash.notPaired') + '.', text: t('dash.notPairedText'), acts: button('repair', t('dash.pairNow'), 'btn-primary btn-xs') });
    }
    if (local && !local.reachable) {
      list.push({ level: 'fail', text: t('dash.serviceDown'), acts: button('restart', t('dash.restart'), 'btn-danger btn-xs', { disabled: S.busy }) });
    } else if (d && d.gameClassWindow && d.gameClassWindow.running > 1) {
      list.push({ level: 'fail', bold: t('gcw.twoTitle'), text: t('gcw.two', { n: d.gameClassWindow.running }) });
    } else if (d && d.gameClassWindow && d.gameClassWindow.hidden) {
      list.push({ level: 'info', bold: t('gcw.hiddenTitle'), text: t('gcw.hidden') });
    } else if (local && local.serverAlive === false) {
      list.push({ level: 'warn', text: t('dash.serverOffline') });
    } else if (local && local.status === 'unavailable' && local.lastError) {
      list.push({ level: 'fail', text: t('dash.status.unavailable'), detail: local.lastError, acts: button('restart', t('dash.restart'), 'btn-danger btn-xs', { disabled: S.busy }) });
    }
    if (push && push.enabled && push.lastError) {
      list.push({ level: 'warn', text: pushText(push.lastError) });
    }
    if (d && d.disk && d.disk.problem) {
      list.push({ level: 'warn', bold: t('disk.alertTitle'), text: t('disk.alertText'), detail: d.disk.findings.join('; ') });
    }
    if (manajoy && manajoy.reachable && manajoy.stationsUnmapped > 0) {
      list.push({ level: 'info', text: t('dash.unmappedHint', { n: manajoy.stationsUnmapped }), hall: false });
    }
    if (S.layout && S.layout.sync.state === 'conflict') {
      list.push({ level: 'warn', bold: t('sync.conflictTitle') + '.', text: t('sync.conflictText', { n: S.layout.sync.detail || '?' }),
        acts: button('layout-use-server', t('sync.useServer'), 'btn-secondary btn-xs', { disabled: S.busy })
          + button('layout-use-local', t('sync.useLocal'), 'btn-primary btn-xs', { disabled: S.busy }) });
    }
    if (S.barAlerts && S.barAlerts.length) {
      const names = S.barAlerts.slice(0, 4).map((a) => (a.state === 'low' ? t('bar.alertLow', { name: a.name, n: a.remaining }) : t('bar.alertOut', { name: a.name }))).join(' · ');
      list.push({ level: 'warn', bold: t('bar.alertTitle'), text: names + (S.barAlerts.length > 4 ? ' …' : ''),
        acts: `<button class="btn btn-secondary btn-xs" data-act="nav" data-section="bar">${esc(t('bar.open'))}</button>` });
    }
    const rank = { fail: 0, warn: 1, info: 2 };
    return list.sort((a, b) => rank[a.level] - rank[b.level]);
  }

  function alertHtml(a) {
    const full = [a.bold, a.text, a.detail].filter(Boolean).join(' ');
    return `<div class="alert compact ${a.level}" role="${a.level === 'fail' ? 'alert' : 'status'}">
      <span class="al-ic">${ICON[a.level] || ICON.info}</span>
      <div class="grow" title="${esc(full)}">${a.bold ? `<b>${esc(a.bold)}</b> ` : ''}${esc(a.text)}${a.detail ? ` <span class="al-detail">${esc(a.detail)}</span>` : ''}</div>
      ${a.acts ? `<div class="al-acts">${a.acts}</div>` : ''}
    </div>`;
  }

  /** At most two alerts above the map; the rest fold behind "Yana N ta xabar". */
  function dashAlerts() {
    const list = alertList().filter((a) => a.hall !== false);
    if (!list.length) return '';
    const shown = list.length > 2 && !S.alertsOpen ? list.slice(0, 2) : list;
    const toggle = list.length > 2
      ? `<button class="alerts-more" data-act="alerts-more">${esc(S.alertsOpen ? t('alerts.less') : t('alerts.more', { n: list.length - 2 }))}</button>`
      : '';
    return `<div class="alert-stack">${shown.map(alertHtml).join('')}${toggle}</div>`;
  }

  /** Status chips with counts; a click filters the map and the list (the same as the old stat cards). */
  function dashStats() {
    if (!S.dash) return `<div class="schips">${[1, 2, 3, 4].map(() => '<span class="skel skel-chip"></span>').join('')}</div>`;
    const local = S.dash.local;
    const counts = (local && local.counts) || {};
    const other = (counts.disabled || 0) + (counts.unknown || 0);
    const chip = (state, n, label, title) => `<button class="schip ${state || 'all'}${(S.filter.state || '') === state ? ' on' : ''}" data-act="filter-state" data-state="${state}"${title ? ` title="${esc(title)}"` : ''}>
      <i></i><span>${esc(label)}</span><b>${n}</b></button>`;
    return `<div class="schips" role="group">
      ${chip('', pcsOf().length, t('filter.all'))}
      ${chip('available', counts.available || 0, t('state.available'))}
      ${chip('busy', counts.busy || 0, t('state.busy'))}
      ${chip('offline', counts.offline || 0, t('state.offline'))}
      ${other ? chip('other', other, t('state.disabled'), `${t('state.disabled')} / ${t('state.unknown')}`) : ''}
    </div>`;
  }

  /** Zone chips (with counts) and the search. */
  function pcsTools() {
    const pcs = pcsOf();
    const zones = [];
    for (const pc of pcs) {
      const key = pc.zone || NO_ZONE;
      const zone = zones.find((z) => z.key === key);
      if (zone) zone.n++; else zones.push({ key, label: pc.zone || t('zone.none'), n: 1 });
    }
    const chip = (key, label, n) => `<button class="chip${S.filter.zone === key ? ' on' : ''}" data-act="filter-zone" data-zone="${esc(key)}">${esc(label)}<b>${n}</b></button>`;
    const chips = zones.length > 1 ? `<div class="chips zones" role="group">${zones.map((z) => chip(z.key, z.label, z.n)).join('')}</div>` : '';
    return `${chips}<span class="spacer"></span>
      <input class="search" id="pcSearch" type="search" autocomplete="off" spellcheck="false" placeholder="${esc(t('map.search'))}" aria-label="${esc(t('map.search'))}">
      ${filtering() ? `<button class="chip clear" data-act="filter-clear">${esc(t('filter.clear'))} ✕</button>` : ''}`;
  }

  function pcsSignature() {
    const pcs = pcsOf();
    const tab = S.view === 'monitor' ? (hasMap() ? 'map' : 'list') : S.view === 'mapFull' ? 'map' : currentTab();
    const parts = [S.view, tab, S.lang, S.dash ? 'live' : 'wait', pcs.map((pc) => `${pc.name}|${pc.zone}`).join(',')];
    if (tab === 'map') parts.push(JSON.stringify(layoutDoc()), S.layout ? S.layout.sync.state : '', (S.layout && S.layout.sync.skipped || []).join(','));
    if (tab === 'list' && S.view !== 'monitor') parts.push(S.filter.zone, S.filter.state, S.filter.q, S.filter.state ? pcs.map((pc) => pc.state).join(',') : '');
    return parts.join('§');
  }

  function pcsBody() {
    const pcs = pcsOf();
    if (!S.dash) return '<div class="skel skel-map" aria-busy="true"></div>';
    if (!pcs.length) return `<p class="muted pad">${esc(t('dash.noPcs'))}</p>`;
    const monitor = S.view === 'monitor';
    const full = S.view === 'mapFull';
    const tab = monitor ? (hasMap() ? 'map' : 'list') : full ? 'map' : currentTab();
    if (tab === 'map') {
      if (!S.layout) return '<div class="skel skel-map" aria-busy="true"></div>';
      if (!hasMap()) {
        return `<div class="map-empty">
          <div class="map-empty-art" aria-hidden="true">${[1, 2, 3, 4, 5, 6].map(() => '<span></span>').join('')}</div>
          <h2>${esc(t('map.emptyTitle'))}</h2>
          <p class="muted">${esc(t('map.emptyText'))}</p>
          ${button('map-edit', t('map.draw'))}
        </div>`;
      }
      const placed = new Set(MAP.placedNames(layoutDoc(), pcs));
      const missing = pcs.filter((pc) => !placed.has(pc.name)).map((pc) => pc.number);
      const skipped = (S.layout.sync.skipped || []);
      const notes = [];
      if (!monitor && missing.length) notes.push(`<span>${esc(t('map.notPlaced', { list: missing.join(', ') }))}</span>`);
      if (!monitor && skipped.length && S.state && S.state.paired) notes.push(`<span>${esc(t('sync.skipped', { n: skipped.length }))}</span>`);
      if (full) notes.length = 0;
      const svg = MAP.viewSvg({
        doc: layoutDoc(), pcs, look: pcLook, t, match: matches, selected: S.selected,
        viewBox: monitor ? null : full ? S.fullView : S.mapView, controls: !monitor,
        fullButton: monitor ? null : full ? 'map-full-exit' : 'map-full',
      });
      if (monitor) return svg;
      // The legend and notes sit in a strip under the map, so they never cover a PC.
      return `<div class="map-stage">${svg}<div class="map-foot">${mapLegend()}${notes.length ? `<div class="map-notes">${notes.join('')}</div>` : ''}</div></div>`;
    }
    const zones = [];
    for (const pc of pcs) {
      if (!matches(pc)) continue;
      let zone = zones.find((z) => z.name === pc.zone);
      if (!zone) zones.push(zone = { name: pc.zone, pcs: [] });
      zone.pcs.push(pc);
    }
    if (!zones.length) return `<p class="muted pad">${esc(t('filter.none'))}</p>`;
    const cta = !monitor && S.layout && !hasMap() ? `<div class="map-cta">
        <div class="map-empty-art small" aria-hidden="true">${[1, 2, 3, 4].map(() => '<span></span>').join('')}</div>
        <div class="grow"><b>${esc(t('map.emptyTitle'))}</b><div class="small">${esc(t('map.emptyText'))}</div></div>
        ${button('map-edit', t('map.draw'), 'btn-primary btn-sm')}
      </div>` : '';
    return `<div class="pc-list">${cta}${zones.map((zone) => `
      <div class="zone-title">${esc(zone.name || t('zone.none'))} <span>${zone.pcs.length}</span></div>
      <div class="pcs">${zone.pcs.map(pcTile).join('')}</div>`).join('')}</div>`;
  }

  /** Colours and the three tile marks, explained once under the map. */
  function mapLegend() {
    const states = ['available', 'busy', 'offline', 'disabled'].map((state) =>
      `<span class="lg ${state}"><i></i>${esc(t('state.' + state))}</span>`).join('');
    const marks = [['time', 'map.lg.left'], ['resv', 'map.lg.reserved'], ['post', 'map.lg.postpay']].map(([icon, key]) =>
      `<span class="lg mark"><svg viewBox="0 0 12 12" aria-hidden="true">${MAP.tileIcon(icon)}</svg>${esc(t(key))}</span>`).join('');
    return `<div class="legend">${states}<span class="lg-sep"></span>${marks}</div>`;
  }

  function pcTile(pc) {
    const { sub } = pcLook(pc);
    return `<button class="pc ${esc(pc.state)}${S.selected === pc.name ? ' sel' : ''}" data-act="pc" data-pc="${esc(pc.name)}">
      <div class="num">${esc(pc.number)}</div>
      <div class="st">${esc(t('state.' + pc.state))}</div>
      <div class="sub">${esc(sub)}</div>
    </button>`;
  }

  /** Poll update of the PC area without re-rendering it (keeps scroll, zoom and focus). */
  function patchPcs(el) {
    const pcs = pcsOf();
    if (el.querySelector('svg.cmap')) {
      MAP.patchView(el, { doc: layoutDoc(), pcs, look: pcLook, match: matches, selected: S.selected });
      return;
    }
    const byName = new Map(pcs.map((pc) => [pc.name, pc]));
    for (const tile of el.querySelectorAll('.pc[data-pc]')) {
      const pc = byName.get(tile.dataset.pc);
      if (!pc) continue;
      const cls = `pc ${pc.state}${S.selected === pc.name ? ' sel' : ''}`;
      if (tile.className !== cls) tile.className = cls;
      tile.querySelector('.st').textContent = t('state.' + pc.state);
      tile.querySelector('.sub').textContent = pcLook(pc).sub;
    }
  }

  function pcDetail() {
    const pc = S.selected && pcsOf().find((p) => p.name === S.selected);
    if (S.view === 'monitor') return '';
    const full = S.view === 'mapFull';
    if (!pc) {
      // The hall keeps this column: a hint until a PC is picked (hidden on narrow windows).
      return full ? '' : `<aside class="hall-side side-empty">
        <div class="se-art">${ICON.pointer}</div>
        <b>${esc(t('hall.pickTitle'))}</b>
        <p>${esc(t('hall.pickText'))}</p>
      </aside>`;
    }
    const { sub } = pcLook(pc);
    const onMap = !!layoutDoc().pcs[pc.name];
    const gc = I18N.uz['gc.' + pc.gcState] !== undefined ? t('gc.' + pc.gcState) : pc.gcState;
    return `${full ? '' : '<aside class="hall-side">'}<div class="pc-detail ${esc(pc.state)}" role="region" aria-label="PC ${esc(pc.number)}">
      <div class="pd-head">
        <div class="pd-num">PC ${esc(pc.number)}</div>
        <span class="pd-state">${esc(t('state.' + pc.state))}</span>
        <span class="spacer"></span>
        <button class="pd-close" data-act="pc-close" aria-label="${esc(t('common.close'))}">✕</button>
      </div>
      <dl class="kv">
        <div><dt>${esc(t('pcd.zone'))}</dt><dd>${esc(pc.zone || '—')}</dd></div>
        <div><dt>${esc(t('pcd.gc'))}</dt><dd>${esc(gc)}</dd></div>
        ${sub ? `<div><dt>${esc(t('pcd.session'))}</dt><dd>${esc(sub)}</dd></div>` : ''}
        ${pc.nextReservationAt && pc.state === 'busy' ? `<div><dt>${esc(t('pcd.next'))}</dt><dd>${esc(clock(pc.nextReservationAt))}</dd></div>` : ''}
      </dl>
      <div class="cashier-slot"></div>
      <div class="btns">
        ${onMap && !full && currentTab() !== 'map' ? button('pc-show', t('pcd.show'), 'btn-secondary btn-sm') : ''}
        ${!onMap ? button('map-edit', t('pcd.place'), 'btn-secondary btn-sm') : ''}
      </div>
    </div>${full ? '' : '</aside>'}`;
  }

  // ---------------------------------------------------------------- cashier assistant (price quotes only)
  // Lives in one persistent element that is re-attached to the PC detail panel after every poll re-render,
  // so typed amounts and focus survive. Prices come from GameClass's own tariff rules (Pricing/ in C#);
  // the session itself is still started in GameClass.

  const cashierEl = document.createElement('div');
  cashierEl.className = 'cashier';
  const C = { pc: null, state: null, pricing: null, tariffId: null, mode: 'duration', minutes: 60, money: null, variantId: null, quote: null, qerror: null, error: null, loading: false, loadedAt: 0, seq: 0 };
  let quoteTimer = null;
  const CASHIER_REFRESH_MS = 60000;

  function attachCashier() {
    const slot = $view.querySelector('.cashier-slot');
    if (slot && cashierEl.parentNode !== slot) slot.appendChild(cashierEl);
  }

  const money = (value) => fmtMoney(value, C.pricing && C.pricing.currency);

  function openCashier(pc) {
    clearTimeout(quoteTimer);
    Object.assign(C, { pc: pc.number, state: pc.state, pricing: null, tariffId: null, mode: pc.state === 'busy' ? 'add' : 'duration', minutes: 60, money: null, variantId: null, quote: null, qerror: null, error: null });
    renderCashier();
    loadPricing();
  }

  async function loadPricing() {
    const pc = C.pc;
    C.loading = true;
    try {
      const pricing = await api('GET', `pricing?pc=${encodeURIComponent(pc)}`);
      if (C.pc !== pc) return;
      C.pricing = pricing;
      C.error = null;
      C.loadedAt = Date.now();
      if (!pricing.tariffs.some((tf) => tf.id === C.tariffId)) C.tariffId = pricing.tariffs.length ? pricing.tariffs[0].id : null;
    } catch (error) {
      if (C.pc !== pc) return;
      C.pricing = null;
      C.error = error;
    }
    C.loading = false;
    renderCashier();
    requestQuote(0);
  }

  /** Called on every poll: re-price when the selected PC changes state, and once a minute (day/night rates). */
  function cashierTick() {
    if (C.pc == null) return;
    const pc = pcsOf().find((p) => p.number === C.pc);
    if (!pc) return;
    if (pc.state !== C.state) {
      C.state = pc.state;
      C.mode = pc.state === 'busy' ? 'add' : (C.mode === 'add' ? 'duration' : C.mode);
      loadPricing();
    } else if (Date.now() - C.loadedAt > CASHIER_REFRESH_MS && !C.loading) {
      loadPricing();
    }
  }

  const currentOffer = () => C.pricing && C.pricing.tariffs.find((tf) => tf.id === C.tariffId);

  function quoteRequest() {
    const base = { pc: C.pc, tariffId: C.tariffId || 0, mode: C.mode, minutes: null, money: null, variantId: null };
    if (C.mode === 'duration') return C.minutes > 0 ? { ...base, minutes: C.minutes } : null;
    if (C.mode === 'money' || C.mode === 'add') return C.money > 0 ? { ...base, money: C.money } : null;
    if (C.mode === 'packet') return C.variantId ? { ...base, variantId: C.variantId } : null;
    return base;
  }

  function requestQuote(delay) {
    clearTimeout(quoteTimer);
    quoteTimer = setTimeout(doQuote, delay);
  }

  async function doQuote() {
    const request = C.pricing ? quoteRequest() : null;
    if (!request || (C.mode !== 'add' && !C.tariffId) || (C.mode === 'add' && !(C.pricing.session && C.pricing.session.canAdd))) {
      C.quote = null;
      C.qerror = null;
      renderQuote();
      return;
    }
    const seq = ++C.seq;
    try {
      const quote = await api('POST', 'quote', request);
      if (seq !== C.seq) return;
      C.quote = quote;
      C.qerror = null;
    } catch (error) {
      if (seq !== C.seq) return;
      C.quote = null;
      C.qerror = error;
    }
    renderQuote();
  }

  function cashierModes() {
    const pc = pcsOf().find((p) => p.number === C.pc);
    if (pc && pc.state === 'busy') return ['add'];
    const offer = currentOffer();
    return ['duration', 'money', ...(offer && offer.packets.length ? ['packet'] : []), 'postpay'];
  }

  function renderCashier() {
    const focused = document.activeElement && cashierEl.contains(document.activeElement) ? document.activeElement.id : null;
    const pc = pcsOf().find((p) => p.number === C.pc);
    let body;
    if (C.error) {
      body = errorBox(C.error);
    } else if (!C.pricing) {
      body = `<div class="cs-loading"><span class="spinner"></span>${esc(t('cs.loading'))}</div>`;
    } else if (pc && pc.state === 'disabled') {
      body = `<p class="small muted">${esc(t('cs.maintenance'))}</p>`;
    } else {
      body = cashierControls();
    }
    cashierEl.innerHTML = `<div class="cs-title">${esc(t('cs.title'))}</div>${body}<div class="cs-result" aria-live="polite"></div>
      <p class="cs-foot">${esc(t('cs.footer'))}</p>`;
    // The field always shows what is being priced, so a chip and the field can never disagree.
    const minutesInput = cashierEl.querySelector('#csMinutes');
    if (minutesInput) minutesInput.value = C.minutes || '';
    const moneyInput = cashierEl.querySelector('#csMoney');
    if (moneyInput) moneyInput.value = C.money != null ? C.money : '';
    if (focused) { const el = cashierEl.querySelector('#' + focused); if (el) el.focus(); }
    renderQuote();
  }

  const cashierQuickMinutes = () => (currentOffer() || { quickMinutes: [] }).quickMinutes;
  const cashierQuickMoney = () => {
    const offer = currentOffer() || (C.pricing && C.pricing.tariffs[0]);
    return offer ? offer.quickMoney : [];
  };

  function cashierControls() {
    const p = C.pricing;
    const s = p.session;
    let html = '';
    if (s) {
      html += `<div class="cs-session">${esc(s.postpay
        ? t('cs.sessionPostpay', { tariff: s.tariffName || '' })
        : t('cs.session', { tariff: s.tariffName || '', end: s.endsAt ? clock(s.endsAt) : '—', paid: money(s.prepaid) }))}</div>`;
    }
    const modes = cashierModes();
    if (!modes.includes(C.mode)) C.mode = modes[0];
    if (C.mode !== 'add') {
      if (p.tariffs.length > 1) {
        html += `<div class="cs-tariffs">${p.tariffs.map((tf) => `<button class="chip${tf.id === C.tariffId ? ' on' : ''}" data-act="cs-tariff" data-id="${tf.id}">${esc(tf.name)}</button>`).join('')}</div>`;
      }
      const offer = currentOffer();
      if (!offer) return html + `<p class="small muted">${esc(t('cs.noTariff'))}</p>`;
      if (offer.error) return html + errorBox({ code: 'quote_' + offer.error });
      html += `<div class="cs-rate">${esc(t('cs.rateNow', { name: offer.currentVariant || '', price: money(offer.hourCost) }))}</div>`;
    }
    if (modes.length > 1) {
      html += `<div class="tabs cs-tabs">${modes.map((m) => `<button data-act="cs-mode" data-mode="${m}" class="${m === C.mode ? 'on' : ''}">${esc(t('cs.tab.' + m))}</button>`).join('')}</div>`;
    }
    if (C.mode === 'add' && !(s && s.canAdd)) {
      return html + `<p class="cs-note">${esc(t('cs.add.' + ((s && s.addUnavailable) || 'no_session')))}</p>`;
    }
    const offer = currentOffer();
    if (C.mode === 'duration') {
      html += `<div class="cs-chips">${cashierQuickMinutes().map((m) => `<button class="chip${C.minutes === m ? ' on' : ''}" data-act="cs-min" data-v="${m}">${esc(minutes(m))}</button>`).join('')}</div>
        <label class="cs-input"><span>${esc(t('cs.customMinutes'))}</span><input class="input" id="csMinutes" type="number" min="1" max="1440" inputmode="numeric"><span class="unit">${esc(t('common.min'))}</span></label>`;
    } else if (C.mode === 'money' || C.mode === 'add') {
      html += `<div class="cs-chips money">${cashierQuickMoney().map((v) => `<button class="chip${C.money === v ? ' on' : ''}" data-act="cs-money" data-v="${v}">${esc(money(v))}</button>`).join('')}</div>
        <label class="cs-input"><span>${esc(t('cs.amount'))}</span><input class="input" id="csMoney" type="number" min="0" step="any" inputmode="decimal"><span class="unit">${esc(cur(p.currency))}</span></label>`;
      if (C.mode === 'money' && offer) html += `<p class="cs-hint">${esc(t('cs.limits', { min: money(offer.minMoney), max: money(offer.maxMoney) }))}</p>`;
    } else if (C.mode === 'packet') {
      html += `<div class="cs-packets">${offer.packets.map((pk) => `<button class="cs-packet${C.variantId === pk.variantId ? ' on' : ''}" data-act="cs-packet" data-id="${pk.variantId}">
          <b>${esc(pk.name)}</b><span>${esc(money(pk.cost))}</span><small>${esc(clock(pk.start))} – ${esc(clock(pk.end))}</small></button>`).join('')}</div>`;
    } else if (C.mode === 'postpay') {
      html += `<p class="cs-hint">${esc(t('cs.postpayHint'))}</p>`;
    }
    return html;
  }

  function renderQuote() {
    const el = cashierEl.querySelector('.cs-result');
    if (!el) return;
    if (C.qerror) {
      const { message, detail } = errorText(C.qerror);
      el.innerHTML = `<div class="cs-error">${esc(message)}${detail && C.qerror.code === 'quote_below_min' ? ` <b>${esc(money(Number(detail)))}</b>` : ''}</div>`;
      return;
    }
    const q = C.quote;
    if (!q) { el.innerHTML = ''; return; }
    const warnings = (q.warnings || []).map((w) => {
      const offer = currentOffer() || {};
      const vars = { n: q.requestedMinutes != null ? q.minutes - q.requestedMinutes : 0, max: money(offer.maxMoney), t: q.reservation ? clock(q.reservation) : '' };
      return `<li>${esc(t('cs.w.' + w, vars))}</li>`;
    }).join('');
    const main = q.mode === 'add'
      ? `<div class="cs-big">+${esc(minutes(q.minutes))}</div>
         <div class="cs-line">${esc(t('cs.newEnd', { t: clock(q.end) }))} <span class="muted">(${esc(t('cs.was', { t: clock(q.start) }))})</span></div>`
      : `<div class="cs-big">${esc(money(q.cost))}</div>
         <div class="cs-line"><b>${esc(clock(q.start))} → ${esc(clock(q.end))}</b> · ${esc(minutes(q.minutes))}</div>`;
    el.innerHTML = `<div class="cs-quote">${main}${warnings ? `<ul class="cs-warn">${warnings}</ul>` : ''}</div>`;
  }

  // ---------------------------------------------------------------- prices page (GameClass tariffs per zone)

  const P = { rows: null, loading: false, loadedAt: 0 };
  const PRICES_REFRESH_MS = 60000;

  /** One PC per zone is enough: tariffs belong to computer groups. */
  async function loadPrices() {
    const zones = new Map();
    for (const pc of pcsOf()) if (!zones.has(pc.zone)) zones.set(pc.zone, pc.number);
    if (P.loading || !zones.size) return;
    P.loading = true;
    renderRegion('phead');
    P.rows = await Promise.all([...zones].map(async ([zone, number]) => {
      try {
        return { zone, pricing: await api('GET', `pricing?pc=${number}`) };
      } catch (error) {
        return { zone, error };
      }
    }));
    P.loading = false;
    P.loadedAt = Date.now();
    for (const name of ['phead', 'prices']) renderRegion(name);
  }

  function renderPrices() {
    return `<div class="page-head" data-region="phead">${pricesHead()}</div>
      <p class="page-lead">${esc(t('cs.footer'))}</p>
      <div data-region="prices">${pricesBody()}</div>`;
  }

  function pricesHead() {
    return `<h1>${esc(t('prices.title'))}</h1>
      ${P.loadedAt ? `<span class="updated">${esc(t('prices.updated', { t: clock(P.loadedAt) }))}</span>` : ''}
      <span class="spacer"></span>
      <button class="btn btn-secondary btn-sm" data-act="prices-refresh" ${P.loading ? 'disabled' : ''}>${P.loading ? '<span class="spinner"></span>' : ICON.refresh}${esc(t('prices.refresh'))}</button>`;
  }

  function pricesBody() {
    if (!P.rows) return `<div class="price-grid">${[1, 2, 3].map(() => '<div class="skel skel-card"></div>').join('')}</div>`;
    if (!P.rows.length) return `<p class="muted">${esc(t('dash.noPcs'))}</p>`;
    return `<div class="price-grid">${P.rows.map(({ zone, pricing, error }) => {
      let body;
      if (error) body = `<p class="muted">${esc(errorText(error).message)}</p>`;
      else if (!pricing.tariffs.length) body = `<p class="muted">${esc(t('cs.noTariff'))}</p>`;
      else body = pricing.tariffs.map((tf) => tariffBlock(tf, pricing.currency)).join('');
      return `<section class="card price-card"><div class="pz">${esc(zone || t('zone.none'))}</div>${body}</section>`;
    }).join('')}</div>`;
  }

  function tariffBlock(tf, currency) {
    const fmt = (v) => fmtMoney(v, currency);
    const now = tf.error
      ? `<p class="muted small">${esc(t('error.quote_' + tf.error))}</p>`
      : `<div class="pt-now">${tf.currentVariant ? `<span class="pt-var">${esc(tf.currentVariant)}</span>` : ''}<b>${esc(fmt(tf.hourCost))}</b><span>${esc(t('prices.perHour'))}</span></div>`;
    const packets = tf.packets.length ? `<div class="pt-label">${esc(t('prices.packets'))}</div><ul class="pt-packets">${tf.packets.map((pk) =>
      `<li><span>${esc(pk.name)}</span><b>${esc(fmt(pk.cost))}</b><small>${esc(clock(pk.end))} ${esc(t('prices.till'))}</small></li>`).join('')}</ul>` : '';
    return `<div class="pt">
      <div class="pt-name">${esc(tf.name)}</div>
      ${now}${packets}
      <div class="pt-lim">${esc(t('prices.limits'))}: ${esc(fmt(tf.minMoney))} – ${esc(fmt(tf.maxMoney))}</div>
    </div>`;
  }

  // ---------------------------------------------------------------- settings page (connection, service, map, removal)

  function renderSettings() {
    return `<div class="page-head"><h1>${esc(t('nav.settings'))}</h1></div>
      <p class="page-lead">${esc(t('settings.lead'))}</p>
      <div data-region="release">${onlineUpdateCard()}</div>
      <div data-region="settings">${dashSettings()}</div>`;
  }

  function onlineUpdateCard() {
    const r = S.release || { state: 'idle', currentVersion: S.state.version };
    const working = ['checking', 'downloading', 'installing'].includes(r.state);
    const percent = r.size ? Math.min(100, Math.floor(r.downloaded * 100 / r.size)) : 0;
    const text = r.error ? t('error.' + r.error) : t('online.' + r.state, { v: r.latestVersion || '', p: percent });
    return `<section class="card online-update">
      <div class="online-update-head"><div><h2>${esc(t('online.title'))}</h2><p class="small muted">${esc(t('online.version', { v: r.currentVersion }))}</p></div>
        ${button('online-check', t('online.check'), 'btn-secondary btn-sm', { disabled: S.busy || working })}</div>
      <p class="small${r.error ? ' online-error' : ' muted'}" role="status">${esc(text)}</p>
      ${r.lastResult && r.lastResult !== 'updated' ? `<p class="small online-error">${esc(t('online.result.' + r.lastResult))}</p>` : ''}
      ${r.state === 'downloading' ? `<progress max="100" value="${percent}" aria-label="${esc(t('online.downloading', { p: percent }))}"></progress>` : ''}
      ${r.state === 'ready' ? `<div>${button('online-apply', t('online.apply'), 'btn-primary btn-sm', { disabled: S.busy })}<p class="small muted">${esc(t('online.keepData'))}</p></div>` :
        r.available && !working ? `<div>${button('online-download', t('online.download', { v: r.latestVersion }), 'btn-primary btn-sm', { disabled: S.busy })}</div>` : ''}
    </section>`;
  }

  async function checkOnlineUpdate(announce = false) {
    if (!S.state || !S.state.installed || S.busy || (S.release && ['checking', 'downloading', 'ready', 'installing'].includes(S.release.state))) return;
    S.release = { ...(S.release || {}), state: 'checking', currentVersion: S.state.version };
    renderRegion('release');
    try { S.release = await api('POST', 'releases/check'); }
    catch (error) { S.release = { state: 'failed', currentVersion: S.state.version, error: error.code === 'busy' ? 'busy' : 'online_unavailable' }; }
    renderRegion('release');
    renderRail(false);
    if (announce && S.release.available) toast(t('online.notice', { v: S.release.latestVersion }));
  }

  function dashSettings() {
    const d = S.dash;
    const local = d && d.local;
    const push = d && d.push;
    const manajoy = d && d.manajoy;
    const status = (text, level) => `<span class="settings-status${level ? ' ' + level : ''}">${esc(text)}</span>`;
    const notes = (filter) => alertList().filter(filter).map(alertHtml).join('');
    const manajoyCard = S.state && S.state.paired ? `
      <section class="card">
        <h2 class="settings-title"><span class="settings-icon">${ICON.send}</span>${esc(t('dash.manajoy'))}</h2>
        <dl class="kv">
          <div><dt>${esc(t('dash.connectedTo'))}</dt><dd>${esc((manajoy && manajoy.clubName) || S.state.clubName || '—')}</dd></div>
          <div><dt>${esc(t('dash.liveForBuyers'))}</dt><dd>${status(manajoy && manajoy.reachable ? (manajoy.liveEnabled ? t('dash.yes') : t('dash.no')) : '—', manajoy && manajoy.reachable ? (manajoy.liveEnabled ? 'ok' : 'warn') : '')}</dd></div>
          <div><dt>${esc(t('dash.stations'))}</dt><dd>${esc(manajoy && manajoy.reachable ? `${manajoy.stationsConfirmed} / ${manajoy.stationsTotal}` : '—')}</dd></div>
          <div><dt>${esc(t('dash.lastSent'))}</dt><dd>${esc(push && push.lastSuccessAt ? ago(push.lastSuccessAt) : t('dash.never'))}</dd></div>
        </dl>
        ${notes((a) => a.hall === false || (push && push.lastError && a.text === pushText(push.lastError)))}
        <div class="btns two">
          ${button('repair', t('dash.repair'), 'btn-secondary btn-sm', { disabled: S.busy })}
          ${button('stop-push', t('dash.stopPush'), 'btn-danger-ghost btn-sm', { disabled: S.busy })}
        </div>
      </section>` : `
      <section class="card">
        <h2 class="settings-title"><span class="settings-icon">${ICON.send}</span>${esc(t('dash.manajoy'))}</h2>
        <p class="muted">${esc(t('dash.notPairedText'))}</p>
        <div class="btns">${button('repair', t('dash.pairNow'), 'btn-primary btn-sm')}</div>
      </section>`;
    const gcServer = local && local.serverAlive === true ? t('dash.gcAlive') : local && local.serverAlive === false ? t('dash.gcDown') : '—';
    const old = updateAvailable();
    const sync = syncInfo();
    return `<div class="settings-grid">
      ${manajoyCard}
      <section class="card">
        <h2 class="settings-title"><span class="settings-icon">${ICON.monitor}</span>${esc(t('dash.system'))}</h2>
        <dl class="kv">
          <div><dt>${esc(t('dash.service'))}</dt><dd>${status(d && d.serviceStatus ? (I18N.uz['svc.' + d.serviceStatus] !== undefined ? t('svc.' + d.serviceStatus) : d.serviceStatus) : '—', d && d.serviceStatus ? (d.serviceStatus === 'Running' ? 'ok' : 'warn') : '')}</dd></div>
          <div><dt>${esc(t('dash.gcServer'))}</dt><dd>${status(gcServer, local && local.serverAlive != null ? (local.serverAlive ? 'ok' : 'warn') : '')}</dd></div>
          <div><dt>${esc(t('dash.database'))}</dt><dd>${esc((local && local.dbProvider) || '—')}</dd></div>
          <div><dt>${esc(t('dash.version'))}</dt><dd>${esc((local && local.bridgeVersion) || S.state.version)}</dd></div>
          <div><dt>${esc(t('dash.disk'))}</dt><dd>${esc(d && d.disk ? t(d.disk.problem ? 'disk.bad' : 'disk.ok') : '—')}</dd></div>
        </dl>
        ${old ? alertHtml({ level: 'info', text: t('dash.updateAvailable', { new: S.state.version, old }), acts: button('update', t('dash.update'), 'btn-primary btn-xs', { disabled: S.busy, spinner: S.busy }) }) : ''}
        <div class="btns two">
          ${button('restart', t('dash.restart'), 'btn-secondary btn-sm', { disabled: S.busy })}
          ${button('report', t('dash.report'), 'btn-secondary btn-sm', { disabled: S.busy })}
        </div>
      </section>
      ${S.state.autoStart == null ? '' : `<section class="card settings-autostart">
        <span class="settings-icon">${ICON.monitor}</span>
        <div class="grow"><h2>${esc(t('auto.title'))}</h2><p class="small muted">${esc(t('auto.text'))}</p></div>
        <button class="switch${S.state.autoStart ? ' on' : ''}" data-act="autostart" role="switch" aria-checked="${S.state.autoStart}" aria-label="${esc(t('auto.title'))}" ${S.busy ? 'disabled' : ''}><i></i></button>
      </section>`}
      ${REPORTS.settingsCard()}
      <section class="card settings-shortcut">
        <div class="settings-card-head"><h2 class="settings-title"><span class="settings-icon">${ICON.hall}</span>${esc(t('settings.map'))}</h2>
        ${sync.text ? `<span class="sync ${sync.cls}"><span class="led"></span>${esc(sync.text)}</span>` : ''}</div>
        <p class="muted small">${esc(t('sync.hint'))}</p>
        <div class="btns">${pcsOf().length ? button('map-edit', t(hasMap() ? 'map.edit' : 'map.draw'), 'btn-secondary btn-sm') : ''}</div>
      </section>
      <section class="card danger-zone">
        <div class="grow"><h2>${esc(t('settings.danger'))}</h2>
        <p class="muted small">${esc(t('settings.dangerText'))}</p></div>
        <div class="btns two">
          ${button('reinstall', t('dash.reinstall'), 'btn-secondary btn-sm', { disabled: S.busy })}
          ${button('uninstall', t('dash.uninstall'), 'btn-danger-ghost btn-sm', { disabled: S.busy })}
        </div>
      </section>
    </div>`;
  }

  // ---------------------------------------------------------------- monitor (full screen, for a second screen / TV)

  function renderMonitor() {
    return `<div class="mon">
      <div data-region="monhead">${monHead()}</div>
      <div class="mon-body" data-region="pcs">${pcsBody()}</div>
    </div>`;
  }

  function monHead() {
    const local = S.dash && S.dash.local;
    const counts = (local && local.counts) || {};
    const now = new Date();
    const other = (counts.disabled || 0) + (counts.unknown || 0);
    const status = local ? local.status : 'starting';
    return `<div class="mon-head">
      <div class="mon-club">
        <img src="logo-white.png" alt="">
        <div><div class="mon-name">${esc((S.state && S.state.clubName) || (S.state && S.state.machineName) || '')}</div>
        <div class="mon-sub"><span class="pill ${statusLevel(status)}"><span class="led"></span>${esc(t('dash.status.' + status))}</span></div></div>
      </div>
      <div class="mon-counts">
        <div class="mc available"><b>${counts.available || 0}</b><span>${esc(t('state.available'))}</span></div>
        <div class="mc busy"><b>${counts.busy || 0}</b><span>${esc(t('state.busy'))}</span></div>
        <div class="mc offline"><b>${counts.offline || 0}</b><span>${esc(t('state.offline'))}</span></div>
        ${other ? `<div class="mc other"><b>${other}</b><span>${esc(t('state.disabled'))}</span></div>` : ''}
      </div>
      <div class="mon-clock">${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}</div>
      <button class="mon-exit" data-act="monitor-exit" title="Esc">${esc(t('mon.exit'))}</button>
    </div>`;
  }

  // ---------------------------------------------------------------- full-screen map (light, interactive)

  function renderMapFull() {
    return `<div class="mf">
      <div data-region="fullhead">${fullHead()}</div>
      <div class="mf-body">
        <div class="mf-map" data-region="pcs">${pcsBody()}</div>
        <div data-region="detail">${pcDetail()}</div>
      </div>
    </div>`;
  }

  function fullHead() {
    const local = S.dash && S.dash.local;
    const counts = (local && local.counts) || {};
    const status = local ? local.status : 'starting';
    const chip = (state, n) => `<button class="mf-count ${state}${S.filter.state === state ? ' on' : ''}" data-act="filter-state" data-state="${state}">
      <b>${n}</b><span>${esc(state === 'other' ? t('state.disabled') : t('state.' + state))}</span></button>`;
    return `<div class="mf-bar">
      <button class="mf-back" data-act="map-full-exit" title="Esc">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M15 6l-6 6 6 6"/></svg>
        <span>${esc(t('map.fullExit'))}</span></button>
      <div class="mf-title">
        <div class="mf-name">${esc((S.state && S.state.clubName) || (S.state && S.state.machineName) || '')}</div>
        <span class="pill ${statusLevel(status)}"><span class="led"></span>${esc(t('dash.status.' + status))}</span>
      </div>
      <div class="mf-counts">
        ${chip('available', counts.available || 0)}${chip('busy', counts.busy || 0)}${chip('offline', counts.offline || 0)}
        ${(counts.disabled || 0) + (counts.unknown || 0) ? chip('other', (counts.disabled || 0) + (counts.unknown || 0)) : ''}
      </div>
      <span class="spacer"></span>
      <input class="search" id="pcSearch" type="search" autocomplete="off" spellcheck="false" placeholder="${esc(t('map.search'))}" aria-label="${esc(t('map.search'))}">
      <button class="btn btn-secondary btn-sm icon-only" data-act="map-edit" title="${esc(t(hasMap() ? 'map.edit' : 'map.draw'))}" aria-label="${esc(t(hasMap() ? 'map.edit' : 'map.draw'))}">${ICON.pencil}</button>
    </div>`;
  }

  const REGIONS = {
    fullhead: fullHead,
    head: dashHead,
    alerts: dashAlerts,
    stats: dashStats,
    tools: pcsTools,
    detail: pcDetail,
    settings: dashSettings,
    release: onlineUpdateCard,
    phead: pricesHead,
    prices: pricesBody,
    monhead: monHead,
  };

  /** Re-renders one region (or all), and on polls only what changed. */
  function renderRegion(name, force = true) {
    const el = $view.querySelector(`[data-region="${name}"]`);
    if (!el) return;
    if (name === 'pcs') {
      const sig = pcsSignature();
      if (force || regionCache.pcs !== sig) {
        el.innerHTML = pcsBody();
        regionCache.pcs = sig;
        wireMap();
      } else {
        patchPcs(el);
      }
      return;
    }
    const html = REGIONS[name]();
    if (!force && regionCache[name] === html) return;
    const searchFocused = document.activeElement && document.activeElement.id === 'pcSearch' && el.contains(document.activeElement);
    el.innerHTML = html;
    regionCache[name] = html;
    if (el.querySelector('#pcSearch')) restoreSearch(searchFocused);
    if (name === 'detail') attachCashier();
  }

  function patchRegions() {
    for (const el of $view.querySelectorAll('[data-region]')) renderRegion(el.dataset.region, false);
    renderRail(false);
  }

  function cacheRegions() {
    regionCache = {};
    for (const el of $view.querySelectorAll('[data-region]')) {
      const name = el.dataset.region;
      regionCache[name] = name === 'pcs' ? pcsSignature() : el.innerHTML;
    }
    // innerHTML is normalized by the browser; compare against our own strings from now on.
    for (const name of Object.keys(regionCache)) if (name !== 'pcs' && REGIONS[name]) regionCache[name] = REGIONS[name]();
  }

  function restoreSearch(focus) {
    const input = document.getElementById('pcSearch');
    if (!input) return;
    input.value = S.filter.q;
    if (focus) { input.focus(); input.setSelectionRange(input.value.length, input.value.length); }
  }

  const mapStore = {
    set: (box) => {
      if (S.view === 'dashboard') S.mapView = box;
      else if (S.view === 'mapFull') S.fullView = box;
    },
  };

  function wireMap() {
    const svg = $view.querySelector('svg.cmap');
    if (svg) MAP.attachViewer(svg, mapStore);
  }

  function mountEditor() {
    const root = document.getElementById('editorRoot');
    if (!root) return;
    const draft = S.editDraft;
    editor = MAP.createEditor(root, {
      doc: draft ? draft.doc : layoutDoc(),
      dirty: draft ? draft.dirty : false,
      pcs: pcsOf(),
      look: pcLook,
      t,
      sync: () => {
        const s = syncInfo();
        return { cls: s.cls, text: s.text };
      },
      onSave: async (doc) => {
        S.layout = await api('PUT', 'layout', doc);
        const s = syncInfo();
        toast(s.state === 'conflict' ? t('sync.conflictToast') : `${t('edit.saved')} · ${s.text}`);
        if (s.state === 'conflict') conflictModal();
      },
      onExit: () => { S.editDraft = null; go(S.editReturn === 'mapFull' ? 'mapFull' : 'dashboard'); },
      onError: (error) => {
        const { message, detail } = errorText(error);
        toast(detail ? `${message} (${detail})` : message);
      },
      confirm: confirmModal,
      dialog: dialogModal,
      toast,
    });
  }

  async function loadLayout() {
    try {
      S.layout = await api('GET', 'layout');
    } catch (error) {
      if (error.code === 'network') S.error = error;
      S.layout = { layout: { pcs: {}, objects: [] }, sync: { state: 'local', skipped: [] }, grid: 1000 };
    }
    if (['dashboard', 'monitor', 'mapFull'].includes(S.view)) {
      for (const name of ['head', 'alerts', 'tools', 'fullhead', 'pcs', 'detail', 'settings']) renderRegion(name);
    }
  }

  async function openEditor() {
    if (!S.dash) await refreshDashboard();
    if (!S.layout || !S.layout.sync) await loadLayout();
    S.editReturn = S.view;
    S.editDraft = null;
    S.selected = null;
    go('mapEdit');
  }

  function conflictModal() {
    const sync = S.layout && S.layout.sync;
    $modal.innerHTML = `<div class="modal" role="dialog" aria-modal="true">
      <h2>${esc(t('sync.conflictTitle'))}</h2><p>${esc(t('sync.conflictText', { n: (sync && sync.detail) || '?' }))}</p>
      <div class="actions"><span class="spacer"></span>
        <button class="btn btn-secondary" data-modal="server">${esc(t('sync.useServer'))}</button>
        <button class="btn btn-primary" data-modal="local">${esc(t('sync.useLocal'))}</button>
      </div></div>`;
    $modal.hidden = false;
    $modal.onclick = (event) => {
      const choice = event.target.closest('[data-modal]');
      if (!choice && event.target !== $modal) return;
      $modal.hidden = true;
      if (choice) resolveLayout(choice.dataset.modal === 'server');
    };
  }

  async function resolveLayout(useServer) {
    try {
      S.layout = await api('POST', 'layout/resolve', { useServer });
      toast(syncInfo().text);
    } catch (error) {
      toast(errorText(error).message);
    }
    if (S.view === 'mapEdit') { S.editDraft = null; render(); } else { for (const name of ['head', 'alerts', 'tools', 'pcs', 'settings']) renderRegion(name); }
  }

  // ---------------------------------------------------------------- render

  function render() {
    renderRail();
    const view = S.view;
    if (editor) {
      // A re-render inside the editor (language switch) keeps the unsaved drawing.
      S.editDraft = view === 'mapEdit' ? editor.state() : null;
      editor.destroy();
      editor = null;
    }
    const hall = view === 'dashboard' && S.section === 'hall';
    document.body.classList.toggle('monitor-mode', view === 'monitor');
    document.body.classList.toggle('focus-mode', view === 'mapEdit' || view === 'mapFull');
    document.body.classList.toggle('dash', view === 'dashboard' || (view === 'pair' && S.pairFromDashboard));
    $view.classList.toggle('wide', view === 'dashboard' && !hall);
    $view.classList.toggle('settings-view', view === 'dashboard' && S.section === 'settings');
    $view.classList.toggle('hall-view', hall);
    $view.classList.toggle('full', view === 'mapEdit' || view === 'monitor' || view === 'mapFull');
    $view.classList.toggle('flush', view === 'mapFull');
    $view.innerHTML = (views[view] || views.loading)();
    const title = { dashboard: 'nav.' + S.section, mapEdit: 'edit.title', monitor: 'mon.title', mapFull: 'map.full' }[view] || 'app.subtitle';
    document.title = `${t('app.title')} · ${t(title)}`;
    document.documentElement.lang = S.lang;
    restoreForm();
    if (view === 'dashboard' || view === 'monitor' || view === 'mapFull') {
      cacheRegions();
      restoreSearch(false);
      wireMap();
      attachCashier();
    }
    if (view === 'mapEdit') mountEditor();
  }

  /** Values are put back after every render (never into HTML attributes, so the password stays out of the DOM source). */
  function restoreForm() {
    const set = (id, value) => { const el = document.getElementById(id); if (el) el.value = value; };
    set('adminUser', S.form.adminUser);
    set('adminPassword', S.form.adminPassword);
    set('server', S.form.server);
    set('database', S.form.database);
    set('code', S.form.code);
    const code = document.getElementById('code');
    if (code && !S.busy) code.focus();
  }

  function readForm() {
    const get = (id) => { const el = document.getElementById(id); return el ? el.value : undefined; };
    for (const id of ['adminUser', 'adminPassword', 'server', 'database']) {
      const value = get(id);
      if (value !== undefined) S.form[id] = value;
    }
    const windowsAuth = document.getElementById('windowsAuth');
    if (windowsAuth) S.form.windowsAuth = windowsAuth.checked;
  }

  function go(view) {
    S.view = view;
    S.error = null;
    stopPolling();
    if (view === 'dashboard' || view === 'monitor' || view === 'mapFull') startPolling();
    render();
    document.querySelector('.main').scrollTop = 0;
    if (view === 'preflight' && !S.preflight && !S.busy) actions.preflight();
    if (view === 'dashboard' && S.state && S.state.installed) loadLayout();
  }

  async function run(fn) {
    if (S.busy) return;
    S.busy = true;
    S.error = null;
    render();
    try {
      await fn();
    } catch (error) {
      S.error = error;
    } finally {
      S.busy = false;
      render();
    }
  }

  // ---------------------------------------------------------------- actions

  const actions = {
    lang: (el) => {
      readForm();
      S.lang = el.dataset.lang === 'ru' ? 'ru' : 'uz';
      savePrefs({ lang: S.lang });
      render();
    },
    tab: (el) => {
      S.tab = el.dataset.tab === 'map' ? 'map' : 'list';
      savePrefs({ dashTab: S.tab });
      for (const name of ['head', 'pcs', 'detail']) renderRegion(name);
    },
    'filter-state': (el) => {
      S.filter.state = S.filter.state === el.dataset.state ? '' : el.dataset.state;
      for (const name of ['stats', 'tools', 'pcs', 'fullhead']) renderRegion(name);
    },
    'filter-zone': (el) => {
      S.filter.zone = S.filter.zone === el.dataset.zone ? '' : el.dataset.zone;
      for (const name of ['tools', 'pcs']) renderRegion(name);
    },
    'filter-clear': () => {
      S.filter = { q: '', zone: '', state: '' };
      for (const name of ['stats', 'tools', 'pcs', 'fullhead']) renderRegion(name);
    },
    pc: (el) => {
      if (S.view !== 'dashboard' && S.view !== 'mapFull') return;
      S.selected = S.selected === el.dataset.pc ? null : el.dataset.pc;
      const picked = S.selected && pcsOf().find((p) => p.name === S.selected);
      if (picked) openCashier(picked); else C.pc = null;
      renderRegion('detail');
      renderRegion('pcs', false);
    },
    'pc-close': () => { S.selected = null; C.pc = null; renderRegion('detail'); renderRegion('pcs', false); },
    'cs-tariff': (el) => { C.tariffId = Number(el.dataset.id); C.variantId = null; renderCashier(); requestQuote(0); },
    'cs-mode': (el) => { C.mode = el.dataset.mode; C.quote = null; C.qerror = null; renderCashier(); requestQuote(0); },
    'cs-min': (el) => { C.minutes = Number(el.dataset.v); renderCashier(); requestQuote(0); },
    'cs-money': (el) => { C.money = Number(el.dataset.v); renderCashier(); requestQuote(0); },
    'cs-packet': (el) => { C.variantId = Number(el.dataset.id); renderCashier(); requestQuote(0); },
    nav: (el) => {
      const section = ['hall', 'prices', 'reports', 'bar', 'settings'].includes(el.dataset.section) ? el.dataset.section : 'hall';
      S.section = section;
      if (S.view !== 'dashboard') { S.pairFromDashboard = false; go('dashboard'); } else { render(); document.querySelector('.main').scrollTop = 0; }
      if (section === 'prices' && (!P.rows || Date.now() - P.loadedAt > PRICES_REFRESH_MS)) loadPrices();
      if (section === 'reports') REPORTS.enter();
      if (section === 'bar') BAR.enter();
    },
    prices: () => actions.nav({ dataset: { section: 'prices' } }),
    'prices-refresh': () => loadPrices(),
    'alerts-more': () => { S.alertsOpen = !S.alertsOpen; renderRegion('alerts'); },
    autostart: () => run(async () => {
      S.state = await api('POST', 'autostart', { enabled: !S.state.autoStart });
      toast(t(S.state.autoStart ? 'auto.on' : 'auto.off'));
    }),
    'pc-show': () => {
      const p = layoutDoc().pcs[S.selected];
      S.tab = 'map';
      S.mapView = p ? { x: p.x - 250, y: p.y - 160, w: 500, h: 320 } : null;
      for (const name of ['head', 'pcs', 'detail']) renderRegion(name);
    },
    'map-edit': () => openEditor(),
    'map-full': () => {
      S.fullView = null;
      go('mapFull');
      const root = document.documentElement;
      if (root.requestFullscreen && !document.fullscreenElement) root.requestFullscreen().catch(() => { /* the window still fills */ });
    },
    'map-full-exit': () => exitFull(),
    'rail-toggle': () => {
      savePrefs({ rail: railCollapsed() ? 'open' : 'closed' });
      renderRail();
    },
    'map-zoom': (el) => {
      const svg = $view.querySelector('svg.cmap');
      if (svg) MAP.zoomButton(svg, mapStore, el.dataset.z, MAP.viewFit(layoutDoc(), pcsOf()));
    },
    monitor: () => {
      S.selected = null;
      go('monitor');
      const root = document.documentElement;
      if (root.requestFullscreen) root.requestFullscreen().catch(() => { /* not allowed: the window still fills */ });
    },
    'monitor-exit': () => exitFull(),
    'layout-use-server': () => resolveLayout(true),
    'layout-use-local': () => resolveLayout(false),
    start: () => go('preflight'),
    'back-welcome': () => go(S.state && S.state.installed ? 'dashboard' : 'welcome'),
    preflight: () => run(async () => {
      S.preflight = null;
      S.preflight = await api('POST', 'preflight');
    }),
    'to-preflight': () => { readForm(); go('preflight'); },
    'to-database': () => go('database'),
    database: () => {
      readForm();
      return run(async () => {
        S.db = null;
        S.db = await api('POST', 'database', {
          adminUser: S.form.adminUser,
          adminPassword: S.form.adminPassword,
          windowsAuth: S.form.windowsAuth,
          server: S.form.server || null,
          database: S.form.database || null,
        });
        if (S.db.success) S.form.adminPassword = '';
      });
    },
    'to-install': () => go('install'),
    install: () => run(async () => {
      S.install = null;
      S.install = await api('POST', 'install');
      S.state = await api('GET', 'state');
    }),
    'to-pair': () => { S.pairFromDashboard = false; S.pair = null; go('pair'); },
    pair: () => {
      if (S.form.code.replace('-', '').length !== 8) { S.error = { code: 'code_short' }; render(); return; }
      return run(async () => {
        S.pair = await api('POST', 'pair', { code: S.form.code });
        S.form.code = '';
        S.layout = null;
        S.state = await api('GET', 'state');
      });
    },
    'skip-pair': () => go('dashboard'),
    'to-done': () => go('done'),
    'to-dashboard': () => { S.pairFromDashboard = false; go('dashboard'); },
    repair: () => { S.pairFromDashboard = true; S.pair = null; S.form.code = ''; go('pair'); },
    restart: () => run(async () => {
      await api('POST', 'service/restart');
      toast(t('restart.done'));
      await refreshDashboard();
    }),
    'online-check': () => checkOnlineUpdate(),
    'online-download': () => run(async () => {
      S.release = { ...S.release, state: 'downloading', downloaded: 0, error: null };
      renderRegion('release');
      let active = true;
      const progress = setInterval(async () => {
        try { const status = await api('GET', 'releases/status'); if (active) { S.release = status; renderRegion('release'); } } catch { /* the POST reports a network failure */ }
      }, 750);
      try { S.release = await api('POST', 'releases/download'); }
      catch { S.release = { ...S.release, state: 'failed', error: 'online_unavailable' }; }
      finally { active = false; clearInterval(progress); }
    }),
    'online-apply': () => run(async () => {
      try { S.release = await api('POST', 'releases/apply'); }
      catch (error) { S.release = { ...S.release, error: error.code || 'online_install_failed' }; return; }
      if (S.release.state === 'installing') {
        stopPolling();
        toast(t('online.installing'));
        setTimeout(() => window.close(), 1200);
      }
    }),
    update: () => run(async () => {
      const result = await api('POST', 'update');
      if (!result.success) {
        const failed = result.steps.find((step) => step.level === 'fail');
        throw { code: 'update_failed', detail: failed ? `${t('check.' + failed.id)}: ${failed.detail}` : '' };
      }
      S.state = await api('GET', 'state');
      toast(t('update.done', { v: S.state.version }));
      await refreshDashboard();
    }),
    reinstall: () => {
      S.preflight = null;
      S.db = null;
      S.install = null;
      S.pair = null;
      S.pairFromDashboard = false;
      go('preflight');
    },
    report: () => run(async () => {
      const report = await api('POST', 'report');
      toast(t('report.done', { path: report.path }));
    }),
    'stop-push': () => confirmModal(t('dash.stopPush'), t('push.stopConfirm'), t('common.yesStop'), () => run(async () => {
      await api('POST', 'push/disable');
      S.state = await api('GET', 'state');
      toast(t('push.stopped'));
      await refreshDashboard();
    })),
    uninstall: () => uninstallModal(),
    close: async () => {
      try { await api('POST', 'close'); } catch { /* already gone */ }
      window.close();
    },
  };

  function confirmModal(title, text, confirmLabel, onConfirm) {
    $modal.innerHTML = `<div class="modal" role="dialog" aria-modal="true">
      <h2>${esc(title)}</h2><p>${esc(text)}</p>
      <div class="actions"><span class="spacer"></span>
        <button class="btn btn-secondary" data-modal="cancel">${esc(t('common.cancel'))}</button>
        <button class="btn btn-danger" data-modal="ok">${esc(confirmLabel)}</button>
      </div></div>`;
    $modal.hidden = false;
    $modal.onclick = (event) => {
      const choice = event.target.closest('[data-modal]');
      if (!choice && event.target !== $modal) return;
      $modal.hidden = true;
      if (choice && choice.dataset.modal === 'ok') onConfirm();
    };
  }

  /** A small form in the modal; `onOk` gets the modal element to read its fields. */
  function dialogModal(html, okLabel, onOk) {
    $modal.innerHTML = `<div class="modal" role="dialog" aria-modal="true">${html}
      <div class="actions"><span class="spacer"></span>
        <button class="btn btn-secondary" data-modal="cancel">${esc(t('common.cancel'))}</button>
        <button class="btn btn-primary" data-modal="ok">${esc(okLabel)}</button>
      </div></div>`;
    $modal.hidden = false;
    $modal.onclick = (event) => {
      const choice = event.target.closest('[data-modal]');
      if (!choice && event.target !== $modal) return;
      if (choice && choice.dataset.modal === 'ok') onOk($modal);
      $modal.hidden = true;
    };
  }

  /** Leaves the monitor or the full-screen map (and the browser's full screen). */
  function exitFull() {
    if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});
    if (S.view === 'monitor' || S.view === 'mapFull') go('dashboard');
  }

  function savePrefs(change) {
    S.prefs = Object.assign({}, S.prefs, change);
    api('PUT', 'prefs', change).catch(() => { /* a preference is not worth an error */ });
  }

  function uninstallModal() {
    $modal.innerHTML = `<div class="modal" role="dialog" aria-modal="true">
      <h2>${esc(t('uninstall.title'))}</h2><p>${esc(t('uninstall.text'))}</p>
      <div class="form">
        <label class="toggle"><input type="checkbox" id="dropLogin">${esc(t('uninstall.dropLogin'))}</label>
        <div class="field" id="dropFields" hidden>
          <label for="uAdminPassword">${esc(t('database.adminPassword'))} (sa)</label>
          <input class="input" id="uAdminPassword" type="password" autocomplete="new-password">
          <div class="help">${esc(t('database.passwordHint'))}</div>
        </div>
      </div>
      <div class="actions"><span class="spacer"></span>
        <button class="btn btn-secondary" data-modal="cancel">${esc(t('common.cancel'))}</button>
        <button class="btn btn-danger" data-modal="ok">${esc(t('uninstall.confirm'))}</button>
      </div></div>`;
    $modal.hidden = false;
    const drop = document.getElementById('dropLogin');
    drop.onchange = () => { document.getElementById('dropFields').hidden = !drop.checked; };
    $modal.onclick = (event) => {
      const choice = event.target.closest('[data-modal]');
      if (!choice && event.target !== $modal) return;
      if (!choice) { $modal.hidden = true; return; }
      const request = {
        dropLogin: drop.checked,
        adminUser: 'sa',
        adminPassword: document.getElementById('uAdminPassword').value,
        windowsAuth: false,
      };
      $modal.hidden = true;
      if (choice.dataset.modal !== 'ok') return;
      run(async () => {
        S.uninstall = await api('POST', 'uninstall', request);
        stopPolling();
        S.state = await api('GET', 'state');
        S.view = 'uninstalled';
      });
    };
  }

  // ---------------------------------------------------------------- dashboard polling

  let barAlertsAt = 0;

  /** Items running low (names and counts only, no PIN): polled once a minute for the hall and the rail. */
  async function refreshBarAlerts() {
    if (!S.state || !S.state.installed || Date.now() - barAlertsAt < 60000) return;
    barAlertsAt = Date.now();
    try {
      S.barAlerts = await api('GET', 'bar/alerts');
    } catch {
      S.barAlerts = null;
    }
  }

  async function refreshDashboard() {
    refreshBarAlerts();
    try {
      S.dash = await api('GET', 'dashboard');
    } catch (error) {
      if (error.code === 'network') { S.error = error; }
    }
  }

  function startPolling() {
    const tick = async () => {
      await refreshDashboard();
      if (['dashboard', 'monitor', 'mapFull'].includes(S.view) && !S.busy) {
        patchRegions();
        cashierTick();
        if (S.view === 'dashboard' && S.section === 'prices' && (!P.rows || Date.now() - P.loadedAt > PRICES_REFRESH_MS)) loadPrices();
      }
      pollTimer = setTimeout(tick, S.view === 'monitor' ? MONITOR_POLL_MS : POLL_MS);
    };
    tick();
  }

  function stopPolling() {
    clearTimeout(pollTimer);
    pollTimer = null;
  }

  // ---------------------------------------------------------------- events

  document.addEventListener('click', (event) => {
    const el = event.target.closest('[data-act]');
    if (!el || el.disabled) return;
    if (el.dataset.act.startsWith('rp-')) { REPORTS.act(el.dataset.act, el); return; }
    if (el.dataset.act.startsWith('bar-')) { BAR.act(el.dataset.act, el); return; }
    const action = actions[el.dataset.act];
    if (action) action(el);
  });

  document.addEventListener('change', (event) => {
    if (event.target.id === 'windowsAuth') { readForm(); render(); }
  });

  document.addEventListener('input', (event) => {
    if (event.target.id === 'csMinutes' || event.target.id === 'csMoney') {
      const value = Number(String(event.target.value).replace(',', '.'));
      if (event.target.id === 'csMinutes') C.minutes = value > 0 ? Math.round(value) : 0;
      else C.money = value > 0 ? value : null;
      const typed = event.target.id === 'csMinutes' ? C.minutes : C.money;
      for (const chip of cashierEl.querySelectorAll(event.target.id === 'csMinutes' ? '[data-act="cs-min"]' : '[data-act="cs-money"]')) {
        chip.classList.toggle('on', Number(chip.dataset.v) === typed);
      }
      requestQuote(250);
      return;
    }
    if (event.target.id === 'pcSearch') {
      S.filter.q = event.target.value.trim();
      renderRegion('pcs');
      const clear = $view.querySelector('[data-act="filter-clear"]');
      if (!!clear !== filtering()) renderRegion('tools');
      return;
    }
    if (event.target.id !== 'code') return;
    const raw = event.target.value.toUpperCase().split('').filter((c) => CODE_ALPHABET.test(c)).join('').slice(0, 8);
    const formatted = raw.length > 4 ? `${raw.slice(0, 4)}-${raw.slice(4)}` : raw;
    event.target.value = formatted;
    const wasComplete = S.form.code.replace('-', '').length === 8;
    S.form.code = formatted;
    const button = document.querySelector('[data-act="pair"]');
    if (button) button.disabled = S.busy || raw.length !== 8;
    const preview = document.querySelector('.phone .code');
    if (preview) preview.textContent = formatted || 'ABCD-2345';
    if (!wasComplete && raw.length === 8 && S.error) { S.error = null; }
  });

  document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement && (S.view === 'monitor' || S.view === 'mapFull')) go('dashboard');
  });

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && (S.view === 'dashboard' || S.view === 'mapFull') && S.selected) { actions['pc-close'](); return; }
    if (event.key === 'Escape' && (S.view === 'monitor' || S.view === 'mapFull')) { exitFull(); return; }
    if (event.key !== 'Enter') return;
    if (event.target.id === 'code') actions.pair();
    else if (['adminUser', 'adminPassword'].includes(event.target.id)) actions.database();
  });

  // ---------------------------------------------------------------- boot

  async function boot() {
    const hash = new URLSearchParams(location.hash.slice(1));
    if (hash.get('k')) {
      try { sessionStorage.setItem('mj.k', hash.get('k')); } catch { /* ignore */ }
      S.token = hash.get('k');
    } else {
      try { S.token = sessionStorage.getItem('mj.k'); } catch { S.token = null; }
    }
    const requestedView = hash.get('view');
    // The token must not stay in the address bar or the history.
    history.replaceState(null, '', location.pathname);
    try {
      S.prefs = (await api('GET', 'prefs')) || {};
      if (S.prefs.lang === 'ru' || S.prefs.lang === 'uz') S.lang = S.prefs.lang;
    } catch { /* defaults */ }
    if (hash.get('lang') === 'ru' || hash.get('lang') === 'uz') S.lang = hash.get('lang');

    render();
    try {
      S.state = await api('GET', 'state');
    } catch (error) {
      S.view = 'welcome';
      S.error = error;
      $view.innerHTML = errorBox(error);
      return;
    }

    if (S.state.demo) {
      $demo.textContent = t('demo.banner');
      $demo.hidden = false;
    }

    setInterval(() => { api('POST', 'ping').catch(() => {}); }, PING_MS);

    if (S.state.demo && requestedView) {
      await demoJump(requestedView);
      void checkOnlineUpdate(true);
      return;
    }
    go(S.state.installed ? 'dashboard' : 'welcome');
    void checkOnlineUpdate(true);
    setInterval(() => { void checkOnlineUpdate(); }, 6 * 60 * 60 * 1000);
  }

  /** Demo / screenshot helper: open a step with its result already filled (only in --setup-demo). */
  async function demoJump(view) {
    if (['database', 'install', 'pair', 'done', 'dashboard'].includes(view)) {
      S.preflight = await api('POST', 'preflight');
    }
    if (['install', 'pair', 'done', 'dashboard'].includes(view)) {
      S.db = await api('POST', 'database', { adminUser: 'sa', adminPassword: '', windowsAuth: false });
    }
    if (['pair', 'done', 'dashboard'].includes(view)) {
      S.install = await api('POST', 'install');
    }
    if (['done', 'dashboard', 'paired', 'map', 'list', 'mapEdit', 'monitor', 'detail', 'mapFull', 'cashier', 'prices', 'settings', 'reports', 'bar'].includes(view)) {
      S.preflight = S.preflight || await api('POST', 'preflight');
      S.db = S.db || await api('POST', 'database', { adminUser: 'sa', adminPassword: '', windowsAuth: false });
      S.install = S.install || await api('POST', 'install');
      S.pair = await api('POST', 'pair', { code: 'TEST-2345' });
    }
    S.state = await api('GET', 'state');
    if (view === 'database-error') {
      S.form.adminPassword = 'wrong';
      go('database');
      return actions.database();
    }
    if (view === 'paired') { S.view = 'pair'; render(); return; }
    if (['map', 'list', 'detail', 'monitor', 'mapEdit', 'mapFull', 'cashier', 'prices', 'settings', 'reports', 'bar'].includes(view)) {
      S.tab = view === 'list' ? 'list' : 'map';
      S.section = ['prices', 'settings', 'reports', 'bar'].includes(view) ? view : 'hall';
      await refreshDashboard();
      await loadLayout();
      if (view === 'detail' || view === 'cashier') {
        S.selected = view === 'cashier' ? '3' : '7';
        const picked = pcsOf().find((p) => p.name === S.selected);
        if (picked) openCashier(picked);
      }
      if (view === 'mapEdit') { await openEditor(); return; }
      go(view === 'monitor' || view === 'mapFull' ? view : 'dashboard');
      if (view === 'prices') loadPrices();
      if (view === 'reports') REPORTS.enter();
      if (view === 'bar') BAR.enter();
      return;
    }
    go(view);
  }

  boot();
})();
