(() => {
  'use strict';

  const $ = (s) => document.querySelector(s);
  const $$ = (s) => Array.prototype.slice.call(document.querySelectorAll(s));
  const canvas = $('#canvas');
  const ctx = canvas.getContext('2d');
  let W = innerWidth, H = innerHeight, DPR = Math.min(devicePixelRatio || 1, 2);

  const DEMO_DURATION = 34;
  const DEMO_LYRICS = [
    { time: 0.4, text: '夜空把这一句点亮' },
    { time: 4.6, text: '让所有沉默开出花' },
    { time: 8.8, text: '把光留在你眼底' },
    { time: 13.0, text: '我们都在寻找一个答案' },
    { time: 17.2, text: '烟花落下之前' },
    { time: 21.4, text: '再唱一次' },
    { time: 25.6, text: '把夜晚折成一张唱片' },
    { time: 29.8, text: '直到天亮' }
  ];

  // 18 个网页样式。颜色/字体/版式/场景都按 Folia 0.7.27 的样式名和方向重画，
  // 不是 Folia 原版 Pixi/WebGL 引擎的代码。
  const STYLES = [
    { id: 'sonnet', name: '商籁', en: 'SONNET', desc: '排版拼贴 / 衬线大字', bg: ['#121318', '#050508'], ink: '#f4f4f5', dim: '#848791', accent: '#c6c8d0', accent2: '#71717a', font: 'mincho', layout: 'sonnetModes', scene: 'sonnet', scale: 1.0, note: '拼贴 · 衬线 · 纸上墨迹' },
    { id: 'tempera', name: '凝彩', en: 'TEMPERA', desc: '纸片色块 / 拼贴海报', bg: ['#271d17', '#0e0b0a'], ink: '#f9eccf', dim: '#b79674', accent: '#e85d3b', accent2: '#5da8c4', font: 'display', layout: 'poster', scene: 'tempera', scale: 1.0, note: '纸片 · 色块 · 手作拼贴' },
    { id: 'lumiere', name: '绘光', en: 'LUMIERE', desc: '舞台光束 / 暖色泛光', bg: ['#0c0908', '#030203'], ink: '#fff4e5', dim: '#ae977d', accent: '#ffb154', accent2: '#e67850', font: 'sans', layout: 'rows', scene: 'lumiere', scale: 1.0, note: '光束 · 尘埃 · 舞台' },
    { id: 'hanabi', name: '花火', en: 'HANABI', desc: '夜空烟花 / 彩纸歌词', bg: ['#0a0c1e', '#04040c'], ink: '#fff6e1', dim: '#a0aac8', accent: '#ff4878', accent2: '#4cc9f0', font: 'display', layout: 'hanabi', scene: 'hanabi', scale: 1.0, note: '烟花 · 彩纸 · 镜头推进' },
    { id: 'machi', name: '町景', en: 'MACHI', desc: '霓虹街景 / 招牌字', bg: ['#060a16', '#020308'], ink: '#e6f0ff', dim: '#7891aa', accent: '#ff2e88', accent2: '#00e5ff', font: 'sans', layout: 'poster', scene: 'machi', scale: 1.0, note: '霓虹 · 雨夜 · 招牌' },
    { id: 'nebbia', name: '雾笺', en: 'NEBBIA', desc: '起雾车窗 / 雨滴', bg: ['#4e5964', '#20262c'], ink: '#f0f6fa', dim: '#a6b5c2', accent: '#b4d2e4', accent2: '#7e9cb0', font: 'sans', layout: 'stack', scene: 'nebbia', scale: 1.0, note: '雾 · 雨滴 · 车窗' },
    { id: 'classic', name: '流光', en: 'CLASSIC', desc: '流光轨迹 / 渐变字', bg: ['#05060a', '#020205'], ink: '#f5f7ff', dim: '#8c96b4', accent: '#7dd3fc', accent2: '#c084fc', font: 'sans', layout: 'rows', scene: 'classic', scale: 1.0, note: '流光 · 轨迹 · 渐变' },
    { id: 'cadenze', name: '心象', en: 'CADENZE', desc: '墨色心象 / 朱印', bg: ['#130f0d', '#060505'], ink: '#f1e9dc', dim: '#968778', accent: '#b23a2e', accent2: '#6e5a4b', font: 'serif', layout: 'collage', scene: 'cadenze', scale: 1.0, note: '水墨 · 朱印 · 留白' },
    { id: 'partita', name: '云阶', en: 'PARTITA', desc: '云层台阶 / 柔光', bg: ['#d0dbe7', '#93a3b8'], ink: '#1c2430', dim: '#5a6978', accent: '#5078aa', accent2: '#c8aa78', font: 'serif', layout: 'rows', scene: 'partita', scale: 1.0, light: true, note: '云层 · 台阶 · 柔光' },
    { id: 'fume', name: '浮名', en: 'FUME', desc: '浮动的名字 / 细字', bg: ['#0a0a0c', '#040405'], ink: '#e8e8ee', dim: '#82848e', accent: '#9aa0b2', accent2: '#5a5c69', font: 'serif', layout: 'stack', scene: 'fume', scale: 1.0, note: '烟 · 名字 · 浮动' },
    { id: 'tilt', name: '倾述', en: 'TILT', desc: '信纸手写 / 倾斜', bg: ['#19140f', '#090705'], ink: '#f4e7d3', dim: '#aa9178', accent: '#d97941', accent2: '#b4a082', font: 'hand', layout: 'stack', scene: 'tilt', scale: 1.0, note: '信纸 · 手写 · 倾斜' },
    { id: 'ponder', name: '思索', en: 'PONDER', desc: '极简暗色 / 等宽', bg: ['#0b0c10', '#040507'], ink: '#d8dbe4', dim: '#787d8c', accent: '#6b7280', accent2: '#505564', font: 'mono', layout: 'stack', scene: 'ponder', scale: 1.0, note: '环 · 留白 · 等宽' },
    { id: 'still', name: '清', en: 'STILL', desc: '留白 / 浅色纸', bg: ['#f4f3ee', '#d2d0c8'], ink: '#171717', dim: '#646464', accent: '#4b555f', accent2: '#a0a09b', font: 'serif', layout: 'stack', scene: 'still', scale: 1.05, light: true, note: '纸 · 留白 · 一点红' },
    { id: 'pendolo', name: '时计', en: 'PENDOLO', desc: '钟表齿轮 / 黄铜', bg: ['#0d0a07', '#040302'], ink: '#f7e7b0', dim: '#aa966e', accent: '#c9a227', accent2: '#82643c', font: 'mono', layout: 'stack', scene: 'pendolo', scale: 1.0, note: '钟 · 齿轮 · 黄铜' },
    { id: 'cappella', name: '群唱', en: 'CAPPELLA', desc: '多重人声 / 叠句', bg: ['#0c0a10', '#040408'], ink: '#ffffff', dim: '#a0a0b4', accent: '#ff78b4', accent2: '#78c8ff', font: 'sans', layout: 'poster', scene: 'cappella', scale: 1.0, note: '声部 · 叠句 · 舞台' },
    { id: 'claddagh', name: '回环', en: 'CLADDAGH', desc: '环形轨迹 / 青绿', bg: ['#081311', '#020605'], ink: '#eafff7', dim: '#82aaa0', accent: '#34d399', accent2: '#5eead4', font: 'sans', layout: 'rows', scene: 'claddagh', scale: 1.0, note: '环 · 回环 · 青绿' },
    { id: 'monet', name: '莫奈', en: 'MONET', desc: '印象派水色 / 柔焦', bg: ['#86a3b5', '#40596c'], ink: '#f7fbff', dim: '#bcd0de', accent: '#a6cddc', accent2: '#7eb09a', font: 'serif', layout: 'collage', scene: 'monet', scale: 1.0, note: '水色 · 柔焦 · 花瓣' },
    { id: 'diorama', name: '镜台', en: 'DIORAMA', desc: '镜面舞台 / 对称', bg: ['#0b0c10', '#030407'], ink: '#f0f0f5', dim: '#8c919b', accent: '#b8a1ff', accent2: '#788cb4', font: 'serif', layout: 'poster', scene: 'diorama', scale: 1.0, note: '镜面 · 对称 · 舞台' }
  ];

  const DEFAULTS = {
    styleId: 'hanabi',
    bloom: true,
    push: true,
    spark: 1,
    sparkGlow: 1,
    chorusGlow: 1.3,
    glow: 0.8,
    trail: true,
    camera: 1,
    motion: 1,
    transparent: false,
    dim: 0,
    decor: 1,
    fontScale: 1,
    subtitle: false,
    reduceMotion: false,
    transitions: true,
    randomSong: false
  };

  let settings = Object.assign({}, DEFAULTS, readSettings());
  let style = STYLES.filter(s => s.id === settings.styleId)[0] || STYLES[3];
  let state = null;
  let demo = true;
  let demoStart = performance.now();
  let lastStateAt = performance.now();
  let lineIndex = -1;
  let shot = null;
  let lastBurstAt = 0;
  let lineFlash = 0;
  let panelOpen = false;

  let bits = { stars: [], confetti: [], stickers: [], dust: [], drops: [], bokeh: [], petals: [], letters: [], blocks: [], gears: [], clouds: [], rain: [] };
  let bursts = [];
  let camera = { x: 0, y: 0, z: 1.04, r: 0 };
  let cameraTarget = { x: 0, y: 0, z: 1.04, r: 0 };
  let wallpaperImage = null;
  let wallpaperReady = false;

  // ------------------------------------------------------------------ settings

  function readSettings() {
    try { return JSON.parse(localStorage.getItem('capsule-hanabi-settings') || '{}'); }
    catch (e) { return {}; }
  }

  function saveSettings() {
    try { localStorage.setItem('capsule-hanabi-settings', JSON.stringify(settings)); } catch (e) { }
  }

  function post(msg) {
    if (window.chrome && chrome.webview) {
      try { chrome.webview.postMessage(msg); return true; } catch (e) { }
    }
    return false;
  }

  function toast(text) {
    const el = $('#toast');
    if (!el) return;
    el.textContent = text;
    el.classList.add('show');
    clearTimeout(toast.t);
    toast.t = setTimeout(() => el.classList.remove('show'), 1800);
  }

  function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
  function rnd(seed, salt) {
    const x = Math.sin(seed * 127.1 + salt * 311.7 + 74.7) * 43758.5453;
    return x - Math.floor(x);
  }
  function easeOutExpo(t) { t = clamp(t, 0, 1); return t >= 1 ? 1 : 1 - Math.pow(2, -10 * t); }
  function easeOutBack(t) {
    t = clamp(t, 0, 1);
    const c1 = 1.70158, c3 = c1 + 1;
    return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2);
  }
  function isCjk(ch) { return /[\u3400-\u9fff\u3040-\u30ff\uac00-\ud7af]/.test(ch); }

  function familyFor(kind) {
    if (kind === 'serif') return '"Georgia","Times New Roman","Songti SC","SimSun",serif';
    if (kind === 'mincho') return '"Yu Mincho","游明朝","Hiragino Mincho ProN","SimSun","Songti SC","Times New Roman",serif';
    if (kind === 'gothic') return '"Yu Gothic","游ゴシック","Microsoft YaHei UI","Segoe UI",sans-serif';
    if (kind === 'rounded') return '"Yu Gothic UI","Microsoft YaHei UI","Arial Rounded MT Bold","Segoe UI",sans-serif';
    if (kind === 'cinema') return '"Palatino Linotype","Book Antiqua","Georgia","Times New Roman",serif';
    if (kind === 'display') return '"Arial Black","Microsoft YaHei UI",sans-serif';
    if (kind === 'condensed') return '"Arial Narrow","Impact","Segoe UI",sans-serif';
    if (kind === 'hand') return '"Segoe Script","KaiTi",cursive';
    if (kind === 'mono') return '"Consolas","Microsoft YaHei UI",monospace';
    if (kind === 'hanabi') return '"Yu Mincho","Yu Gothic","Microsoft YaHei UI","Segoe UI",sans-serif';
    return '"Segoe UI","Microsoft YaHei UI",sans-serif';
  }

  function fontFor(size, bold, kind, italic) {
    const size2 = Math.max(8, size * (settings.fontScale || 1));
    const family = familyFor(kind || (style ? style.font : 'sans'));
    return `${italic ? 'italic ' : ''}${bold ? 900 : 700} ${size2}px ${family}`;
  }

  function measure(text, size, kind) {
    ctx.font = fontFor(size, true, kind);
    return ctx.measureText(text).width;
  }

  function splitWords(text) {
    const out = [];
    if (!text) return out;
    const clean = String(text).replace(/\s+/g, ' ').trim();
    if (!clean) return out;
    let latin = '', cjk = '';
    const flushLatin = () => { const t = latin.trim(); latin = ''; if (t) out.push(t); };
    const flushCjk = () => {
      if (!cjk) return;
      const run = cjk; cjk = '';
      let i = 0;
      while (i < run.length) {
        const take = Math.min(3, run.length - i);
        out.push(run.slice(i, i + take));
        i += take;
      }
    };
    for (let i = 0; i < clean.length; i++) {
      const ch = clean[i];
      if (ch === ' ') { flushLatin(); flushCjk(); continue; }
      if (isCjk(ch)) { flushLatin(); cjk += ch; }
      else { flushCjk(); latin += ch; }
    }
    flushLatin(); flushCjk();
    return out;
  }

  function palette() {
    return [style.ink, style.accent, style.accent2, style.ink, style.accent];
  }

  // ------------------------------------------------------------------ state

  function applyState(msg) {
    if (!msg) return;
    const hasSong = !!(msg.title || msg.songMid);
    const hadSong = !!(state && state.title);
    const newKey = (msg.songMid || '') + '|' + (msg.title || '');
    if (newKey !== (state && state.key)) {
      lineIndex = -1;
      shot = null;
      bursts = [];
      if (settings.randomSong && hasSong && hadSong && msg.title) {
        const others = STYLES.filter(s => s.id !== style.id);
        const pick = others[Math.floor(Math.random() * others.length)];
        applyStyle(pick.id, true);
      }
    }
    demo = !hasSong && (!msg.lyrics || msg.lyrics.length === 0);
    state = {
      key: newKey,
      title: msg.title || '',
      artist: msg.artist || '',
      position: Number(msg.position) || 0,
      duration: Number(msg.duration) || 0,
      playing: !!msg.playing,
      lyrics: Array.isArray(msg.lyrics) ? msg.lyrics : [],
      lastAt: performance.now(),
      demo: demo
    };
    if (demo) state.playing = true;
    lastStateAt = performance.now();
    renderBar();
    ensureLine(false);
  }

  function currentPosition(now) {
    if (!state) return ((now - demoStart) / 1000) % DEMO_DURATION;
    if (state.demo) return ((now - demoStart) / 1000) % DEMO_DURATION;
    let p = state.position + (state.playing ? (now - state.lastAt) / 1000 : 0);
    if (state.duration > 0) p = Math.min(p, state.duration + 3);
    return Math.max(0, p);
  }

  function lyricLines() {
    if (state && !state.demo && state.lyrics.length) return state.lyrics;
    if (state && state.demo) return DEMO_LYRICS;
    return [];
  }

  function currentLineText() {
    const lines = lyricLines();
    if (!lines.length) return '';
    const p = currentPosition(performance.now());
    let line = lines[0];
    for (const l of lines) if (l.time <= p) line = l;
    return line.text || '';
  }

  function ensureLine(force) {
    const now = performance.now();
    const pos = currentPosition(now);
    const lines = lyricLines();
    if (!lines.length) {
      if (!shot || shot.kind !== 'title') buildTitleShot();
      return;
    }
    if (pos < lines[0].time - 0.2) {
      if (!shot || shot.kind !== 'title') buildTitleShot();
      return;
    }
    let idx = 0;
    for (let i = 0; i < lines.length; i++) {
      if (lines[i].time <= pos + 0.03) idx = i;
      else break;
    }
    if (idx !== lineIndex || force || !shot || shot.kind !== 'line') buildLine(lines, idx);
  }

  // ------------------------------------------------------------------ layout

  function basicDecoFor(idx, start) {
    const d = [];
    d.push({ type: 'label', x: W * .11, y: H * .14, rot: -0.1, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
    d.push({ type: 'sticker', shape: 'star', x: W * .86, y: H * .18, rot: -.2, size: 14, color: style.accent2, start: start + 0.4 });
    d.push({ type: 'sticker', shape: 'flower', x: W * .13, y: H * .78, rot: .2, size: 15, color: style.accent, start: start + 0.45 });
    if (style.light || style.id === 'tilt' || style.id === 'cadenze') {
      d.push({ type: 'dash', x: 0, y: 0, x1: W * .08, y1: H * .73, x2: W * .92, y2: H * .73, color: style.accent, start: start + 0.45 });
    }
    if (style.id === 'machi' || style.id === 'classic' || style.id === 'claddagh') {
      d.push({ type: 'tape', x: W * .82, y: H * .24, rot: .35, w: 62, h: 16, color: hexA(style.accent, .26), start: start + 0.35 });
    }
    return d;
  }

  function buildTitleShot() {
    const title = (state && state.title) || 'HANABI · 花火';
    const artist = (state && state.artist) || 'CAPSULE × WEBVIEW2';
    const tokens = splitWords(title);
    const hero = tokens.length ? tokens[tokens.length - 1] : title;
    const size = Math.min(H * 0.17, W / 8.5);
    const words = [{
      text: hero, size: size, x: W * 0.5, y: H * 0.47, rot: -0.015,
      color: style.ink, start: -0.2, enterX: 0, enterY: 20, hero: true, block: false
    }];
    for (let i = 0; i < tokens.length - 1 && i < 3; i++) {
      words.push({
        text: tokens[i], size: size * 0.28,
        x: W * 0.5 + (i - 1) * (measure(tokens[i], size * 0.28) + 30), y: H * 0.47 + size * 0.72,
        rot: 0, color: i % 2 ? style.accent2 : style.accent, start: 0.08 + i * 0.1,
        enterX: 0, enterY: 16, hero: false, block: false
      });
    }
    words.push({
      text: artist, size: Math.min(24, H * 0.026), x: W * 0.5, y: H * 0.47 + size * 0.95,
      rot: 0, color: style.dim, start: 0.15, enterX: 0, enterY: 12, hero: false, block: false
    });
    shot = {
      kind: 'title', words: words,
      ghost: { text: hero, size: size * 1.7, x: W * 0.5, y: H * 0.43, rot: -0.05 },
      lineIndex: -1, start: 0, end: 8, seed: 1
    };
    lineIndex = -1;
    cameraTarget = { x: 0.01, y: 0.005, z: 1.03, r: -0.006 };
    lastBurstAt = performance.now() - 1200;
  }

  // ------------------------------------------------------------------ card / scrapbook composition

  function joinPhrase(tokens) {
    if (!tokens || !tokens.length) return '';
    let cjk = 0, latin = 0;
    for (const t of tokens) {
      if (isCjk(t.charAt(0))) cjk++; else latin++;
    }
    return cjk >= latin ? tokens.join('') : tokens.join(' ');
  }

  function rotatePoint(lx, ly, rot) {
    const c = Math.cos(rot), s = Math.sin(rot);
    return { x: lx * c - ly * s, y: lx * s + ly * c };
  }

  function cardPaper() {
    return style.cardPaper || (style.light ? '#fbf7ec' : '#f3e5c8');
  }

  function buildCardComposition(tokens, idx, start, end, wordTimes, base, heroIndex, nextLine) {
    const useRibbon = ['tempera', 'hanabi', 'partita', 'monet', 'diorama'].indexOf(style.id) >= 0;
    const variant = idx % 3;
    const ribbonSide = variant === 1 ? -1 : 1;
    const cx = W * (W < 900 ? 0.5 : (useRibbon ? (ribbonSide > 0 ? 0.44 : 0.56) : (variant === 1 ? 0.54 : 0.5)));
    const cy = H * (W < 900 ? 0.5 : 0.51);
    const cw = W * (W < 900 ? 0.84 : (useRibbon ? 0.60 : 0.66));
    const ch = H * (W < 900 ? 0.34 : 0.32);
    const cardRot = (rnd(idx, 900) - 0.5) * 0.07 + (variant === 1 ? 0.022 : -0.018);
    const ink = '#241b14';
    const paper = cardPaper();
    const cardFont = style.id === 'tilt' ? 'hand' : 'serif';

    const cards = [];
    const deco = [];
    const words = [];

    // 第二张纸/错位底卡，模仿 Folia 的剪贴簿层叠
    if (style.layout === 'card' && (style.light || style.id === 'tilt' || style.id === 'cadenze' || style.id === 'sonnet' || style.id === 'diorama')) {
      cards.push({
        x: cx + 18, y: cy + 16, w: cw * 0.97, h: ch, rot: cardRot + 0.026,
        paper: style.light ? '#e8ddc9' : '#e8d3ae', border: hexA(style.accent, .55),
        start: start + 0.02, back: true
      });
    }
    cards.push({
      x: cx, y: cy, w: cw, h: ch, rot: cardRot,
      paper: paper, border: style.accent, start: start + 0.04
    });

    const cut = tokens.length > 2 ? Math.max(1, Math.ceil(tokens.length * 0.55)) : tokens.length;
    let l1 = tokens.slice(0, cut);
    let l2 = tokens.slice(cut);
    if (!l2.length && tokens.length > 1) {
      l1 = tokens.slice(0, Math.ceil(tokens.length / 2));
      l2 = tokens.slice(Math.ceil(tokens.length / 2));
    }

    function fitLine(line, size, maxW) {
      let total = 0;
      for (const t of line) total += measure(t, size, cardFont);
      total += 16 * Math.max(0, line.length - 1);
      if (total > maxW && total > 0) size *= maxW / total;
      return size;
    }

    function addCardLine(line, firstIndex, localY, size, color, highlight) {
      if (!line.length) return;
      size = fitLine(line, size, cw * 0.84);
      const widths = line.map(t => measure(t, size, cardFont));
      let allCjk = true;
      for (const t of line) if (!isCjk(t.charAt(0))) allCjk = false;
      const gap = allCjk ? 2 : 16;
      let total = widths.reduce((a, b) => a + b, 0) + gap * Math.max(0, line.length - 1);
      let lx = -total / 2;
      for (let i = 0; i < line.length; i++) {
        const p = rotatePoint(lx + widths[i] / 2, localY, cardRot);
        const tIndex = Math.min(wordTimes.length - 1, firstIndex + i);
        words.push({
          text: line[i], size: size, x: cx + p.x, y: cy + p.y,
          rot: cardRot, color: color, fontKind: cardFont,
          start: wordTimes[tIndex] || start, enterX: 0, enterY: 18,
          hero: false, block: false, card: true,
          cardBlock: !!highlight && i === Math.floor(line.length / 2)
        });
        lx += widths[i] + gap;
      }
    }

    addCardLine(l1, 0, -ch * 0.17, base * 0.60, ink, false);
    if (l2.length) addCardLine(l2, cut, ch * 0.13, base * 0.76, ink, true);

    let ribbon = null;
    if (useRibbon) {
      // 斜向红/彩色飘带：用下一句或本句前几个词
      let ribbonText = '';
      if (nextLine && nextLine.text) {
        const nt = splitWords(nextLine.text);
        ribbonText = joinPhrase(nt.slice(0, 4));
      }
      if (!ribbonText) ribbonText = joinPhrase(tokens.slice(0, Math.min(4, tokens.length)));
      if (ribbonText.length > 28) ribbonText = ribbonText.slice(0, 28);
      ribbon = {
        x: W * (ribbonSide > 0 ? 0.80 : 0.20), y: H * (W < 900 ? 0.30 : 0.38),
        rot: ribbonSide > 0 ? -1.05 : 1.05,
        length: W * 0.54, thick: H * (W < 900 ? 0.12 : 0.095),
        color: style.accent, start: start + 0.18
      };
      let ribbonSize = base * 0.44;
      while (measure(ribbonText, ribbonSize, 'condensed') > ribbon.length * 0.86 && ribbonSize > 12) ribbonSize -= 2;
      const ribbonInk = luminance(style.accent) > 0.62 ? '#1d1812' : '#fff8ee';
      words.push({
        text: ribbonText, size: ribbonSize, x: ribbon.x, y: ribbon.y,
        rot: ribbon.rot, color: ribbonInk, fontKind: 'condensed',
        start: start + 0.24, enterX: 0, enterY: 0, hero: false, block: false, ribbon: true
      });

    }

    const ghostText = tokens[heroIndex] || tokens[0];
    const ghost = {
      text: ghostText, size: Math.min(base * 1.5, H * 0.22),
      x: W * 0.5, y: H * 0.5, rot: cardRot - 0.05, color: style.ink
    };

    // 贴纸/胶带/编号/虚线/剪刀/印章
    deco.push({ type: 'label', x: cx - cw * 0.38, y: cy - ch * 0.42, rot: cardRot, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.38 });
    deco.push({ type: 'tape', x: cx - cw * 0.34, y: cy - ch * 0.50, rot: cardRot - 0.35, w: 74, h: 20, color: hexA(style.ink, .26), start: start + 0.28 });
    deco.push({ type: 'tape', x: cx + cw * 0.33, y: cy - ch * 0.47, rot: cardRot + 0.28, w: 66, h: 18, color: hexA(style.accent, .32), start: start + 0.30 });
    deco.push({ type: 'sticker', shape: 'flower', x: cx - cw * 0.44, y: cy + ch * 0.34, rot: 0.2, size: 24, color: style.accent, start: start + 0.4 });
    deco.push({ type: 'sticker', shape: 'star', x: cx + cw * 0.44, y: cy - ch * 0.34, rot: -0.2, size: 22, color: style.accent2, start: start + 0.45 });
    deco.push({ type: 'sticker', shape: 'heart', x: cx + cw * 0.40, y: cy + ch * 0.40, rot: 0.15, size: 18, color: style.accent, start: start + 0.5 });
    if (style.id === 'tilt' || style.id === 'still' || style.id === 'cadenze' || style.id === 'sonnet') {
      deco.push({ type: 'dash', x: 0, y: 0, x1: W * 0.06, y1: H * 0.72, x2: W * 0.94, y2: H * 0.72, color: style.accent, start: start + 0.5 });
      deco.push({ type: 'scissors', x: W * 0.91, y: H * 0.72, rot: 0.4, color: style.dim, start: start + 0.55 });
      deco.push({ type: 'stamp', x: W * 0.82, y: H * 0.24, rot: -0.2, color: style.accent, start: start + 0.45 });
    }
    deco.push({ type: 'dot', x: cx - cw * 0.46, y: H * 0.78, color: style.ink, start: start + 0.4 });
    deco.push({ type: 'dot', x: cx - cw * 0.40, y: H * 0.78, color: style.dim, start: start + 0.45 });

    return { words: words, cards: cards, ribbon: ribbon, deco: deco, ghost: ghost };
  }

  // ------------------------------------------------------------------ HANABI scrapbook modes

  function badgeUnits(tokens) {
    const out = [];
    for (const t of tokens) {
      let cjk = true;
      for (let i = 0; i < t.length; i++) if (!isCjk(t.charAt(i))) cjk = false;
      if (cjk && t.length > 1) {
        for (const ch of t) out.push(ch);
      } else {
        out.push(t);
      }
    }
    return out;
  }

  function buildHanabiComposition(tokens, idx, start, end, wordTimes, base, heroIndex) {
    const mode = idx % 6;
    const words = [];
    const deco = [];
    const cards = [];
    const pal = palette();
    const badgePalette = [style.accent, style.accent2, '#f4c95d', '#c6a8ff', '#ff9e7a', '#f4efe4'];
    const fontKind = mode === 2 ? 'gothic' : mode === 3 ? 'serif' : mode === 5 ? 'cinema' : 'mincho';
    const now = performance.now();

    function addLine(line, firstIndex, cx, cy, size, rot, color, badges) {
      if (!line.length) return;
      let units = badges ? badgeUnits(line) : line;
      let widths = units.map(t => measure(t, size, fontKind));
      let total = widths.reduce((a, b) => a + b, 0) + (badges ? 8 : 18) * Math.max(0, units.length - 1);
      const maxW = W * 0.62;
      if (total > maxW) {
        const k = maxW / total;
        size *= k;
        widths = units.map(t => measure(t, size, fontKind));
        total = widths.reduce((a, b) => a + b, 0) + (badges ? 8 : 18) * Math.max(0, units.length - 1);
      }
      let lx = -total / 2;
      for (let i = 0; i < units.length; i++) {
        const p = rotatePoint(lx + widths[i] / 2, 0, rot);
        const tIndex = Math.min(wordTimes.length - 1, firstIndex + i);
        const unit = units[i];
        words.push({
          text: unit, size: size, x: cx + p.x, y: cy + p.y, rot: rot,
          color: badges ? '#fff8ee' : color, fontKind: fontKind,
          start: wordTimes[tIndex] || start, enterX: 0, enterY: 18,
          hero: false, block: false, hanabi: true,
          thin: mode === 0,
          badge: !!badges,
          badgeColor: badges ? badgePalette[(i + idx) % badgePalette.length] : null
        });
        lx += widths[i] + (badges ? 8 : 18);
      }
    }

    if (mode === 0) {
      // 虚线圆 / 胶片边框里的居中歌词
      const cx = W * 0.5, cy = H * 0.52;
      const cut = Math.max(1, Math.ceil(tokens.length / 2));
      addLine(tokens.slice(0, cut), 0, cx, cy - H * 0.055, base * 0.86, -0.02, '#fff8ee', false);
      addLine(tokens.slice(cut), cut, cx, cy + H * 0.055, base * 0.86, 0.02, '#fff8ee', false);
      deco.push({ type: 'label', x: cx - W * 0.25, y: cy - H * 0.23, rot: -0.12, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
      deco.push({ type: 'sticker', shape: 'flower', x: cx + W * 0.21, y: cy + H * 0.20, rot: .2, size: 18, color: style.accent, start: start + 0.4 });
      deco.push({ type: 'sticker', shape: 'star', x: cx - W * 0.23, y: cy + H * 0.17, rot: -.2, size: 16, color: '#f4c95d', start: start + 0.45 });
      shot = {
        kind: 'line', words: words, cards: null, ribbon: null, deco: deco,
        ghost: { text: tokens[heroIndex], size: base * 1.9, x: cx, y: cy, rot: -0.05, color: style.ink },
        hanabi: { mode: 'circle', cx: cx, cy: cy, rx: W * 0.23, ry: H * 0.25, rot: -.12 },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    } else if (mode === 1) {
      // 奶油圆角卡片 + 每个字一个彩色圆徽章
      const cx = W * 0.5, cy = H * 0.51;
      const cw = W * 0.62, ch = H * 0.34, rot = -0.06 + (rnd(idx, 8) - .5) * .04;
      const units = badgeUnits(tokens);
      const split = Math.max(1, Math.ceil(units.length / 2));
      const first = units.slice(0, split), second = units.slice(split);
      const size = base * 0.60;
      // 这里直接按单位画，避免 addLine 再拆字符
      function addBadgeUnits(units2, firstIndex, y) {
        if (!units2.length) return;
        let widths = units2.map(t => measure(t, size, fontKind) + size * .55);
        let total = widths.reduce((a, b) => a + b, 0) + 8 * Math.max(0, units2.length - 1);
        let maxW = cw * .84;
        let k = total > maxW ? maxW / total : 1;
        const sz = size * k;
        widths = units2.map(t => measure(t, sz, fontKind) + sz * .55);
        total = widths.reduce((a, b) => a + b, 0) + 8 * Math.max(0, units2.length - 1);
        let lx = -total / 2;
        for (let i = 0; i < units2.length; i++) {
          const local = rotatePoint(lx + widths[i] / 2, y, rot);
          words.push({
            text: units2[i], size: sz, x: cx + local.x, y: cy + local.y, rot: rot,
            color: '#fff8ee', fontKind: fontKind,
            start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
            enterX: 0, enterY: 16, hero: false, block: false, hanabi: true, badge: true,
            badgeColor: badgePalette[(i + idx) % badgePalette.length]
          });
          lx += widths[i] + 8;
        }
      }
      addBadgeUnits(first, 0, -ch * .16);
      addBadgeUnits(second, split, ch * .15);
      cards.push({ x: cx, y: cy, w: cw, h: ch, rot: rot, paper: '#f3e5c8', border: style.accent, start: start + 0.03, rounded: true });
      deco.push({ type: 'label', x: cx - cw * .40, y: cy - ch * .44, rot: rot, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.32 });
      deco.push({ type: 'tape', x: cx - cw * .36, y: cy - ch * .52, rot: rot - .3, w: 66, h: 18, color: hexA(style.ink, .24), start: start + 0.28 });
      deco.push({ type: 'sticker', shape: 'flower', x: cx + cw * .46, y: cy + ch * .40, rot: .2, size: 18, color: style.accent, start: start + 0.4 });
      deco.push({ type: 'sticker', shape: 'star', x: cx - cw * .46, y: cy + ch * .38, rot: -.2, size: 14, color: '#f4c95d', start: start + 0.45 });
      shot = {
        kind: 'line', words: words, cards: cards, ribbon: null, deco: deco,
        ghost: { text: tokens[heroIndex], size: base * 1.55, x: cx, y: cy, rot: rot - .06, color: style.ink },
        hanabi: { mode: 'badge', cx: cx, cy: cy, rx: W * .23, ry: H * 0.25, rot: -.18 },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    } else if (mode === 3) {
      // 奶油卡 + 双斜飘带 + 金色放射光
      const cx = W * 0.5, cy = H * 0.52;
      const cw = W * 0.58, ch = H * 0.28, rot = -0.05;
      const cut = Math.max(1, Math.ceil(tokens.length / 2));
      const ink = '#241b14';

      function addRibbonCardLine(line, firstIndex, y, size, highlight) {
        if (!line.length) return;
        let widths = line.map(t => measure(t, size, 'serif'));
        let total = widths.reduce((a, b) => a + b, 0) + 16 * Math.max(0, line.length - 1);
        if (total > cw * .86) {
          size *= cw * .86 / total;
          widths = line.map(t => measure(t, size, 'serif'));
          total = widths.reduce((a, b) => a + b, 0) + 16 * Math.max(0, line.length - 1);
        }
        let lx = -total / 2;
        for (let i = 0; i < line.length; i++) {
          const p = rotatePoint(lx + widths[i] / 2, y, rot);
          words.push({
            text: line[i], size: size, x: cx + p.x, y: cy + p.y, rot: rot,
            color: ink, fontKind: 'serif',
            start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
            enterX: 0, enterY: 18, hero: false, block: false, card: true,
            cardBlock: !!highlight && i === Math.floor(line.length / 2)
          });
          lx += widths[i] + 16;
        }
      }
      addRibbonCardLine(tokens.slice(0, cut), 0, -ch * .16, base * .62, false);
      addRibbonCardLine(tokens.slice(cut), cut, ch * .14, base * .76, true);
      cards.push({ x: cx, y: cy, w: cw, h: ch, rot: rot, paper: '#f5e6c8', border: style.accent, start: start + 0.03, rounded: true });

      let ribbonsText = joinPhrase(tokens.slice(Math.max(0, tokens.length - 3)));
      if (ribbonsText.length > 20) ribbonsText = ribbonsText.slice(0, 20);
      const r2 = { x: W * 0.70, y: H * 0.42, rot: -1.0, length: W * 0.48, thick: H * 0.075, color: style.accent2, start: start + 0.2 };
      const r1 = { x: W * 0.67, y: H * 0.37, rot: -1.0, length: W * 0.48, thick: H * 0.085, color: style.accent, start: start + 0.16 };
      words.push({
        text: ribbonsText, size: base * .38, x: r1.x, y: r1.y, rot: r1.rot,
        color: '#fff8ee', fontKind: 'condensed', start: start + 0.24,
        enterX: 0, enterY: 0, hero: false, block: false, ribbon: true
      });
      deco.push({ type: 'label', x: cx - cw * .38, y: cy - ch * .42, rot: rot, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
      deco.push({ type: 'tape', x: cx - cw * .34, y: cy - ch * .5, rot: rot - .3, w: 66, h: 18, color: hexA(style.ink, .24), start: start + 0.26 });
      deco.push({ type: 'sticker', shape: 'flower', x: cx + cw * .47, y: cy + ch * .38, rot: .2, size: 18, color: style.accent, start: start + 0.4 });
      deco.push({ type: 'sticker', shape: 'star', x: cx - cw * .46, y: cy + ch * .4, rot: -.2, size: 14, color: '#f4c95d', start: start + 0.45 });
      shot = {
        kind: 'line', words: words, cards: cards, ribbon: null, ribbons: [r2, r1], deco: deco,
        ghost: { text: tokens[heroIndex], size: base * 1.5, x: cx, y: cy, rot: rot - .06, color: style.ink },
        hanabi: { mode: 'ribbonCard', cx: cx, cy: cy, rot: rot },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    } else if (mode === 4) {
      // 竖向彩色胶囊条：字真正竖着排，不再横向溢出
      const cx = W * 0.5;
      const bands = [
        { x: W * 0.455, y: H * 0.53, w: W * 0.06, h: H * 0.54, rot: -0.03, color: style.accent2, start: start + 0.04 },
        { x: W * 0.5, y: H * 0.5, w: W * 0.105, h: H * 0.62, rot: 0.015, color: style.accent, start: start + 0.08 },
        { x: W * 0.545, y: H * 0.54, w: W * 0.052, h: H * 0.50, rot: 0.04, color: '#f4c95d', start: start + 0.12 }
      ];
      const chars = Array.from(joinPhrase(tokens)).filter(ch => ch && ch.trim().length > 0);
      let size = Math.min(base * 0.72, (H * 0.52) / Math.max(1, chars.length * 1.05), W * 0.095);
      // 让最宽的字也落在中央胶囊里
      for (let guard = 0; guard < 20; guard++) {
        let maxW = 0;
        for (const ch of chars) maxW = Math.max(maxW, measure(ch, size, fontKind));
        if (maxW <= W * 0.085) break;
        size *= 0.92;
      }
      const totalH = chars.length * size * 1.08;
      let y = H * 0.5 - totalH / 2;
      for (let i = 0; i < chars.length; i++) {
        words.push({
          text: chars[i], size: size, x: cx, y: y + size * .54, rot: 0,
          color: '#fff8ee', fontKind: fontKind,
          start: start + (i / Math.max(1, chars.length)) * Math.min(0.8, Math.max(0.3, end - start)),
          enterX: 0, enterY: 26, hero: false, block: false, hanabi: true
        });
        y += size * 1.08;
      }
      deco.push({ type: 'label', x: W * .36, y: H * .15, rot: -0.1, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
      deco.push({ type: 'dash', x: 0, y: 0, x1: W * .18, y1: H * .18, x2: W * .18, y2: H * .82, color: style.accent2, start: start + 0.35 });
      shot = {
        kind: 'line', words: words, cards: null, ribbon: null, deco: deco,
        ghost: { text: tokens[heroIndex], size: base * .9, x: W * .3, y: H * .5, rot: -1.2, color: style.ink },
        hanabi: { mode: 'verticalRibbon', bands: bands, repeat: joinPhrase(tokens.slice(0, 2)), rot: 0 },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    } else if (mode === 5) {
      // 电影卡片：上下胶片齿孔 + 衬线标题
      const cx = W * 0.5, cy = H * 0.52;
      const cw = W * 0.60, ch = H * 0.30, rot = -0.045;
      const cut = Math.max(1, Math.ceil(tokens.length / 2));
      function addCinemaLine(line, firstIndex, y, size) {
        if (!line.length) return;
        let widths = line.map(t => measure(t, size, fontKind));
        let total = widths.reduce((a, b) => a + b, 0) + 16 * Math.max(0, line.length - 1);
        if (total > cw * .82) {
          size *= cw * .82 / total;
          widths = line.map(t => measure(t, size, fontKind));
          total = widths.reduce((a, b) => a + b, 0) + 16 * Math.max(0, line.length - 1);
        }
        let lx = -total / 2;
        for (let i = 0; i < line.length; i++) {
          const p = rotatePoint(lx + widths[i] / 2, y, rot);
          words.push({
            text: line[i], size: size, x: cx + p.x, y: cy + p.y, rot: rot,
            color: '#1b1520', fontKind: fontKind,
            start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
            enterX: 0, enterY: 18, hero: false, block: false, card: true
          });
          lx += widths[i] + 16;
        }
      }
      addCinemaLine(tokens.slice(0, cut), 0, -ch * .12, base * .62);
      addCinemaLine(tokens.slice(cut), cut, ch * .17, base * .76);
      cards.push({ x: cx, y: cy, w: cw, h: ch, rot: rot, paper: '#f4e6c8', border: '#1b1520', start: start + 0.03, rounded: true, cinema: true });
      deco.push({ type: 'label', x: cx - cw * .4, y: cy - ch * .44, rot: rot, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
      deco.push({ type: 'sticker', shape: 'star', x: cx + cw * .46, y: cy - ch * .38, rot: -.2, size: 15, color: '#f4c95d', start: start + 0.4 });
      shot = {
        kind: 'line', words: words, cards: cards, ribbon: null, deco: deco,
        ghost: { text: tokens[heroIndex], size: base * 1.45, x: cx, y: cy, rot: rot - .05, color: style.ink },
        hanabi: { mode: 'cinema', cx: cx, cy: cy, rot: rot },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    } else {
      // 竖排字柱：紧密排在一起，块与块之间只留很小缝
      const allChars = [];
      for (const t of tokens) for (const ch of Array.from(t)) allChars.push(ch);
      const columnCount = Math.min(4, Math.max(2, Math.ceil(allChars.length / 2)));
      const chunks = [];
      let cursor = 0;
      for (let c = 0; c < columnCount; c++) {
        const remainCols = columnCount - c;
        const take = Math.ceil((allChars.length - cursor) / remainCols);
        chunks.push(allChars.slice(cursor, cursor + take));
        cursor += take;
      }
      const maxChars = Math.max(1, Math.max.apply(null, chunks.map(a => a.length)));
      const columnColors = [style.accent2, '#2ec5c9', '#c88a4a', '#f4e6c8'];
      const ghostText = joinPhrase(tokens.slice(0, Math.min(2, tokens.length)));
      const size = Math.min(base * 0.95, (H * 0.68) / Math.max(1, maxChars * 1.03), W * 0.078);
      const gap = size * 1.03;
      const center = W * 0.56;
      const columnX = [];
      for (let c = 0; c < columnCount; c++) columnX.push(center + (c - (columnCount - 1) / 2) * gap * 1.06);

      let charIndex = 0;
      const totalChars = allChars.length;
      for (let c = 0; c < columnCount; c++) {
        const chars = chunks[c];
        if (!chars.length) continue;
        const totalH = chars.length * gap;
        let y = H * 0.5 - totalH / 2;
        const textColor = luminance(columnColors[c]) > .62 ? '#241b14' : '#fff8ee';
        for (let i = 0; i < chars.length; i++) {
          words.push({
            text: chars[i], size: size, x: columnX[c], y: y + size * .52, rot: 0,
            color: textColor, fontKind: fontKind,
            start: start + (charIndex / Math.max(1, totalChars)) * Math.min(.75, Math.max(.3, end - start)),
            enterX: 0, enterY: 18, hero: false, block: false, hanabi: true,
            verticalBlock: true, blockColor: columnColors[c]
          });
          y += gap;
          charIndex++;
        }
      }
      const leftEdge = columnX[0] - gap * 0.7;
      const rightEdge = columnX[columnCount - 1] + gap * 0.7;
      deco.push({ type: 'dash', x: 0, y: 0, x1: W * .17, y1: H * .18, x2: W * .17, y2: H * .84, color: style.accent2, start: start + 0.25 });
      deco.push({ type: 'label', x: W * .11, y: H * .15, rot: -0.1, text: 'No.' + String(idx + 1).padStart(2, '0'), color: style.dim, start: start + 0.3 });
      deco.push({ type: 'sticker', shape: 'flower', x: leftEdge - W * .03, y: H * .25, rot: .2, size: 20, color: style.accent, start: start + 0.35 });
      deco.push({ type: 'bubble', x: rightEdge + W * .035, y: H * .14, rot: .1, text: 'yay!', color: '#f8f1df', start: start + 0.4 });
      deco.push({ type: 'credit', x: center, y: H * .075, rot: 0, text: (state && state.artist) ? state.artist : '', color: style.ink, start: start + 0.3 });
      shot = {
        kind: 'line', words: words, cards: null, ribbon: null, deco: deco,
        ghost: { text: ghostText, size: base * 1.25, x: W * .22, y: H * .5, rot: -1.2, color: style.ink },
        hanabi: { mode: 'vertical', ghostText: ghostText, cx: center, columns: columnCount, columnX: columnX },
        lineIndex: idx, start: start, end: end, seed: idx + 1
      };
    }

    lineIndex = idx;
    lineFlash = 1;
    cameraTarget = {
      x: (rnd(idx, 60) - .5) * .04 * (settings.camera || 1),
      y: (rnd(idx, 61) - .5) * .03 * (settings.camera || 1),
      z: 1.03 + rnd(idx, 62) * .05,
      r: (rnd(idx, 63) - .5) * .01
    };
    if (settings.bloom) {
      spawnBurst(W * (0.2 + rnd(idx, 70) * 0.6), H * (0.15 + rnd(idx, 71) * 0.3), 1.2, true);
      if (idx % 4 === 3) spawnBurst(W * .6, H * .18, 1.6, false);
    }
  }

  function buildLine(lines, idx) {
    const line = lines[idx];
    const next = lines[idx + 1];
    const start = line.time;
    const end = next ? Math.min(next.time, start + 8) : start + 5.5;
    const tokens = splitWords(line.text || '');
    if (!tokens.length) { buildTitleShot(); return; }

    const base = clamp(W / 19, 44, 100) * (style.scale || 1);
    let heroIndex = 0, heroLen = -1;
    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i].length > heroLen) { heroLen = tokens[i].length; heroIndex = i; }
    }
    const heroSize = Math.min(base * (style.layout === 'stack' ? 1.35 : 1.85), H * 0.18);
    const hero = {
      text: tokens[heroIndex], size: heroSize,
      x: W * (style.layout === 'poster' ? 0.36 : 0.5), y: H * (style.layout === 'stack' ? 0.42 : 0.47),
      rot: (rnd(idx, 3) - 0.5) * 0.03, color: style.ink, hero: true, block: false, start: 0, enterX: 0, enterY: 24
    };
    const words = [];
    let totalChars = 0;
    for (const t of tokens) totalChars += Math.max(1, t.length);
    let acc = 0;
    const wordTimes = [];
    // 一行歌词应该在 1 秒左右全部入场；不要按整行时长慢慢挤牙膏，
    // 否则截到中段时会只看到一两个词（Folia 的入场是短 stagger）。
    const entranceSpan = Math.min(0.75, Math.max(0.45, (end - start) * 0.24));
    for (let i = 0; i < tokens.length; i++) {
      const chars = Math.max(1, tokens[i].length);
      wordTimes.push(start + entranceSpan * (acc / totalChars));
      acc += chars;
    }
    const pal = palette();
    const layout = style.layout || 'collage';
    let customDeco = null;
    let customCards = null;

    if (layout === 'hanabi') {
      buildHanabiComposition(tokens, idx, start, end, wordTimes, base, heroIndex);
      return;
    }

    if (layout === 'sonnetModes') {
      // 商籁：三种版式轮换（小纸卡 / 大衬线引文 / 竖排诗），和绘光区分开
      const mode = idx % 3;
      if (mode === 0) {
        // 居中细衬线，不再用纸卡
        const cut = Math.max(1, Math.ceil(tokens.length / 2));
        function addCenterLine(line, firstIndex, y, size) {
          if (!line.length) return;
          let widths = line.map(t => measure(t, size, 'serif'));
          let total = widths.reduce((a, b) => a + b, 0) + 22 * Math.max(0, line.length - 1);
          if (total > W * .68) {
            size *= W * .68 / total;
            widths = line.map(t => measure(t, size, 'serif'));
            total = widths.reduce((a, b) => a + b, 0) + 22 * Math.max(0, line.length - 1);
          }
          let lx = -total / 2;
          for (let i = 0; i < line.length; i++) {
            words.push({
              text: line[i], size: size, x: W * .5 + lx + widths[i] / 2, y: y, rot: 0,
              color: '#1c1c1e', fontKind: 'serif',
              start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
              enterX: 0, enterY: 16, hero: false, block: false
            });
            lx += widths[i] + 22;
          }
        }
        addCenterLine(tokens.slice(0, cut), 0, H * .46, base * .62);
        addCenterLine(tokens.slice(cut), cut, H * .56, base * .74);
        customDeco = [
          { type: 'rule', x: W * .5, y: H * .38, w: W * .52, color: hexA('#1c1c1e', .2), start: start + 0.15 },
          { type: 'rule', x: W * .5, y: H * .64, w: W * .52, color: hexA('#1c1c1e', .14), start: start + 0.2 },
          { type: 'label', x: W * .13, y: H * .22, rot: -0.1, text: 'SONNET / 商籁', color: style.dim, start: start + 0.25 },
          { type: 'corner', x: W * .07, y: H * .12, rot: 0, size: 30, color: hexA('#1c1c1e', .3), start: start + 0.2 },
          { type: 'corner', x: W * .93, y: H * .88, rot: Math.PI, size: 30, color: hexA('#1c1c1e', .3), start: start + 0.24 }
        ];
      } else if (mode === 1) {
        const cut = Math.max(1, Math.ceil(tokens.length / 2));
        function addQuoteLine(line, firstIndex, y, size) {
          if (!line.length) return;
          let widths = line.map(t => measure(t, size, 'serif'));
          let total = widths.reduce((a, b) => a + b, 0) + 22 * Math.max(0, line.length - 1);
          if (total > W * .74) {
            size *= W * .74 / total;
            widths = line.map(t => measure(t, size, 'serif'));
            total = widths.reduce((a, b) => a + b, 0) + 22 * Math.max(0, line.length - 1);
          }
          let lx = -W * .37;
          for (let i = 0; i < line.length; i++) {
            words.push({
              text: line[i], size: size, x: W * .5 + lx + widths[i] / 2, y: y, rot: 0,
              color: '#1c1c1e', fontKind: 'serif',
              start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
              enterX: 0, enterY: 14, hero: false, block: false
            });
            lx += widths[i] + 22;
          }
        }
        addQuoteLine(tokens.slice(0, cut), 0, H * .45, base * .9);
        addQuoteLine(tokens.slice(cut), cut, H * .58, base * .62);
        customDeco = [
          { type: 'rule', x: W * .5, y: H * .30, w: W * .74, color: hexA('#1c1c1e', .22), start: start + 0.15 },
          { type: 'rule', x: W * .5, y: H * .66, w: W * .74, color: hexA('#1c1c1e', .16), start: start + 0.2 },
          { type: 'label', x: W * .13, y: H * .22, rot: -0.1, text: 'SONNET / 商籁', color: style.dim, start: start + 0.25 }
        ];
      } else {
        const chars = Array.from(joinPhrase(tokens));
        const cut = Math.ceil(chars.length / 2);
        const cols = [chars.slice(0, cut), chars.slice(cut)];
        const maxChars = Math.max(1, cols[0].length);
        const size = Math.min(base * 0.62, H * .52 / (maxChars * 1.15), W * .075);
        const gap = size * 1.12;
        let charIndex = 0;
        for (let c = 0; c < cols.length; c++) {
          const col = cols[c];
          const x = W * .5 + (c - .5) * size * 1.6;
          let y = H * .5 - col.length * gap / 2;
          for (let i = 0; i < col.length; i++) {
            words.push({
              text: col[i], size: size, x: x, y: y + size * .52, rot: 0,
              color: '#1c1c1e', fontKind: 'mincho', thin: true,
              start: start + (charIndex / Math.max(1, chars.length)) * Math.min(.8, Math.max(.3, end - start)),
              enterX: 0, enterY: 14, hero: false, block: false
            });
            y += gap; charIndex++;
          }
        }
        customDeco = [
          { type: 'corner', x: W * .07, y: H * .12, rot: 0, size: 34, color: hexA('#1c1c1e', .4), start: start + 0.2 },
          { type: 'corner', x: W * .93, y: H * .88, rot: Math.PI, size: 34, color: hexA('#1c1c1e', .4), start: start + 0.24 },
          { type: 'ring', x: W * .5, y: H * .5, rx: W * .115, ry: H * .23, rot: 0, color: hexA('#1c1c1e', .24), dash: true, start: start + 0.15 },
          { type: 'box', x: W * .5, y: H * .5, w: W * .26, h: H * .58, rot: 0, color: hexA('#1c1c1e', .38), start: start + 0.15 },
          { type: 'label', x: W * .12, y: H * .14, rot: -0.1, text: 'SONNET / 商籁', color: style.dim, start: start + 0.3 }
        ];
      }
      hero.size = base * .7;
    } else if (layout === 'quietVertical') {
      // 商籁 / 绘光：极简竖排，细衬线，长句自动分成两列
      const chars = Array.from(joinPhrase(tokens));
      const colCount = chars.length > 10 ? 2 : 1;
      const chunks = [];
      const cut = Math.ceil(chars.length / colCount);
      for (let c = 0; c < colCount; c++) chunks.push(chars.slice(c * cut, (c + 1) * cut));
      const maxChars = Math.max(1, chunks[0].length);
      const size = Math.min(base * 0.62, (H * 0.55) / Math.max(1, maxChars * 1.15), W * 0.075);
      const gap = size * 1.12;
      let charIndex = 0;
      for (let c = 0; c < colCount; c++) {
        const cc = chunks[c];
        if (!cc.length) continue;
        const x = W * 0.5 + (c - (colCount - 1) / 2) * size * 1.5;
        let y = H * 0.5 - cc.length * gap / 2;
        for (let i = 0; i < cc.length; i++) {
          words.push({
            text: cc[i], size: size, x: x, y: y + size * .52, rot: 0,
            color: style.ink, fontKind: style.font, thin: true,
            start: start + (charIndex / Math.max(1, chars.length)) * Math.min(0.8, Math.max(0.3, end - start)),
            enterX: 0, enterY: 16, hero: false, block: false
          });
          y += gap;
          charIndex++;
        }
      }
      customDeco = [
        { type: 'corner', x: W * .07, y: H * .12, rot: 0, size: 34, color: hexA(style.ink, .45), start: start + 0.2 },
        { type: 'corner', x: W * .93, y: H * .12, rot: Math.PI / 2, size: 34, color: hexA(style.ink, .45), start: start + 0.22 },
        { type: 'corner', x: W * .07, y: H * .88, rot: -Math.PI / 2, size: 34, color: hexA(style.ink, .45), start: start + 0.24 },
        { type: 'corner', x: W * .93, y: H * .88, rot: Math.PI, size: 34, color: hexA(style.ink, .45), start: start + 0.26 },
        { type: 'label', x: W * .12, y: H * .14, rot: -0.1, text: style.id === 'sonnet' ? 'SONNET / 商籁' : 'LUMIÈRE / 绘光', color: style.dim, start: start + 0.3 },
        { type: 'sticker', shape: 'star', x: W * .82, y: H * .2, rot: -.2, size: 12, color: style.accent2, start: start + 0.4 }
      ];
      if (style.id === 'sonnet') {
        customDeco.push({ type: 'ring', x: W * .5, y: H * .5, rx: W * .115, ry: H * .24, rot: 0, color: hexA(style.ink, .28), dash: true, start: start + 0.15 });
      }
    } else if (layout === 'paperCircle') {
      // 凝彩：浅灰纸、黑色圆、黑色衬线字
      const cut = Math.max(1, Math.ceil(tokens.length / 2));
      function addPaperLine(line, firstIndex, y, size) {
        if (!line.length) return;
        let widths = line.map(t => measure(t, size, 'serif'));
        let total = widths.reduce((a, b) => a + b, 0) + 18 * Math.max(0, line.length - 1);
        if (total > W * .62) {
          size *= W * .62 / total;
          widths = line.map(t => measure(t, size, 'serif'));
          total = widths.reduce((a, b) => a + b, 0) + 18 * Math.max(0, line.length - 1);
        }
        let lx = -total / 2;
        for (let i = 0; i < line.length; i++) {
          words.push({
            text: line[i], size: size, x: W * .5 + lx + widths[i] / 2, y: y, rot: 0,
            color: '#171717', fontKind: 'serif',
            start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
            enterX: 0, enterY: 14, hero: false, block: false
          });
          lx += widths[i] + 18;
        }
      }
      addPaperLine(tokens.slice(0, cut), 0, H * .58, base * .72);
      addPaperLine(tokens.slice(cut), cut, H * .68, base * .88);
      customDeco = [
        { type: 'label', x: W * .12, y: H * .14, rot: -0.1, text: 'TEMPERA / 凝彩', color: '#4b4640', start: start + 0.3 },
        { type: 'corner', x: W * .07, y: H * .12, rot: 0, size: 30, color: hexA('#4b4640', .45), start: start + 0.2 },
        { type: 'corner', x: W * .93, y: H * .88, rot: Math.PI, size: 30, color: hexA('#4b4640', .45), start: start + 0.24 }
      ];
    } else if (layout === 'card') {
      const comp = buildCardComposition(tokens, idx, start, end, wordTimes, base, heroIndex, next);
      shot = {
        kind: 'line', words: comp.words, cards: comp.cards, ribbon: comp.ribbon, deco: comp.deco,
        ghost: comp.ghost, lineIndex: idx, start: start, end: end, seed: idx + 1
      };
      lineIndex = idx;
      lineFlash = 1;
      cameraTarget = {
        x: (rnd(idx, 60) - 0.5) * 0.032 * (settings.camera || 1),
        y: (rnd(idx, 61) - 0.5) * 0.026 * (settings.camera || 1),
        z: 1.025 + rnd(idx, 62) * 0.04,
        r: (rnd(idx, 63) - 0.5) * 0.008
      };
      if (settings.bloom && style.scene === 'hanabi') {
        spawnBurst(W * (0.2 + rnd(idx, 70) * 0.6), H * (0.16 + rnd(idx, 71) * 0.28), 1.2, true);
        if (idx % 4 === 3) spawnBurst(W * (0.3 + rnd(idx, 72) * 0.4), H * 0.16, 1.6, false);
      }
      return;
    }

    if (layout === 'quietVertical' || layout === 'paperCircle' || layout === 'sonnetModes') {
      // 词已在专用排版里生成，跳过通用 rows/stack/poster/collage
    } else if (layout === 'rows') {
      const rowCount = tokens.length > 5 ? 2 : 1;
      const rowHeight = H * 0.22;
      let cursor = 0;
      for (let r = 0; r < rowCount; r++) {
        const count = Math.ceil((tokens.length - cursor) / (rowCount - r));
        const row = tokens.slice(cursor, cursor + count);
        cursor += count;
        let widths = row.map((t, i) => measure(t, base * (0.68 + rnd(idx + r, i) * 0.2)));
        let total = widths.reduce((a, b) => a + b, 0) + 28 * Math.max(0, row.length - 1);
        let x = W * 0.5 - total / 2;
        for (let i = 0; i < row.length; i++) {
          const size = base * (0.68 + rnd(idx + r, i) * 0.2);
          words.push({
            text: row[i], size: size, x: x + widths[i] / 2,
            y: H * 0.48 + (r - (rowCount - 1) / 2) * rowHeight,
            rot: (rnd(idx + r, i + 10) - 0.5) * 0.015,
            color: row[i] === tokens[heroIndex] ? style.ink : pal[(i + r + idx) % pal.length],
            start: wordTimes[Math.min(tokens.length - 1, tokens.indexOf(row[i]))],
            enterX: 0, enterY: 26, hero: false, block: false
          });
          x += widths[i] + 28;
        }
      }
    } else if (layout === 'stack') {
      let gap = base * 0.16;
      let total = 0;
      const sizes = [];
      for (let i = 0; i < tokens.length; i++) {
        const size = base * (i === heroIndex ? 1.35 : 0.78);
        sizes.push(size);
        total += size * 1.05 + gap;
      }
      const maxH = H * 0.7;
      let shrink = total > maxH ? maxH / total : 1;
      let y = H * 0.5 - (total * shrink) / 2;
      for (let i = 0; i < tokens.length; i++) {
        const size = sizes[i] * shrink;
        words.push({
          text: tokens[i], size: size,
          x: W * 0.5 + (i % 2 ? 1 : -1) * Math.min(20, W * 0.012),
          y: y + (size * 1.05) / 2,
          rot: 0, color: i === heroIndex ? style.ink : pal[(i + idx) % pal.length],
          start: wordTimes[i], enterX: (i % 2 ? 1 : -1) * 22, enterY: 0, hero: i === heroIndex, block: false
        });
        y += size * 1.05 + gap * shrink;
      }
    } else if (layout === 'editorial') {
      // 商籁：衬线编辑页，左对齐，细规则线
      const cut = Math.max(1, Math.ceil(tokens.length * 0.55));
      function addEditorial(line, firstIndex, y, size) {
        if (!line.length) return;
        let widths = line.map(t => measure(t, size, 'serif'));
        let total = widths.reduce((a, b) => a + b, 0) + 20 * Math.max(0, line.length - 1);
        if (total > W * .72) {
          size *= W * .72 / total;
          widths = line.map(t => measure(t, size, 'serif'));
          total = widths.reduce((a, b) => a + b, 0) + 20 * Math.max(0, line.length - 1);
        }
        let lx = -W * .36;
        for (let i = 0; i < line.length; i++) {
          words.push({
            text: line[i], size: size, x: W * .5 + lx + widths[i] / 2, y: y,
            rot: 0, color: i % 3 === 2 ? style.dim : style.ink, fontKind: 'serif',
            start: wordTimes[Math.min(wordTimes.length - 1, firstIndex + i)] || start,
            enterX: 0, enterY: 14, hero: false, block: false
          });
          lx += widths[i] + 20;
        }
      }
      addEditorial(tokens.slice(0, cut), 0, H * .38, base * 0.82);
      addEditorial(tokens.slice(cut), cut, H * .53, base * 1.05);
      hero.size = base * 1.15;
      hero.x = W * .5; hero.y = H * .48;
      customDeco = basicDecoFor(idx, start);
      customDeco.push({ type: 'rule', x: W * .5, y: H * .28, w: W * .78, color: hexA(style.ink, .22), start: start + 0.2 });
      customDeco.push({ type: 'rule', x: W * .5, y: H * .64, w: W * .78, color: hexA(style.ink, .16), start: start + 0.25 });
      customDeco.push({ type: 'label', x: W * .12, y: H * .22, rot: -0.1, text: 'SONNET / 商籁', color: style.dim, start: start + 0.28 });
    } else if (layout === 'poster') {
      // 大字在左，其余词在右侧排成海报栏
      words.push(hero);
      const heroW = measure(hero.text, hero.size);
      const placed = [{ l: hero.x - heroW / 2 - 10, t: hero.y - hero.size * .7, r: hero.x + heroW / 2 + 10, b: hero.y + hero.size * .7 }];
      const support = tokens.filter((t, i) => i !== heroIndex).slice(0, 6);
      let y = H * 0.2;
      for (let i = 0; i < support.length; i++) {
        const size = base * (0.58 + rnd(idx, i + 8) * 0.18);
        const x = W * (0.54 + (i % 2) * 0.18);
        y = H * (0.18 + i * 0.105);
        words.push({
          text: support[i], size: size, x: x, y: y,
          rot: (rnd(idx, i + 20) - 0.5) * 0.04,
          color: pal[(i + idx) % pal.length],
          start: wordTimes[Math.min(tokens.length - 1, tokens.indexOf(support[i]))],
          enterX: 30, enterY: 0, hero: false, block: i % 2 === 0
        });
      }
    } else {
      // 拼贴：hero 中间，其余词围绕，做碰撞检查
      const heroW = measure(hero.text, hero.size);
      const placed = [{ l: hero.x - heroW / 2 - 10, t: hero.y - hero.size * .7, r: hero.x + heroW / 2 + 10, b: hero.y + hero.size * .7 }];
      for (let i = 0; i < tokens.length; i++) {
        if (i === heroIndex) continue;
        const size = base * (0.62 + rnd(idx, 10 + i) * 0.18);
        const w = measure(tokens[i], size);
        const h = size * 1.1;
        let x = W * 0.5, y = H * 0.48, ok = false;
        let angle = rnd(idx, 20 + i) * Math.PI * 2;
        let radius = heroSize * 0.65 + 70;
        for (let attempt = 0; attempt < 160; attempt++) {
          x = hero.x + Math.cos(angle) * radius;
          y = hero.y + Math.sin(angle) * radius * 0.62;
          const rect = { l: x - w / 2 - 22, t: y - h / 2 - 18, r: x + w / 2 + 22, b: y + h / 2 + 18 };
          if (rect.l > W * 0.05 && rect.r < W * 0.95 && rect.t > H * 0.10 && rect.b < H * 0.88 &&
              !placed.some(p => !(rect.r < p.l || rect.l > p.r || rect.b < p.t || rect.t > p.b))) {
            placed.push(rect); ok = true; break;
          }
          angle += 0.15;
          if (attempt % 12 === 11) radius += 34;
        }
        if (!ok) { x = (i % 2 ? 0.17 : 0.83) * W; y = (0.18 + (i * 0.13) % 0.62) * H; }
        words.push({
          text: tokens[i], size: size, x: x, y: y,
          rot: (rnd(idx, 30 + i) - 0.5) * 0.14,
          color: pal[(i + idx) % pal.length],
          start: wordTimes[i],
          enterX: (x < W * 0.5 ? -1 : 1) * (26 + rnd(idx, 40 + i) * 34),
          enterY: (rnd(idx, 50 + i) - 0.5) * 46,
          hero: false, block: i % 3 === 0
        });
      }
      words.push(hero);
    }

    if (layout === 'rows') {
      for (const w of words) if (w.text === tokens[heroIndex]) w.hero = true;
    }

    shot = {
      kind: 'line', words: words, cards: customCards,
      ghost: { text: hero.text, size: hero.size * 1.35, x: hero.x + W * 0.01, y: hero.y - H * 0.02, rot: -0.06 },
      deco: customDeco,
      lineIndex: idx, start: start, end: end, seed: idx + 1
    };
    lineIndex = idx;
    lineFlash = 1;
    cameraTarget = {
      x: (rnd(idx, 60) - 0.5) * 0.045 * (settings.camera || 1),
      y: (rnd(idx, 61) - 0.5) * 0.035 * (settings.camera || 1),
      z: 1.035 + rnd(idx, 62) * 0.055,
      r: (rnd(idx, 63) - 0.5) * 0.012
    };
    if (settings.bloom && style.scene === 'hanabi') {
      spawnBurst(W * (0.22 + rnd(idx, 70) * 0.56), H * (0.18 + rnd(idx, 71) * 0.30), 1.2, true);
      if (idx % 4 === 3) spawnBurst(W * (0.28 + rnd(idx, 72) * 0.44), H * (0.16 + rnd(idx, 73) * 0.22), 1.7, false);
    }
  }

  // ------------------------------------------------------------------ scenery

  function resize() {
    W = innerWidth; H = innerHeight;
    DPR = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(W * DPR);
    canvas.height = Math.round(H * DPR);
    canvas.style.width = W + 'px';
    canvas.style.height = H + 'px';
    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    makeBits();
    if (shot) {
      if (shot.kind === 'title') buildTitleShot();
      else {
        const lines = lyricLines();
        if (lines.length && lineIndex >= 0) buildLine(lines, lineIndex);
      }
    }
  }

  function makeBits() {
    bits = { stars: [], confetti: [], stickers: [], dust: [], drops: [], bokeh: [], petals: [], letters: [], blocks: [], gears: [], clouds: [], rain: [] };
    for (let i = 0; i < 100; i++) bits.stars.push({ x: rnd(i, 1) * W, y: rnd(i, 2) * H * .8, s: .6 + rnd(i, 3) * 1.7, a: .16 + rnd(i, 4) * .55, p: rnd(i, 5) * 6.28 });
    for (let i = 0; i < 28; i++) bits.confetti.push({ x: rnd(i, 11) * W, y: rnd(i, 12) * H, vx: (rnd(i, 13) - .5) * 14, vy: 10 + rnd(i, 14) * 24, s: 3 + rnd(i, 15) * 7, r: rnd(i, 16) * 6.28, vr: (rnd(i, 17) - .5) * 2.6, c: [style.accent, style.accent2, style.ink][i % 3], a: .25 + rnd(i, 18) * .5 });
    for (let i = 0; i < 6; i++) bits.stickers.push({ x: W * (.06 + rnd(i, 21) * .88), y: H * (.06 + rnd(i, 22) * .84), w: 48 + rnd(i, 23) * 110, h: 10 + rnd(i, 24) * 18, r: (rnd(i, 25) - .5) * .8, c: [style.accent, style.accent2, style.ink][i % 3], a: .12 + rnd(i, 26) * .18 });
    for (let i = 0; i < 90; i++) bits.dust.push({ x: rnd(i, 31) * W, y: rnd(i, 32) * H, s: .5 + rnd(i, 33) * 2, vx: (rnd(i, 34) - .5) * 6, vy: -2 - rnd(i, 35) * 10, a: .08 + rnd(i, 36) * .35 });
    for (let i = 0; i < 40; i++) bits.drops.push({ x: rnd(i, 41) * W, y: rnd(i, 42) * H, r: 1 + rnd(i, 43) * 4, len: 6 + rnd(i, 44) * 28, v: 30 + rnd(i, 45) * 50 });
    for (let i = 0; i < 24; i++) bits.bokeh.push({ x: rnd(i, 51) * W, y: rnd(i, 52) * H, r: 12 + rnd(i, 53) * 60, a: .02 + rnd(i, 54) * .08, c: [style.accent, style.accent2][i % 2] });
    for (let i = 0; i < 18; i++) bits.petals.push({ x: rnd(i, 61) * W, y: rnd(i, 62) * H, r: 4 + rnd(i, 63) * 9, v: 8 + rnd(i, 64) * 18, a: .08 + rnd(i, 65) * .2, c: [style.accent, style.accent2, style.ink][i % 3] });
    for (let i = 0; i < 18; i++) bits.letters.push({ x: rnd(i, 71) * W, y: rnd(i, 72) * H, ch: 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'[Math.floor(rnd(i, 73) * 26)], s: 14 + rnd(i, 74) * 38, a: .03 + rnd(i, 75) * .08, v: 2 + rnd(i, 76) * 6 });
    for (let i = 0; i < 10; i++) bits.blocks.push({ x: rnd(i, 81) * W, y: rnd(i, 82) * H, w: 50 + rnd(i, 83) * 160, h: 18 + rnd(i, 84) * 90, r: (rnd(i, 85) - .5) * .5, c: [style.accent, style.accent2, style.ink][i % 3], a: .06 + rnd(i, 86) * .12, vr: (rnd(i, 87) - .5) * .1 });
    for (let i = 0; i < 6; i++) bits.gears.push({ x: W * (.15 + rnd(i, 91) * .7), y: H * (.15 + rnd(i, 92) * .65), r: 50 + rnd(i, 93) * 130, teeth: 10 + Math.floor(rnd(i, 94) * 10), a: .03 + rnd(i, 95) * .06, v: .05 + rnd(i, 96) * .12 });
    for (let i = 0; i < 8; i++) bits.clouds.push({ x: rnd(i, 101) * W, y: H * (.08 + rnd(i, 102) * .45), w: 180 + rnd(i, 103) * 380, h: 40 + rnd(i, 104) * 100, a: .04 + rnd(i, 105) * .1, v: 2 + rnd(i, 106) * 6 });
    for (let i = 0; i < 120; i++) bits.rain.push({ x: rnd(i, 111) * W, y: rnd(i, 112) * H, len: 10 + rnd(i, 113) * 30, v: 220 + rnd(i, 114) * 260, a: .04 + rnd(i, 115) * .16 });
  }

  function bg(top, bottom) {
    const g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, top); g.addColorStop(1, bottom);
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
  }
  function glowAt(x, y, r, color, alpha) {
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0, hexA(color, alpha)); g.addColorStop(1, hexA(color, 0));
    ctx.fillStyle = g; ctx.fillRect(x - r, y - r, r * 2, r * 2);
  }
  function hexA(hex, a) {
    if (!hex || hex[0] !== '#') return `rgba(255,255,255,${a})`;
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }

  function luminance(hex) {
    if (!hex || hex[0] !== '#') return 0.5;
    const n = parseInt(hex.slice(1), 16);
    const r = ((n >> 16) & 255) / 255, g = ((n >> 8) & 255) / 255, b = (n & 255) / 255;
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }
  function drawDust(now, color, speed) {
    ctx.save();
    for (const p of bits.dust) {
      const x = (p.x + p.vx * now * .001 * speed + W) % W;
      const y = (p.y + p.vy * now * .001 * speed + H) % H;
      ctx.globalAlpha = p.a * (settings.decor || 1);
      ctx.fillStyle = color;
      ctx.beginPath(); ctx.arc(x, y, p.s, 0, 6.28); ctx.fill();
    }
    ctx.restore();
  }
  function drawBokeh(now) {
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (const b of bits.bokeh) {
      const x = b.x + Math.sin(now * .00012 + b.y) * 18;
      glowAt(x, b.y, b.r, b.c, b.a);
    }
    ctx.restore();
  }
  function drawRain(now, alpha, lines) {
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, alpha);
    ctx.lineWidth = 1;
    for (const r of (lines ? bits.rain : bits.drops)) {
      const y = (r.y + now * .001 * (r.v || 60)) % (H + 40);
      ctx.globalAlpha = (r.a || alpha) * (settings.decor || 1);
      ctx.beginPath();
      ctx.moveTo(r.x, y);
      ctx.lineTo(r.x - 2, y + (r.len || 10));
      ctx.stroke();
      if (!lines) { ctx.beginPath(); ctx.arc(r.x, y, r.r || 1.5, 0, 6.28); ctx.fillStyle = hexA(style.ink, alpha); ctx.fill(); }
    }
    ctx.restore();
  }
  function drawClouds(now) {
    ctx.save();
    for (const c of bits.clouds) {
      const x = (c.x + now * .00001 * c.v * 100) % (W + c.w) - c.w;
      const g = ctx.createRadialGradient(x, c.y, 0, x, c.y, c.w * .5);
      g.addColorStop(0, hexA('#ffffff', c.a * 1.4));
      g.addColorStop(1, hexA('#ffffff', 0));
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.ellipse(x, c.y, c.w * .5, c.h * .5, 0, 0, 6.28); ctx.fill();
    }
    ctx.restore();
  }

  // ------------------------------------------------------------------ 18 scenes

  function sceneSonnet(now) {
    // 商籁：高明度灰纸 + 墨烟 + 细颗粒（Folia 商籁预览是浅灰，不是暗场）
    const g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, '#dedcd6');
    g.addColorStop(.55, '#c9c8c3');
    g.addColorStop(1, '#b1b0ac');
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
    ctx.save();
    for (let i = 0; i < 10; i++) {
      const x = W * (.08 + rnd(i, 1501) * .84) + Math.sin(now * .00018 + i) * 18;
      const y = H * (.1 + rnd(i, 1502) * .8) + Math.cos(now * .00015 + i) * 12;
      const r = 90 + rnd(i, 1503) * 280;
      const gg = ctx.createRadialGradient(x, y, 0, x, y, r);
      gg.addColorStop(0, i % 3 === 0 ? 'rgba(30,28,26,.13)' : 'rgba(255,255,255,.13)');
      gg.addColorStop(1, 'rgba(255,255,255,0)');
      ctx.fillStyle = gg;
      ctx.beginPath(); ctx.ellipse(x, y, r, r * .55, rnd(i, 1504) * 3, 0, 6.28); ctx.fill();
    }
    ctx.globalAlpha = .16;
    ctx.strokeStyle = '#7d7a74';
    for (let i = 0; i < 30; i++) {
      const y = H * (.06 + i * .028);
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
    }
    ctx.restore();
    drawDust(now, '#4c4a47', .16);
    return;
  }

  function sceneTempera(now) {
    bg(style.bg[0], style.bg[1]);
    // 放射状纸片光芒，参考 Folia 凝彩的拼贴底
    ctx.save();
    ctx.translate(W * .52, H * .47);
    ctx.rotate(now * .000012);
    const rayColors = [style.accent, style.accent2, '#f4c95d', style.ink];
    for (let i = 0; i < 28; i++) {
      const a = i / 28 * Math.PI * 2 + Math.sin(now * .0001 + i) * .01;
      const len = Math.max(W, H) * .85;
      ctx.rotate(Math.PI * 2 / 28);
      ctx.globalAlpha = .055 + .025 * (i % 3);
      ctx.fillStyle = rayColors[i % rayColors.length];
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.lineTo(Math.cos(a) * len, Math.sin(a) * len);
      ctx.lineTo(Math.cos(a + .06) * len, Math.sin(a + .06) * len);
      ctx.closePath();
      ctx.fill();
    }
    ctx.restore();
    glowAt(W * .2, H * .2, W * .5, style.accent, .12);
    glowAt(W * .8, H * .8, W * .4, style.accent2, .10);
    ctx.save();
    for (const b of bits.blocks) {
      ctx.save();
      ctx.translate(b.x + Math.sin(now * .0002 + b.y) * 10, b.y + Math.cos(now * .00017 + b.x) * 8);
      ctx.rotate(b.r + now * .00002 * b.vr * 100);
      ctx.globalAlpha = b.a * 2.2;
      ctx.fillStyle = b.c;
      ctx.fillRect(-b.w / 2, -b.h / 2, b.w, b.h);
      ctx.restore();
    }
    ctx.globalAlpha = .18;
    for (let i = 0; i < 180; i++) {
      const x = rnd(i, 201) * W, y = rnd(i, 202) * H;
      ctx.fillStyle = hexA(style.ink, .5);
      ctx.beginPath(); ctx.arc(x, y, 1.2, 0, 6.28); ctx.fill();
    }
    ctx.restore();
    drawDust(now, style.ink, .25);
  }

  function sceneLumiere(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 5; i++) {
      const x = W * (.12 + i * .18);
      const sway = Math.sin(now * .00025 + i) * 60;
      const g = ctx.createLinearGradient(x, 0, x + sway, H);
      g.addColorStop(0, hexA(style.accent, .18));
      g.addColorStop(.6, hexA(style.accent2, .04));
      g.addColorStop(1, hexA(style.accent2, 0));
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.moveTo(x - 20, 0); ctx.lineTo(x + 30, 0); ctx.lineTo(x + 150 + sway, H); ctx.lineTo(x - 120 + sway, H); ctx.closePath(); ctx.fill();
    }
    ctx.restore();
    glowAt(W * .5, H * .9, W * .4, style.accent, .12);
    drawDust(now, style.accent, 1.4);
  }

  function sceneHanabi(now) {
    const g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, style.bg[0]); g.addColorStop(.5, '#0a0d1d'); g.addColorStop(1, '#120b20');
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
    glowAt(W * .5, H * .42, W * .5, style.accent, .10);
    glowAt(W * .72, H * .55, W * .35, style.accent2, .045);
    ctx.save();
    for (const s of bits.stars) {
      ctx.globalAlpha = s.a * (.65 + .35 * Math.sin(now * .0008 + s.p));
      ctx.fillStyle = style.ink;
      ctx.beginPath(); ctx.arc(s.x, s.y, s.s, 0, 6.28); ctx.fill();
    }
    ctx.restore();
    ctx.save();
    for (const s of bits.stickers) {
      ctx.save(); ctx.translate(s.x, s.y); ctx.rotate(s.r); ctx.globalAlpha = s.a; ctx.fillStyle = s.c; ctx.fillRect(-s.w / 2, -s.h / 2, s.w, s.h); ctx.restore();
    }
    ctx.restore();
    for (const p of bits.confetti) {
      ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.globalAlpha = p.a; ctx.fillStyle = p.c; ctx.fillRect(-p.s / 2, -p.s / 4, p.s, p.s * .5); ctx.restore();
    }
    // 斜向流星/雨线
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 34; i++) {
      const x = (rnd(i, 1201) * W + now * .012 * (1 + i % 3)) % W;
      const y = (rnd(i, 1202) * H + now * .028 * (1 + i % 2)) % H;
      ctx.globalAlpha = .035 + .06 * (i % 3);
      ctx.strokeStyle = i % 4 === 1 ? style.accent2 : style.ink;
      ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - 30, y + 90); ctx.stroke();
    }
    ctx.restore();
  }

  function sceneMachi(now) {
    bg(style.bg[0], style.bg[1]);
    // 楼体剪影
    ctx.save();
    ctx.fillStyle = 'rgba(2,5,12,.72)';
    for (let i = 0; i < 14; i++) {
      const w = W * (.04 + rnd(i, 301) * .08);
      const h = H * (.25 + rnd(i, 302) * .55);
      const x = W * (i / 14);
      ctx.fillRect(x, H - h, w, h);
      ctx.fillStyle = hexA(style.accent2, .10);
      for (let j = 0; j < 5; j++) if (rnd(i, 310 + j) > .55) ctx.fillRect(x + 8 + rnd(i, 320 + j) * w * .7, H - h + 14 + j * 22, 3, 3);
      ctx.fillStyle = 'rgba(2,5,12,.72)';
    }
    ctx.restore();
    // 霓虹招牌
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    const signs = [
      { x: .13, y: .22, w: .15, h: .018, c: style.accent, v: 1 },
      { x: .73, y: .28, w: .12, h: .014, c: style.accent2, v: 2 },
      { x: .08, y: .62, w: .10, h: .012, c: style.accent, v: 3 },
      { x: .78, y: .70, w: .14, h: .016, c: style.accent2, v: 4 }
    ];
    for (const s of signs) {
      const flick = .55 + .45 * Math.sin(now * .004 * (s.v % 2 ? 1 : -1) + s.v);
      ctx.globalAlpha = flick * .8;
      ctx.fillStyle = s.c;
      ctx.shadowColor = s.c; ctx.shadowBlur = 24;
      ctx.fillRect(W * s.x, H * s.y, W * s.w, H * s.h);
      ctx.shadowBlur = 0;
      ctx.fillStyle = hexA(style.ink, .55);
      for (let k = 0; k < 4; k++) ctx.fillRect(W * s.x + 6 + k * (W * s.w / 4.4), H * s.y + 3, W * s.w / 6, H * s.h - 6);
    }
    ctx.restore();
    drawRain(now, .18, true);
    glowAt(W * .5, H * 1.02, W * .4, style.accent, .12);
  }

  function sceneNebbia(now) {
    bg(style.bg[0], style.bg[1]);
    glowAt(W * .3, H * .25, W * .5, '#ffffff', .06);
    drawClouds(now);
    drawBokeh(now);
    drawRain(now, .16, false);
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, .12); ctx.lineWidth = 2;
    ctx.strokeRect(W * .06, H * .08, W * .88, H * .82);
    ctx.beginPath(); ctx.moveTo(W * .52, H * .08); ctx.lineTo(W * .52, H * .9); ctx.stroke();
    ctx.restore();
    ctx.save();
    ctx.fillStyle = hexA('#ffffff', .04);
    ctx.beginPath(); ctx.ellipse(W * .45, H * .55, W * .32, H * .2, -.2, 0, 6.28); ctx.fill();
    ctx.restore();
  }

  function sceneClassic(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 7; i++) {
      const off = Math.sin(now * .0003 + i) * H * .2;
      const g = ctx.createLinearGradient(0, H * .2 + off, W, H * .8 + off);
      g.addColorStop(0, hexA(style.accent, 0));
      g.addColorStop(.5, hexA(i % 2 ? style.accent : style.accent2, .14));
      g.addColorStop(1, hexA(style.accent2, 0));
      ctx.strokeStyle = g; ctx.lineWidth = 1 + (i % 3);
      ctx.beginPath();
      ctx.moveTo(-100, H * .3 + i * 45 + off);
      ctx.bezierCurveTo(W * .3, H * .1 + i * 30 - off, W * .7, H * .9 - i * 20 + off, W + 100, H * .5 + i * 35);
      ctx.stroke();
    }
    ctx.restore();
    drawDust(now, style.accent, .8);
    for (const p of bits.confetti) {
      ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.globalAlpha = p.a * .6; ctx.fillStyle = p.c; ctx.fillRect(-p.s / 2, -p.s / 4, p.s, p.s * .5); ctx.restore();
    }
  }

  function sceneCadenze(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    for (let i = 0; i < 10; i++) {
      const x = W * (.1 + rnd(i, 401) * .8), y = H * (.1 + rnd(i, 402) * .8);
      const r = 60 + rnd(i, 403) * 220;
      const g = ctx.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, hexA(i % 3 ? '#000000' : style.accent, .12));
      g.addColorStop(1, hexA(style.accent, 0));
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(x, y, r, 0, 6.28); ctx.fill();
    }
    ctx.strokeStyle = hexA(style.ink, .10); ctx.lineWidth = 2;
    for (let i = 0; i < 3; i++) {
      ctx.beginPath();
      const y = H * (.3 + i * .2);
      ctx.moveTo(W * .1, y);
      ctx.bezierCurveTo(W * .35, y - 80, W * .58, y + 70, W * .9, y - 20);
      ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    ctx.fillStyle = hexA(style.accent, .8);
    ctx.fillRect(W * .83, H * .78, 48, 58);
    ctx.strokeStyle = hexA(style.ink, .6); ctx.lineWidth = 2;
    ctx.strokeRect(W * .835, H * .785, 38, 48);
    ctx.restore();
    drawDust(now, style.ink, .25);
  }

  function scenePartita(now) {
    bg(style.bg[0], style.bg[1]);
    glowAt(W * .72, H * .18, W * .4, '#fff4dc', .2);
    drawClouds(now);
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, .10); ctx.lineWidth = 1.5;
    for (let i = 0; i < 7; i++) {
      const y = H * .55 + i * 55;
      ctx.beginPath(); ctx.moveTo(W * .08, y); ctx.lineTo(W * .92, y); ctx.stroke();
    }
    for (let i = 0; i < 5; i++) {
      const x = W * (.14 + i * .18);
      ctx.beginPath(); ctx.moveTo(x, H * .55); ctx.lineTo(x + 40, H * .95); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    ctx.fillStyle = hexA(style.ink, .06);
    ctx.beginPath(); ctx.moveTo(W * .55, H); ctx.lineTo(W * .7, H * .55); ctx.lineTo(W * .95, H * .55); ctx.lineTo(W * .95, H); ctx.fill();
    ctx.restore();
  }

  function sceneFume(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    for (const l of bits.letters) {
      const y = (l.y - now * .001 * l.v + H) % H;
      ctx.globalAlpha = l.a;
      ctx.fillStyle = style.ink;
      ctx.font = `${l.s}px "Times New Roman", serif`;
      ctx.fillText(l.ch, l.x, y);
    }
    ctx.restore();
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 4; i++) {
      const y = H * (.55 + i * .08);
      const g = ctx.createLinearGradient(0, y, W, y + 40);
      g.addColorStop(0, hexA(style.accent, 0));
      g.addColorStop(.5, hexA(style.accent, .10));
      g.addColorStop(1, hexA(style.accent, 0));
      ctx.fillStyle = g; ctx.fillRect(0, y, W, 50);
    }
    ctx.restore();
    drawDust(now, style.accent, .35);
  }

  function sceneTilt(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.translate(W * .5, H * .5);
    ctx.rotate(-.035);
    ctx.fillStyle = 'rgba(244,231,211,.055)';
    ctx.fillRect(-W * .38, -H * .38, W * .76, H * .76);
    ctx.strokeStyle = hexA(style.ink, .16); ctx.lineWidth = 1.5;
    ctx.strokeRect(-W * .38, -H * .38, W * .76, H * .76);
    ctx.strokeStyle = hexA(style.ink, .08);
    for (let i = 0; i < 12; i++) {
      const y = -H * .3 + i * 42;
      ctx.beginPath(); ctx.moveTo(-W * .32, y); ctx.lineTo(W * .32, y); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    ctx.globalAlpha = .25;
    ctx.fillStyle = style.accent;
    ctx.beginPath(); ctx.arc(W * .8, H * .22, 26, 0, 6.28); ctx.fill();
    ctx.restore();
    for (let i = 0; i < 3; i++) {
      ctx.save();
      ctx.translate(W * .1, H * (.25 + i * .22));
      ctx.rotate((rnd(i, 501) - .5) * .3);
      ctx.fillStyle = hexA(style.accent2, .18);
      ctx.fillRect(0, 0, 90 + rnd(i, 502) * 80, 8);
      ctx.restore();
    }
  }

  function scenePonder(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, .07); ctx.lineWidth = 1;
    for (let i = 0; i < 6; i++) {
      const r = W * (.1 + i * .1) + Math.sin(now * .0005 + i) * 10;
      ctx.beginPath(); ctx.arc(W * .5, H * .5, r, 0, 6.28); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    for (let i = 0; i < 20; i++) {
      ctx.globalAlpha = .12 + .2 * Math.abs(Math.sin(now * .0008 + i));
      ctx.fillStyle = style.ink;
      ctx.beginPath(); ctx.arc(W * (.2 + rnd(i, 601) * .6), H * (.2 + rnd(i, 602) * .6), 1.5, 0, 6.28); ctx.fill();
    }
    ctx.restore();
    drawDust(now, style.ink, .2);
  }

  function sceneStill(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.globalAlpha = .14;
    for (let i = 0; i < 80; i++) {
      ctx.fillStyle = i % 2 ? '#ffffff' : '#8a8a82';
      ctx.fillRect(rnd(i, 701) * W, rnd(i, 702) * H, 1.5, 1.5);
    }
    ctx.restore();
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, .18); ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(W * .08, H * .72); ctx.lineTo(W * .92, H * .72); ctx.stroke();
    ctx.restore();
    ctx.save();
    ctx.fillStyle = hexA('#b23a2e', .55);
    ctx.beginPath(); ctx.arc(W * .78, H * .3, 7, 0, 6.28); ctx.fill();
    ctx.restore();
    ctx.save();
    ctx.globalAlpha = .06;
    ctx.fillStyle = '#000';
    ctx.beginPath(); ctx.ellipse(W * .5, H * .86, W * .32, 30, 0, 0, 6.28); ctx.fill();
    ctx.restore();
  }

  function scenePendolo(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.translate(W * .5, H * .48);
    ctx.strokeStyle = hexA(style.accent, .22); ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(0, 0, Math.min(W, H) * .34, 0, 6.28); ctx.stroke();
    for (let i = 0; i < 60; i++) {
      const a = i / 60 * 6.28;
      const r1 = Math.min(W, H) * .30, r2 = r1 - (i % 5 ? 8 : 18);
      ctx.strokeStyle = hexA(style.accent, i % 5 ? .12 : .35);
      ctx.beginPath(); ctx.moveTo(Math.cos(a) * r1, Math.sin(a) * r1); ctx.lineTo(Math.cos(a) * r2, Math.sin(a) * r2); ctx.stroke();
    }
    ctx.rotate(now * .00018);
    ctx.strokeStyle = hexA(style.ink, .55); ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(0, -Math.min(W, H) * .27); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(Math.min(W, H) * .17, Math.min(W, H) * .08); ctx.stroke();
    ctx.restore();
    ctx.save();
    ctx.globalAlpha = .5;
    for (const gr of bits.gears) {
      ctx.save(); ctx.translate(gr.x, gr.y); ctx.rotate(now * .0001 * gr.v * 10);
      ctx.strokeStyle = hexA(style.accent, gr.a * 2); ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(0, 0, gr.r, 0, 6.28); ctx.stroke();
      for (let i = 0; i < gr.teeth; i++) {
        const a = i / gr.teeth * 6.28;
        ctx.beginPath(); ctx.moveTo(Math.cos(a) * gr.r, Math.sin(a) * gr.r); ctx.lineTo(Math.cos(a) * (gr.r + 10), Math.sin(a) * (gr.r + 10)); ctx.stroke();
      }
      ctx.restore();
    }
    ctx.restore();
    drawDust(now, style.accent, .25);
  }

  function sceneCappella(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 5; i++) {
      const x = W * (.2 + i * .15), sway = Math.sin(now * .0002 + i) * 30;
      const g = ctx.createLinearGradient(x, 0, x + sway, H);
      g.addColorStop(0, hexA(i % 2 ? style.accent : style.accent2, .14));
      g.addColorStop(1, hexA(style.accent2, 0));
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.moveTo(x - 30, 0); ctx.lineTo(x + 30, 0); ctx.lineTo(x + 120 + sway, H); ctx.lineTo(x - 100 + sway, H); ctx.fill();
    }
    ctx.restore();
    ctx.save();
    for (let i = 0; i < 3; i++) {
      ctx.strokeStyle = hexA(style.ink, .10); ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(W * .5, H * .55, W * (.2 + i * .07), Math.PI * 1.05, Math.PI * 1.95); ctx.stroke();
    }
    ctx.restore();
    drawDust(now, style.ink, .45);
  }

  function sceneCladdagh(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 4; i++) {
      const x = W * (.35 + i * .1), y = H * (.42 + Math.sin(now * .0003 + i) * .06);
      const r = W * (.12 + i * .025);
      ctx.strokeStyle = hexA(i % 2 ? style.accent : style.accent2, .18);
      ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(x, y, r, 0, 6.28); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    for (let i = 0; i < 70; i++) {
      const x = (rnd(i, 801) * W + now * .02 * (1 + i % 3)) % W;
      const y = H * (.2 + rnd(i, 802) * .6) + Math.sin(now * .0006 + i) * 8;
      ctx.globalAlpha = .2 + .3 * Math.abs(Math.sin(now * .001 + i));
      ctx.fillStyle = i % 2 ? style.accent : style.accent2;
      ctx.beginPath(); ctx.arc(x, y, 1.6, 0, 6.28); ctx.fill();
    }
    ctx.restore();
    drawDust(now, style.ink, .4);
  }

  function sceneMonet(now) {
    bg(style.bg[0], style.bg[1]);
    ctx.save();
    for (let i = 0; i < 9; i++) {
      const x = W * (.12 + rnd(i, 901) * .76) + Math.sin(now * .0001 + i) * 20;
      const y = H * (.12 + rnd(i, 902) * .76) + Math.cos(now * .00012 + i) * 14;
      const r = 80 + rnd(i, 903) * 200;
      const c = [style.accent, style.accent2, '#ffffff', '#7eb09a'][i % 4];
      const g = ctx.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, hexA(c, .16)); g.addColorStop(1, hexA(c, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, 6.28); ctx.fill();
    }
    ctx.restore();
    ctx.save();
    ctx.strokeStyle = hexA(style.ink, .06); ctx.lineWidth = 1;
    for (let i = 0; i < 10; i++) {
      const y = H * (.1 + i * .09);
      ctx.beginPath(); ctx.moveTo(0, y);
      for (let x = 0; x <= W; x += 40) ctx.lineTo(x, y + Math.sin(x * .01 + now * .0005 + i) * 5);
      ctx.stroke();
    }
    ctx.restore();
    for (const p of bits.petals) {
      const y = (p.y + now * .001 * p.v) % H;
      ctx.save(); ctx.translate(p.x + Math.sin(now * .0005 + p.y) * 12, y); ctx.rotate(p.r + now * .0002); ctx.globalAlpha = p.a; ctx.fillStyle = p.c; ctx.beginPath(); ctx.ellipse(0, 0, p.r, p.r * .45, 0, 0, 6.28); ctx.fill(); ctx.restore();
    }
    ctx.save();
    const soft = ctx.createRadialGradient(W * .5, H * .5, 0, W * .5, H * .5, W * .4);
    soft.addColorStop(0, 'rgba(255,255,255,.08)');
    soft.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = soft;
    ctx.fillRect(0, 0, W, H);
    ctx.restore();
  }

  function sceneDiorama(now) {
    bg(style.bg[0], style.bg[1]);
    // 镜面地板
    ctx.save();
    const g = ctx.createLinearGradient(0, H * .55, 0, H);
    g.addColorStop(0, hexA(style.accent, .10)); g.addColorStop(1, hexA(style.ink, 0));
    ctx.fillStyle = g; ctx.fillRect(0, H * .55, W, H * .45);
    ctx.strokeStyle = hexA(style.ink, .08); ctx.lineWidth = 1;
    for (let i = 0; i < 12; i++) {
      const y = H * .56 + i * 24;
      ctx.beginPath(); ctx.moveTo(W * (.04 + i * .01), y); ctx.lineTo(W * .96, y); ctx.stroke();
    }
    ctx.restore();
    // 对称灯柱
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (let i = 0; i < 4; i++) {
      const x = W * (.2 + i * .2);
      const gg = ctx.createLinearGradient(x, 0, x, H * .55);
      gg.addColorStop(0, hexA(style.accent, .16)); gg.addColorStop(1, hexA(style.accent, 0));
      ctx.fillStyle = gg;
      ctx.beginPath(); ctx.moveTo(x - 4, 0); ctx.lineTo(x + 4, 0); ctx.lineTo(x + 60, H * .55); ctx.lineTo(x - 60, H * .55); ctx.fill();
    }
    ctx.restore();
    // 镜面倒影
    ctx.save();
    ctx.globalAlpha = .12;
    ctx.scale(1, -1);
    ctx.translate(0, -H * 1.08);
    ctx.fillStyle = style.ink;
    ctx.font = fontFor(H * .05, true);
    ctx.textAlign = 'center';
    ctx.fillText('DIORAMA', W * .5, H * .52);
    ctx.restore();
    drawDust(now, style.ink, .3);
  }

  const SCENES = {
    sonnet: sceneSonnet, tempera: sceneTempera, lumiere: sceneLumiere, hanabi: sceneHanabi,
    machi: sceneMachi, nebbia: sceneNebbia, classic: sceneClassic, cadenze: sceneCadenze,
    partita: scenePartita, fume: sceneFume, tilt: sceneTilt, ponder: scenePonder,
    still: sceneStill, pendolo: scenePendolo, cappella: sceneCappella, claddagh: sceneCladdagh,
    monet: sceneMonet, diorama: sceneDiorama
  };

  // ------------------------------------------------------------------ fireworks

  function spawnBurst(x, y, scale, initial) {
    if (!settings.bloom && !initial) return;
    const count = Math.round((22 + rnd(Date.now(), x + y) * 22) * Math.max(0.1, settings.spark) * (settings.decor || 1));
    const color = [style.accent, style.accent2, style.ink, '#ffd166'][Math.floor(rnd(Date.now(), y) * 4)];
    const burst = {
      x: x, y: y, born: performance.now(), life: 1.15 + rnd(Date.now(), x) * .4,
      scale: scale || 1, color: color,
      trail: settings.trail ? { fromY: H * .92 } : null, parts: []
    };
    for (let i = 0; i < count; i++) {
      burst.parts.push({ a: (i / count) * 6.28 + rnd(i, x) * .06, speed: (.45 + rnd(i, y) * .75) * burst.scale, size: 1.3 + rnd(i, 3) * 2.4 });
    }
    bursts.push(burst);
    if (bursts.length > 14) bursts.shift();
    lastBurstAt = performance.now();
  }

  function updateBursts(now, dt) {
    if (style.scene === 'hanabi' && settings.bloom && !settings.reduceMotion && now - lastBurstAt > 1650) {
      spawnBurst(W * (.16 + Math.random() * .68), H * (.12 + Math.random() * .34), .9 + Math.random() * .7, false);
    }
    for (let i = bursts.length - 1; i >= 0; i--) {
      const b = bursts[i];
      if (now - b.born > b.life * 1000 + 500) bursts.splice(i, 1);
    }
    if (style.scene === 'hanabi') {
      for (const p of bits.confetti) {
        p.x += p.vx * dt; p.y += p.vy * dt; p.r += p.vr * dt;
        if (p.y > H + 20) { p.y = -20; p.x = Math.random() * W; }
        if (p.x < -20) p.x = W + 20; if (p.x > W + 20) p.x = -20;
      }
    }
  }

  function drawBursts(now) {
    if (style.scene !== 'hanabi' || !bursts.length) return;
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (const b of bursts) {
      const t = clamp((now - b.born) / (b.life * 1000), 0, 1);
      const fade = 1 - t;
      if (fade <= 0) continue;
      if (b.trail) {
        const g = ctx.createLinearGradient(b.x, b.trail.fromY, b.x, b.y);
        g.addColorStop(0, 'rgba(255,255,255,0)'); g.addColorStop(.7, b.color); g.addColorStop(1, 'rgba(255,255,255,0)');
        ctx.strokeStyle = g; ctx.globalAlpha = fade * .45 * settings.sparkGlow; ctx.lineWidth = 1.4;
        ctx.beginPath(); ctx.moveTo(b.x, b.trail.fromY); ctx.lineTo(b.x, b.y); ctx.stroke();
      }
      for (const p of b.parts) {
        const d = p.speed * t * H * .18;
        const x = b.x + Math.cos(p.a) * d;
        const y = b.y + Math.sin(p.a) * d * .72 + t * t * H * .055;
        const a = Math.pow(fade, 1.7) * settings.sparkGlow;
        if (a <= .01) continue;
        ctx.globalAlpha = Math.min(1, a);
        ctx.fillStyle = p.size > 3 ? style.ink : b.color;
        ctx.shadowColor = b.color; ctx.shadowBlur = 14 * settings.sparkGlow;
        ctx.beginPath(); ctx.arc(x, y, p.size * (1 - t * .55), 0, 6.28); ctx.fill();
      }
    }
    ctx.restore();
  }

  // ------------------------------------------------------------------ draw

  function applyCamera(now) {
    const pos = currentPosition(now);
    const p = shot ? clamp((pos - shot.start) / Math.max(1, shot.end - shot.start), 0, 1) : 0;
    let z = cameraTarget.z * (.98 + p * .07);
    let x = cameraTarget.x * W, y = cameraTarget.y * H, r = cameraTarget.r;
    const t = now * .001;
    if (settings.reduceMotion) { z = 1.01; x = 0; y = 0; r = 0; }
    else if (settings.push) {
      const beat = Math.pow(.5 + .5 * Math.sin(t * Math.PI * 2 * 1.55), 5);
      z *= 1 + beat * .012 * (settings.camera || 1);
      x += Math.sin(t * .23) * 8; y += Math.cos(t * .19) * 6;
    } else {
      x += Math.sin(t * .23) * 8; y += Math.cos(t * .19) * 6;
    }
    camera.x += (x - camera.x) * .12;
    camera.y += (y - camera.y) * .12;
    camera.z += (z - camera.z) * .12;
    camera.r += (r - camera.r) * .12;
    ctx.save();
    ctx.translate(W / 2, H / 2);
    ctx.rotate(camera.r);
    ctx.scale(camera.z, camera.z);
    ctx.translate(-W / 2 + camera.x, -H / 2 + camera.y);
  }

  function drawGhost(now) {
    if (!shot || !shot.ghost) return;
    const g = shot.ghost;
    ctx.save();
    ctx.globalAlpha = .14 * settings.glow;
    ctx.strokeStyle = hexA(style.ink, .8);
    ctx.lineWidth = 1.2;
    ctx.font = fontFor(g.size, true);
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.translate(g.x, g.y); ctx.rotate(g.rot);
    ctx.strokeText(g.text, 0, 0);
    ctx.restore();
  }

  function drawWord(w, pos) {
    const p = clamp((pos - w.start) / .55, 0, 1);
    if (p <= 0) return;
    const e = easeOutExpo(p);
    const eBadge = w.badge ? easeOutBack(p) : e;
    const alpha = .12 + .88 * e;
    const scale = w.badge ? (.55 + .45 * eBadge) : (.76 + .24 * e);
    const now = performance.now();
    const breathe = settings.reduceMotion ? 0 : Math.sin(now * .0011 + (w.start || 0) * 3 + (w.x || 0) * .01);
    const x = w.x + w.enterX * (1 - e) + breathe * (w.card ? .5 : 1.2);
    const y = w.y + w.enterY * (1 - e) + breathe * (w.card ? .8 : 1.8);
    const rot = w.rot + (1 - e) * (w.hero ? .04 : .11) + (settings.reduceMotion ? 0 : breathe * .0015);
    const wd = measure(w.text, w.size, w.fontKind);

    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(rot);
    ctx.scale(scale, scale);
    ctx.globalAlpha = alpha * .9;

    if (w.verticalBlock) {
      ctx.save();
      ctx.fillStyle = w.blockColor || style.accent2;
      ctx.fillRect(-w.size * .46, -w.size * .50, w.size * .92, w.size * .96);
      ctx.strokeStyle = 'rgba(255,255,255,.35)';
      ctx.lineWidth = 1;
      ctx.strokeRect(-w.size * .46, -w.size * .50, w.size * .92, w.size * .96);
      ctx.restore();
    }

    if (w.verticalBar) {
      ctx.save();
      ctx.fillStyle = hexA(w.badgeColor || style.accent, .22);
      ctx.fillRect(-wd / 2 - 18, -w.size * .62, wd + 36, w.size * 1.24);
      ctx.fillStyle = hexA(w.badgeColor || style.accent, .55);
      ctx.fillRect(-wd / 2 - 18, -w.size * .62, 4, w.size * 1.24);
      ctx.restore();
    }

    if (w.badge) {
      ctx.save();
      ctx.shadowColor = 'rgba(0,0,0,.35)';
      ctx.shadowBlur = 14;
      ctx.fillStyle = w.badgeColor || style.accent;
      if (w.text.length <= 1 || isCjk(w.text.charAt(0))) {
        ctx.beginPath();
        ctx.arc(0, 0, w.size * .60, 0, 6.28);
        ctx.fill();
      } else {
        const bw = wd + w.size * .60;
        roundRectPath(ctx, -bw / 2, -w.size * .55, bw, w.size * 1.10, w.size * .55);
        ctx.fill();
      }
      ctx.shadowBlur = 0;
      ctx.restore();
    }

    if (w.cardBlock) {
      ctx.save();
      ctx.fillStyle = 'rgba(244,201,93,.58)';
      const padX = w.size * .12, padY = w.size * .08;
      ctx.fillRect(-wd / 2 - padX, -w.size * .5 - padY, wd + padX * 2, w.size + padY * 2);
      ctx.restore();
    }

    if (w.block) {
      ctx.save();
      ctx.fillStyle = hexA(style.accent, .13);
      ctx.strokeStyle = hexA(style.ink, .10);
      ctx.lineWidth = 1;
      const padX = w.size * .16, padY = w.size * .06;
      ctx.fillRect(-wd / 2 - padX, -w.size * .5 - padY, wd + padX * 2, w.size * 1.0 + padY * 2);
      ctx.strokeRect(-wd / 2 - padX, -w.size * .5 - padY, wd + padX * 2, w.size * 1.0 + padY * 2);
      ctx.restore();
    }

    ctx.font = fontFor(w.size, !w.thin, w.fontKind, w.italic);
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.shadowColor = style.light ? 'rgba(0,0,0,.35)' : w.color;
    ctx.shadowBlur = (w.hero ? 26 : 12) * settings.glow;
    if (settings.motion > .1 && !settings.reduceMotion) {
      ctx.globalAlpha = alpha * .22 * settings.motion;
      ctx.fillStyle = style.accent;
      ctx.fillText(w.text, -2.2, 1.2);
      ctx.fillStyle = style.accent2;
      ctx.fillText(w.text, 2.2, -1.2);
    }
    ctx.globalAlpha = alpha;
    ctx.fillStyle = w.color;
    ctx.fillText(w.text, 0, 0);
    ctx.restore();
  }

  function roundRectPath(c, x, y, w, h, r) {
    r = Math.min(r, w / 2, h / 2);
    c.beginPath();
    c.moveTo(x + r, y);
    c.arcTo(x + w, y, x + w, y + h, r);
    c.arcTo(x + w, y + h, x, y + h, r);
    c.arcTo(x, y + h, x, y, r);
    c.arcTo(x, y, x + w, y, r);
    c.closePath();
  }

  function drawCards(now, pos) {
    if (!shot || !shot.cards) return;
    for (const card of shot.cards) {
      const t = clamp((pos - card.start) / 0.5, 0, 1);
      if (t <= 0) continue;
      const e = easeOutExpo(t);
      ctx.save();
      ctx.translate(card.x, card.y + (1 - e) * 18);
      ctx.rotate(card.rot + (1 - e) * 0.025);
      const scale = 0.90 + 0.10 * e;
      ctx.scale(scale, scale);
      ctx.globalAlpha = Math.min(1, e);
      ctx.shadowColor = 'rgba(0,0,0,.48)';
      ctx.shadowBlur = card.back ? 24 : 34;
      ctx.shadowOffsetY = card.back ? 8 : 14;
      ctx.fillStyle = card.paper;
      if (card.rounded) {
        roundRectPath(ctx, -card.w / 2, -card.h / 2, card.w, card.h, 14);
        ctx.fill();
      } else {
        ctx.fillRect(-card.w / 2, -card.h / 2, card.w, card.h);
      }
      ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
      ctx.strokeStyle = card.border;
      ctx.lineWidth = card.back ? 1.2 : 2.2;
      if (card.rounded) {
        roundRectPath(ctx, -card.w / 2, -card.h / 2, card.w, card.h, 14);
        ctx.stroke();
      } else {
        ctx.strokeRect(-card.w / 2, -card.h / 2, card.w, card.h);
      }
      if (!card.back) {
        ctx.save();
        ctx.globalAlpha = 0.10;
        ctx.strokeStyle = '#8b6b45';
        for (let i = 0; i < 12; i++) {
          const y = -card.h / 2 + 14 + i * (card.h / 13);
          ctx.beginPath(); ctx.moveTo(-card.w / 2 + 12, y); ctx.lineTo(card.w / 2 - 12, y); ctx.stroke();
        }
        ctx.restore();
      }
      if (card.cinema) {
        ctx.save();
        ctx.fillStyle = '#17131a';
        ctx.fillRect(-card.w / 2 + 8, -card.h / 2 + 5, card.w - 16, 20);
        ctx.fillRect(-card.w / 2 + 8, card.h / 2 - 25, card.w - 16, 20);
        ctx.fillStyle = '#f4e6c8';
        const holes = Math.floor((card.w - 32) / 28);
        for (let i = 0; i < holes; i++) {
          const x = -card.w / 2 + 20 + i * 28;
          ctx.fillRect(x, -card.h / 2 + 9, 12, 11);
          ctx.fillRect(x, card.h / 2 - 21, 12, 11);
        }
        ctx.restore();
      }
      ctx.restore();
    }
  }

  function drawRibbonBg(now, pos) {
    if (!shot || !shot.ribbon) return;
    const r = shot.ribbon;
    const t = clamp((pos - r.start) / 0.45, 0, 1);
    if (t <= 0) return;
    const e = easeOutExpo(t);
    ctx.save();
    ctx.translate(r.x, r.y);
    ctx.rotate(r.rot);
    ctx.globalAlpha = Math.min(1, e);
    ctx.scale(0.9 + 0.1 * e, 1);
    ctx.shadowColor = 'rgba(0,0,0,.35)';
    ctx.shadowBlur = 22;
    ctx.fillStyle = r.color;
    ctx.fillRect(-r.length / 2, -r.thick / 2, r.length, r.thick);
    ctx.shadowBlur = 0;
    ctx.strokeStyle = 'rgba(255,255,255,.42)';
    ctx.lineWidth = 1.2;
    ctx.strokeRect(-r.length / 2 + 3, -r.thick / 2 + 3, r.length - 6, r.thick - 6);
    ctx.restore();
  }

  function drawDeco(now, pos) {
    if (!shot || !shot.deco) return;
    for (const d of shot.deco) {
      const t = clamp((pos - (d.start || 0)) / 0.4, 0, 1);
      if (t <= 0) continue;
      const e = easeOutExpo(t);
      ctx.save();
      ctx.globalAlpha = e;
      const bob = (settings.reduceMotion || d.type === 'dash') ? 0 : Math.sin(performance.now() * .0012 + (d.start || 0) * 5) * 1.5;
      ctx.translate((d.x || 0) + bob, (d.y || 0) - bob);
      ctx.rotate(d.rot || 0);
      if (d.type === 'corner') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(d.size, 0);
        ctx.lineTo(0, 0);
        ctx.lineTo(0, d.size);
        ctx.stroke();
      } else if (d.type === 'ring') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 1.5;
        if (d.dash) ctx.setLineDash([5, 8]);
        ctx.beginPath(); ctx.ellipse(0, 0, d.rx, d.ry, d.rot || 0, 0, 6.28); ctx.stroke();
        ctx.setLineDash([]);
      } else if (d.type === 'box') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 1.4;
        ctx.strokeRect(-d.w / 2, -d.h / 2, d.w, d.h);
      } else if (d.type === 'rule') {
        const p = clamp((pos - (d.start || 0)) / 0.55, 0, 1);
        const w = d.w * easeOutExpo(p);
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(-w / 2, 0);
        ctx.lineTo(w / 2, 0);
        ctx.stroke();
      } else if (d.type === 'label') {
        ctx.font = fontFor(13, true, 'mono');
        const tw = ctx.measureText(d.text).width + 18;
        ctx.fillStyle = 'rgba(20,17,14,.78)';
        ctx.fillRect(-tw / 2, -12, tw, 24);
        ctx.fillStyle = '#fff3df';
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText(d.text, 0, 1);
      } else if (d.type === 'credit') {
        if (!d.text) { ctx.restore(); continue; }
        ctx.font = fontFor(12, true, 'mono');
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillStyle = hexA(d.color || '#ffffff', .42);
        ctx.fillText(d.text, 0, 0);
      } else if (d.type === 'bubble') {
        ctx.fillStyle = d.color || '#f8f1df';
        roundRectPath(ctx, -40, -20, 80, 40, 18);
        ctx.fill();
        ctx.beginPath();
        ctx.moveTo(-12, 16); ctx.lineTo(-2, 30); ctx.lineTo(6, 16); ctx.closePath(); ctx.fill();
        ctx.fillStyle = '#2b2030';
        ctx.font = fontFor(18, true, 'hand');
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText(d.text || 'yay!', 0, -1);
      } else if (d.type === 'tape') {
        ctx.fillStyle = d.color;
        ctx.fillRect(-d.w / 2, -d.h / 2, d.w, d.h);
        ctx.globalAlpha = e * .5;
        ctx.fillStyle = 'rgba(255,255,255,.4)';
        for (let i = -2; i <= 2; i++) ctx.fillRect(i * 14 - 4, -d.h / 2, 2, d.h);
      } else if (d.type === 'sticker') {
        const s = d.size || 20;
        ctx.fillStyle = d.color;
        if (d.shape === 'flower') {
          for (let i = 0; i < 5; i++) {
            const a = i / 5 * Math.PI * 2;
            ctx.beginPath(); ctx.ellipse(Math.cos(a) * s * .6, Math.sin(a) * s * .6, s * .42, s * .24, a, 0, 6.28); ctx.fill();
          }
          ctx.fillStyle = '#fff3df';
          ctx.beginPath(); ctx.arc(0, 0, s * .28, 0, 6.28); ctx.fill();
        } else if (d.shape === 'star') {
          ctx.beginPath();
          for (let i = 0; i < 10; i++) {
            const a = -Math.PI / 2 + i * Math.PI / 5;
            const r = i % 2 ? s * .42 : s;
            if (i === 0) ctx.moveTo(Math.cos(a) * r, Math.sin(a) * r);
            else ctx.lineTo(Math.cos(a) * r, Math.sin(a) * r);
          }
          ctx.closePath(); ctx.fill();
        } else {
          ctx.beginPath();
          ctx.moveTo(0, s * .55);
          ctx.bezierCurveTo(-s * 1.2, -s * .35, -s * .5, -s * 1.1, 0, -s * .35);
          ctx.bezierCurveTo(s * .5, -s * 1.1, s * 1.2, -s * .35, 0, s * .55);
          ctx.fill();
        }
      } else if (d.type === 'dash') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 2;
        ctx.setLineDash([9, 8]);
        ctx.beginPath(); ctx.moveTo(d.x1 - d.x, d.y1 - d.y); ctx.lineTo(d.x2 - d.x, d.y2 - d.y); ctx.stroke();
        ctx.setLineDash([]);
      } else if (d.type === 'scissors') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 2.2;
        ctx.beginPath(); ctx.arc(-7, 0, 5, 0, 6.28); ctx.stroke();
        ctx.beginPath(); ctx.arc(7, 0, 5, 0, 6.28); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(-4, 3); ctx.lineTo(12, 18); ctx.moveTo(4, 3); ctx.lineTo(-12, 18); ctx.stroke();
      } else if (d.type === 'stamp') {
        ctx.strokeStyle = d.color;
        ctx.lineWidth = 3;
        ctx.beginPath(); ctx.arc(0, 0, 30, 0, 6.28); ctx.stroke();
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.arc(0, 0, 24, 0, 6.28); ctx.stroke();
        ctx.fillStyle = d.color;
        ctx.font = fontFor(12, true, 'mono');
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText('FOLIA', 0, 0);
      } else if (d.type === 'dot') {
        ctx.fillStyle = d.color;
        ctx.beginPath(); ctx.arc(0, 0, 3, 0, 6.28); ctx.fill();
      }
      ctx.restore();
    }
  }

  function drawRibbons(now, pos) {
    if (!shot || !shot.ribbons) return;
    for (const r of shot.ribbons) {
      const t = clamp((pos - r.start) / 0.45, 0, 1);
      if (t <= 0) continue;
      const e = easeOutExpo(t);
      ctx.save();
      ctx.translate(r.x + (1 - e) * 34, r.y);
      ctx.rotate(r.rot);
      ctx.globalAlpha = Math.min(1, e);
      ctx.scale(0.9 + 0.1 * e, 1);
      ctx.shadowColor = 'rgba(0,0,0,.35)';
      ctx.shadowBlur = 22;
      ctx.fillStyle = r.color;
      ctx.fillRect(-r.length / 2, -r.thick / 2, r.length, r.thick);
      ctx.shadowBlur = 0;
      ctx.strokeStyle = 'rgba(255,255,255,.42)';
      ctx.lineWidth = 1.2;
      ctx.strokeRect(-r.length / 2 + 3, -r.thick / 2 + 3, r.length - 6, r.thick - 6);
      ctx.restore();
    }
  }

  function drawHanabiBack(now, pos) {
    if (!shot || !shot.hanabi) return;
    const h = shot.hanabi;
    if (h.mode !== 'ribbonCard') {
      ctx.save();
      ctx.translate(W * .5, H * .58);
      ctx.rotate(now * .00002);
      ctx.globalCompositeOperation = 'lighter';
      for (let i = 0; i < 18; i++) {
        const a = i / 18 * Math.PI * 2;
        ctx.globalAlpha = .022 + .012 * (i % 3);
        ctx.fillStyle = i % 2 ? '#f4c95d' : style.accent2;
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(Math.cos(a) * W * .55, Math.sin(a) * W * .55);
        ctx.lineTo(Math.cos(a + .07) * W * .55, Math.sin(a + .07) * W * .55);
        ctx.closePath(); ctx.fill();
      }
      ctx.restore();
    }
    if (h.mode === 'verticalRibbon') {
      const t = clamp((pos - shot.start) / .55, 0, 1);
      const e = easeOutExpo(t);
      for (const band of h.bands) {
        ctx.save();
        ctx.translate(band.x, band.y);
        ctx.rotate(band.rot);
        ctx.globalAlpha = e;
        ctx.shadowColor = 'rgba(0,0,0,.35)';
        ctx.shadowBlur = 22;
        ctx.fillStyle = band.color;
        roundRectPath(ctx, -band.w / 2, -band.h / 2, band.w, band.h, band.w / 2);
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.strokeStyle = 'rgba(255,255,255,.42)';
        ctx.lineWidth = 1;
        roundRectPath(ctx, -band.w / 2 + 3, -band.h / 2 + 3, band.w - 6, band.h - 6, band.w / 2);
        ctx.stroke();
        ctx.restore();
      }
      const rep = h.repeat || '';
      if (rep) {
        ctx.save();
        ctx.font = fontFor(H * .12, true, 'display');
        ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
        ctx.fillStyle = hexA(style.ink, .10);
        for (let i = 0; i < 7; i++) ctx.fillText(rep, W * .06 + i * W * .14, H * .86 + Math.sin(now * .001 + i) * 3);
        ctx.restore();
      }
    }
    if (h.mode === 'ribbonCard') {
      ctx.save();
      ctx.translate(h.cx, h.cy + H * .08);
      ctx.rotate(now * .000035);
      ctx.globalCompositeOperation = 'lighter';
      for (let i = 0; i < 24; i++) {
        const a = i / 24 * Math.PI * 2;
        ctx.globalAlpha = .035 + .02 * (i % 3);
        ctx.fillStyle = i % 2 ? '#f4c95d' : style.accent2;
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(Math.cos(a) * W * .55, Math.sin(a) * W * .55);
        ctx.lineTo(Math.cos(a + .06) * W * .55, Math.sin(a + .06) * W * .55);
        ctx.closePath(); ctx.fill();
      }
      ctx.restore();
    }
    if (h.mode === 'circle' || h.mode === 'badge' || h.mode === 'ribbonCard') {
      ctx.save();
      const t = clamp((pos - shot.start) / .6, 0, 1);
      ctx.globalAlpha = easeOutExpo(t) * .8;
      ctx.translate(h.cx, h.cy);
      ctx.rotate(h.rot + (h.mode !== 'ribbonCard' ? now * .00015 : 0));
      ctx.strokeStyle = hexA(style.accent2, .65);
      ctx.lineWidth = 1.6;
      ctx.setLineDash([7, 9]);
      ctx.beginPath(); ctx.ellipse(0, 0, h.rx || W * .23, h.ry || H * .25, 0, 0, 6.28); ctx.stroke();
      ctx.setLineDash([3, 7]);
      ctx.beginPath(); ctx.ellipse(0, 0, (h.rx || W * .23) * .84, (h.ry || H * .25) * .82, 0, 0, 6.28); ctx.stroke();
      ctx.setLineDash([]);
      ctx.restore();
    }
    if (h.mode === 'vertical' && h.ghostText) {
      ctx.save();
      const chars = Array.from(h.ghostText);
      const fs = H * .072;
      ctx.font = `${fs}px "Yu Gothic","Microsoft YaHei UI",sans-serif`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      const cols = [.10, .21, .32, .43, .54, .65, .76, .87];
      for (let c = 0; c < cols.length; c++) {
        ctx.globalAlpha = .045 + .018 * (c % 3);
        ctx.fillStyle = c % 4 === 1 ? style.accent2 : style.ink;
        const x = W * cols[c];
        for (let i = 0; i < chars.length * 4; i++) {
          const ch = chars[i % chars.length];
          ctx.fillText(ch, x, H * .07 + i * fs * 1.02 + (now * .006) % (fs * 2));
        }
      }
      ctx.restore();
      // 青蓝色波浪线
      ctx.save();
      ctx.strokeStyle = hexA(style.accent2, .72);
      ctx.lineWidth = 2;
      ctx.shadowColor = style.accent2;
      ctx.shadowBlur = 12;
      const waveX = (h.columnX && h.columnX.length) ? h.columnX : [W * .44, W * .52, W * .60, W * .68];
      for (let c = 0; c < waveX.length; c++) {
        const x = waveX[c] + (c % 2 ? 8 : -8);
        ctx.beginPath();
        for (let y = H * .10; y <= H * .90; y += 8) {
          const xx = x + Math.sin(y * .018 + now * .0012 + c * 1.7) * (18 + c * 4);
          if (y === H * .10) ctx.moveTo(xx, y); else ctx.lineTo(xx, y);
        }
        ctx.stroke();
      }
      ctx.restore();
    }
  }

  function drawHanabiFront(now, pos) {
    if (!shot || !shot.hanabi) return;
    const h = shot.hanabi;
    if (h.mode === 'circle' || h.mode === 'badge') {
      ctx.save();
      ctx.globalAlpha = .55;
      ctx.fillStyle = style.accent2;
      for (let i = 0; i < 14; i++) {
        const a = i * 2.4 + now * .0002;
        const x = h.cx + Math.cos(a) * h.rx * 1.12;
        const y = h.cy + Math.sin(a) * h.ry * 1.12;
        ctx.fillRect(x, y, 2, 2);
      }
      ctx.restore();
    }
    // 花火的胶片孔边（所有模式都有）
    drawHanabiFilmstrip(now);

    // 顶部小字 / 底部三点 / 虚线：让画面不空，接近 Folia 的收尾细节
    if (state && state.artist && h.mode !== 'vertical') {
      ctx.save();
      ctx.font = '11px Consolas, monospace';
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillStyle = hexA(style.ink, .36);
      ctx.fillText(state.artist, W * .5, H * .085);
      ctx.restore();
    }
    ctx.save();
    ctx.fillStyle = hexA(style.dim, .8);
    for (let i = 0; i < 3; i++) {
      ctx.beginPath();
      ctx.arc(W * .085 + i * 15, H * .915, 2.2, 0, 6.28);
      ctx.fill();
    }
    ctx.strokeStyle = hexA(style.ink, .22);
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 8]);
    ctx.beginPath();
    ctx.moveTo(W * .12, H * .905);
    ctx.lineTo(W * .88, H * .905);
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.restore();
  }

  function drawHanabiFilmstrip(now) {
    ctx.save();
    const x = W * .052;
    const step = H * .048;
    const scroll = (now * .012) % step;
    for (let i = 0; i < 18; i++) {
      const y = H * .08 + ((i * step - scroll) % (step * 18) + step * 18) % (step * 18);
      const c = [style.accent, style.accent2, '#f4c95d', '#c6a8ff'][i % 4];
      ctx.globalAlpha = .55;
      ctx.fillStyle = hexA(c, .7);
      ctx.fillRect(x, y, 11, 13);
      ctx.globalAlpha = .35;
      ctx.fillStyle = '#0a0712';
      ctx.fillRect(x + 3, y + 4, 5, 5);
    }
    ctx.restore();
  }

  function drawStage(now) {
    const pos = currentPosition(now);
    if (settings.transparent) {
      ctx.clearRect(0, 0, W, H);
      if (wallpaperReady && wallpaperImage) {
        const scale = Math.max(W / wallpaperImage.width, H / wallpaperImage.height);
        const dw = wallpaperImage.width * scale;
        const dh = wallpaperImage.height * scale;
        ctx.drawImage(wallpaperImage, (W - dw) / 2, (H - dh) / 2, dw, dh);
        if (settings.dim > 0) {
          ctx.fillStyle = 'rgba(0,0,0,' + settings.dim + ')';
          ctx.fillRect(0, 0, W, H);
        }
      }
    } else {
      (SCENES[style.scene] || sceneHanabi)(now);
      if (settings.dim > 0) {
        ctx.save();
        ctx.fillStyle = 'rgba(0,0,0,' + settings.dim + ')';
        ctx.fillRect(0, 0, W, H);
        ctx.restore();
      }
    }

    ctx.save();
    applyCamera(now);
    drawGhost(now);
    drawBursts(now);
    if (shot && shot.hanabi) drawHanabiBack(now, pos);
    if (shot && shot.cards) drawCards(now, pos);
    if (shot && shot.ribbon) drawRibbonBg(now, pos);
    if (shot && shot.ribbons) drawRibbons(now, pos);
    if (shot) {
      const words = shot.words.slice().sort((a, b) => (a.hero ? 1 : 0) - (b.hero ? 1 : 0));
      for (const w of words) drawWord(w, pos);
    }
    if (shot && shot.hanabi) drawHanabiFront(now, pos);
    if (shot && shot.deco) drawDeco(now, pos);
    ctx.restore();

    if (lineFlash > 0 && !settings.reduceMotion) {
      ctx.save();
      ctx.globalAlpha = lineFlash * .04;
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, W, H);
      ctx.restore();
      lineFlash *= .90;
    }
  }

  // ------------------------------------------------------------------ panel

  function applyStyle(id, save) {
    const found = STYLES.filter(s => s.id === id)[0];
    if (!found) return;
    style = found;
    settings.styleId = id;
    if (save !== false) saveSettings();
    document.documentElement.style.setProperty('--accent', style.accent);
    document.documentElement.style.setProperty('--accent2', style.accent2);
    document.documentElement.style.setProperty('--ink', style.ink);
    document.documentElement.style.setProperty('--dim', style.dim);
    document.body.classList.toggle('light-style', !!style.light);
    const tag = $('#styleTagName');
    if (tag) tag.textContent = `${style.name} / ${style.en}`;
    const sub = $('#panelSub');
    if (sub) sub.textContent = `${style.en} · ${style.name}`;
    const kicker = $('#previewKicker');
    if (kicker) kicker.textContent = `${style.en} / ${style.name}`;
    const note = $('#previewNote');
    if (note) note.textContent = style.note || style.desc;
    const art = $('#previewArt');
    if (art) {
      art.style.background = `radial-gradient(circle at 28% 18%, ${hexA(style.accent, .34)}, transparent 38%), radial-gradient(circle at 82% 72%, ${hexA(style.accent2, .24)}, transparent 42%), linear-gradient(160deg, ${style.bg[0]}, ${style.bg[1]} 70%)`;
      art.style.color = style.ink;
      const strong = art.querySelector('strong');
      if (strong) strong.style.color = style.ink;
    }
    const fontInfo = $('#fontInfo');
    if (fontInfo) fontInfo.textContent = `当前样式：${style.name} / ${fontLabel(style.font)}`;
    shot = null; lineIndex = -1; bursts = [];
    makeBits();
    ensureLine(true);
    buildStyleGrid();
    syncPanel();
    renderBar();
  }

  function fontLabel(kind) {
    if (kind === 'serif') return '衬线（Times / 宋体）';
    if (kind === 'display') return '无衬线粗体（Arial Black / 雅黑）';
    if (kind === 'hand') return '手写（Segoe Script / 楷体）';
    if (kind === 'mono') return '等宽（Consolas）';
    return '无衬线（Segoe UI / 雅黑）';
  }

  function buildStyleGrid() {
    const grid = $('#styleGrid');
    if (!grid) return;
    grid.innerHTML = '';
    for (const s of STYLES) {
      const b = document.createElement('button');
      b.className = 'style-card' + (s.id === style.id ? ' selected' : '');
      b.setAttribute('data-style', s.id);
      b.style.setProperty('--card-top', s.bg[0]);
      b.style.setProperty('--card-bottom', s.bg[1]);
      b.style.setProperty('--card-accent', s.accent);
      b.style.setProperty('--card-ink', s.ink);
      b.innerHTML = `<span class="glyph">${s.name.charAt(0)}</span><span class="name">${s.name}</span>`;
      b.addEventListener('click', () => applyStyle(s.id, true));
      grid.appendChild(b);
    }
  }

  function showTab(name) {
    $$('.tab').forEach(t => t.classList.toggle('active', t.getAttribute('data-tab') === name));
    $$('.tab-page').forEach(p => p.classList.toggle('active', p.getAttribute('data-page') === name));
  }

  function syncPanel() {
    const setCheck = (id, key) => { const el = $(id); if (el) el.checked = !!settings[key]; };
    const setVal = (id, key, out, digits) => {
      const el = $(id); if (el) el.value = settings[key];
      const o = $(out); if (o) o.textContent = Number(settings[key]).toFixed(digits == null ? 2 : digits);
    };
    setCheck('#optBloom', 'bloom'); setCheck('#optPush', 'push'); setCheck('#optTrail', 'trail');
    setCheck('#optTransparent', 'transparent'); setCheck('#optSubtitle', 'subtitle');
    setCheck('#optReduce', 'reduceMotion'); setCheck('#optTransitions', 'transitions'); setCheck('#optRandom', 'randomSong');
    setVal('#optSpark', 'spark', '#outSpark'); setVal('#optSparkGlow', 'sparkGlow', '#outSparkGlow');
    setVal('#optChorus', 'chorusGlow', '#outChorus'); setVal('#optGlow', 'glow', '#outGlow');
    setVal('#optCamera', 'camera', '#outCamera'); setVal('#optMotion', 'motion', '#outMotion');
    setVal('#optDim', 'dim', '#outDim'); setVal('#optDecor', 'decor', '#outDecor');
    setVal('#optFontScale', 'fontScale', '#outFontScale');
    const bgdim = $('#bgDim'); if (bgdim) bgdim.style.opacity = 0;
    const previewLine = $('#previewLine');
    if (previewLine) previewLine.textContent = currentLineText() || (style.note || style.desc);
    const sub = $('#subtitle');
    if (sub) {
      sub.style.display = settings.subtitle ? 'block' : 'none';
      sub.textContent = currentLineText();
    }
    const info = $('#subtitleInfo');
    if (info) info.textContent = currentLineText() || '当前没有歌词。';
  }

  function setTransparent(value) {
    settings.transparent = !!value;
    document.body.classList.toggle('transparent', settings.transparent);
    saveSettings();
    post({ type: 'transparent', value: settings.transparent });
  }

  function resetSettings() {
    settings = Object.assign({}, DEFAULTS);
    setTransparent(false);
    applyStyle(settings.styleId, true);
    syncPanel();
  }

  // ------------------------------------------------------------------ bar/UI

  function fmt(sec) {
    sec = Math.max(0, sec || 0);
    const m = Math.floor(sec / 60), s = Math.floor(sec % 60);
    return m + ':' + String(s).padStart(2, '0');
  }

  function setPlayingIcon(playing) {
    const btn = $('#playBtn');
    if (!btn) return;
    btn.classList.toggle('playing', playing);
    const icon = $('#playIcon');
    icon.innerHTML = playing
      ? '<rect x="7" y="5" width="3.6" height="14" rx="1.4"></rect><rect x="13.4" y="5" width="3.6" height="14" rx="1.4"></rect>'
      : '<path d="M8 5.5 19 12 8 18.5Z"></path>';
  }

  function renderBar() {
    if (!state) return;
    const pos = currentPosition(performance.now());
    const dur = state.duration || 0;
    const title = state.title || (state.demo ? 'HANABI · 花火（演示）' : '等待播放');
    const titleEl = $('#title');
    if (titleEl.textContent !== title) titleEl.textContent = title;
    $('#curTime').textContent = fmt(pos);
    $('#durTime').textContent = dur > 0 ? fmt(dur) : (state.demo ? fmt(DEMO_DURATION) : '0:00');
    const ratio = dur > 0 ? clamp(pos / dur, 0, 1) : (state.demo ? ((pos % DEMO_DURATION) / DEMO_DURATION) : 0);
    $('#fill').style.width = (ratio * 100) + '%';
    $('#knob').style.left = (ratio * 100) + '%';
    setPlayingIcon(state.playing);
    const sub = $('#subtitle');
    if (sub) sub.textContent = currentLineText();
    const sinfo = $('#subtitleInfo');
    if (sinfo) sinfo.textContent = currentLineText() || '当前没有歌词。';
    if (panelOpen) {
      const pl = $('#previewLine');
      if (pl) pl.textContent = currentLineText() || (style.note || style.desc);
    }
  }

  function showPanel(show) {
    panelOpen = show;
    $('#panel').classList.toggle('hidden', !show);
    $('#panelDimmer').classList.toggle('hidden', !show);
    if (show) { buildStyleGrid(); syncPanel(); }
  }

  function bindUI() {
    $('#playBtn').addEventListener('click', () => post({ type: 'toggle' }));
    $('#closeBtn').addEventListener('click', () => post({ type: 'close' }));
    $('#styleBtn').addEventListener('click', () => showPanel(!panelOpen));
    $('#panelClose').addEventListener('click', () => showPanel(false));
    $('#panelDimmer').addEventListener('click', () => showPanel(false));
    $$('.tab').forEach(t => t.addEventListener('click', () => showTab(t.getAttribute('data-tab'))));

    const track = $('#track');
    const seekFromEvent = (e) => {
      const r = track.getBoundingClientRect();
      const ratio = clamp((e.clientX - r.left) / Math.max(1, r.width), 0, 1);
      const dur = state && state.duration > 0 ? state.duration : DEMO_DURATION;
      const target = ratio * dur;
      $('#fill').style.width = (ratio * 100) + '%';
      $('#knob').style.left = (ratio * 100) + '%';
      $('#curTime').textContent = fmt(target);
      return target;
    };
    let dragging = false;
    track.addEventListener('pointerdown', (e) => { dragging = true; track.setPointerCapture(e.pointerId); seekFromEvent(e); });
    track.addEventListener('pointermove', (e) => { if (dragging) seekFromEvent(e); });
    track.addEventListener('pointerup', (e) => {
      if (!dragging) return;
      dragging = false;
      post({ type: 'seek', position: seekFromEvent(e) });
    });

    const bindNumber = (id, key, output, digits) => {
      const el = $(id); if (!el) return;
      el.addEventListener('input', () => {
        settings[key] = Number(el.value);
        const o = $(output); if (o) o.textContent = settings[key].toFixed(digits == null ? 2 : digits);
        if (key === 'dim') { const d = $('#bgDim'); if (d) d.style.opacity = 0; }
        if (key === 'fontScale') { const sl = shot ? shot.lineIndex : -1; shot = null; lineIndex = sl; ensureLine(true); }
        saveSettings();
      });
    };
    bindNumber('#optSpark', 'spark', '#outSpark'); bindNumber('#optSparkGlow', 'sparkGlow', '#outSparkGlow');
    bindNumber('#optChorus', 'chorusGlow', '#outChorus'); bindNumber('#optGlow', 'glow', '#outGlow');
    bindNumber('#optCamera', 'camera', '#outCamera'); bindNumber('#optMotion', 'motion', '#outMotion');
    bindNumber('#optDim', 'dim', '#outDim'); bindNumber('#optDecor', 'decor', '#outDecor');
    bindNumber('#optFontScale', 'fontScale', '#outFontScale');

    const bindCheck = (id, key, after) => {
      const el = $(id); if (!el) return;
      el.addEventListener('change', () => {
        settings[key] = el.checked;
        saveSettings();
        if (after) after(el.checked);
      });
    };
    bindCheck('#optBloom', 'bloom'); bindCheck('#optPush', 'push'); bindCheck('#optTrail', 'trail');
    bindCheck('#optTransparent', 'transparent', setTransparent);
    bindCheck('#optSubtitle', 'subtitle', () => { const s = $('#subtitle'); if (s) s.style.display = settings.subtitle ? 'block' : 'none'; });
    bindCheck('#optReduce', 'reduceMotion'); bindCheck('#optTransitions', 'transitions'); bindCheck('#optRandom', 'randomSong');
    const reset = $('#btnReset'); if (reset) reset.addEventListener('click', resetSettings);

    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        if (panelOpen) { showPanel(false); e.preventDefault(); return; }
        post({ type: 'close' }); e.preventDefault();
      } else if (e.key === ' ') { post({ type: 'toggle' }); e.preventDefault(); }
      else if (e.key === 'ArrowLeft') { const p = currentPosition(performance.now()); post({ type: 'seek', position: Math.max(0, p - 5) }); e.preventDefault(); }
      else if (e.key === 'ArrowRight') { const p = currentPosition(performance.now()); post({ type: 'seek', position: p + 5 }); e.preventDefault(); }
    });

    let cursorTimer = 0;
    document.addEventListener('mousemove', () => {
      document.body.style.cursor = 'default';
      clearTimeout(cursorTimer);
      cursorTimer = setTimeout(() => { document.body.style.cursor = 'none'; }, 3000);
    });
  }

  function boot() {
    if (settings.styleId === undefined) settings.styleId = 'hanabi';
    style = STYLES.filter(s => s.id === settings.styleId)[0] || STYLES[3];
    applyStyle(style.id, false);
    resize();
    window.addEventListener('resize', resize);
    bindUI();
    showTab('animation');

    state = {
      key: 'demo', title: 'HANABI · 花火（演示）', artist: 'CAPSULE × WEBVIEW2',
      position: 0, duration: DEMO_DURATION, playing: true, lyrics: DEMO_LYRICS, lastAt: performance.now(), demo: true
    };
    demoStart = performance.now();
    renderBar();
    buildTitleShot();
    ensureLine(true);
    if (window.chrome && chrome.webview) {
      chrome.webview.addEventListener('message', (e) => {
        const msg = e.data;
        if (!msg) return;
        if (msg.type === 'state') applyState(msg);
        else if (msg.type === 'wallpaper' && msg.data) {
          wallpaperImage = new Image();
          wallpaperImage.onload = () => { wallpaperReady = true; };
          wallpaperImage.src = msg.data;
        }
      });
      post({ type: 'ready' });
      post({ type: 'transparent', value: !!settings.transparent });
    }
    setTransparent(settings.transparent);

    let last = performance.now();
    function frame(now) {
      const dt = Math.min(.05, (now - last) / 1000);
      last = now;
      if (!state) return;
      if (state.demo) state.position = ((now - demoStart) / 1000) % DEMO_DURATION;
      ensureLine(false);
      updateBursts(now, dt);
      drawStage(now);
      if (now % 100 < 20) renderBar();
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  boot();
})();










