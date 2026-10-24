namespace TheCelestialDiviner.Services;

/// <summary>
/// 键帽 Web 叠加源的内嵌网页（OBS 浏览器源加载；CEF 透明背景渲染）。
/// 复刻 keyviz lowprofile 键帽视觉（双层结构 + 按压下沉 + 连击角标，参数与
/// KeycapOverlayWindow 一致：size=32 / 帽高 72 / 容器 80 / 圆角 20 / 下沉 8）。
/// 协议（JSON 文本帧）：
///   hello    {scheme{face,base,text,border}, opacity, reminder, iconRev, keys[]}   ← 连接即发
///   snapshot {keys[], bumps{label:n}}                                              ← 按住快照
///   pulse    {label, holdMs, count}                                                ← 连发脉冲 / 方案切换
///   reminder {visible}                                                             ← 提醒键帽显隐
///   scheme   {face,base,text,border}                                               ← 配色切换
///   config   {opacity, iconRev}                                                    ← 透明度 / 图标版本
/// token=demo 时进入本地演示模式（随机按键 + 脉冲），供普通浏览器预览样式。
/// </summary>
public static class WebOverlayPage
{
    public const string Html = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>衍天键帽 · OBS 浏览器源</title>
<style>
  html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; }
  :root { --face: #6d28d9; --base: #4c1d95; --text: #fbbf24; --border: #4c1d95; }
  #root {
    position: fixed; right: 8px; bottom: 0;
    display: flex; align-items: flex-end; gap: 6px;
    transition: opacity 200ms linear;
  }
  .cap { position: relative; height: 80px; min-width: 72px;
         opacity: 1; transition: opacity 300ms ease; }
  .cap.modifier { min-width: 80px; }
  .cap.fading { opacity: 0; }
  .cap .base { position: absolute; left: 0; right: 0; bottom: 0; height: 72px;
               border-radius: 20px; background: var(--base); border: 2px solid var(--border);
               box-sizing: border-box; }
  .cap .face { position: absolute; left: 0; right: 0; top: 0; height: 72px;
               border-radius: 20px; background: var(--face); border: 2px solid var(--border);
               box-sizing: border-box; display: flex; align-items: center; justify-content: center;
               transform: translateY(0); transition: transform 50ms ease-out; }
  .cap.pressed .face { transform: translateY(8px); }
  .cap .label { font: 500 32px/1 "Segoe UI", "Microsoft YaHei", sans-serif; color: var(--text); }
  .cap svg.icon { width: 25.6px; height: 25.6px; stroke: var(--text); stroke-width: 2; fill: none;
                  stroke-linecap: round; stroke-linejoin: round; }
  .cap .face img { width: 40px; height: 40px; }
  .badge { position: absolute; top: -8px; right: -8px; min-width: 24px; height: 24px;
           border-radius: 12px; background: var(--text); color: var(--face);
           font: 700 13px/24px "Segoe UI", "Microsoft YaHei", sans-serif;
           text-align: center; padding: 0 4px; box-sizing: border-box;
           visibility: hidden; transform-origin: 50% 50%; }
  .badge.show { visibility: visible; }
  .badge.pop { animation: badge-pop 150ms ease-out; }
  @keyframes badge-pop { from { transform: scale(1.2); } to { transform: scale(1); } }
  /* 提醒键帽恒居序列末位（与桌面悬浮层一致）：flex order 压过 DOM 追加顺序 */
  .cap.reminder { order: 999; }
