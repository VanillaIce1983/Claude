# Zeichnet das Blechbestand-Logo: blauer Hintergrund, Stapel aus Blechtafeln.
# Aufruf: python3 make_icon.py ../assets [vorschau.png]   (benötigt Pillow)
import sys
from PIL import Image, ImageDraw, ImageFilter

def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))

def hexc(h):
    h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (255,)

def vgrad(size, top, bottom):
    w, h = size
    g = Image.new('RGBA', size)
    d = ImageDraw.Draw(g)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=lerp(top, bottom, y / max(1, h - 1)))
    return g

def poly_fill(img, pts, top, bottom):
    """Polygon mit senkrechtem Farbverlauf füllen."""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    box = (int(min(xs)), int(min(ys)), int(max(xs)) + 1, int(max(ys)) + 1)
    grad = vgrad((box[2] - box[0], box[3] - box[1]), top, bottom)
    mask = Image.new('L', grad.size, 0)
    ImageDraw.Draw(mask).polygon([(x - box[0], y - box[1]) for x, y in pts], fill=255)
    img.paste(grad, box[:2], mask)

def render(px, simple=False):
    S = 4                       # Überabtastung
    W = px * S
    k = W / 256.0               # Zeichnung im 256er-Raster
    img = Image.new('RGBA', (W, W), (0, 0, 0, 0))

    # Hintergrund: abgerundetes Quadrat, blauer Verlauf
    bg = vgrad((W, W), hexc('#2E8BE6'), hexc('#0B4A9E'))
    m = Image.new('L', (W, W), 0)
    r = (40 if not simple else 36) * k
    inset = 8 * k if px >= 48 else 0
    ImageDraw.Draw(m).rounded_rectangle([inset, inset, W - 1 - inset, W - 1 - inset], radius=r, fill=255)
    img.paste(bg, (0, 0), m)
    # leichter Glanz oben
    gl = Image.new('RGBA', (W, W), (255, 255, 255, 0))
    gd = ImageDraw.Draw(gl)
    gd.ellipse([-60 * k, -150 * k, 316 * k, 110 * k], fill=(255, 255, 255, 34))
    gl.putalpha(Image.composite(gl.getchannel('A'), Image.new('L', (W, W), 0), m))
    img = Image.alpha_composite(img, gl)

    d = ImageDraw.Draw(img)
    if simple:
        # Kleine Größen: drei kräftige Tafeln, klar getrennt
        sheets = [(0, 150), (-6, 118), (5, 86)]   # (Versatz x, Oberkante y), unten nach oben
        t, depth, dx = 22, 26, 34
        x0, x1 = 44, 182
    else:
        sheets = [(3, 182), (-5, 167), (7, 152), (-3, 137), (2, 122)]
        t, depth, dx = 15, 40, 38
        x0, x1 = 40, 178

    def P(x, y): return (x * k, y * k)

    # Schatten unter dem Stapel
    if not simple:
        sh = Image.new('RGBA', (W, W), (0, 0, 0, 0))
        ImageDraw.Draw(sh).polygon([P(x0 - 2, sheets[0][1] + t + 4), P(x1 + 6, sheets[0][1] + t + 4),
                                    P(x1 + dx + 10, sheets[0][1] + t - depth + 4), P(x0 + dx + 4, sheets[0][1] + t - depth + 4)],
                                   fill=(0, 20, 60, 110))
        sh = sh.filter(ImageFilter.GaussianBlur(6 * k))
        img = Image.alpha_composite(img, sh)
        d = ImageDraw.Draw(img)

    for i, (ox, y) in enumerate(sheets):
        a, b = x0 + ox, x1 + ox
        top_face = [P(a, y), P(b, y), P(b + dx, y - depth), P(a + dx, y - depth)]
        front = [P(a, y), P(b, y), P(b, y + t), P(a, y + t)]
        side = [P(b, y), P(b + dx, y - depth), P(b + dx, y - depth + t), P(b, y + t)]
        poly_fill(img, top_face, hexc('#FFFFFF'), hexc('#D5DEE6'))
        poly_fill(img, front, hexc('#E4EBF1'), hexc('#8EA2B3'))
        poly_fill(img, side, hexc('#A9B9C6'), hexc('#6B8194'))
        d = ImageDraw.Draw(img)
        # dunkle Trennfuge unter jeder Tafel
        lw = max(1, int(round(2.2 * k)))
        d.line([P(a, y + t), P(b, y + t), P(b + dx, y - depth + t)], fill=hexc('#3E556B'), width=lw)
        # Kante oben hell
        d.line([P(a, y), P(b, y)], fill=(255, 255, 255, 255), width=max(1, int(round(1.6 * k))))

    if not simple:
        # gebürstete Lichtreflexe auf der obersten Tafel
        ox, y = sheets[-1]
        a, b = x0 + ox, x1 + ox
        ov = Image.new('RGBA', (W, W), (0, 0, 0, 0))
        od = ImageDraw.Draw(ov)
        for f, alpha in ((0.30, 120), (0.42, 70), (0.70, 90)):
            xa = a + (b - a) * f
            od.line([P(xa, y - 3), P(xa + dx - 8, y - depth + 4)], fill=(150, 175, 200, alpha), width=max(1, int(round(2 * k))))
        img = Image.alpha_composite(img, ov)

    return img.resize((px, px), Image.LANCZOS)

