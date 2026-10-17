"""Compare saved Unity YAML by file ID, allowing only explicitly named subtrees.

Read-only: no Unity assets are created or changed. Scene block ordering is immaterial.
Used for the reviewed env_046 reconciliation and the art_006 scene scope check.
"""
import argparse
import hashlib
import re
import subprocess
from pathlib import Path


def blocks(raw):
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    parts = re.split(r"(?m)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n", text)
    result = {}
    for i in range(1, len(parts), 3):
        key = int(parts[i + 1])
        if key in result:
            raise ValueError(f"Duplicate file ID {key}")
        result[key] = (int(parts[i]), parts[i + 2].rstrip())
    return result


def reference(body, field):
    match = re.search(rf"(?m)^\s*{field}: \{{fileID: (-?\d+)(?:,|\}})", body)
    return int(match[1]) if match else 0


def subtree_ids(items, roots):
    transforms = {key: value for key, value in items.items() if value[0] in (4, 224)}
    go_transform = {reference(body, "m_GameObject"): key for key, (_, body) in transforms.items() if reference(body, "m_GameObject")}
    named = {}
    for key, (kind, body) in items.items():
        if kind == 1:
            match = re.search(r"(?m)^  m_Name: (.*)$", body)
            if match and key in go_transform:
                named.setdefault(match[1], []).append(go_transform[key])
    names = {value: name for name, values in named.items() for value in values}
    def full_path(key):
        path, visited = [], set()
        while key and key not in visited:
            visited.add(key)
            path.append(names.get(key, f"#{key}"))
            key = reference(transforms.get(key, (0, ""))[1], "m_Father")
        return "/".join(reversed(path))

    selected = set()
    for name in roots:
        matches = [key for key in transforms if full_path(key) == name or full_path(key).endswith("/" + name)]
        if len(matches) != 1:
            raise ValueError(f"Missing/ambiguous subtree {name}: {matches}")
        selected.add(matches[0])
    selected_prefabs = set()
    changed = True
    while changed:
        old_size = len(selected) + len(selected_prefabs)
        for key, (_, body) in transforms.items():
            if reference(body, "m_Father") in selected or reference(body, "m_PrefabInstance") in selected_prefabs:
                selected.add(key)
        for key, (kind, body) in items.items():
            if kind == 1001 and reference(body, "m_TransformParent") in selected:
                selected_prefabs.add(key)
        changed = old_size != len(selected) + len(selected_prefabs)
    selected_go = {reference(transforms[key][1], "m_GameObject") for key in selected} - {0}
    return selected | selected_go | selected_prefabs | {
        key for key, (_, body) in items.items()
        if reference(body, "m_GameObject") in selected_go or reference(body, "m_PrefabInstance") in selected_prefabs
    }


def audit(before, after, roots):
    a, b = blocks(before), blocks(after)
    allowed_a, allowed_b = subtree_ids(a, roots), subtree_ids(b, roots)
    removed, added = a.keys() - b.keys(), b.keys() - a.keys()
    changed = {key for key in a.keys() & b.keys() if a[key] != b[key]}
    outside = (removed - allowed_a) | (added - allowed_b) | (changed - (allowed_a & allowed_b))
    if outside:
        raise ValueError(f"Changed blocks outside {roots}: {sorted(outside)[:25]}")
    return (
        "PASS: Unity YAML ownership audit\n"
        f"Before SHA-256: {hashlib.sha256(before).hexdigest()}\n"
        f"After SHA-256: {hashlib.sha256(after).hexdigest()}\n"
        f"Allowed subtrees: {', '.join(roots)}\n"
        f"Blocks: {len(removed)} removed, {len(added)} added, {len(changed)} changed; zero outside changes.\n"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before-revision", required=True)
    parser.add_argument("--scene", default="Assets/FPS/Scenes/MainScene.unity")
    parser.add_argument("--root", action="append", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    before = subprocess.check_output(["git", "show", f"{args.before_revision}:{args.scene}"])
    after = Path(args.scene).read_bytes()
    report = audit(before, after, args.root)
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text(report, encoding="utf-8")
    print(report, end="")


if __name__ == "__main__":
    main()
