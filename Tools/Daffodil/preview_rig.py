"""Renders DaffodilRig poses the way DaffodilPuppet.cs builds them (hierarchy, pivots, rotations) for checking."""
import json
import math
import sys

import numpy as np
from PIL import Image, ImageDraw

OUT = sys.argv[1]
atlas = Image.open(f"{OUT}/DaffodilRig.png").convert("RGBA")
layout = json.load(open(f"{OUT}/DaffodilRigLayout.json"))
SIZE = atlas.size[1]
parts = {p["name"]: p for p in layout["parts"]}


def sprite(p):
    x, y, w, h = p["rect"]
    return atlas.crop((x, SIZE - y - h, x + w, SIZE - y))


def mat_rot(deg):
    r = math.radians(deg)
    return np.array([[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]])


def mat_t(x, y):
    return np.array([[1, 0, x], [0, 1, y], [0, 0, 1]], float)


def mat_s(sx, sy):
    return np.array([[sx, 0, 0], [0, sy, 0], [0, 0, 1]], float)


def group_of(n):
    if n.startswith("ring") or n == "sparkles":
        return "jets"
    if n == "body" or n.startswith("arm"):
        return "body"
    return "head"


def render(pose, W=520, H=760, bg=(150, 98, 72)):
    """pose: dict with lean, head_tilt, head_dy, rot{name}, scale{name}, hide set, root_dy, alpha{name}."""
    canvas = Image.new("RGBA", (W, H), bg + (255,))
    origin = (W / 2, H - 150 - pose.get("root_dy", 0))
    groups = {"jets": (0.0, 0.0), "body": tuple(parts["body"]["pos"]), "head": tuple(parts["head"]["pos"])}
    root = mat_t(*origin) @ mat_s(1, -1) @ mat_rot(pose.get("lean", 0))       # art px, y up -> canvas
    for p in sorted(layout["parts"], key=lambda q: q["order"]):
        n = p["name"]
        if n == "glow" or n in pose.get("hide", ()):
            continue
        g = group_of(n)
        gx, gy = groups[g]
        G = mat_t(gx, gy + (pose.get("head_dy", 0) if g == "head" else 0))
        if g == "head":
            G = G @ mat_rot(pose.get("head_tilt", 0))
        rx, ry = p["pos"][0] - gx, p["pos"][1] - gy
        P = mat_t(rx, ry) @ mat_rot(pose.get("rot", {}).get(n, 0)) @ mat_s(*pose.get("scale", {}).get(n, (1, 1)))
        img = sprite(p)
        w, h = img.size
        px, py = p["pivot"][0] * w, p["pivot"][1] * h
        # sprite pixel (u,v) with v down -> local (u - px, (h - v) - py) y up
        S = mat_t(-px, -(h - py)) @ np.array([[1, 0, 0], [0, -1, 0], [0, 0, 1]], float)
        S = mat_t(0, 0) @ np.array([[1, 0, -px], [0, -1, h - py], [0, 0, 1]], float)
        M = root @ G @ P @ S
        inv = np.linalg.inv(M)
        a = pose.get("alpha", {}).get(n, 1.0)
        if a < 1:
            arr = np.array(img).astype(float)
            arr[..., 3] *= a
            img = Image.fromarray(arr.astype(np.uint8))
        layer = img.transform((W, H), Image.AFFINE, data=tuple(inv[:2].flatten()), resample=Image.BILINEAR)
        canvas.alpha_composite(layer)
    return canvas


idle = {}
moving = {"lean": -11, "rot": {"earL": 7 * 0.9 + -11 * 0.6, "earR": -7 * 0.9 + -11 * 0.6, "armL": 9, "armR": -9,
                              "flower": 8}, "head_tilt": -4}
scan = {"root_dy": 26, "rot": {"earL": -9 + 2, "earR": 9 - 2, "armL": -20, "armR": 20},
        "scale": {"earL": (1.06, 1.06), "earR": (1.06, 1.06), "eyeL": (1.08, 1.08), "eyeR": (1.08, 1.08),
                  "flower": (1.05, 1.08)}, "hide": {"eyeClosedL", "eyeClosedR"}}
low = {"rot": {"earL": 12, "earR": -12}, "scale": {"eyeL": (1, 0.55), "eyeR": (1, 0.55)},
       "alpha": {"ring2": 0.15, "ring4": 0.15}, "hide": {"eyeClosedL", "eyeClosedR"}}
sleep = {"root_dy": -140, "lean": -4, "head_tilt": -9, "head_dy": -13,
         "rot": {"earL": 28, "earR": -28, "armL": 12, "armR": -12, "flower": -10},
         "hide": {"eyeL", "eyeR", "ring1", "ring2", "ring3", "ring4", "ring5", "sparkles"}}
for d in (idle, moving, low):
    d.setdefault("hide", set()).update({"eyeClosedL", "eyeClosedR"})

frames = [render(p) for p in (idle, moving, scan, low, sleep)]
sheet = Image.new("RGBA", (520 * 5, 760))
for i, f in enumerate(frames):
    sheet.paste(f, (520 * i, 0))
d = ImageDraw.Draw(sheet)
for i, label in enumerate(["idle", "moving right", "scanning", "low charge", "asleep"]):
    d.text((520 * i + 12, 12), label, fill=(255, 255, 255, 255))
sheet.resize((sheet.width // 2, sheet.height // 2), Image.LANCZOS).save("poses.png")

# an idle loop as a GIF (blinks, bob, paddling, ear twitch, rings), same formulas as the module
gif = []
for k in range(48):
    t = k / 24.0
    bob = math.sin(t * 2.1 * 2 * math.pi / 2.1 * 0.5 * 2) * 0.05
    bob = math.sin(t * 2.1 * 3) * 0.05 + math.sin(t * 3.7 * 3 + 1) * 0.012
    ph_rings = {}
    pose = {"root_dy": bob * 440, "head_dy": -bob * 0.35 * 440 * 0.6,
            "rot": {"earL": math.sin(t * 1.7 * 3) * 3 + (14 * math.sin((t - 1.0) / 0.35 * math.pi) if 1.0 <= t < 1.35 else 0),
                    "earR": math.sin(t * 1.7 * 3) * 3,
                    "armL": math.sin(t * 2.4 * 3) * 6, "armR": -math.sin(t * 2.4 * 3 + 0.6) * 6,
                    "flower": math.sin(t * 1.3 * 3) * 4},
            "hide": {"eyeClosedL", "eyeClosedR"}, "scale": {}, "alpha": {}}
    if 0.5 <= t < 0.6:
        open_ = 0.1
        pose["scale"]["eyeL"] = (1, open_)
        pose["scale"]["eyeR"] = (1, open_)
    for i in range(1, 6):
        ph = t * 6.5 * 1.5 - i * 0.9
        pose["scale"]["ring%d" % i] = (1 + math.sin(ph + 1) * 0.08, 1)
        pose["alpha"]["ring%d" % i] = 0.8 + 0.2 * math.sin(t * 13 + i)
    pose["alpha"]["sparkles"] = 0.45 + 0.55 * abs(math.sin(t * 1.9 * 2))
    gif.append(render(pose).resize((260, 380), Image.LANCZOS).convert("P", palette=Image.ADAPTIVE))
gif[0].save("idle.gif", save_all=True, append_images=gif[1:], duration=42, loop=0)
