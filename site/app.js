// Sims Mod Manager — сайт. Общий скрипт для русской и английской версии; тексты приходят из #i18n.
(() => {
  const T = JSON.parse(document.getElementById('i18n').textContent);
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const restart = (el, cls) => { el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls); };
  const fmt = (s, o) => s.replace(/\{(\w+)\}/g, (_, k) => o[k]);
  const store = {
    get: k => { try { return localStorage.getItem(k); } catch { return null; } },
    set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* браузер может запрещать хранилище */ } },
  };

  // ── Язык: при первом заходе на русскую страницу не из СНГ — открываем английскую ──
  document.querySelectorAll('.lang a').forEach(a => a.addEventListener('click', () => store.set('smm-lang', a.dataset.lang)));
  if (T.lang === 'ru' && !store.get('smm-lang')) {
    const langs = navigator.languages || [navigator.language || ''];
    const ours = langs.some(l => /^(ru|uk|be|kk|ky|uz|hy|az|ka|tg|ro-md)/i.test(l));
    if (!ours && location.protocol.startsWith('http')) { location.replace('en/' + location.hash); return; }
  }

  // ── Коробок: смотрит на курсор, радуется, если гладить, подпрыгивает от клика ──
  const tpl = document.getElementById('buddy-svg');
  const buddies = [...document.querySelectorAll('[data-buddy]')];
  buddies.forEach(buddy => {
    buddy.appendChild(tpl.content.cloneNode(true));
    let lastDir = 0, rub = 0, calm;
    buddy.addEventListener('pointermove', e => {
      const dir = Math.sign(e.movementX);
      if (dir && lastDir && dir !== lastDir) rub++;
      if (dir) lastDir = dir;
      if (rub < 3) return;
      buddy.classList.add('petted');
      if (!reduce && Math.random() < 0.2) {
        const h = document.createElement('span');
        h.className = 'heart'; h.textContent = '♥';
        h.style.left = 20 + Math.random() * 60 + '%'; h.style.top = '-4px';
        buddy.appendChild(h); setTimeout(() => h.remove(), 1000);
      }
      clearTimeout(calm);
      calm = setTimeout(() => { buddy.classList.remove('petted'); rub = 0; }, 900);
    });
    buddy.addEventListener('pointerleave', () => { lastDir = 0; });
    buddy.addEventListener('click', () => { if (!reduce) restart(buddy, 'hop'); });
  });
  addEventListener('pointermove', e => {
    buddies.forEach(buddy => {
      const r = buddy.getBoundingClientRect();
      if (r.bottom < -200 || r.top > innerHeight + 200) return;
      const dx = e.clientX - (r.left + r.width / 2), dy = e.clientY - (r.top + r.height * 0.4);
      const d = Math.hypot(dx, dy) || 1, k = Math.min(1, d / 160);
      buddy.querySelectorAll('.pupil').forEach(p => p.style.transform = `translate(${(dx / d) * 2.6 * k}px, ${(dy / d) * 2.4 * k}px)`);
    });
  }, { passive: true });

  // ── Окно выпрямляется при прокрутке ──
  const showcase = document.getElementById('showcase');
  const tilt = () => {
    const r = showcase.getBoundingClientRect();
    showcase.style.setProperty('--p', reduce ? 1 : clamp((innerHeight - r.top) / (innerHeight * 0.85) - 0.2).toFixed(3));
  };

  // ── Архив: тащишь мышкой и бросаешь в окно ──
  const zip = document.getElementById('zip');
  const win = document.getElementById('window');
  const toast = document.getElementById('toast');
  const heroBuddy = document.getElementById('hero-buddy');
  const heroBubble = heroBuddy.querySelector('.bubble');
  let drag = null, busy = false;
  const overWindow = (x, y) => { const r = win.getBoundingClientRect(); return x > r.left && x < r.right && y > r.top && y < r.bottom; };
  const installed = () => {
    busy = true;
    zip.classList.add('gone');
    win.classList.remove('over');
    toast.classList.add('show');
    heroBubble.textContent = T.installed;
    heroBuddy.classList.add('talk', 'cheer');
    if (!reduce) restart(heroBuddy, 'hop');
    setTimeout(() => {
      toast.classList.remove('show');
      heroBuddy.classList.remove('talk', 'cheer');
      heroBubble.textContent = T.pet;
      zip.style.transform = '';
      zip.classList.remove('gone');
      busy = false;
    }, 3600);
  };
  zip.addEventListener('pointerdown', e => {
    if (busy) return;
    zip.setPointerCapture(e.pointerId);
    zip.classList.remove('back');
    drag = { x: e.clientX, y: e.clientY, moved: false };
  });
  zip.addEventListener('pointermove', e => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (Math.hypot(dx, dy) > 4) { drag.moved = true; zip.classList.add('dragging'); }
    zip.style.transform = `translate(${dx}px, ${dy}px)`;
    win.classList.toggle('over', overWindow(e.clientX, e.clientY));
  });
  const drop = e => {
    if (!drag) return;
    const moved = drag.moved; drag = null;
    zip.classList.remove('dragging');
    if (!moved || overWindow(e.clientX, e.clientY)) { installed(); return; } // клик тоже «бросает» архив
    zip.classList.add('back'); zip.style.transform = ''; win.classList.remove('over');
  };
  zip.addEventListener('pointerup', drop);
  zip.addEventListener('pointercancel', drop);
  zip.addEventListener('keydown', e => { if ((e.key === 'Enter' || e.key === ' ') && !busy) { e.preventDefault(); installed(); } });

  // ── До и после: ползунок тянется мышкой, пальцем или стрелками ──
  const cmp = document.getElementById('compare');
  const setPos = v => { v = clamp(v, 0, 100); cmp.style.setProperty('--pos', v + '%'); cmp.setAttribute('aria-valuenow', Math.round(v)); };
  let cmpDrag = false, cmpTouched = false;
  const fromEvent = e => { const r = cmp.getBoundingClientRect(); return (e.clientX - r.left) / r.width * 100; };
  cmp.addEventListener('pointerdown', e => { cmpDrag = true; cmpTouched = true; cmp.setPointerCapture(e.pointerId); setPos(fromEvent(e)); });
  cmp.addEventListener('pointermove', e => { if (cmpDrag) setPos(fromEvent(e)); });
  cmp.addEventListener('pointerup', () => { cmpDrag = false; });
  cmp.addEventListener('keydown', e => {
    const now = +cmp.getAttribute('aria-valuenow');
    if (e.key === 'ArrowLeft') { setPos(now - 5); e.preventDefault(); cmpTouched = true; }
    if (e.key === 'ArrowRight') { setPos(now + 5); e.preventDefault(); cmpTouched = true; }
  });
  // Подсказка: один раз сам «прогуливается», когда появляется на экране
  if (!reduce) new IntersectionObserver(([e], o) => {
    if (!e.isIntersecting) return;
    o.disconnect();
    const t0 = performance.now();
    const step = t => {
      if (cmpTouched) return;
      const k = (t - t0) / 1800;
      if (k >= 1) { setPos(50); return; }
      setPos(50 + Math.sin(k * Math.PI * 2) * 22 * (1 - k));
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }, { threshold: 0.6 }).observe(cmp);

  // ── Демо в игре: F9 → курсор нажимает «Откатить» → окно игры «Вернуться к точке?» ──
  const demo = document.getElementById('ingame-demo');
  const key = document.getElementById('keycap');
  const cursor = document.getElementById('cursor');
  const ripple = document.getElementById('ripple');
  let demoTimer = null, hotkey = 'F9';
  const at = (el, x, y) => { el.style.left = x + '%'; el.style.top = y + '%'; };
  const runDemo = () => {
    [
      [0,    () => { demo.dataset.phase = '0'; at(cursor, 72, 76); key.classList.add('down'); }],
      [220,  () => key.classList.remove('down')],
      [600,  () => { demo.dataset.phase = '1'; }],
      [700,  () => at(cursor, 15.2, 31.6)],
      [1900, () => { demo.dataset.phase = '2'; restart(cursor, 'click'); at(ripple, 15.2, 31.6); restart(ripple, 'go'); }],
      [2400, () => { demo.dataset.phase = '3'; at(cursor, 52, 58); }],
      [3700, () => { restart(cursor, 'click'); at(ripple, 52, 58); restart(ripple, 'go'); }],
      [5600, () => { demo.dataset.phase = '0'; }],
    ].forEach(([t, f]) => setTimeout(f, t));
  };
  if (!reduce) {
    new IntersectionObserver(([e]) => {
      if (e.isIntersecting && !demoTimer) { runDemo(); demoTimer = setInterval(runDemo, 7000); }
      if (!e.isIntersecting && demoTimer) { clearInterval(demoTimer); demoTimer = null; demo.dataset.phase = '0'; }
    }, { threshold: 0.5 }).observe(demo);
  }

  // ── Шкала времени: ползунок идёт к «сейчас», потом перематывается к точке ──
  const tl = document.getElementById('timeline');
  const marks = tl.querySelectorAll('.mark');
  const moments = tl.querySelectorAll('.moment');
  const timeline = () => {
    const r = tl.getBoundingClientRect();
    const p = reduce ? 1 : clamp((innerHeight * 0.92 - r.top) / (innerHeight * 0.55));
    let x, ghost = 0, rewind = false, stage;
    if (p < 0.72) { x = p / 0.72; stage = x < 0.45 ? 0 : 1; }
    else { const q = clamp((p - 0.72) / 0.2); x = 1 - q * 0.55; ghost = q; rewind = true; stage = 2; }
    tl.style.setProperty('--x', x.toFixed(3));
    tl.style.setProperty('--fill', x.toFixed(3));
    tl.style.setProperty('--ghost', ghost.toFixed(3));
    tl.classList.toggle('rewind', rewind);
    marks.forEach(m => { const a = +m.dataset.at; m.classList.toggle('on', a === 0 || (a === 1 && x >= 0.45) || (a === 2 && (rewind || x >= 0.99))); });
    moments.forEach(m => m.classList.toggle('on', +m.dataset.m <= stage));
  };

  // ── Отличия: справа меняется скриншот под текущий пункт ──
  const items = document.querySelectorAll('.f-item');
  const figs = document.querySelectorAll('.f-stage figure');
  const io = new IntersectionObserver(es => es.forEach(e => {
    if (!e.isIntersecting) return;
    const i = e.target.dataset.f;
    items.forEach(it => it.classList.toggle('on', it.dataset.f === i));
    figs.forEach(f => f.classList.toggle('on', f.dataset.f === i));
  }), { rootMargin: '-45% 0px -45% 0px' });
  items.forEach(it => io.observe(it));

  // ── Настрой под себя: всё на странице настоящее и работает ──
  document.querySelectorAll('.switch').forEach(sw => sw.addEventListener('click', () => {
    sw.setAttribute('aria-checked', sw.getAttribute('aria-checked') === 'true' ? 'false' : 'true');
  }));
  document.getElementById('buddy-switch').addEventListener('click', e => {
    document.body.classList.toggle('no-buddy', e.currentTarget.getAttribute('aria-checked') === 'false');
  });
  document.querySelectorAll('.seg').forEach(seg => seg.querySelectorAll('button').forEach(b => b.addEventListener('click', () => {
    seg.querySelectorAll('button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
  })));
  const keep = document.getElementById('keep'), keepOut = document.getElementById('keep-out');
  keep.addEventListener('input', () => { keepOut.textContent = +keep.value >= 98 ? T.forever : fmt(T.days, { n: keep.value }); });
  // Выбор клавиши: следующая нажатая клавиша становится клавишей оверлея — и в демо выше тоже
  const pick = document.getElementById('key-pick');
  const keyName = e => {
    const mods = [e.ctrlKey && 'Ctrl', e.shiftKey && 'Shift', e.altKey && 'Alt'].filter(Boolean);
    let k = e.key;
    if (['Control', 'Shift', 'Alt', 'Meta'].includes(k)) return null;
    if (k === ' ') k = 'Space';
    else if (k.length === 1) k = (e.code.startsWith('Key') ? e.code.slice(3) : e.code.startsWith('Digit') ? e.code.slice(5) : k.toUpperCase());
    if (!mods.length && /^[A-Z0-9]$/.test(k)) return null; // одиночная буква мешала бы играть
    return [...mods, k].join('+');
  };
  pick.addEventListener('click', () => { pick.classList.add('listen'); pick.textContent = T.listen; });
  pick.addEventListener('blur', () => { pick.classList.remove('listen'); pick.textContent = hotkey; });
  pick.addEventListener('keydown', e => {
    if (!pick.classList.contains('listen')) return;
    if (e.key === 'Tab') return;
    e.preventDefault();
    if (e.key === 'Escape') { pick.blur(); return; }
    const name = keyName(e);
    if (!name) return;
    hotkey = name;
    pick.textContent = name; key.textContent = name.length > 4 ? name.split('+').pop() : name;
    pick.classList.remove('listen');
  });

  // ── С телефона: скопировать ссылку и открыть на компьютере ──
  const copy = document.getElementById('pc-copy'), link = document.getElementById('pc-link');
  copy.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(link.value); } catch { link.select(); }
    copy.textContent = T.copied;
  });

  // ── Версия и свежесть сборки — с GitHub ──
  const ago = iso => {
    const days = Math.floor((Date.now() - Date.parse(iso)) / 864e5);
    return days <= 0 ? T.today : days === 1 ? T.yesterday : fmt(T.daysAgo, { n: days });
  };
  fetch('https://api.github.com/repos/DrowWithSkadi/SimsModManager/releases/tags/dev')
    .then(r => r.ok ? r.json() : Promise.reject())
    .then(rel => {
      const setup = rel.assets.find(a => /Setup\.exe$/.test(a.name));
      const v = (rel.name.match(/\d+\.\d+\.\d+/) || [''])[0];
      const total = rel.assets.filter(a => /Setup\.exe$|Portable\.zip$/.test(a.name)).reduce((s, a) => s + a.download_count, 0);
      if (!v || !setup) return;
      const el = document.getElementById('release');
      el.innerHTML = '<span class="dot"></span>';
      el.append(fmt(T.release, { v, when: ago(setup.updated_at) }) + (total >= 100 ? fmt(T.downloads, { n: total }) : ''));
    }).catch(() => {});
  // ── Кристалл крутится, когда доходишь до скачивания ──
  const crystal = document.getElementById('crystal');
  if (!reduce) new IntersectionObserver(([e]) => { if (e.isIntersecting) restart(crystal, 'spin'); }, { threshold: 0.8 }).observe(crystal);

  let ticking = false;
  const frame = () => { ticking = false; tilt(); timeline(); };
  addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(frame); } }, { passive: true });
  addEventListener('resize', frame);
  frame();
})();
