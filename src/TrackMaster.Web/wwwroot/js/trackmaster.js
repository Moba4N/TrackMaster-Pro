// TrackMaster Pro im Browser: Zeichenprotokoll auf <canvas>, Zeiger und Tastatur, Dateien, Speicher, Druck und 3D (WebGL).
// Der Programmkern läuft als .NET WebAssembly; diese Datei ist nur die Brücke zum Browser.

let entry = null;
async function E() {
    if (entry) return entry;
    const { getAssemblyExports } = await globalThis.getDotnetRuntime(0);
    const exports = await getAssemblyExports("TrackMaster.Web.dll");
    entry = exports.TrackMaster.Web.Interop.JsEntry;
    return entry;
}
E();

// ---------- Zeichenflächen ----------

const surfaces = new Map();
let fontFamily = "'Segoe UI Variable Text','Segoe UI',Roboto,system-ui,sans-serif";

export function setFont(family) { fontFamily = family; }

function surface(id) {
    let s = surfaces.get(id);
    const canvas = document.getElementById(id);
    if (!canvas) return null;
    if (!s || s.canvas !== canvas) {
        s = { canvas, ctx: canvas.getContext("2d"), paths: new Map(), dpr: 1, pending: false, w: 0, h: 0 };
        surfaces.set(id, s);
    }
    return s;
}

export function resetCanvas(id) { surfaces.delete(id); }

export function requestFrame(id) {
    const s = surface(id);
    if (!s || s.pending) return;
    s.pending = true;
    requestAnimationFrame(() => {
        s.pending = false;
        if (entry) entry.Render(id);
        else E().then(e => e.Render(id));
    });
}

