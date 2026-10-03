"""SVG 글리프를 둥근 사각 바탕 위에 올려 ICO/미리보기로 만든다. 사용: render.py <bg hex> <fg hex> <out.ico> <svg> [--preview out.png]"""
import sys, io, skia
from PIL import Image

def render(svg_path, bg, fg, size, pad=0.17, radius=0.22):
    surface = skia.Surface(size, size)
    c = surface.getCanvas(); c.clear(skia.Color4f(0, 0, 0, 0))
    paint = skia.Paint(AntiAlias=True, Color=skia.Color(*bg))
    r = size * radius
    c.drawRRect(skia.RRect.MakeRectXY(skia.Rect.MakeWH(size, size), r, r), paint)
    data = open(svg_path, 'rb').read().decode()
    # 글리프 색을 바꾼다 — stroke/fill 의 currentColor 를 fg 로
    data = data.replace('currentColor', '#%02x%02x%02x' % fg)
    stream = skia.MemoryStream(data.encode(), True)
    dom = skia.SVGDOM.MakeFromStream(stream)
    inner = size * (1 - 2 * pad)
    vb = dom.containerSize()
    if vb.width() == 0:
        dom.setContainerSize(skia.Size(24, 24)); vb = dom.containerSize()
    scale = inner / max(vb.width(), vb.height())
    c.save(); c.translate(size * pad, size * pad); c.scale(scale, scale); dom.render(c); c.restore()
    img = surface.makeImageSnapshot()
    return Image.open(io.BytesIO(img.encodeToData().bytes())).convert('RGBA')

def hexrgb(h): h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))

if __name__ == '__main__':
    bg, fg, out, svg = hexrgb(sys.argv[1]), hexrgb(sys.argv[2]), sys.argv[3], sys.argv[4]
    big = render(svg, bg, fg, 256)
    big.save(out, format='ICO', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    if '--preview' in sys.argv:
        p = sys.argv[sys.argv.index('--preview') + 1]
        strip = Image.new('RGBA', (16 + 32 + 48 + 64 + 40, 72), (32, 32, 32, 255)); x = 8
        for s in (16, 32, 48, 64):
            strip.paste(render(svg, bg, fg, s), (x, (72 - s) // 2)); x += s + 8
        strip.save(p)
