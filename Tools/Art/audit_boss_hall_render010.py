"""Read-only YAML/metadata audit for corebuster_render_010; local authoring tool."""
import collections
import difflib
import json
from pathlib import Path
import re
import subprocess

scene = Path('Assets/FPS/Scenes/MainScene.unity')
original = subprocess.check_output(['git', 'show', 'HEAD:' + scene.as_posix()]).decode('utf-8')
current = scene.read_text(encoding='utf-8')
pattern = re.compile(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n', re.M)
def blocks(text):
    starts = list(pattern.finditer(text))
    return {m[2]: (m[1], text[m.end(): starts[i+1].start() if i+1 < len(starts) else len(text)]) for i, m in enumerate(starts)}
a, b = blocks(original), blocks(current)
added, removed = sorted(b.keys() - a.keys()), sorted(a.keys() - b.keys())
changed = [k for k in a.keys() & b.keys() if a[k] != b[k]]
print('Added blocks:', len(added), dict(collections.Counter(b[k][0] for k in added)))
print('Removed original blocks:', len(removed))
print('Changed original blocks:', len(changed))
for key in changed[:8]:
    delta = list(difflib.unified_diff(a[key][1].splitlines(), b[key][1].splitlines(), n=1))
    print(key, a[key][0], '\n'.join(delta[:28]))
root = Path('Assets/FPS/Art/BossHall')
missing = [p.as_posix() for p in root.rglob('*') if p.is_file() and p.suffix != '.meta' and not Path(str(p)+'.meta').exists()]
metas = list(root.rglob('*.meta')) + [Path(str(root)+'.meta')]
guids = [re.search(r'^guid: (\w+)', p.read_text(encoding='utf-8'), re.M)[1] for p in metas]
duplicates = [g for g, n in collections.Counter(guids).items() if n > 1]
print('Missing asset metas:', missing, 'Duplicate owned GUIDs:', duplicates)
bindings = json.loads((root/'Rendering/SurfaceBindings.json').read_text())
print('Manifest:', bindings['sourceRenderers'], 'source renderers;', len(bindings['surfaces']), 'skins;', bindings['duplicateSubmeshes'], 'duplicates')
assert not removed and not missing and not duplicates
# Unity may reorder whole YAML blocks on save. Compare their exact contents rather than diff line totals.
assert changed == ['175026277'], 'Only the existing Rendering Transform child list may change'
def without_children(text):
    return re.sub(r'(?m)^  m_Children:(?: \[\])?\n(?:  - \{fileID: -?\d+\}\n)*', '', text)
assert without_children(a[changed[0]][1]) == without_children(b[changed[0]][1])
skins = len(bindings['surfaces'])
assert collections.Counter(b[k][0] for k in added) == {'1': skins, '4': skins, '23': skins, '33': skins}
