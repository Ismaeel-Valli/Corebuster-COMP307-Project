"""One-time minimal scene edit when Unity's license service blocks the editor.

The tracked ReactorAudioInstaller is the Unity-authored rebuild path. This
local fallback copies a serialized AudioSource from the same saved scene and
adds it to the existing reactor glow without reserializing unrelated objects.
"""
from pathlib import Path
import re

path = Path("Assets/FPS/Scenes/MainScene.unity")
raw = path.read_bytes()
newline = b"\r\n" if b"\r\n" in raw else b"\n"
text = raw.decode("utf-8").replace("\r\n", "\n")
if "&450427856" in text or "&450427857" in text:
    raise SystemExit("Reactor audio component IDs already exist; refusing to edit.")
for component in ("  - component: {fileID: 450427855}\n",):
    if text.count(component) != 1:
        raise SystemExit("Reactor glow component anchor is not unique.")
text = text.replace(
    "  - component: {fileID: 450427855}\n",
    "  - component: {fileID: 450427855}\n"
    "  - component: {fileID: 450427856}\n"
    "  - component: {fileID: 450427857}\n",
    1,
)
match = re.search(r"(?ms)^--- !u!82 &401639795\n.*?(?=^--- !u!)", text)
if not match:
    raise SystemExit("Existing Unity AudioSource template is missing.")
source = match.group().replace("&401639795", "&450427856", 1)
source = source.replace("m_GameObject: {fileID: 401639793}", "m_GameObject: {fileID: 450427851}", 1)
source = source.replace("guid: 1e9200d7ca7cae44e8483dfcdfce82d8", "guid: 1bb72fa01479480db568afad7e481f33", 1)
source = source.replace("m_Volume: 0.35", "m_Volume: 0.7", 1)
source = source.replace("MinDistance: 1.5", "MinDistance: 2", 1)
source = source.replace("MaxDistance: 10", "MaxDistance: 16", 1)
driver = """--- !u!114 &450427857
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 450427851}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 61e2ab3e84494c1d810b6617ff7fa039, type: 3}
  m_Name:
  m_EditorClassIdentifier: fps.Game::Unity.FPS.Game.ReactorInstabilityAudio
  field: {fileID: 2100000, guid: a3f0120378009174caac011dfddcfc88, type: 2}
  baseVolume: 0.7
"""
anchor = "--- !u!108 &450427855\n"
start = text.index(anchor)
end = text.index("--- !u!", start + len(anchor))
text = text[:end] + source + driver + text[end:]
path.write_bytes(text.replace("\n", newline.decode()).encode("utf-8"))
print("Attached Reactor_Unstable_Loop to Reactor_Core_Glow with an AudioSource and ReactorInstabilityAudio.")