</style>
</head>
<body>
<div id="root"></div>
<script>
"use strict";
const root = document.getElementById("root");
const params = new URLSearchParams(location.search);
const token = params.get("token");
const REMINDER_LABEL = "启";
const MODIFIERS = new Set(["Ctrl", "Alt", "Shift", "Win", "Meta", "Cmd", "Option"]);
const ICONS = {
  "左键": "M12,7.318 L12,10 M5,10 L5,15 A7,7 0 0,0 19,15 L19,9 C19,5.473 16.392,2.485 13,2 M7,2 A2,2 0 1,1 7,6 A2,2 0 1,1 7,2 Z",
  "中键": "M12,2 A7,7 0 0,1 19,9 L19,15 A7,7 0 0,1 12,22 A7,7 0 0,1 5,15 L5,9 A7,7 0 0,1 12,2 Z M12,6 L12,10",
  "右键": "M12,7.318 L12,10 M19,10 L19,15 A7,7 0 0,1 5,15 L5,9 C5,5.473 7.608,2.485 11,2 M17,2 A2,2 0 1,1 17,6 A2,2 0 1,1 17,2 Z"
};
const caps = new Map();   // label -> {el, badge, count, retireTimer, fadeTimer, pulseTimer}
let reminderEl = null;
let iconRev = -1;
let clockOffset = 0;   // Date.now() - 服务端 TickCount（hello 校准；后续消息实测端到端延迟）
let lastDelayMs = 0;

// 端到端延迟探针：标题实时显示（发送时刻 → 页面收到）。排除浏览器渲染耗时，
// 纯数据链路延迟（进程 → WS → CEF/浏览器 JS 收到）。诊断卡顿时先看这里的数字。
function measureDelay(t) {
  if (typeof t !== "number") return;
  lastDelayMs = Math.max(0, Date.now() - t - clockOffset);
  document.title = "衍天键帽 · 延迟 " + lastDelayMs + "ms";
}

function svgIcon(path) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("class", "icon");
  const p = document.createElementNS("http://www.w3.org/2000/svg", "path");
  p.setAttribute("d", path);
  svg.appendChild(p);
  return svg;
}

function ensureCap(label) {
  let cap = caps.get(label);
  if (cap) {
    if (cap.retireTimer) { clearTimeout(cap.retireTimer); cap.retireTimer = null; }
    if (cap.fadeTimer) { clearTimeout(cap.fadeTimer); cap.fadeTimer = null; }
    cap.el.classList.remove("fading");
    return cap;
  }
  const el = document.createElement("div");
  el.className = "cap" + (MODIFIERS.has(label) ? " modifier" : "");
  const base = document.createElement("div"); base.className = "base";
  const face = document.createElement("div"); face.className = "face";
  const badge = document.createElement("div"); badge.className = "badge";
  if (ICONS[label]) face.appendChild(svgIcon(ICONS[label]));
  else {
    const span = document.createElement("span");
    span.className = "label"; span.textContent = label;
    face.appendChild(span);
  }
  el.appendChild(base); el.appendChild(face); el.appendChild(badge);
  root.appendChild(el);
  cap = { el, badge, count: 0, retireTimer: null, fadeTimer: null, pulseTimer: null };
  caps.set(label, cap);
  return cap;
}

function bumpBadge(cap, inc) {
  cap.count = Math.min(99, cap.count + inc);
  if (cap.count < 2) return;
  cap.badge.textContent = String(cap.count);
  cap.badge.classList.add("show");
  cap.badge.classList.remove("pop");
  void cap.badge.offsetWidth;   // 强制重排：连发期间可重复触发弹跳动画
  cap.badge.classList.add("pop");
}

function press(cap) { cap.el.classList.add("pressed"); }
function releaseVisual(cap) { cap.el.classList.remove("pressed"); }

// 1.2s 空闲宽限后 300ms 淡出移除（连击计数随键帽销毁归零，与桌面悬浮层一致）
function armRetire(cap, label) {
  if (cap.retireTimer) clearTimeout(cap.retireTimer);
  cap.retireTimer = setTimeout(() => {
    cap.el.classList.add("fading");
    cap.fadeTimer = setTimeout(() => { cap.el.remove(); caps.delete(label); }, 300);
  }, 1200);
}

function onSnapshot(keys, bumps) {
  const held = new Set(keys);
  for (const [label, cap] of caps) {
    if (held.has(label)) continue;
    releaseVisual(cap);
    armRetire(cap, label);
  }
  for (const label of keys) {
    const cap = ensureCap(label);
    press(cap);
    const inc = (bumps && bumps[label]) || 0;
    if (inc > 0) bumpBadge(cap, inc);
  }
}

