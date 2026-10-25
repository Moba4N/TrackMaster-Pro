import math, re
from xtp import routes, length, curvature, reverse_route, transform, dist, to_my, norm

def f(v, d=2):
    s = f"{round(v, d):.{d}f}".rstrip('0').rstrip('.')
    return '0' if s in ('-0', '') else s

def num_de(v, d=1):
    s = f(v, d)
    return s.replace('.', ',')

def is_straight(prims):
    return all(p['k'] == 'L' for p in prims) and abs(norm(prims[-1]['he'] - prims[0]['h'])) < 0.05

def seg_code(p):
    if p['k'] == 'L':
        return f"Ln({f(p['len'])})"
    return f"Ar({f(p['r'])}, {f(p['sweep'], 3)}, {p['turn']})"

def route_code(prims):
    s = prims[0]
    return f"Rt({f(s['start'][0])}, {f(s['start'][1])}, {f(s['h'], 3)}, {', '.join(seg_code(p) for p in prims)})"

def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'

def merge(prims):
    """Benachbarte gerade Stücke in gleicher Richtung zusammenfassen."""
    out = []
    for p in prims:
        if out and p['k'] == 'L' and out[-1]['k'] == 'L' and abs(norm(p['h'] - out[-1]['h'])) < 0.01:
            q = dict(out[-1]); q['len'] += p['len']; q['end'] = p['end']; out[-1] = q
        elif p['k'] == 'L' and p['len'] < 0.005:
            continue
        else:
            out.append(p)
    return out

RADIUS = re.compile(r'\b(R\d+[a-z]?)\b')
EXTRA = [(r'remote|motor|elec|dcc|antrieb', 'mit Antrieb'), (r'cond frog|polaris', 'polarisiert'), (r'hi-speed|slim|schlank', 'schlank')]

def suffix(t):
    tl = t.lower()
    out = [s for pat, s in EXTRA if re.search(pat, tl)]
    return (' ' + ', '.join(out)) if out else ''

