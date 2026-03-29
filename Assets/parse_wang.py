import urllib.request
from PIL import Image
import os

url = "https://www.boristhebrave.com/permanent/24/06/cr31/stagecast/art/atlas/blob/wangbl.png"
req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
file_path = "C:\\unityProjects\\Evolution\\Assets\\wangbl.png"

with urllib.request.urlopen(req) as response:
    with open(file_path, "wb") as out_file:
        out_file.write(response.read())

img = Image.open(file_path).convert("RGBA")
w, h = img.size
cols, rows = 7, 7
tw, th = w // cols, h // rows

def is_fg(x, y):
    r,g,b,a = img.getpixel((x, y))
    return a > 128

mappings = {}
for ty in range(rows):
    for tx in range(cols):
        cx = tx * tw
        cy = ty * th
        
        # Check if tile has any foreground
        c = is_fg(cx + tw//2, cy + th//2)
        n = is_fg(cx + tw//2, cy + 2)
        s = is_fg(cx + tw//2, cy + th - 3)
        e = is_fg(cx + tw - 3, cy + th//2)
        w_fg = is_fg(cx + 2, cy + th//2)
        
        if not c and not n and not s and not e and not w_fg:
            continue # empty
            
        ne = is_fg(cx + tw - 3, cy + 2)
        se = is_fg(cx + tw - 3, cy + th - 3)
        sw = is_fg(cx + 2, cy + th - 3)
        nw = is_fg(cx + 2, cy + 2)
        
        ortho = (1 if n else 0) | (2 if e else 0) | (4 if s else 0) | (8 if w_fg else 0)
        d1 = 1 if (ne and n and e) else 0
        d2 = 2 if (se and s and e) else 0
        d3 = 4 if (sw and s and w_fg) else 0
        d4 = 8 if (nw and n and w_fg) else 0
        diag = d1 | d2 | d3 | d4
        
        mask = ortho | (diag << 4)
        if mask not in mappings:
            mappings[mask] = (tx, ty)

print("private Vector2Int GetWangBorisPosition(int mask) {")
print("    int ortho = mask & 15;")
print("    int diag = mask >> 4;")
for mask, (tx, ty) in sorted(mappings.items()):
    ortho = mask & 15
    diag = mask >> 4
    # Note: Unity coordinate system for Y is inverted if we consider ty=0 is top!
    # If the user sets Y=0 as the top row, we just return ty directly.
    print(f"    if (ortho == {ortho} && diag == {diag}) return new Vector2Int({tx}, {ty});")
print("    return new Vector2Int(0, 0);")
print("}")
