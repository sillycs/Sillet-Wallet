# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Raton team (raton.fun)
# Packs the extension source (what Firefox reviewers ask for). Usage: python scripts/pack-source.py <out.zip>
import os, sys, zipfile

here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
skip_dirs = {"node_modules", "dist", ".git"}
skip_files = {"chains.build.json", "coins.build.json"}
out = os.path.abspath(sys.argv[1])
os.makedirs(os.path.dirname(out), exist_ok=True)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(here):
        dirs[:] = sorted(d for d in dirs if d not in skip_dirs)
        for f in sorted(files):
            if f in skip_files:
                continue
            full = os.path.join(root, f)
            z.write(full, os.path.relpath(full, here).replace(os.sep, "/"))
    z.write(os.path.join(os.path.dirname(here), "LICENSE"), "LICENSE")
print("wrote", out)
