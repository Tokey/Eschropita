"""Cuts Assets/Sprites/Daffodil.png into puppet layers for DaffodilPuppet.cs.

Usage: python3 Tools/Daffodil/cut_rig.py Assets/Sprites/Daffodil.png Assets/Overhaul/Resources/Overhaul/Daffodil
(needs numpy, pillow, opencv-python-headless, scipy). Writes the DaffodilRig.png atlas and DaffodilRigLayout.json (part
rects, pivots, positions, sorting) into the output folder, plus check images into the working directory.
Tools/Daffodil/preview_rig.py <output folder> renders the puppet's poses from them. Hidden areas behind moving parts are filled in so a part can move a
little without opening holes.
"""
import json
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

SRC = sys.argv[1]
OUT = sys.argv[2]

im = Image.open(SRC).convert("RGBA")
A = np.array(im).astype(np.int32)
H, W = A.shape[:2]
R, G, B, AL = A[..., 0], A[..., 1], A[..., 2], A[..., 3]
yy, xx = np.mgrid[0:H, 0:W]
solid = AL > 4


def ellipse(cx, cy, a, b):
    return ((xx - cx) / a) ** 2 + ((yy - cy) / b) ** 2 <= 1.0


def poly(pts):
    m = Image.new("L", (W, H), 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return np.array(m) > 0


def dilate(m, r):
    if r <= 0:
        return m
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
    return cv2.dilate(m.astype(np.uint8), k) > 0


def drop_specks(m, min_px):
    lab, n = ndimage.label(m)
    if n == 0:
        return m
    sizes = ndimage.sum(m, lab, range(1, n + 1))
    keep = np.zeros(n + 1, bool)
    keep[1:] = sizes >= min_px
    return keep[lab]


warm = (R - B) > 22                          # orange, yellow, pink, brown lines
dark = (R + G + B) < 300
lum = (R + G + B) / 3

# ---------------------------------------------------------------- regions
head_ell = ellipse(247, 366, 133, 104)
bolts = ellipse(151, 305, 18, 18) | ellipse(335.5, 298, 18, 18)
ring_col = ((R - B) > 60) & (yy >= 640)
sparkle = (yy >= 618) & (np.minimum(np.minimum(R, G), B) > 185) & solid & ~(ellipse(233, 565, 92, 86))
sparkle = drop_specks(sparkle | (dilate(sparkle, 1) & solid & (lum > 150) & (yy >= 618) & ~ring_col), 1)
rings = solid & (yy >= 645) & ~sparkle & (ring_col | (yy >= 652))

earL = solid & (yy < 330) & (xx < 205) & ~head_ell & ~bolts
earR = solid & (yy < 330) & (xx > 290) & ~head_ell & ~bolts

polyL = [(84, 506), (100, 492), (125, 487), (147, 477), (170, 474), (186, 476), (195, 481), (194, 496), (188, 514),
         (176, 529), (162, 541), (152, 559), (140, 574), (133, 582), (133, 596), (124, 608), (80, 610), (40, 596),
         (36, 530), (55, 508)]
polyR = [(299, 481), (330, 486), (350, 492), (362, 500), (372, 505), (386, 515), (395, 530), (397, 546), (390, 562),
         (381, 577), (366, 592), (345, 597), (329, 590), (321, 576), (316, 560), (313, 545), (311, 530), (305, 505)]
torso_core = ellipse(233, 565, 86, 81)
def arm_mask(pts):
    core = poly(pts)
    near = dilate(core, 9) & ~core
    apron = (R - G) > 45
    stray = near & (yy >= 480) & ~apron & (~torso_core | (lum < 125) | (warm & (yy < 526)))
    return solid & (core | stray) & (yy >= 462)
armL = arm_mask(polyL)
armR = arm_mask(polyR)

SPLIT = 458
body = solid & (yy >= SPLIT) & ~rings & ~sparkle & ~armL & ~armR
head = solid & (yy < SPLIT) & ~earL & ~earR

eye_c = [(183, 392), (315.5, 387)]
eyes = []
for (cx, cy) in eye_c:
    zone = ellipse(cx, cy, 52, 52) & head
    m = zone & (warm | (dark & (R >= B)))
    m = ndimage.binary_fill_holes(dilate(m, 1)) & zone
    m = drop_specks(m, 40)
    eyes.append(dilate(m, 1) & head)
nose_zone = ellipse(250, 398, 20, 15) & head
nose = ndimage.binary_fill_holes(dilate(nose_zone & (warm | dark), 1)) & nose_zone
nose = dilate(drop_specks(nose, 20), 1) & head

stemmy = ((G - B) > 6) & ((G - R) > 4)
box = (xx >= 188) & (xx <= 322) & (yy >= 268) & (yy <= 410)
flower = box & head & (warm | stemmy) & ~eyes[0] & ~eyes[1] & ~dilate(nose, 2)
flower = drop_specks(flower, 12)
flower = dilate(flower, 1) & head & ~eyes[0] & ~eyes[1] & ~nose

# ---------------------------------------------------------------- layers
def layer(mask):
    out = np.zeros_like(A)
    out[mask] = A[mask]
    return out


def nn_fill(img, known, region):
    """Copies the nearest known pixel into region (keeps colours exact; fine for hidden overlaps)."""
    _, (iy, ix) = ndimage.distance_transform_edt(~known, return_indices=True)
    out = img.copy()
    out[region] = img[iy[region], ix[region]]
    return out


def telea(img, known_mask, hole, radius=5):
    """Paints over a hole from its surroundings (colour and alpha separately)."""
    rgb = img[..., :3].astype(np.uint8)
    al = img[..., 3].astype(np.uint8)
    m = hole.astype(np.uint8) * 255
    rgb2 = cv2.inpaint(rgb, m, radius, cv2.INPAINT_TELEA)
    al2 = cv2.inpaint(al, m, radius, cv2.INPAINT_TELEA)
    out = img.copy()
    out[hole, :3] = rgb2[hole]
    out[hole, 3] = al2[hole]
    return out


parts = {}

# head: eyes, nose and flower painted over; grows a little under the collar
L_head = layer(head)
hole = dilate(eyes[0] | eyes[1] | nose | flower, 2) & head
L_head = telea(L_head, head & ~hole, hole, 6)
under = head_ell & (yy >= SPLIT) & (yy <= 476)
L_head = nn_fill(L_head, head & (yy < SPLIT), under)
L_head[under, 3] = 255
parts["head"] = L_head

# ears: reach a few pixels in under the dome rim so a twitch never opens a gap
for name, m in (("earL", earL), ("earR", earR)):
    L = layer(m)
    ext = dilate(m, 5) & head_ell & ~m
    L = nn_fill(L, m, ext)
    parts[name] = L

# body: the torso continues under the shoulder pads
L_body = layer(body)
torso = ellipse(233, 565, 88, 83) & (yy >= 476)
greyish = (np.abs(R - G) < 18) & (np.abs(G - B) < 28) & (lum > 60) & (lum < 185)
hidden = (torso & (armL | armR) & ~body) | (body & dilate(armL | armR, 6) & greyish & (yy < 540))   # sleeve leftovers
clean = body & torso & (lum > 170) & (B >= R)          # plain torso paint, no stray sleeve or apron edge
L_body = nn_fill(L_body, clean, hidden)
L_body[hidden, 3] = 255
parts["body"] = L_body

parts["armL"] = layer(armL)
parts["armR"] = layer(armR)
parts["eyeL"] = layer(eyes[0])
parts["eyeR"] = layer(eyes[1])
parts["nose"] = layer(nose)
parts["flower"] = layer(flower)
parts["sparkles"] = layer(sparkle)

bands = [("ring1", 645, 694), ("ring2", 694, 719), ("ring3", 719, 744), ("ring4", 744, 761), ("ring5", 761, 790)]
for name, y0, y1 in bands:
    parts[name] = layer(rings & (yy >= y0) & (yy < y1))


# closed eyes: a soft curved lash line in the eyes' outline brown, drawn at 4x and brought down
def closed_eye(cx, cy, width, tilt):
    S = 4
    w, h = 140, 60
    img = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = []
    for i in range(41):
        t = i / 40.0
        x = (t - 0.5) * width
        y = 10 * (1 - (2 * t - 1) ** 2)            # a gentle smile-shaped curve: closed and content
        y += tilt * (t - 0.5)
        pts.append(((w / 2 + x) * S, (h / 2 - 4 + y) * S))
    for k in range(len(pts) - 1):
        t = k / (len(pts) - 1)
        r = (2.2 + 2.0 * np.sin(np.pi * t)) * S
        d.line([pts[k], pts[k + 1]], fill=(92, 42, 22, 255), width=int(r * 2))
        d.ellipse([pts[k][0] - r, pts[k][1] - r, pts[k][0] + r, pts[k][1] + r], fill=(92, 42, 22, 255))
    img = img.resize((w, h), Image.LANCZOS)
    arr = np.array(img).astype(np.int32)
    rng = np.random.default_rng(7)
    grain = rng.normal(0, 14, arr.shape[:2])
    for c in range(3):
        arr[..., c] = np.clip(arr[..., c] + grain, 0, 255)
    full = np.zeros_like(A)
    x0, y0 = int(round(cx - w / 2)), int(round(cy - h / 2))
    full[y0:y0 + h, x0:x0 + w] = arr
    return full


parts["eyeClosedL"] = closed_eye(183, 398, 66, 3)
parts["eyeClosedR"] = closed_eye(315.5, 393, 66, -3)


def soft_circle(n, color):
    yy2, xx2 = np.mgrid[0:n, 0:n]
    r = np.sqrt((xx2 - (n - 1) / 2) ** 2 + (yy2 - (n - 1) / 2) ** 2) / ((n - 1) / 2)
    a = np.clip(1 - r, 0, 1) ** 2
    out = np.zeros((n, n, 4), np.int32)
    out[..., 0], out[..., 1], out[..., 2] = color
    out[..., 3] = (a * 255).astype(np.int32)
    return out


# ---------------------------------------------------------------- layout
ROOT = (247.0, 650.0)                    # bottom of the body, centre: the puppet's origin
PIVOTS = {
    "earL": (162, 298), "earR": (333, 290), "head": (247, 462), "flower": (252, 394), "eyeL": (183, 392),
    "eyeR": (315.5, 387), "eyeClosedL": (183, 398), "eyeClosedR": (315.5, 393), "nose": (250, 398),
    "body": (247, 560), "armL": (182, 486), "armR": (306, 488), "sparkles": (247, 700),
}
ORDER = {"ring5": 0, "ring4": 1, "ring3": 2, "ring2": 3, "ring1": 4, "sparkles": 5, "earL": 10, "earR": 10,
         "head": 20, "flower": 22, "eyeL": 24, "eyeR": 24, "eyeClosedL": 25, "eyeClosedR": 25, "nose": 26,
         "body": 30, "armL": 32, "armR": 32}

pieces = []
for name, img in parts.items():
    m = img[..., 3] > 0
    ys, xs = np.where(m)
    x0, x1, y0, y1 = xs.min() - 5, xs.max() + 6, ys.min() - 5, ys.max() + 6
    crop = img[y0:y1, x0:x1].astype(np.uint8)
    if name in PIVOTS:
        px, py = PIVOTS[name]
    else:
        px, py = (xs.min() + xs.max()) / 2.0, (ys.min() + ys.max()) / 2.0
    pieces.append(dict(name=name, crop=crop, x0=x0, y0=y0, px=px, py=py))
glow = soft_circle(96, (255, 255, 255))
pieces.append(dict(name="glow", crop=glow.astype(np.uint8), x0=0, y0=0, px=47.5, py=47.5, free=True))

# shelf-pack into a square atlas
pieces.sort(key=lambda p: -p["crop"].shape[0])
SIZE_W, SIZE = 1024, 512                  # SIZE: atlas height (Unity rects count from the bottom)
atlas = np.zeros((SIZE, SIZE_W, 4), np.uint8)
x = y = shelf = 0
PAD = 10                                  # room between parts so mipmaps never blend neighbours
for p in pieces:
    h, w = p["crop"].shape[:2]
    if x + w + PAD > SIZE_W:
        x, y, shelf = 0, y + shelf + PAD, 0
    if y + h > SIZE:
        raise SystemExit("atlas full")
    atlas[y:y + h, x:x + w] = p["crop"]
    p["ax"], p["ay"] = x, y
    x += w + PAD
    shelf = max(shelf, h)
used = y + shelf
print("atlas rows used:", used)

# straight alpha with colour bled into transparent texels, so filtering never pulls in black fringes
alpha = atlas[..., 3] > 0
if alpha.any():
    _, (iy, ix) = ndimage.distance_transform_edt(~alpha, return_indices=True)
    bled = atlas[iy, ix].copy()
    bled[..., 3] = atlas[..., 3]
    atlas = np.where(alpha[..., None], atlas, bled)
    atlas[~alpha, 3] = 0

Image.fromarray(atlas).save(f"{OUT}/DaffodilRig.png", optimize=True)

layout = {"pixelsPerUnit": 440.0, "parts": []}
for p in pieces:
    h, w = p["crop"].shape[:2]
    # Unity rects start bottom-left
    rect = [int(p["ax"]), int(SIZE - p["ay"] - h), int(w), int(h)]
    pvx = (p["px"] - p["x0"]) / w
    pvy = 1.0 - (p["py"] - p["y0"]) / h
    pos = [0.0, 0.0] if p.get("free") else [round(p["px"] - ROOT[0], 2), round(ROOT[1] - p["py"], 2)]
    layout["parts"].append(dict(name=p["name"], rect=rect, pivot=[round(pvx, 4), round(pvy, 4)], pos=pos,
                                order=ORDER.get(p["name"], 50)))
layout["parts"].sort(key=lambda q: q["order"])
with open(f"{OUT}/DaffodilRigLayout.json", "w") as f:
    json.dump(layout, f, indent=1)

# ---------------------------------------------------------------- checks
def comp(names, bg=(40, 40, 60)):
    canvas = Image.new("RGBA", (W, H), bg + (255,))
    for q in sorted(names, key=lambda n: ORDER.get(n, 50)):
        canvas.alpha_composite(Image.fromarray(parts[q].astype(np.uint8)))
    return canvas


normal = [n for n in parts if not n.startswith("eyeClosed")]
orig = Image.new("RGBA", (W, H), (40, 40, 60, 255))
orig.alpha_composite(im)
c = comp(normal)
diff = np.abs(np.array(c).astype(int) - np.array(orig).astype(int))[..., :3].max(axis=2)
print("reassembly: pixels off by >24:", int((diff > 24).sum()), "of", int(solid.sum()))
sheet = Image.new("RGBA", (W * 3, H), (0, 0, 0, 255))
sheet.paste(orig, (0, 0))
sheet.paste(c, (W, 0))
sleepy = comp([n for n in parts if n not in ("eyeL", "eyeR")])
sheet.paste(sleepy, (2 * W, 0))
sheet.save("check.png")
heads = comp(["head", "earL", "earR", "body"])
heads.save("check_holes.png")
comp(["body"]).crop((20, 440, 420, 660)).resize((800, 440), Image.NEAREST).save("check_body.png")
arms = comp(["armL", "armR"]).crop((20, 440, 420, 660)).resize((800, 440), Image.NEAREST).save("check_arms.png")
Image.fromarray(atlas).save("atlas_preview.png")
