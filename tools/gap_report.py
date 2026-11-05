#!/usr/bin/env python3
"""Report narrow walkable gaps (0 < clearance < player diameter) between box colliders."""
import json
import math
import sys
from itertools import combinations
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Artifacts/Skatepark/gaps.json"
OUT = ROOT / "Artifacts/Skatepark/gap_report.txt"

PLAYER_D = 0.9
MAX_GAP = 1.25
MIN_BLOCK_TOP = 0.35   # above step offset (0.3) => actually blocks
MAX_BLOCK_BOTTOM = 1.20


def corners(c):
    a = math.radians(c["yaw"])
    ca, sa = math.cos(a), math.sin(a)
    hx, hz = c["hx"], c["hz"]
    pts = []
    for lx, lz in ((-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)):
        pts.append((c["x"] + lx * ca + lz * sa, c["z"] - lx * sa + lz * ca))
    return pts


def axes(poly):
    out = []
    n = len(poly)
    for i in range(n):
        x0, z0 = poly[i]
        x1, z1 = poly[(i + 1) % n]
        ex, ez = x1 - x0, z1 - z0
        ln = math.hypot(ex, ez)
        if ln > 1e-9:
            out.append((-ez / ln, ex / ln))
    return out


def project(poly, ax):
    vals = [p[0] * ax[0] + p[1] * ax[1] for p in poly]
    return min(vals), max(vals)


def seg_dist(p, a, b):
    px, pz = p
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    den = dx * dx + dz * dz
    t = 0.0 if den < 1e-12 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / den))
    return math.hypot(px - (ax + t * dx), pz - (az + t * dz))


def poly_gap(A, B):
    """0 when overlapping (SAT), else min vertex-edge distance."""
    for ax in axes(A) + axes(B):
        a0, a1 = project(A, ax)
        b0, b1 = project(B, ax)
        if a1 < b0 or b1 < a0:
            # separated on this axis; keep the exact distance via vertex-edge
            d = min(min(seg_dist(p, B[i], B[(i + 1) % 4]) for i in range(4)) for p in A)
            d = min(d, min(min(seg_dist(p, A[i], A[(i + 1) % 4]) for i in range(4)) for p in B))
            return d
    return 0.0


def main():
    data = json.loads(Path(sys.argv[1] if len(sys.argv) > 1 else DATA).read_text())
    blockers = [c for c in data
                if c["y1"] > MIN_BLOCK_TOP and c["y0"] < MAX_BLOCK_BOTTOM
                and not c["name"].startswith("Ground_Main")]
    polys = {i: corners(c) for i, c in enumerate(blockers)}

    rows = []
    for i, j in combinations(range(len(blockers)), 2):
        A, B = blockers[i], blockers[j]
        d = poly_gap(polys[i], polys[j])
        if 0.02 < d <= MAX_GAP:
            mx = (A["x"] + B["x"]) * 0.5
            mz = (A["z"] + B["z"]) * 0.5
            rows.append((d, A["name"], B["name"], mx, mz))

    rows.sort()
    lines = [f"blockers={len(blockers)}  pairs with 0.02 < gap <= {MAX_GAP}: {len(rows)}", ""]
    for d, na, nb, mx, mz in rows:
        flag = "TRAP" if d < PLAYER_D else "tight"
        lines.append(f"{flag:5s} {d:5.2f}m  near ({mx:6.2f},{mz:6.2f})  {na}  <->  {nb}")
    OUT.write_text("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