function rgba(p) {
    const a = (p >>> 24) & 255, r = (p >>> 16) & 255, g = (p >>> 8) & 255, b = p & 255;
    return a === 255 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${(a / 255).toFixed(3)})`;
}

const caps = ["butt", "round", "square"];
const joins = ["miter", "round", "bevel"];

function buildPath(d, start, n) {
    const p = new Path2D();
    let i = start;
    const end = start + n;
    while (i < end) {
        switch (d[i]) {
            case 0: p.moveTo(d[i + 1], d[i + 2]); i += 3; break;
            case 1: p.lineTo(d[i + 1], d[i + 2]); i += 3; break;
            case 2: p.closePath(); i += 1; break;
            case 3: p.quadraticCurveTo(d[i + 1], d[i + 2], d[i + 3], d[i + 4]); i += 5; break;
            case 4: p.moveTo(d[i + 1] + d[i + 3], d[i + 2]); p.arc(d[i + 1], d[i + 2], Math.max(0, d[i + 3]), 0, Math.PI * 2); i += 4; break;
            default: i = end;
        }
    }
    return p;
}

function hairline(ctx, s) {
    const m = ctx.getTransform();
    return s.dpr / Math.max(1e-9, Math.hypot(m.a, m.b));
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
}

function paint(ctx, s, style, color, width, fillFn, strokeFn) {
    const c = rgba(color);
    if (style === 0 || style === 2) { ctx.fillStyle = c; fillFn(); }
    if (style === 1 || style === 2) {
        ctx.strokeStyle = c;
        ctx.lineWidth = width > 0 ? width : hairline(ctx, s);
        strokeFn();
    }
}

export function draw(id, view, strings) {
    const s = surface(id);
    if (!s) return;
    const d = view.slice();
    const ctx = s.ctx;
    const used = new Set();
    ctx.setTransform(s.dpr, 0, 0, s.dpr, 0, 0);
    ctx.lineJoin = "round";
    let i = 0;
    while (i < d.length) {
        switch (d[i]) {
            case 1: ctx.save(); i += 1; break;
            case 2: ctx.restore(); i += 1; break;
            case 3: ctx.translate(d[i + 1], d[i + 2]); i += 3; break;
            case 4: ctx.rotate(d[i + 1] * Math.PI / 180); i += 2; break;
            case 5: ctx.scale(d[i + 1], d[i + 2]); i += 3; break;
            case 6: ctx.transform(d[i + 1], d[i + 2], d[i + 3], d[i + 4], d[i + 5], d[i + 6]); i += 7; break;
            case 7: {
                const p = s.paths.get(d[i + 1]);
                used.add(d[i + 1]);
                if (p) ctx.clip(p.path, p.evenOdd ? "evenodd" : "nonzero");
                i += 2;
                break;
            }
            case 8: {
                const pid = d[i + 1], evenOdd = d[i + 2] > 0, n = d[i + 3];
                s.paths.set(pid, { path: buildPath(d, i + 4, n), evenOdd });
                i += 4 + n;
                break;
            }
            case 9: {
                const pid = d[i + 1], style = d[i + 2], color = d[i + 3], width = d[i + 4];
                const dashes = d[i + 7];
                used.add(pid);
                const p = s.paths.get(pid);
                if (p) {
                    ctx.lineCap = caps[d[i + 5]] || "butt";
                    ctx.lineJoin = joins[d[i + 6]] || "miter";
                    if (dashes > 0) {
                        ctx.setLineDash(Array.from(d.subarray(i + 8, i + 8 + dashes)));
                        ctx.lineDashOffset = d[i + 8 + dashes];
                    }
                    paint(ctx, s, style, color, width,
                        () => ctx.fill(p.path, p.evenOdd ? "evenodd" : "nonzero"),
                        () => ctx.stroke(p.path));
                    if (dashes > 0) ctx.setLineDash([]);
                }
                i += 9 + dashes;
                break;
            }
            case 10: {
                ctx.beginPath();
                ctx.moveTo(d[i + 1], d[i + 2]);
                ctx.lineTo(d[i + 3], d[i + 4]);
                ctx.lineCap = caps[d[i + 7]] || "butt";
                paint(ctx, s, 1, d[i + 5], d[i + 6], null, () => ctx.stroke());
                i += 8;
                break;
            }
            case 11: {
                ctx.beginPath();
                ctx.arc(d[i + 1], d[i + 2], Math.max(0, d[i + 3]), 0, Math.PI * 2);
                paint(ctx, s, d[i + 4], d[i + 5], d[i + 6], () => ctx.fill(), () => ctx.stroke());
                i += 7;
                break;
            }
            case 12: {
                const l = d[i + 1], t = d[i + 2], r = d[i + 3], b = d[i + 4];
                ctx.lineJoin = joins[d[i + 8]] || "miter";
                paint(ctx, s, d[i + 5], d[i + 6], d[i + 7], () => ctx.fillRect(l, t, r - l, b - t), () => ctx.strokeRect(l, t, r - l, b - t));
                i += 9;
                break;
            }
            case 13: {
                const l = d[i + 1], t = d[i + 2], r = d[i + 3], b = d[i + 4];
                roundRect(ctx, l, t, r - l, b - t, Math.min(d[i + 5], (r - l) / 2, (b - t) / 2));
                paint(ctx, s, d[i + 7], d[i + 8], d[i + 9], () => ctx.fill(), () => ctx.stroke());
                i += 10;
                break;
            }
            case 14: {
                const text = strings[d[i + 1]];
                ctx.font = `${d[i + 7] > 0 ? "600 " : ""}${d[i + 4]}px ${fontFamily}`;
                ctx.textAlign = ["left", "center", "right"][d[i + 6]] || "left";
                ctx.textBaseline = "alphabetic";
                ctx.fillStyle = rgba(d[i + 5]);
                ctx.fillText(text, d[i + 2], d[i + 3]);
                i += 8;
                break;
            }
            case 15: {
                const text = strings[d[i + 1]];
                const cx = d[i + 2], cy = d[i + 3], size = d[i + 4], pad = d[i + 7];
                ctx.font = `${size}px ${fontFamily}`;
                const w = ctx.measureText(text).width;
                const h = size * 1.35;
                roundRect(ctx, cx - w / 2 - pad, cy - h / 2, w + 2 * pad, h, pad);
                ctx.fillStyle = rgba(d[i + 6]);
                ctx.fill();
                ctx.fillStyle = rgba(d[i + 5]);
                ctx.textAlign = "center";
                ctx.textBaseline = "middle";
                ctx.fillText(text, cx, cy + size * 0.04);
                i += 8;
                break;
            }
            case 16: {
                ctx.save();
                ctx.setTransform(1, 0, 0, 1, 0, 0);
                ctx.fillStyle = rgba(d[i + 1]);
                ctx.fillRect(0, 0, s.canvas.width, s.canvas.height);
                ctx.restore();
                i += 2;
                break;
            }
            case 17: {
                ctx.beginPath();
                ctx.rect(d[i + 1], d[i + 2], d[i + 3] - d[i + 1], d[i + 4] - d[i + 2]);
                ctx.clip();
                i += 5;
                break;
            }
            default:
                i = d.length;
        }
    }
    for (const k of s.paths.keys()) if (!used.has(k)) s.paths.delete(k);
}

// ---------- Zeichenfläche des Plans: Größe, Zeiger, Ziehen aus der Bibliothek ----------

function mods(e) {
    return (e.shiftKey ? 1 : 0) | (e.ctrlKey || e.metaKey ? 2 : 0) | (e.altKey ? 4 : 0) | (spaceDown ? 8 : 0);
}

let spaceDown = false;
let lastPointerType = "mouse";

export function attachPlan(id) {
    const canvas = document.getElementById(id);
    if (!canvas || canvas.dataset.attached) return;
    canvas.dataset.attached = "1";
    const host = canvas.parentElement;
    const pos = e => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };

    const resize = async () => {
        const r = host.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        const s = surface(id);
        const w = Math.max(1, Math.floor(r.width)), h = Math.max(1, Math.floor(r.height));
        if (s.w === w && s.h === h && s.dpr === dpr) return;
        s.dpr = dpr; s.w = w; s.h = h;
        canvas.width = Math.round(w * dpr);
        canvas.height = Math.round(h * dpr);
        canvas.style.width = w + "px";
        canvas.style.height = h + "px";
        s.paths.clear();
        const e = await E();
        e.Resize(id, w, h);
        e.Render(id);
    };
    new ResizeObserver(resize).observe(host);
    resize();

    canvas.addEventListener("pointerdown", async e => {
        lastPointerType = e.pointerType;
        canvas.focus({ preventScroll: true });
        try { canvas.setPointerCapture(e.pointerId); } catch { }
        const [x, y] = pos(e);
        (await E()).PointerDown(e.pointerId, x, y, e.button, mods(e), e.pointerType !== "mouse", e.timeStamp);
        e.preventDefault();
    });
    canvas.addEventListener("pointermove", e => {
        if (!entry) return;
        const [x, y] = pos(e);
        entry.PointerMove(e.pointerId, x, y, mods(e), e.pointerType !== "mouse");
    });
    const up = e => {
        if (!entry) return;
        const [x, y] = pos(e);
        entry.PointerUp(e.pointerId, x, y, mods(e), e.pointerType !== "mouse", e.timeStamp);
    };
    canvas.addEventListener("pointerup", up);
    canvas.addEventListener("pointercancel", e => entry && entry.PointerCancel(e.pointerId));
    canvas.addEventListener("pointerleave", e => { if (entry && e.pointerType === "mouse") entry.PointerLeave(); });
    canvas.addEventListener("wheel", e => {
        e.preventDefault();
        if (!entry) return;
        const [x, y] = pos(e);
        const scale = e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 300 : 1;
        entry.Wheel(x, y, e.deltaY * scale, mods(e));
    }, { passive: false });
    canvas.addEventListener("dblclick", e => {
        if (!entry || lastPointerType !== "mouse") return;
        const [x, y] = pos(e);
        entry.DoubleClick(x, y);
    });
    canvas.addEventListener("contextmenu", e => e.preventDefault());
    canvas.addEventListener("dragover", e => { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; });
    canvas.addEventListener("drop", e => {
        e.preventDefault();
        const key = e.dataTransfer.getData("text/x-trackmaster") || e.dataTransfer.getData("text/plain");
        if (!key || !entry) return;
        const [x, y] = pos(e);
        entry.Drop(key, x, y);
    });
}

// Gleise aus der Bibliothek ziehen: Schlüssel steht in data-drag-key
document.addEventListener("dragstart", e => {
    const item = e.target && e.target.closest ? e.target.closest("[data-drag-key]") : null;
    if (!item) return;
    e.dataTransfer.setData("text/x-trackmaster", item.dataset.dragKey);
    e.dataTransfer.setData("text/plain", item.dataset.dragKey);
    e.dataTransfer.effectAllowed = "copy";
});

// ---------- Tastatur und Seitenende ----------

function editable(t) {
    return t && (t.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(t.tagName));
}

window.addEventListener("keydown", e => {
    if (!entry || editable(e.target)) return;
    if (e.key === " " && !e.repeat) { spaceDown = true; entry.Space(true); }
    if (entry.KeyDown(e.key, e.ctrlKey || e.metaKey, e.shiftKey, e.altKey)) e.preventDefault();
});
window.addEventListener("keyup", e => {
    if (e.key === " ") { spaceDown = false; if (entry) entry.Space(false); }
});
window.addEventListener("blur", () => { spaceDown = false; if (entry) entry.Space(false); });
window.addEventListener("pagehide", () => entry && entry.BeforeUnload());
document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden" && entry) entry.BeforeUnload(); });

// ---------- Kleinigkeiten ----------

export function setText(elementId, text) {
    const el = document.getElementById(elementId);
    if (el) el.textContent = text;
}

export function vibrate(ms) { try { navigator.vibrate && navigator.vibrate(ms); } catch { } }

export function focusElement(elementId) {
    const el = document.getElementById(elementId);
    if (el) { el.focus(); if (el.select) el.select(); }
}

export function reload() { location.reload(); }

export function browserLanguage() { return navigator.language || "de"; }

/** Adresse der Startseite der umgebenden Webseite aus <meta name="trackmaster-home">; leer = kein Link. */
export function homeUrl() {
    const m = document.querySelector('meta[name="trackmaster-home"]');
    return (m && m.getAttribute("content") || "").trim();
}

export function storageGet(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

export function storageSet(key, value) {
    try { localStorage.setItem(key, value); return true; } catch { return false; }
}

export function storageRemove(key) {
    try { localStorage.removeItem(key); } catch { }
}

export function downloadBytes(fileName, mime, view) {
    // MemoryView gilt nur während des Aufrufs: Inhalt kopieren
    const blob = new Blob([view.slice()], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function download(fileName, mime, text) {
    // CSV mit BOM, damit Excel Umlaute richtig liest
    const content = mime === "text/csv" ? "﻿" + text : text;
    const blob = new Blob([content], { type: mime + ";charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function pickFile(accept) {
    return new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = accept;
        input.style.display = "none";
        let done = false;
        const finish = v => { if (!done) { done = true; resolve(v); input.remove(); } };
        input.addEventListener("change", () => {
            const f = input.files && input.files[0];
            if (!f) { finish(""); return; }
            const r = new FileReader();
            r.onload = () => finish(f.name + "\u0000" + r.result);
            r.onerror = () => finish("");
            r.readAsText(f, "utf-8");
        });
        input.addEventListener("cancel", () => finish(""));
        document.body.appendChild(input);
        input.click();
    });
}

export async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
}

export async function readClipboard() {
    try { return await navigator.clipboard.readText(); } catch { return ""; }
}

export function print(html) {
    const frame = document.createElement("iframe");
    frame.style.position = "fixed";
    frame.style.right = "0";
    frame.style.bottom = "0";
    frame.style.width = "0";
    frame.style.height = "0";
    frame.style.border = "0";
    document.body.appendChild(frame);
    const doc = frame.contentWindow.document;
    doc.open();
    doc.write(html);
    doc.close();
    const go = () => {
        frame.contentWindow.focus();
        frame.contentWindow.print();
        setTimeout(() => frame.remove(), 60000);
    };
    if (doc.readyState === "complete") setTimeout(go, 200); else frame.onload = () => setTimeout(go, 200);
}

// ---------- 3D-Ansicht (WebGL) ----------

const VS = `
uniform mat4 uMvp;
attribute vec3 aPos;
attribute vec3 aNormal;
varying vec3 vNormal;
varying vec3 vPos;
void main() {
    vNormal = aNormal;
    vPos = aPos;
    gl_Position = uMvp * vec4(aPos, 1.0);
}`;

const FS = `
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
uniform vec3 uColor;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform vec3 uFillDir;
uniform vec3 uFillColor;
uniform vec3 uAmbient;
uniform vec3 uEmissive;
uniform vec3 uEye;
uniform float uSpec;
varying vec3 vNormal;
varying vec3 vPos;
void main() {
    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing) n = -n;
    float ds = max(dot(n, -uSunDir), 0.0);
    float df = max(dot(n, -uFillDir), 0.0);
    vec3 c = uColor * (uAmbient + uSunColor * ds + uFillColor * df) + uEmissive;
    if (uSpec > 0.0) {
        vec3 v = normalize(uEye - vPos);
        vec3 h = normalize(v - uSunDir);
        c += uSunColor * pow(max(dot(n, h), 0.0), uSpec) * 0.5;
    }
    gl_FragColor = vec4(min(c, vec3(1.0)), 1.0);
}`;

const SKY_VS = `
attribute vec2 aPos;
varying float vT;
void main() {
    vT = aPos.y * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.999, 1.0);
}`;

const SKY_FS = `
precision mediump float;
uniform vec3 uTop;
uniform vec3 uBottom;
varying float vT;
void main() { gl_FragColor = vec4(mix(uBottom, uTop, vT), 1.0); }`;

function perspective(fovY, aspect, near, far) {
    const f = 1 / Math.tan(fovY / 2), nf = 1 / (near - far);
    return new Float32Array([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0]);
}

function lookAt(e, t, up) {
    let zx = e[0] - t[0], zy = e[1] - t[1], zz = e[2] - t[2];
    let l = Math.hypot(zx, zy, zz) || 1; zx /= l; zy /= l; zz /= l;
    let xx = up[1] * zz - up[2] * zy, xy = up[2] * zx - up[0] * zz, xz = up[0] * zy - up[1] * zx;
    l = Math.hypot(xx, xy, xz) || 1; xx /= l; xy /= l; xz /= l;
    const yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
    return new Float32Array([
        xx, yx, zx, 0, xy, yy, zy, 0, xz, yz, zz, 0,
        -(xx * e[0] + xy * e[1] + xz * e[2]), -(yx * e[0] + yy * e[1] + yz * e[2]), -(zx * e[0] + zy * e[1] + zz * e[2]), 1]);
}

function multiply(a, b) {
    const o = new Float32Array(16);
    for (let c = 0; c < 4; c++)
        for (let r = 0; r < 4; r++)
            o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
    return o;
}

function norm(v) { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; }

const g3 = {
    gl: null, canvas: null, prog: null, sky: null, skyBuf: null, loc: {}, meshes: [], dynamic: [],
    light: null, ride: null, orbit: { yaw: -60, pitch: 35, roll: 0, dist: 1000, target: [0, 0, 0] },
    animating: false, last: 0, frame: 0, observer: null, pointers: new Map(),
};

function compile(gl, type, src) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
    return s;
}

function link(gl, vs, fs) {
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, vs));
    gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(p);
    return p;
}

function upload(gl, data) {
    const b = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, b);
    gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
    return b;
}

function glResize() {
    const c = g3.canvas;
    if (!c) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const r = c.parentElement.getBoundingClientRect();
    c.width = Math.max(1, Math.round(r.width * dpr));
    c.height = Math.max(1, Math.round(r.height * dpr));
    c.style.width = r.width + "px";
    c.style.height = r.height + "px";
    glRender();
}

// Neigung frei von oben bis unter die Platte, dazu Kippen um die Blickachse: die Anlage lässt sich um alle drei Achsen drehen
const PITCH_MIN = -89, PITCH_MAX = 89;

function orbitCamera() {
    const o = g3.orbit;
    const yaw = o.yaw * Math.PI / 180, pitch = Math.min(PITCH_MAX, Math.max(PITCH_MIN, o.pitch)) * Math.PI / 180;
    const eye = [o.target[0] + o.dist * Math.cos(pitch) * Math.sin(yaw), o.target[1] + o.dist * Math.sin(pitch), o.target[2] + o.dist * Math.cos(pitch) * Math.cos(yaw)];
    const f = norm([o.target[0] - eye[0], o.target[1] - eye[1], o.target[2] - eye[2]]);
    const r = norm([f[1] * 0 - f[2] * 1, f[2] * 0 - f[0] * 0, f[0] * 1 - f[1] * 0]);
    const u = [r[1] * f[2] - r[2] * f[1], r[2] * f[0] - r[0] * f[2], r[0] * f[1] - r[1] * f[0]];
    const a = o.roll * Math.PI / 180, c = Math.cos(a), sn = Math.sin(a);
    const up = [u[0] * c + r[0] * sn, u[1] * c + r[1] * sn, u[2] * c + r[2] * sn];
    return { eye, target: o.target, up, fov: 50, near: Math.max(2, o.dist / 2000) };
}

function attach3D(c) {
    const o = g3.orbit;
    const pts = g3.pointers;
    let lastPinch = null;
    c.addEventListener("pointerdown", e => {
        try { c.setPointerCapture(e.pointerId); } catch { }
        pts.set(e.pointerId, { x: e.clientX, y: e.clientY, button: e.button });
        lastPinch = null;
        e.preventDefault();
    });
    c.addEventListener("pointermove", e => {
        const p = pts.get(e.pointerId);
        if (!p || g3.ride) return;
        const dx = e.clientX - p.x, dy = e.clientY - p.y;
        p.x = e.clientX; p.y = e.clientY;
        if (pts.size >= 2) {
            const [a, b] = [...pts.values()];
            const d = Math.hypot(b.x - a.x, b.y - a.y), mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
            if (lastPinch) {
                o.dist = Math.min(100000, Math.max(50, o.dist * lastPinch.d / Math.max(1, d)));
                pan(mx - lastPinch.mx, my - lastPinch.my);
            }
            lastPinch = { d, mx, my };
        } else if (p.button === 2 || p.button === 1 || e.shiftKey) {
            pan(dx, dy);
        } else if (e.ctrlKey || e.altKey) {
            o.roll = ((o.roll + dx * 0.35 + 540) % 360) - 180;
        } else {
            o.yaw -= dx * 0.35;
            o.pitch = Math.min(PITCH_MAX, Math.max(PITCH_MIN, o.pitch + dy * 0.3));
        }
        glRender();
    });
    const up = e => { pts.delete(e.pointerId); lastPinch = null; };
    c.addEventListener("pointerup", up);
    c.addEventListener("pointercancel", up);
    c.addEventListener("contextmenu", e => e.preventDefault());
    c.addEventListener("wheel", e => {
        e.preventDefault();
        if (g3.ride) return;
        const scale = e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 300 : 1;
        o.dist = Math.min(100000, Math.max(50, o.dist * Math.exp(e.deltaY * scale * 0.0012)));
        glRender();
    }, { passive: false });
    function pan(mx, my) {
        const yaw = o.yaw * Math.PI / 180, s = o.dist / 900;
        const right = [Math.cos(yaw), 0, -Math.sin(yaw)], fwd = [-Math.sin(yaw), 0, -Math.cos(yaw)];
        o.target = [o.target[0] - right[0] * mx * s + fwd[0] * my * s, o.target[1], o.target[2] - right[2] * mx * s + fwd[2] * my * s];
    }
}

function glRender() {
    if (g3.frame) return;
    g3.frame = requestAnimationFrame(() => { g3.frame = 0; drawScene(); });
}

function drawScene() {
    const gl = g3.gl;
    if (!gl || !g3.light) return;
    const L = g3.light, loc = g3.loc;
    const w = gl.drawingBufferWidth, h = gl.drawingBufferHeight;
    gl.viewport(0, 0, w, h);
    gl.clearColor(L.skyBottom[0], L.skyBottom[1], L.skyBottom[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    gl.disable(gl.DEPTH_TEST);
    gl.useProgram(g3.sky);
    gl.uniform3fv(loc.top, L.skyTop);
    gl.uniform3fv(loc.bottom, L.skyBottom);
    gl.bindBuffer(gl.ARRAY_BUFFER, g3.skyBuf);
    gl.enableVertexAttribArray(loc.skyPos);
    gl.vertexAttribPointer(loc.skyPos, 2, gl.FLOAT, false, 8, 0);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    gl.disableVertexAttribArray(loc.skyPos);

    const cam = g3.ride || orbitCamera();
    const aspect = h === 0 ? 1 : w / h;
    const fovY = 2 * Math.atan(Math.tan(cam.fov * Math.PI / 360) / aspect);
    const mvp = multiply(perspective(fovY, aspect, cam.near, 200000), lookAt(cam.eye, cam.target, cam.up || [0, 1, 0]));

    gl.enable(gl.DEPTH_TEST);
    gl.useProgram(g3.prog);
    gl.uniformMatrix4fv(loc.mvp, false, mvp);
    gl.uniform3fv(loc.sunDir, norm(L.sunDir));
    gl.uniform3fv(loc.sunColor, L.sun);
    gl.uniform3fv(loc.fillDir, norm(L.fillDir));
    gl.uniform3fv(loc.fillColor, L.fill);
    gl.uniform3fv(loc.ambient, L.ambient);
    gl.uniform3fv(loc.eye, cam.eye);
    gl.enableVertexAttribArray(loc.pos);
    gl.enableVertexAttribArray(loc.normal);
    for (const m of g3.meshes.concat(g3.dynamic)) {
        // Arten: 0 matt, 1 Wasser, 2 Schiene, 3 Fenster, 4 Lampe
        gl.uniform3fv(loc.color, m.kind === 4 ? [0, 0, 0] : m.color);
        gl.uniform3fv(loc.emissive, m.kind === 4 ? m.color : m.kind === 3 && L.windowsLit ? L.glow : [0, 0, 0]);
        gl.uniform1f(loc.spec, m.kind === 1 ? 40 : m.kind === 2 ? 20 : 0);
        gl.bindBuffer(gl.ARRAY_BUFFER, m.buffer);
        gl.vertexAttribPointer(loc.pos, 3, gl.FLOAT, false, 24, 0);
        gl.vertexAttribPointer(loc.normal, 3, gl.FLOAT, false, 24, 12);
        gl.drawArrays(gl.TRIANGLES, 0, m.count);
    }
    gl.disableVertexAttribArray(loc.pos);
    gl.disableVertexAttribArray(loc.normal);
}

function tick(now) {
    if (!g3.animating) return;
    const dt = g3.last ? Math.min((now - g3.last) / 1000, 0.1) : 0;
    g3.last = now;
    if (entry) entry.Tick3D(dt);
    drawScene();
    requestAnimationFrame(tick);
}

export const gl = {
    open(canvasId) {
        const c = document.getElementById(canvasId);
        if (!c) return false;
        const ctx = c.getContext("webgl", { antialias: true }) || c.getContext("experimental-webgl");
        if (!ctx) return false;
        g3.gl = ctx; g3.canvas = c; g3.meshes = []; g3.dynamic = []; g3.ride = null;
        g3.prog = link(ctx, VS, FS);
        g3.sky = link(ctx, SKY_VS, SKY_FS);
        const p = g3.prog;
        g3.loc = {
            mvp: ctx.getUniformLocation(p, "uMvp"), color: ctx.getUniformLocation(p, "uColor"),
            sunDir: ctx.getUniformLocation(p, "uSunDir"), sunColor: ctx.getUniformLocation(p, "uSunColor"),
            fillDir: ctx.getUniformLocation(p, "uFillDir"), fillColor: ctx.getUniformLocation(p, "uFillColor"),
            ambient: ctx.getUniformLocation(p, "uAmbient"), emissive: ctx.getUniformLocation(p, "uEmissive"),
            eye: ctx.getUniformLocation(p, "uEye"), spec: ctx.getUniformLocation(p, "uSpec"),
            pos: ctx.getAttribLocation(p, "aPos"), normal: ctx.getAttribLocation(p, "aNormal"),
            skyPos: ctx.getAttribLocation(g3.sky, "aPos"), top: ctx.getUniformLocation(g3.sky, "uTop"), bottom: ctx.getUniformLocation(g3.sky, "uBottom"),
        };
        g3.skyBuf = upload(ctx, new Float32Array([-1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1]));
        if (!c.dataset.attached) { c.dataset.attached = "1"; attach3D(c); }
        g3.observer = new ResizeObserver(glResize);
        g3.observer.observe(c.parentElement);
        glResize();
        return true;
    },
    close() {
        g3.animating = false;
        if (g3.observer) g3.observer.disconnect();
        const ctx = g3.gl;
        if (ctx) for (const m of g3.meshes.concat(g3.dynamic)) ctx.deleteBuffer(m.buffer);
        g3.gl = null; g3.canvas = null; g3.meshes = []; g3.dynamic = [];
    },
    setMeshes(dataView, metaView, dynamic) {
        const ctx = g3.gl;
        if (!ctx) return;
        const data = new Float32Array(dataView.slice());
        const meta = metaView.slice();
        const list = [];
        let offset = 0;
        for (let i = 0; i < meta.length; i += 5) {
            const count = meta[i];
            const part = data.subarray(offset * 6, (offset + count) * 6);
            offset += count;
            list.push({ count, color: [meta[i + 1], meta[i + 2], meta[i + 3]], kind: meta[i + 4], buffer: upload(ctx, part) });
        }
        const old = dynamic ? g3.dynamic : g3.meshes;
        for (const m of old) ctx.deleteBuffer(m.buffer);
        if (dynamic) g3.dynamic = list; else g3.meshes = list;
        glRender();
    },
    setLight(view) {
        const v = view.slice();
        g3.light = {
            sun: [v[0], v[1], v[2]], sunDir: [v[3], v[4], v[5]], fill: [v[6], v[7], v[8]], fillDir: [v[9], v[10], v[11]],
            ambient: [v[12], v[13], v[14]], skyTop: [v[15], v[16], v[17]], skyBottom: [v[18], v[19], v[20]], windowsLit: v[21] > 0,
            glow: [v[22], v[23], v[24]],
        };
        glRender();
    },
    setCamera(view) {
        const v = view.slice();
        if (v[0] === 0) {
            // Rundumsicht: Ziel und Abstand setzen, Mitfahren beenden
            g3.ride = null;
            g3.orbit.target = [v[1], v[2], v[3]];
            g3.orbit.dist = v[4];
        } else if (v[0] === 1) {
            g3.ride = { eye: [v[1], v[2], v[3]], target: [v[4], v[5], v[6]], fov: v[7], near: v[8] };
        } else g3.ride = null;
        glRender();
    },
    render() { glRender(); },
    // Drehen per Knopf: Achse 0 = X (kippen vor/zurück), 1 = Y (Hochachse), 2 = Z (Blickachse); grad 0 bei Achse −1 = gerade ausrichten
    turn(axis, deg) {
        const o = g3.orbit;
        if (axis === 0) o.pitch = Math.min(PITCH_MAX, Math.max(PITCH_MIN, o.pitch + deg));
        else if (axis === 1) o.yaw = ((o.yaw + deg + 540) % 360) - 180;
        else if (axis === 2) o.roll = ((o.roll + deg + 540) % 360) - 180;
        else { o.yaw = -60; o.pitch = 35; o.roll = 0; }
        glRender();
    },
    animate(on) {
        if (on === g3.animating) return;
        g3.animating = on;
        g3.last = 0;
        if (on) requestAnimationFrame(tick);
    },
};
