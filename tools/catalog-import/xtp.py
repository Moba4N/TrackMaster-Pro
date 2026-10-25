"""Liest XTrackCAD-Parameterdateien (.xtp) und rechnet Gleisgeometrie in TrackMaster-Koordinaten um (mm, y nach unten)."""
import math, re

IN = 25.4

def read_text(path):
    raw = open(path, 'rb').read()
    try:
        return raw.decode('utf-8')
    except UnicodeDecodeError:
        return raw.decode('latin-1')

def parse(path):
    blocks = []
    cur = None
    for line in read_text(path).splitlines():
        s = line.strip()
        if s.startswith('TURNOUT'):
            m = re.match(r'TURNOUT\s+(\S+)\s+"(.*)', s)
            title = m.group(2) if m else s
            title = title.rstrip('"\t ').replace('""', '"')
            cur = {'scale': m.group(1) if m else '', 'title': title, 'E': [], 'segs': [], 'P': []}
            continue
        if cur is None:
            continue
        if s == 'END' or s.startswith('END'):
            blocks.append(cur); cur = None; continue
        parts = s.split()
        if not parts:
            continue
        t = parts[0]
        try:
            if t in ('E', 'T'):
                cur['E'].append((float(parts[1]), float(parts[2]), float(parts[3])))
            elif t == 'S':
                v = list(map(float, parts[3:7]))
                cur['segs'].append(('S', (v[0], v[1]), (v[2], v[3])))
            elif t == 'C':
                v = list(map(float, parts[3:8]))
                cur['segs'].append(('C', abs(v[0]), (v[1], v[2]), v[3], v[4]))
            elif t == 'J':
                cur['segs'].append(('J',))
            elif t == 'P':
                m = re.match(r'P\s+"[^"]*"\s+(.*)', s)
                nums = [int(x) for x in m.group(1).split()] if m else []
                cur['P'].append(nums)
        except (ValueError, IndexError):
            pass
    return blocks

def to_my(x, y):
    return (x * IN, -y * IN)

def compass_to_heading(theta):
    return theta - 90.0

def norm(a):
    return ((a % 360) + 540) % 360 - 180

def prim(seg, reverse):
    """Gerichtetes Teilstück: dict mit start, heading, kind, len / r, sweep, turn, end, endheading (TrackMaster-Koordinaten)."""
    if seg[0] == 'S':
        a, b = seg[1], seg[2]
        if reverse: a, b = b, a
        p0, p1 = to_my(*a), to_my(*b)
        h = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0]))
        L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        return {'k': 'L', 'start': p0, 'h': h, 'len': L, 'end': p1, 'he': h}
    if seg[0] == 'C':
        R, (cx, cy), a0, sw = seg[1], seg[2], seg[3], seg[4]
        def pt(a):
            r = math.radians(a)
            return to_my(cx + R * math.sin(r), cy + R * math.cos(r))
        if not reverse:
            p0, p1 = pt(a0), pt(a0 + sw)
            h0 = compass_to_heading(a0 + 90)
            he = compass_to_heading(a0 + sw + 90)
        else:
            p0, p1 = pt(a0 + sw), pt(a0)
            h0 = compass_to_heading(a0 + sw - 90)
            he = compass_to_heading(a0 - 90)
        turn = 1 if norm(he - h0) > 0 else -1
        return {'k': 'A', 'start': p0, 'h': h0, 'r': R * IN, 'sweep': abs(sw), 'turn': turn, 'end': p1, 'he': he}
    raise ValueError('Spirale')

def dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])

def chain(block, idxs):
    segs = block['segs']
    if any(i < 1 or i > len(segs) or segs[i - 1][0] == 'J' for i in idxs):
        raise ValueError('Segment')
    eps = [to_my(e[0], e[1]) for e in block['E']]
    first = segs[idxs[0] - 1]
    # Ausrichtung des ersten Stücks: Anfang an einem Endpunkt oder am nächsten Stück
    best = None
    for rev in (False, True):
        p = prim(first, rev)
        if len(idxs) > 1:
            nxt = segs[idxs[1] - 1]
            d = min(dist(p['end'], prim(nxt, False)['start']), dist(p['end'], prim(nxt, True)['start']))
        else:
            d = min([dist(p['start'], e) for e in eps] or [0])
        if best is None or d < best[0]:
            best = (d, p)
    out = [best[1]]
    for i in idxs[1:]:
        s = segs[i - 1]
        a, b = prim(s, False), prim(s, True)
        out.append(a if dist(a['start'], out[-1]['end']) <= dist(b['start'], out[-1]['end']) else b)
    # Bei Stücken ohne Anschluss am Anfang (rückwärts aufgeführt) umdrehen
    if eps:
        ds = min(dist(out[0]['start'], e) for e in eps)
        de = min(dist(out[-1]['end'], e) for e in eps)
        if ds > 0.5 and de <= 0.5:
            out = [prim_rev(p) for p in reversed(out)]
    return out

def prim_rev(p):
    q = dict(p)
    q['start'], q['end'] = p['end'], p['start']
    q['h'], q['he'] = norm(p['he'] + 180), norm(p['h'] + 180)
    if p['k'] == 'A':
        q['turn'] = -p['turn']
    return q

def routes(block):
    """Alle verschiedenen Fahrwege (Folge gerichteter Stücke) mit ihren Endpunkt-Indizes."""
    seen = set()
    result = []
    eps = [to_my(e[0], e[1]) for e in block['E']]
    for nums in block['P']:
        sub = []
        for n in nums + [0]:
            if n == 0:
                if sub:
                    key = frozenset(sub)
                    if key not in seen:
                        seen.add(key)
                        ch = chain(block, sub)
                        a = min(range(len(eps)), key=lambda k: dist(eps[k], ch[0]['start'])) if eps else -1
                        b = min(range(len(eps)), key=lambda k: dist(eps[k], ch[-1]['end'])) if eps else -1
                        result.append({'prims': ch, 'a': a, 'b': b,
                                       'da': dist(eps[a], ch[0]['start']) if eps else 0, 'db': dist(eps[b], ch[-1]['end']) if eps else 0})
                    sub = []
            else:
                sub.append(n)
    return result

def length(prims):
    return sum(p['len'] if p['k'] == 'L' else p['r'] * math.radians(p['sweep']) for p in prims)

def curvature(prims):
    return sum(p['sweep'] * p['turn'] for p in prims if p['k'] == 'A')

def reverse_route(r):
    return {'prims': [prim_rev(p) for p in reversed(r['prims'])], 'a': r['b'], 'b': r['a'], 'da': r['db'], 'db': r['da']}

def transform(prims, origin, h0):
    """Verschieben/Drehen, so dass origin im Ursprung liegt und h0 zur Richtung 0 wird."""
    c, s = math.cos(math.radians(-h0)), math.sin(math.radians(-h0))
    def T(p):
        x, y = p[0] - origin[0], p[1] - origin[1]
        return (x * c - y * s, x * s + y * c)
    out = []
    for p in prims:
        q = dict(p)
        q['start'], q['end'] = T(p['start']), T(p['end'])
        q['h'], q['he'] = norm(p['h'] - h0), norm(p['he'] - h0)
        out.append(q)
    return out
