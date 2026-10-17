"""Read-only proof that a Unity scene retains both parents' disjoint object edits."""
import argparse
import hashlib
import re
import subprocess
from pathlib import Path


def blocks(raw):
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    parts = re.split(r"(?m)(^--- !u!\d+ &-?\d+[^\n]*\n)", text)
    result = {}
    for i in range(1, len(parts), 2):
        key = int(re.search(r"&(-?\d+)", parts[i])[1])
        if key in result:
            raise ValueError(f"Duplicate Unity file ID {key}")
        result[key] = parts[i] + parts[i + 1]
    return parts[0], result


def missing(items):
    return {
        int(match[1])
        for body in items.values()
        for match in re.finditer(r"\{fileID: (-?\d+)\}", body)
        if int(match[1]) != 0 and int(match[1]) not in items
    }


def audit(base_raw, ours_raw, theirs_raw, merged_raw):
    versions = [blocks(raw) for raw in (base_raw, ours_raw, theirs_raw, merged_raw)]
    if any(header != versions[0][0] for header, _ in versions):
        raise ValueError("Scene format headers changed")
    base, ours, theirs, merged = [items for _, items in versions]
    expected = {}
    for key in base.keys() | ours.keys() | theirs.keys():
        b, o, t = base.get(key), ours.get(key), theirs.get(key)
        if o == t:
            value = o
        elif o == b:
            value = t
        elif t == b:
            value = o
        else:
            raise ValueError(f"Conflicting object edits require manual review: {key}")
        if value is not None:
            expected[key] = value
    if merged != expected:
        changed = [key for key in merged.keys() | expected.keys() if merged.get(key) != expected.get(key)]
        raise ValueError(f"Merged scene fails exact parent preservation: {changed[:20]}")
    if re.search(rb"(?m)^(<<<<<<<|=======|>>>>>>>)", merged_raw):
        raise ValueError("Unresolved conflict markers")
    dangling = missing(merged) - (missing(base) | missing(ours) | missing(theirs))
    if dangling:
        raise ValueError(f"New dangling scene references: {sorted(dangling)}")
    lines = ["PASS: exact disjoint Unity YAML merge; every changed object and deletion retained from both parents."]
    for name, raw, items in zip(("base", "ours", "theirs", "merged"),
                                (base_raw, ours_raw, theirs_raw, merged_raw), (base, ours, theirs, merged)):
        lines.append(f"{name}: SHA-256 {hashlib.sha256(raw).hexdigest()}, {len(items)} blocks")
    lines.append("Zero colliding edits, duplicate IDs, conflict markers or new dangling local references.")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-revision", required=True)
    parser.add_argument("--ours-revision", required=True)
    parser.add_argument("--theirs-revision", required=True)
    parser.add_argument("--scene", default="Assets/FPS/Scenes/MainScene.unity")
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    revisions = [args.base_revision, args.ours_revision, args.theirs_revision]
    raw = [subprocess.check_output(["git", "show", f"{rev}:{args.scene}"]) for rev in revisions]
    report = audit(*raw, Path(args.scene).read_bytes())
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text(report, encoding="utf-8", newline="\n")
    print(report, end="")


if __name__ == "__main__":
    main()
