using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Stable saved-object identities, hierarchy and serialized component state; no runtime assembly dependency.</summary>
public static class BossHallSnapshot
{
    static readonly Dictionary<EntityId, string> Identities = new Dictionary<EntityId, string>();
    static Scene cachedScene;
    static string cachedSceneSha;
    public static string Id(Object value)
    {
        if (!value) return "null";
        var entity = value.GetEntityId();
        if (Identities.TryGetValue(entity, out var known)) return known;
        var id = GlobalObjectId.GetGlobalObjectIdSlow(value);
        var result = id.targetObjectId == 0 ? Unsaved(value, entity) : id.ToString();
        Identities[entity] = result; return result;
    }
    // Unsaved hierarchy objects (for example hidden text MeshFilters regenerated on load) have session-specific entity
    // IDs. Key them by hierarchy position and component order so the protected digest is reproducible across sessions;
    // a newly created unsaved object still appears as a new key. Non-hierarchy objects keep the entity key.
    static string Unsaved(Object value, EntityId entity)
    {
        var t = value is GameObject g ? g.transform : (value as Component)?.transform;
        if (!t) return "unsaved:" + entity;
        string path = "";
        for (var p = t; p; p = p.parent) path = "/" + p.GetSiblingIndex() + ":" + p.name + path;
        if (value is GameObject) return "unsaved:" + t.gameObject.scene.name + path;
        var same = t.GetComponents(value.GetType());
        return "unsaved:" + t.gameObject.scene.name + path + "#" + value.GetType().FullName + "[" + Array.IndexOf(same, value) + "]";
    }
    public static string HashText(string value)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
    public static string Digest(IReadOnlyDictionary<string, string> values) =>
        HashText(string.Join("\n", values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));

    public static Dictionary<string, string> Capture(Scene scene, Transform excluded, Transform onlySubtree = null)
        => CaptureExcluding(scene, excluded ? new[] { excluded } : Array.Empty<Transform>(), onlySubtree);

    // env_053 has several nested child groups; exclude their child-list references as well as their objects.
    public static Dictionary<string, string> CaptureExcluding(Scene scene, IEnumerable<Transform> excludedGroups, Transform onlySubtree = null)
    {
        var excluded = excludedGroups.Where(t => t).ToArray();
        Canvas.ForceUpdateCanvases(); // Settle driven HUD layout before comparing authored environment state.
        string sceneSha = BossHallSurvey.Hash(scene.path);
        if (cachedScene != scene || cachedSceneSha != sceneSha)
        {
            Identities.Clear(); // Save/reload assigns durable IDs to newly authored objects.
            cachedScene = scene; cachedSceneSha = sceneSha;
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var transforms = (onlySubtree ? onlySubtree.GetComponentsInChildren<Transform>(true) :
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
            .Where(t => !excluded.Any(e => t.IsChildOf(e))).ToArray();
        var objects = transforms.SelectMany(t => new Object[] { t.gameObject }.Concat(t.GetComponents<Component>().Where(c => c)))
            .Where(o => !Identities.ContainsKey(o.GetEntityId())).ToArray();
        var ids = new GlobalObjectId[objects.Length];
        GlobalObjectId.GetGlobalObjectIdsSlow(objects, ids);
        for (int i = 0; i < objects.Length; i++)
        {
            var entity = objects[i].GetEntityId();
            Identities[entity] = ids[i].targetObjectId == 0 ? Unsaved(objects[i], entity) : ids[i].ToString();
        }
        foreach (var t in transforms)
        {
            var children = t.Cast<Transform>().Where(c => !excluded.Contains(c)).Select(Id);
            string vector(Vector3 v) => string.Join(",", new[] { v.x, v.y, v.z }.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
            var q = t.localRotation;
            values.Add(Id(t), HashText(Id(t.parent) + "|" + vector(t.localPosition) + "|" + vector(t.localScale) + "|" +
                string.Join(",", new[] { q.x, q.y, q.z, q.w }.Select(n => n.ToString("R", CultureInfo.InvariantCulture))) + "|" + string.Join(",", children)));
            values.Add(Id(t.gameObject), HashText(Serialize(t.gameObject)));
            foreach (var c in t.GetComponents<Component>().Where(c => !(c is Transform)))
            {
                if (!c) throw new InvalidOperationException("Missing script at " + BossHallFramework.PathOf(t));
                values.Add(Id(c), HashText(c.GetType().FullName + "|" + Serialize(c)));
            }
        }
        values.Add("scene-root-order", HashText(string.Join(",", scene.GetRootGameObjects().Where(g => !excluded.Any(e => g == e.gameObject)).Select(Id))));
        values.Add("render-settings", HashText(Serialize(Unsupported.GetRenderSettings())));
        values.Add("lightmap-settings", HashText(LightmapSettings.lightmapsMode + "|" + Id(Lightmapping.lightingSettings) + "|" +
            string.Join(";", LightmapSettings.lightmaps.Select(l => Id(l.lightmapColor) + "," + Id(l.lightmapDir) + "," + Id(l.shadowMask)))));
        return values;
    }

    // Native Editor JSON is fast; normalize object references to durable global IDs before hashing.
    static readonly Regex References = new Regex("\"(instanceID|entityId|entityID)\"\\s*:\\s*(-?[0-9]+)", RegexOptions.Compiled);
    static string Serialize(Object value) => References.Replace(EditorJsonUtility.ToJson(value), m =>
    {
        EntityId entity = m.Groups[1].Value == "instanceID"
            ? (EntityId)int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
            : EntityId.FromULong(ulong.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        return "\"objectID\":\"" + Id(EditorUtility.EntityIdToObject(entity)) + "\"";
    });

    // Protect shared/other-room materials, source prefabs, loaded navigation and render configuration, including metadata.
    public static Dictionary<string, string> CaptureAssets()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string filter in new[] { "t:Material", "t:Prefab", "t:NavMeshData", "t:RenderPipelineAsset", "t:ScriptableRendererData", "t:LightingSettings", "t:AudioMixer" })
            foreach (string guid in AssetDatabase.FindAssets(filter)) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string path in Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories)) paths.Add(path.Replace('\\', '/'));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in paths.Where(p => !p.StartsWith(BossHallFramework.AssetRoot + "/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (File.Exists(path)) values[path] = BossHallSurvey.Hash(path);
            if (File.Exists(path + ".meta")) values[path + ".meta"] = BossHallSurvey.Hash(path + ".meta");
        }
        return values;
    }

    public static void Compare(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, string label)
    {
        var changed = before.Keys.Union(after.Keys).Where(k => !before.ContainsKey(k) || !after.ContainsKey(k) || before[k] != after[k]).Take(12).Select(k =>
        {
            if (!GlobalObjectId.TryParse(k, out var id)) return k;
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            return k + " (" + (obj ? obj.GetType().Name + " " + obj.name : "missing") + ")";
        }).ToArray();
        if (changed.Length > 0) throw new InvalidOperationException(label + " changed: " + string.Join(", ", changed));
    }
}
