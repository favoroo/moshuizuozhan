#!/usr/bin/env python3
"""Parse NeonSkatepark.unity and report narrow walkable gaps between box colliders.

Unity YAML is a multi-document stream; we only need GameObject / Transform /
BoxCollider / (optionally) MeshCollider records.  Everything is a Y rotation in
practice, so footprints are 2D OBBs (rotated rectangles) and heights are simple
intervals.  A pair is a "trap" when the 2D clearance is positive but smaller
than the player capsule diameter (0.9 m): it reads as an opening yet cannot be
walked through.
"""
import json
import math
import re
import sys
from pathlib import Path

SCENE = Path(__file__).resolve().parent.parent / "Assets/_Project/Scenes/NeonSkatepark.unity"
OUT = Path(__file__).resolve().parent.parent / "Artifacts/Skatepark/gaps.json"

PLAYER_RADIUS = 0.45
PLAYER_HEIGHT = 1.25
STEP_OFFSET = 0.3

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)(.*)$")
FIELD_RE = re.compile(r"^  (m_\w+|minAlpha|fadeSpeed):\s*(.*)$")
VEC_RE = re.compile(r"\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+)\}")
QUAT_RE = re.compile(r"\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+),\s*w:\s*([-\d.eE+]+)\}")


def parse_scene(path):
    docs = {}
    cur_id = None
    cur_lines = []
    with path.open("r", encoding="utf-8", errors="ignore") as fh:
        for line in fh:
            m = DOC_RE.match(line)
            if m:
                if cur_id is not None:
                    docs[cur_id] = (cur_class, cur_lines)
                cur_class = int(m.group(1))
                cur_id = int(m.group(2))
                cur_lines = []
            elif cur_id is not None:
                cur_lines.append(line)
    if cur_id is not None:
        docs[cur_id] = (cur_class, cur_lines)
    return docs


def get_fields(lines, wanted):
    """Return the first block's fields indented by two spaces."""
    out = {}
    for line in lines:
        m = FIELD_RE.match(line)
        if m and m.group(1) in wanted:
            out[m.group(1)] = m.group(2).strip()
    return out


def vec(text):
    m = VEC_RE.search(text or "")
    return tuple(float(v) for v in m.groups()) if m else None


def quat(text):
    m = QUAT_RE.search(text or "")
    return tuple(float(v) for v in m.groups()) if m else (0.0, 0.0, 0.0, 1.0)


def quat_y(quat):
    """Yaw (radians) from a Unity quaternion, assuming pure-Y rotation."""
    x, y, z, w = quat
    return math.atan2(2.0 * (w * y + x * z), 1.0 - 2.0 * (y * y + z * z))


def rot2(v, ang):
    c, s = math.cos(ang), math.sin(ang)
    # Unity: world = R * local with x' = x cos + z sin, z' = -x sin + z cos
    return (v[0] * c + v[2] * s, -v[0] * s + v[2] * c)


def main():
    docs = parse_scene(SCENE)
    go_name = {}
    transforms = {}
    colliders = []  # (go_id, kind, size, center, is_trigger)

    for doc_id, (cls, lines) in docs.items():
        if cls == 1:  # GameObject
            fields = get_fields(lines, {"m_Name", "m_IsActive"})
            go_name[doc_id] = fields.get("m_Name", "")
        elif cls == 4:  # Transform
            fields = get_fields(lines, {
                "m_GameObject", "m_LocalRotation", "m_LocalPosition",
                "m_LocalScale", "m_Father",
            })
            go = int(re.search(r"fileID:\s*(\d+)", fields.get("m_GameObject", "")).group(1))
            father = int(re.search(r"fileID:\s*(\d+)", fields.get("m_Father", "0")).group(1)) \
                if "m_Father" in fields else 0
            transforms[doc_id] = {
                "go": go,
                "rot": quat(fields.get("m_LocalRotation", "")),
                "pos": vec(fields.get("m_LocalPosition", "")),
                "scale": vec(fields.get("m_LocalScale", "")),
                "father": father,
            }
        elif cls in (65, 64, 136, 135):  # Box, Mesh, Capsule, Sphere
            fields = get_fields(lines, {
                "m_GameObject", "m_Size", "m_Center", "m_Enabled",
                "m_IsTrigger", "m_Radius", "m_Height",
            })
            go = int(re.search(r"fileID:\s*(\d+)", fields.get("m_GameObject", "")).group(1))
            colliders.append((doc_id, go, cls, fields))

    go_transform = {}
    for tid, t in transforms.items():
        go_transform[t["go"]] = tid

    def world_xf(tid):
        t = transforms[tid]
        pos = t["pos"]
        ang = quat_y(t["rot"])
        scale = t["scale"]
        father = t["father"]
        if father and father in transforms:
            fpos, fang, fscale = world_xf(father)
            pos = (pos[0] * fscale[0], pos[1] * fscale[1], pos[2] * fscale[2])
            pos = (fpos[0] + pos[0] * math.cos(fang) + pos[2] * math.sin(fang),
                   fpos[1] + pos[1],
                   fpos[2] - pos[0] * math.sin(fang) + pos[2] * math.cos(fang))
            ang += fang
            scale = (scale[0] * fscale[0], scale[1] * fscale[1], scale[2] * fscale[2])
        return pos, ang, scale

    out = []
    for doc_id, go, cls, fields in colliders:
        if fields.get("m_Enabled") == "0":
            continue
        if fields.get("m_IsTrigger") == "1":
            continue
        tid = go_transform.get(go)
        if tid is None:
            continue
        pos, ang, scale = world_xf(tid)
        name = go_name.get(go, "")

        if cls == 65:
            size = vec(fields.get("m_Size", "")) or (1, 1, 1)
            center = vec(fields.get("m_Center", "")) or (0, 0, 0)
        elif cls == 135:
            r = float(fields.get("m_Radius", "0.5"))
            h = float(fields.get("m_Height", "2"))
            size = (r * 2, h, r * 2)
            center = (0, 0, 0)
        elif cls == 64:
            # MeshCollider: no size of its own, assume unit bounds is wrong; skip meshes
            continue
        else:
            r = float(fields.get("m_Radius", "0.5"))
            size = (r * 2, r * 2, r * 2)
            center = (0, 0, 0)

        # local center -> world (yaw only)
        cw = rot2((center[0] * scale[0], center[1] * scale[1], center[2] * scale[2]), ang)
        cx, cz = pos[0] + cw[0], pos[2] + cw[1]
        hx = abs(size[0] * scale[0]) * 0.5
        hy = abs(size[1] * scale[1]) * 0.5
        hz = abs(size[2] * scale[2]) * 0.5
        cy = pos[1] + center[1] * scale[1]
        out.append({
            "name": name,
            "kind": {65: "box", 135: "capsule", 136: "sphere"}[cls],
            "x": round(cx, 4), "z": round(cz, 4), "y": round(cy, 4),
            "hx": round(hx, 4), "hz": round(hz, 4),
            "y0": round(cy - hy, 4), "y1": round(cy + hy, 4),
            "yaw": round(math.degrees(ang) % 360.0, 3),
        })

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, indent=1, ensure_ascii=False))
    print(f"colliders: {len(out)} -> {OUT}")


if __name__ == "__main__":
    main()