def bmp_entry(im):
    """ICO-Eintrag im klassischen BMP-Format (32 Bit mit Alphakanal, von Windows und Mono lesbar)."""
    import struct
    w, h = im.size
    px = im.convert('RGBA').load()
    rows = []
    for y in range(h - 1, -1, -1):                       # BMP: von unten nach oben
        rows.append(b''.join(struct.pack('BBBB', *(lambda p: (p[2], p[1], p[0], p[3]))(px[x, y])) for x in range(w)))
    stride = ((w + 31) // 32) * 4                         # AND-Maske, 1 Bit pro Pixel
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(stride)
        for x in range(w):
            if px[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row
    header = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, len(b''.join(rows)) + len(mask), 0, 0, 0, 0)
    return header + b''.join(rows) + bytes(mask)

def png_entry(im):
    import io
    b = io.BytesIO()
    im.save(b, 'PNG')
    return b.getvalue()

def write_ico(path, images, png_from=256):
    """Mehrere Größen in eine .ico schreiben; ab png_from als PNG (Windows Vista+), darunter als BMP."""
    import struct
    datas = [png_entry(im) if im.size[0] >= png_from else bmp_entry(im) for im in images]
    out = struct.pack('<HHH', 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for im, d in zip(images, datas):
        w, h = im.size
        out += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(d), offset)
        offset += len(d)
    with open(path, 'wb') as f:
        f.write(out + b''.join(datas))

if __name__ == '__main__':
    out = sys.argv[1]
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [render(s, simple=(s <= 32)) for s in sizes]
    # Programmsymbol der .exe (Explorer, Taskleiste, Startmenü)
    write_ico(out + '/blechbestand.ico', imgs, png_from=256)
    # Fenstersymbol: nur BMP-Einträge, damit es auch System.Drawing.Icon überall lesen kann
    write_ico(out + '/fenster.ico', [im for s, im in zip(sizes, imgs) if s <= 64], png_from=10000)
    render(256).save(out + '/logo-256.png')
    render(512).save(out + '/logo-512.png')
    if len(sys.argv) > 2:
        # Vorschau aller Größen nebeneinander (hell und dunkel)
        pv = Image.new('RGBA', (sum(sizes) + 12 * len(sizes) + 12, 2 * 256 + 36), (255, 255, 255, 255))
        x = 12
        for s, im in zip(sizes, imgs):
            pv.paste(im, (x, 256 - s + 6), im)
            dark = Image.new('RGBA', (s, s), (32, 32, 32, 255))
            dark.alpha_composite(im)
            pv.paste(dark, (x, 2 * 256 + 24 - s))
            x += s + 12
        pv.save(sys.argv[2])
