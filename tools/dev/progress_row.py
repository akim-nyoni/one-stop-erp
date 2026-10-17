"""Inserts a row into docs/PROGRESS.md after an existing row.

Usage: python -I tools/dev/progress_row.py <rowfile>
Line 1 of the row file is the start of the row the new one goes after (e.g. "| **DESKTOP-FIELD-HIDING**"); line 2 is the new row.
Keeps the file's line endings. Never use the em-dash character in a row; use a plain hyphen.
"""
import os
import sys

os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
after, row = open(sys.argv[1], encoding="utf-8").read().split("\n", 1)
row = row.strip()
path = "docs/PROGRESS.md"
text = open(path, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in text else "\n"
lines = text.split(nl)
index = next(i for i, line in enumerate(lines) if line.startswith(after))
lines.insert(index + 1, row)
open(path, "w", encoding="utf-8", newline="").write(nl.join(lines))
print("ok")