function onPulse(label, holdMs, count) {
  const cap = ensureCap(label);
  press(cap);
  if (cap.pulseTimer) clearTimeout(cap.pulseTimer);
  cap.pulseTimer = setTimeout(() => { releaseVisual(cap); cap.pulseTimer = null; }, Math.max(10, holdMs));
  bumpBadge(cap, count);
  armRetire(cap, label);
}

function setReminder(visible) {
  if (visible) {
    if (reminderEl) return;
    const el = document.createElement("div");
    el.className = "cap reminder";
    const base = document.createElement("div"); base.className = "base";
    const face = document.createElement("div"); face.className = "face";
    const img = document.createElement("img");
    // 图标请求必须携带令牌：服务端对全部路径统一校验（无令牌的 /icon.png 会被 404 → 文字降级）
    img.src = "/icon.png?token=" + encodeURIComponent(token) + "&v=" + iconRev + "&t=" + Date.now();
    img.onerror = () => {
      const span = document.createElement("span");
      span.className = "label"; span.textContent = REMINDER_LABEL;
      face.replaceChildren(span);
    };
    face.appendChild(img);
    el.appendChild(base); el.appendChild(face);
    root.appendChild(el);
    reminderEl = el;
  } else if (reminderEl) {
    reminderEl.remove();
    reminderEl = null;
  }
}

function refreshReminderIcon() { if (reminderEl) { setReminder(false); setReminder(true); } }

function applyScheme(s) {
  if (!s) return;
  const r = document.documentElement.style;
  r.setProperty("--face", s.face);
  r.setProperty("--base", s.base);
  r.setProperty("--text", s.text);
  r.setProperty("--border", s.border);
}

function setOpacity(v) {
  const o = +v;
  root.style.opacity = isFinite(o) ? Math.max(0, Math.min(1, o)) : 1;
}

function handleMessage(msg) {
  switch (msg.type) {
    case "hello":
      clockOffset = Date.now() - (msg.t | 0);
      applyScheme(msg.scheme);
      setOpacity(msg.opacity);
      iconRev = msg.iconRev | 0;
      setReminder(!!msg.reminder);
      onSnapshot(msg.keys || [], null);
      break;
    case "snapshot": measureDelay(msg.t); onSnapshot(msg.keys || [], msg.bumps); break;
    case "pulse": measureDelay(msg.t); onPulse(String(msg.label), msg.holdMs | 0, Math.max(1, msg.count | 0)); break;
    case "reminder": setReminder(!!msg.visible); break;
    case "scheme": applyScheme(msg); break;
    case "config":
      setOpacity(msg.opacity);
      if ((msg.iconRev | 0) !== iconRev) { iconRev = msg.iconRev | 0; refreshReminderIcon(); }
      break;
  }
}

let ws = null;
function connect() {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  ws = new WebSocket(proto + "//" + location.host + "/ws?token=" + encodeURIComponent(token));
  ws.onmessage = (ev) => { let m; try { m = JSON.parse(ev.data); } catch { return; } handleMessage(m); };
  ws.onclose = () => { ws = null; setTimeout(connect, 1000); };
  ws.onerror = () => { try { ws.close(); } catch {} };
}
connect();

// 演示模式（?token=demo）：普通浏览器直接预览样式，不连服务器。
if (token === "demo") {
  const labels = ["Ctrl", "Shift", "Alt", "A", "D", "F", "左键", "右键", "↑滚"];
  const pick = () => labels[Math.floor(Math.random() * labels.length)];
  applyScheme({ face: "#6d28d9", base: "#4c1d95", text: "#fbbf24", border: "#4c1d95" });
  setOpacity(1);
  setReminder(true);
  setInterval(() => {
    const held = new Set();
    const n = 1 + Math.floor(Math.random() * 3);
    for (let i = 0; i < n; i++) held.add(pick());
    const bumps = {};
    for (const k of held) bumps[k] = 1 + Math.floor(Math.random() * 5);
    onSnapshot([...held], bumps);
    setTimeout(() => onSnapshot([], null), 250 + Math.random() * 600);
  }, 1000);
  setInterval(() => onPulse(pick(), 26, 2 + Math.floor(Math.random() * 20)), 700);
}
</script>
</body>
</html>
""";
}