def classify(block):
    title = block['title']
    raw = [x.strip() for x in title.split('\t')]
    fields = [x for x in raw if x]
    number = fields[-1].strip('"') if len(fields) >= 2 else title
    desc = fields[-2] if len(fields) >= 2 else title
    if len(raw) >= 3 and not raw[-1] and len(fields) >= 2:
        # ohne Artikelnummer: erstes Wort der Beschreibung (z. B. „BG1“)
        desc = fields[-1]
        number = desc.split()[0]
    number = re.sub(r'\s+Right$', ' R', re.sub(r'\s+Left$', ' L', number))
    tl = desc.lower()
    E = block['E']
    if 'turntable' in tl or 'transfer' in tl or 'drehscheibe' in tl or 'schiebebühne' in tl:
        return None, 'Drehscheibe/Schiebebühne'
    try:
        rs = routes(block)
    except ValueError as e:
        return None, f'Geometrie ({e})'
    if not rs:
        return None, 'kein Fahrweg'
    nE = len(E)
    rs = [r if r['da'] <= r['db'] else reverse_route(r) for r in rs]

    if 'flex' in tl and nE == 2 and len(rs) == 1:
        return {'flex': True, 'number': number, 'len': length(rs[0]['prims'])}, None

    def place(r_list, origin_route, decor=()):
        o = origin_route['prims'][0]
        res = [merge(transform(r['prims'], o['start'], o['h'])) for r in r_list]
        dec = [merge(transform(r['prims'], o['start'], o['h'])) for r in decor]
        return res, dec

    if nE == 1 or ('bumper' in tl or 'buffer' in tl or 'prellbock' in tl) and len(rs) == 1:
        L = length(rs[0]['prims'])
        return {'code': f"P({cs(number)}, {f(L)})", 'number': number}, None

    if nE == 2 and len(rs) == 1:
        r = rs[0]
        prims = merge(r['prims'])
        L = length(prims)
        name = 'Gerade'
        for pat, n in [(r'uncoupl|entkuppl', 'Entkupplungsgleis'), (r'contact|circuit|schalt', 'Schaltgleis'), (r'isolat|trenn', 'Trenngleis'),
                       (r'feeder|terminal|anschluss', 'Anschlussgleis'), (r'rerail|aufgleis', 'Aufgleisgleis'), (r'adjust|ausgleich|variable', 'Ausgleichsgleis'),
                       (r'adapt|transition|übergang', 'Übergangsgleis'), (r'end piece|endst', 'Endstück'), (r'connect|verbind', 'Verbindungsgleis'), (r'bridge|brücke', 'Brückengleis')]:
            if re.search(pat, tl):
                name = n; break
        if is_straight(prims):
            return {'code': f"S({cs(number)}, {f(L)}{'' if name == 'Gerade' else ', ' + cs(name)})", 'number': number}, None
        if len(prims) == 1 and prims[0]['k'] == 'A':
            p = prims[0]
            rn = RADIUS.search(desc)
            extra = f", {cs(rn.group(1))}" if rn else ''
            if name != 'Gerade':
                return {'code': f"C({cs(number)}, {f(p['r'])}, {f(p['sweep'], 3)}{extra or ', null'}, {cs(name + ' gebogen')})", 'number': number}, None
            return {'code': f"C({cs(number)}, {f(p['r'])}, {f(p['sweep'], 3)}{extra})", 'number': number}, None
        res, _ = place([r], r)
        turns = {p['turn'] for p in res[0] if p['k'] == 'A'}
        n = name if name != 'Gerade' else ('Gegenbogen' if len(turns) > 1 else 'Bogen')
        spec = f"{num_de(L)} mm"
        cat = 'Bögen' if name == 'Gerade' else 'Sonstige'
        return {'code': f"G({cs(number)}, PieceKind.Straight, {cs(n)}, {cs(spec)}, {cs(cat)}, false, 0, [{route_code(res[0])}])", 'number': number}, None

    # gemeinsamer Anfang aller Fahrwege → Weiche
    common = None
    for e in range(nE):
        if all(e in (r['a'], r['b']) for r in rs):
            starts = [r['prims'][0]['start'] if r['a'] == e else reverse_route(r)['prims'][0]['start'] for r in rs]
            ep = to_my(E[e][0], E[e][1])
            if all(dist(p, ep) < 1 for p in starts):
                common = e; break
    if common is not None and len(rs) >= 2:
        rr = [r if r['a'] == common else reverse_route(r) for r in rs]
        ends = [r['b'] for r in rr]
        if len(set(ends)) != len(rr):
            return None, 'Weiche mit doppelten Enden'
        rr.sort(key=lambda r: (abs(curvature(r['prims'])), curvature(r['prims'])))
        res, _ = place(rr, rr[0])
        curv = [sum(p['sweep'] * p['turn'] for p in x if p['k'] == 'A') for x in res]
        main_len = length(res[0])
        div = next((p for x in res[1:] for p in x if p['k'] == 'A'), None)
        rgeo = f" · R {num_de(div['r'])} · {num_de(div['sweep'])}°" if div else ''
        right = False
        if len(res) >= 4:
            return None, 'Weiche mit mehr als drei Strängen'
        if len(res) == 3:
            name = 'Dreiwegweiche'
        else:
            c0, c1 = curv
            if abs(c0) < 0.5:
                right = c1 > 0
                name = f"Weiche {'rechts' if right else 'links'}"
            elif c0 * c1 > 0:
                right = c1 > 0
                name = f"Bogenweiche {'rechts' if right else 'links'}"
                inner = min((p for x in res for p in x if p['k'] == 'A'), key=lambda p: p['r'])
                outer = max((p for x in res for p in x if p['k'] == 'A'), key=lambda p: p['r'])
                rgeo = f" · R {num_de(inner['r'])} / {num_de(outer['r'])}"
            else:
                name = 'Y-Weiche'
        spec = f"{num_de(main_len)} mm{rgeo}"
        code = f"G({cs(number)}, PieceKind.Switch, {cs(name + suffix(desc))}, {cs(spec)}, \"Weichen\", {'true' if right else 'false'}, 0, [{', '.join(route_code(x) for x in res)}])"
        return {'code': code, 'number': number}, None

    if nE == 4:
        # zwei Fahrwege, die alle vier Anschlüsse abdecken, möglichst gerade
        best = None
        for i in range(len(rs)):
            for j in range(i + 1, len(rs)):
                if len({rs[i]['a'], rs[i]['b'], rs[j]['a'], rs[j]['b']}) == 4:
                    c = abs(curvature(rs[i]['prims'])) + abs(curvature(rs[j]['prims']))
                    if best is None or c < best[0]:
                        best = (c, i, j)
        if best is None:
            return None, 'Kreuzung ohne durchgehende Fahrwege'
        _, i, j = best
        a, b = rs[i], rs[j]
        if 0 in (b['a'], b['b']) and 0 not in (a['a'], a['b']):
            a, b = b, a
        decor = [r for k, r in enumerate(rs) if k not in (i, j)]
        res, dec = place([a, b], a, decor)
        ang = abs(norm(res[1][0]['h'] - res[0][0]['h']))
        ang = min(ang, 180 - ang)
        # Schneiden sich die Fahrwege? (sonst Gleisverbindung)
        def seg_pts(x):
            return [x[0]['start']] + [p['end'] for p in x]
        def inter(p1, p2, q1, q2):
            def o(a, b, c): return (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])
            return o(p1, p2, q1) * o(p1, p2, q2) < 0 and o(q1, q2, p1) * o(q1, q2, p2) < 0
        pa, pb = seg_pts(res[0]), seg_pts(res[1])
        crosses = any(inter(pa[k], pa[k+1], pb[m], pb[m+1]) for k in range(len(pa)-1) for m in range(len(pb)-1))
        slips = len(decor)
        cat = 'Kreuzungen'
        if not crosses and not slips:
            name, drives, cat = ('Doppelgleis' if is_straight(res[0]) else 'Doppelgleis gebogen'), 0, 'Sonstige'
        elif not crosses:
            name, drives = ('Doppelte Gleisverbindung' if slips >= 2 else 'Gleisverbindung'), min(2, slips)
        elif slips >= 2:
            name, drives = f"DKW {num_de(ang)}°", 2
        elif slips == 1:
            name, drives = f"EKW {num_de(ang)}°", 1
        else:
            name, drives = f"Kreuzung {num_de(ang)}°", 0
        spec = f"{num_de(length(res[0]))} mm"
        code = (f"G({cs(number)}, PieceKind.Crossing, {cs(name + suffix(desc))}, {cs(spec)}, {cs(cat)}, false, {drives}, "
                f"[{', '.join(route_code(x) for x in res)}]{', [' + ', '.join(route_code(x) for x in dec) + ']' if dec else ''})")
        return {'code': code, 'number': number}, None
    return None, f'{nE} Anschlüsse, {len(rs)} Fahrwege'
