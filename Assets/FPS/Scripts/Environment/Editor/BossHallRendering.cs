using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Unity.FPS.Game;

/// <summary>corebuster_render_010: derived, collider-free skins; original renderers and geometry are protected.</summary>
public static class BossHallRendering
{
    public const string Folder = BossHallFramework.AssetRoot + "/Rendering";
    public const string ManifestPath = Folder + "/SurfaceBindings.json";
    public const string Output = "ReviewCaptures/BossHall/010";
    const string Kit = "Assets/SciFi Warehouse Kit/Art/Materials/";
    const float Offset = .006f;
    static readonly string[] SourceNames = { "Floor Tile Mat", "Walls", "Ceiling Mat", "Catwalk Mat", "Stairs Mat",
        "Stairs Trans Mat", "Structure Mat", "Bay Door Mat", "Fan Mat", "Vent Trans Mat", "Windows" };
    static readonly string[] FinishNames = { "Gunmetal", "RedOxide", "Brass", "HazardYellow", "WarningRed", "PracticalAmber", "EnergyViolet", "EnergyBlue", "EnergyTeal" };
    static readonly Color[] FinishColours = { new Color(.28f,.29f,.30f), new Color(.45f,.20f,.14f), new Color(.50f,.36f,.19f),
        new Color(.72f,.48f,.10f), new Color(.6f,.02f,.01f), new Color(.7f,.32f,.06f), new Color(.34f,.04f,.8f), new Color(.04f,.27f,.8f), new Color(.02f,.55f,.45f) };

    [Serializable] public sealed class Binding
    {
        public string name, rendererId, kind, sourceMaterial, meshPath, meshSha, materialPath;
        public Vector3 min, max;
    }
    [Serializable] public sealed class Manifest
    {
        public string task, contractSha, protectedSceneDigest;
        public int sourceRenderers, duplicateSubmeshes;
        public List<Binding> surfaces = new List<Binding>();
    }
    static Manifest cached;
    static string cachedSha;
    static readonly Dictionary<string, string> MeshHashes = new Dictionary<string, string>();
    static Manifest ReadManifest()
    {
        if (!File.Exists(ManifestPath)) return null;
        string sha = File.GetLastWriteTimeUtc(ManifestPath).Ticks + "|" + new FileInfo(ManifestPath).Length;
        if (sha != cachedSha)
        {
            cached = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)); cachedSha = sha;
            MeshHashes.Clear();
        }
        if (cached == null || cached.task != "corebuster_render_010" || cached.surfaces.Count == 0)
            throw new InvalidOperationException("Invalid Boss Hall surface bindings.");
        return cached;
    }

    public static bool IsApprovedSkin(Renderer renderer, BossHallFramework.Layout layout)
    {
        var manifest = ReadManifest();
        if (manifest == null) return false;
        var b = manifest.surfaces.SingleOrDefault(s => s.name == renderer.name);
        if (b == null) return false;
        if (manifest.contractSha != layout.contractSha || manifest.protectedSceneDigest != layout.contract.protectedSceneDigest)
            throw new InvalidOperationException("Stale Boss Hall rendering bindings; reconcile before rebuilding.");
        var section = BossHallFramework.FindSection(BossHallFramework.Section.Rendering);
        var filter = renderer.GetComponent<MeshFilter>();
        string path = filter && filter.sharedMesh ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "";
        if (renderer.transform.parent != section || !(renderer is MeshRenderer) || renderer.GetComponents<Component>().Length != 3 ||
            !renderer.enabled || !renderer.gameObject.activeSelf || renderer.gameObject.layer != 0 || renderer.gameObject.tag != "Untagged" ||
            GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) != 0 || renderer.renderingLayerMask != 1 ||
            renderer.transform.localPosition != Vector3.zero || renderer.transform.localRotation != Quaternion.identity || renderer.transform.localScale != Vector3.one ||
            renderer.shadowCastingMode != ShadowCastingMode.Off || !renderer.receiveShadows || path != b.meshPath ||
            renderer.sharedMaterials.Length != 1 || AssetDatabase.GetAssetPath(renderer.sharedMaterial) != b.materialPath ||
            Vector3.Distance(renderer.bounds.min,b.min) > .0001f || Vector3.Distance(renderer.bounds.max,b.max) > .0001f)
            throw new InvalidOperationException("Altered derived Boss Hall skin: " + renderer.name);
        // Cache by file timestamp as well as path; an asset edit in this session must invalidate approval.
        string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + new FileInfo(path).Length;
        if (!MeshHashes.TryGetValue(key, out string hash)) MeshHashes[key] = hash = BossHallSurvey.Hash(path);
        if (hash != b.meshSha || EditorUtility.IsDirty(filter.sharedMesh)) throw new InvalidOperationException("Altered Boss Hall skin mesh: " + path);
        return true;
    }

    public static Material GetFinish(string name)
    {
        if (!FinishNames.Contains(name)) throw new ArgumentException("Unknown Boss Hall finish " + name);
        return AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/M_" + name + ".mat") ??
            throw new InvalidOperationException("Build Boss Hall rendering assets first.");
    }

    /// <summary>Called by every isolated section rebuild. Never edits a source asset or an original renderer.</summary>
    public static void ApplyOwnedMaterials(Transform section)
    {
        if (!File.Exists(ManifestPath)) return;
        foreach (var r in section.GetComponentsInChildren<Renderer>(true))
        {
            var materials = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                string source = materials[i] ? AssetDatabase.GetAssetPath(materials[i]) : "";
                if (!SourceNames.Any(n => source == Kit + n + ".mat")) continue;
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(CopyPath(source)) ?? throw new InvalidOperationException("Missing owned equivalent: " + source);
                changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(r,"Apply Boss Hall owned materials"); r.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
    }

    static string CopyPath(string source) => Folder + "/Materials/Copies/" + AssetDatabase.AssetPathToGUID(source) + ".mat";
    static bool AuthoredTexture(string source) => source != Kit+"Floor Tile Mat.mat" && source != Kit+"Walls.mat" && source != Kit+"Ceiling Mat.mat";
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0,slash));
        AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
    static T Save<T>(T generated,string path) where T : Object
    {
        generated.name = Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        // URP import normalises keyword caches/property ordering. Keep an already equivalent asset byte-identical.
        if(existing is Material prior && generated is Material next && Equivalent(prior,next))
        { Object.DestroyImmediate(generated); return existing; }
        if (existing) { EditorUtility.CopySerialized(generated,existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated,path); existing = generated; }
        BossHallFramework.RequireOwnedAsset(existing,BossHallFramework.Section.Rendering); return existing;
    }
    static bool Equivalent(Material a,Material b)
    {
        if(a.parent||a.shader!=b.shader||a.name!=b.name||a.renderQueue!=b.renderQueue||a.globalIlluminationFlags!=b.globalIlluminationFlags||
            a.GetTag("RenderType",false)!=b.GetTag("RenderType",false))return false;
        foreach(string p in new[]{"_Metallic","_Smoothness","_AlphaClip","_Cutoff","_Surface","_Blend","_ZWrite","_SrcBlend","_DstBlend"})
            if(a.GetFloat(p)!=b.GetFloat(p))return false;
        foreach(string p in new[]{"_BaseColor","_EmissionColor"})if(a.GetColor(p)!=b.GetColor(p))return false;
        foreach(string p in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap","_OcclusionMap","_EmissionMap"})
            if(a.GetTexture(p)!=b.GetTexture(p))return false;
        foreach(string k in new[]{"_EMISSION","_ALPHATEST_ON","_SURFACE_TYPE_TRANSPARENT"})if(a.IsKeywordEnabled(k)!=b.IsKeywordEnabled(k))return false;
        return a.GetTextureScale("_BaseMap")==b.GetTextureScale("_BaseMap") && a.GetTextureOffset("_BaseMap")==b.GetTextureOffset("_BaseMap");
    }
    // First-principles, tileable plate wear. The retained kit textures supply grating alpha and glass only.
    static Texture2D Weather()
    {
        const int n = 512; var pixels = new Color32[n*n];
        for (int y=0;y<n;y++) for (int x=0;x<n;x++)
        {
            uint h; unchecked { h=(uint)(x*73856093) ^ (uint)(y*19349663) ^ 101u; h ^= h>>13; h*=1274126177u; h^=h>>16; }
            float noise=(h&65535)/65535f;
            float seam = x%128<3 || y%128<3 ? .68f : 1;
            float scratch = y%47==0 && x%113>75 ? .13f : 0;
            float linear = (.78f + .09f*(noise-.5f) + scratch)*seam;
            byte v=(byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(linear)*255);
            pixels[y*n+x]=new Color32(v,v,v,255);
        }
        var image = new Texture2D(n,n,TextureFormat.RGBA32,false); image.SetPixels32(pixels);
        string path=Folder+"/Textures/T_WornPlate.png"; byte[] png=image.EncodeToPNG(); Object.DestroyImmediate(image);
        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path,png);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable=true; importer.sRGBTexture=true; importer.mipmapEnabled=true; importer.wrapMode=TextureWrapMode.Repeat;
        importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=8; importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Color Mean(Texture2D texture)
    {
        if (!texture) return Color.white;
        // GPU readback avoids changing a shared source texture's importer, and includes only visible alpha-cutout pixels.
        var rt=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var previous=RenderTexture.active; var read=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
        try
        {
            Graphics.Blit(texture,rt); RenderTexture.active=rt; read.ReadPixels(new Rect(0,0,256,256),0,0); read.Apply();
            double r=0,g=0,b=0; int count=0;
            foreach(var c in read.GetPixels()) { if(c.a<.5f) continue; r+=c.r;g+=c.g;b+=c.b;count++; }
            if(count==0) throw new InvalidOperationException("No visible texels in " + texture.name);
            return new Color((float)(r/count),(float)(g/count),(float)(b/count),1);
        }
        finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(read); }
    }
    static void Configure(Material m,Color target,Texture2D texture,float metallic,float smoothness,bool cutout=false,bool glass=false,bool preserveHue=false)
    {
        m.parent=null; m.shader=Shader.Find("Universal Render Pipeline/Lit"); m.shaderKeywords=Array.Empty<string>();
        var mean=Mean(texture); m.SetTexture("_BaseMap",texture);
        // Structural atlases mix grey bodies and yellow markings. Per-channel averaging would bleach yellow and
        // amplify blue on the grey bodies; use one luminance scale so the authored hues stay intact.
        if(preserveHue){float luminance=.2126f*mean.r+.7152f*mean.g+.0722f*mean.b;mean=new Color(luminance,luminance,luminance,1);}
        m.SetColor("_BaseColor",new Color(target.r/Mathf.Max(.01f,mean.r),target.g/Mathf.Max(.01f,mean.g),target.b/Mathf.Max(.01f,mean.b),glass?.18f:1));
        m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);m.SetFloat("_AlphaClip",cutout?1:0);m.SetFloat("_Cutoff",.5f);
        m.SetFloat("_Surface",glass?1:0);m.SetFloat("_Blend",0);m.SetFloat("_BlendModePreserveSpecular",0);m.SetFloat("_ZWrite",glass?0:1);
        m.SetFloat("_SrcBlend",glass?(float)BlendMode.SrcAlpha:(float)BlendMode.One);
        m.SetFloat("_DstBlend",glass?(float)BlendMode.OneMinusSrcAlpha:(float)BlendMode.Zero);
        m.SetTexture("_BumpMap",null);m.SetTexture("_MetallicGlossMap",null);m.SetTexture("_OcclusionMap",null);
        m.SetColor("_EmissionColor",Color.black);m.SetTexture("_EmissionMap",null); m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        m.SetOverrideTag("RenderType",glass?"Transparent":cutout?"TransparentCutout":"Opaque");
        m.renderQueue=glass?(int)RenderQueue.Transparent:cutout?(int)RenderQueue.AlphaTest:(int)RenderQueue.Geometry;
        if(cutout)m.EnableKeyword("_ALPHATEST_ON"); if(glass)m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        BaseShaderGUI.SetMaterialKeywords(m);
    }
    static void BuildMaterials()
    {
        EnsureFolder(Folder+"/Textures");EnsureFolder(Folder+"/Materials/Copies");EnsureFolder(Folder+"/Meshes");
        var texture=Weather();
        foreach(string name in SourceNames)
        {
            string path=Kit+name+".mat";var source=AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidOperationException("Missing " + path);
            var m=new Material(source);m.parent=null;
            bool cutout=name.Contains("Trans"),glass=name=="Windows";
            var target=name=="Floor Tile Mat"?new Color(.25f,.26f,.27f):name=="Walls"?new Color(.34f,.35f,.36f):new Color(.28f,.29f,.30f);
            bool authored=AuthoredTexture(path);
            var map=authored?source.GetTexture("_MainTex") as Texture2D:texture;
            Configure(m,target,map,.18f,name=="Floor Tile Mat"?.38f:.3f,cutout,glass,authored);
            if(authored){m.SetTextureScale("_BaseMap",source.GetTextureScale("_MainTex"));m.SetTextureOffset("_BaseMap",source.GetTextureOffset("_MainTex"));}
            else {m.SetTextureScale("_BaseMap",Vector2.one);m.SetTextureOffset("_BaseMap",Vector2.zero);}
            Save(m,CopyPath(path));
        }
        for(int i=0;i<FinishNames.Length;i++)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Configure(m,FinishColours[i],i<4?texture:null,i==2?.38f:.2f,.32f);
            if(i>=4){m.SetColor("_EmissionColor",FinishColours[i]*3);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;m.EnableKeyword("_EMISSION");}
            BaseShaderGUI.SetMaterialKeywords(m);
            Save(m,Folder+"/Materials/M_"+FinishNames[i]+".mat");
        }
        AssetDatabase.SaveAssets();
    }

    static string Kind(Transform t)
    {
        string n=HierarchyFolder.PieceRoot(t).name;
        if(n.StartsWith("Floor Tile",StringComparison.Ordinal))return "floor";
        if(n.StartsWith("Ceiling",StringComparison.Ordinal)||n.StartsWith("LP_ceiling_tile",StringComparison.Ordinal)||n.StartsWith("LP_Skylight",StringComparison.Ordinal))return "ceiling";
        if(n.StartsWith("Wall",StringComparison.Ordinal))return "wall";
        if(n.StartsWith("Catwalk",StringComparison.Ordinal)||n.StartsWith("CatWalk",StringComparison.Ordinal))return "deck";
        return null;
    }
    struct Vertex { public Vector3 p; public Vector2 uv; }
    static List<Vertex> Clip(List<Vertex> input,int axis,float plane,bool greater)
    {
        var output=new List<Vertex>();if(input.Count==0)return output;
        var previous=input[input.Count-1];float pd=(previous.p[axis]-plane)*(greater?1:-1);
        foreach(var current in input)
        {
            float d=(current.p[axis]-plane)*(greater?1:-1);
            if((d>=0)!=(pd>=0)){float t=pd/(pd-d);output.Add(new Vertex{p=Vector3.Lerp(previous.p,current.p,t),uv=Vector2.Lerp(previous.uv,current.uv,t)});}
            if(d>=0)output.Add(current);previous=current;pd=d;
        }
        return output;
    }
    static Vector3 Inward(string kind,Bounds bounds,Bounds room)
    {
        if(kind=="floor")return Vector3.up;if(kind=="ceiling")return Vector3.down;if(kind=="deck")return Vector3.zero;
        float[] d={Mathf.Abs(bounds.center.x-room.min.x),Mathf.Abs(bounds.center.x-room.max.x),Mathf.Abs(bounds.center.z-room.min.z),Mathf.Abs(bounds.center.z-room.max.z)};
        return new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back}[Array.IndexOf(d,d.Min())];
    }
    static Manifest BuildSurfaces(BossHallFramework.Layout layout)
    {
        var manifest=new Manifest{task="corebuster_render_010",contractSha=layout.contractSha,protectedSceneDigest=layout.contract.protectedSceneDigest};
        var room=layout.interior;var locator=room;locator.Expand(1.2f);
        var seen=new HashSet<string>();
        var renderers=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r=>Kind(r.transform)!=null && locator.Contains(r.bounds.center)).OrderBy(r=>BossHallSnapshot.Id(r),StringComparer.Ordinal);
        AssetDatabase.StartAssetEditing();
        try
        {
        foreach(var r in renderers)
        {
            var filter=r.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)throw new InvalidOperationException("Missing shell mesh " + r.name);
            string kind=Kind(r.transform);
            // The survey's five sunken tiles are hidden by level tiles. Do not project a hidden duplicate onto the walking surface.
            if(kind=="floor" && r.bounds.max.y<room.min.y-.02f)continue;
            var src=filter.sharedMesh;var positions=src.vertices;var uv=src.uv;
            var inward=Inward(kind,r.bounds,room);manifest.sourceRenderers++;
            for(int sub=0;sub<src.subMeshCount;sub++)
            {
                string source=AssetDatabase.GetAssetPath(r.sharedMaterials[Mathf.Min(sub,r.sharedMaterials.Length-1)]);
                if(!SourceNames.Any(n=>source==Kit+n+".mat"))throw new InvalidOperationException("Unreviewed shell material " + source);
                string geometry=AssetDatabase.GetAssetPath(src)+"|"+src.name+"|"+r.transform.localToWorldMatrix.ToString("F5")+"|"+sub;
                if(!seen.Add(geometry)){manifest.duplicateSubmeshes++;continue;}
                var verts=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var indices=new List<int>();
                var triangles=src.GetTriangles(sub);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var polygon=new List<Vertex>();
                    for(int j=0;j<3;j++){int ix=triangles[i+j];polygon.Add(new Vertex{p=filter.transform.TransformPoint(positions[ix]),uv=uv.Length==positions.Length?uv[ix]:Vector2.zero});}
                    Vector3 normal=Vector3.Cross(polygon[1].p-polygon[0].p,polygon[2].p-polygon[0].p).normalized;
                    // The old 2D shader draws single-sided alpha guards from both sides. Derive the visible inward face
                    // for those sheets rather than dropping a backwards-authored vent/window from the lit pass.
                    if(inward!=Vector3.zero && Vector3.Dot(normal,inward)<-.55f &&
                        (source.Contains("Trans")||source.EndsWith("Windows.mat",StringComparison.Ordinal)))
                    { polygon.Reverse();normal=-normal; }
                    // Mesh triangle winding already includes mirrored-transform handedness; outward floor/roof faces never get copied.
                    if(inward!=Vector3.zero && Vector3.Dot(normal,inward)<.55f)continue;
                    // Negative-scale deck mesh winding needs the same correction Unity applies for rendering.
                    if(kind=="deck" && r.transform.localToWorldMatrix.determinant<0)normal=-normal;
                    for(int j=0;j<polygon.Count;j++)
                    {
                        var v=polygon[j];
                        if(kind!="deck")for(int a=0;a<3;a++)if(Mathf.Abs(inward[a])>.5f)v.p[a]=Mathf.Clamp(v.p[a],room.min[a],room.max[a]);
                        v.p+=(inward==Vector3.zero?normal:inward)*Offset;polygon[j]=v;
                    }
                    for(int a=0;a<3 && polygon.Count>=3;a++){polygon=Clip(polygon,a,room.min[a]+.002f,true);polygon=Clip(polygon,a,room.max[a]-.002f,false);}
                    for(int j=1;j+1<polygon.Count;j++)
                    {
                        if(Vector3.Cross(polygon[j].p-polygon[0].p,polygon[j+1].p-polygon[0].p).sqrMagnitude<1e-12f)continue;
                        var tri=new[]{polygon[0],polygon[j],polygon[j+1]};
                        if(kind=="deck" && r.transform.localToWorldMatrix.determinant<0)Array.Reverse(tri);
                        foreach(var v in tri)
                        {
                            indices.Add(verts.Count);verts.Add(v.p);normals.Add(inward==Vector3.zero?normal:inward);
                            bool authored=AuthoredTexture(source);
                            int axis=Mathf.Abs(normal.y)>.5f?1:Mathf.Abs(normal.x)>.5f?0:2;
                            uvs.Add(authored?v.uv:axis==1?new Vector2(v.p.x/2,v.p.z/2):axis==0?new Vector2(v.p.z/2,v.p.y/2):new Vector2(v.p.x/2,v.p.y/2));
                        }
                    }
                }
                if(verts.Count==0)continue;
                string name="Skin_"+BossHallSnapshot.HashText(BossHallSnapshot.Id(r)+"|"+sub).Substring(0,20);
                var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(verts);mesh.SetNormals(normals);mesh.SetUVs(0,uvs);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
                string path=Folder+"/Meshes/"+name+".asset";mesh=Save(mesh,path);
                manifest.surfaces.Add(new Binding{name=name,rendererId=BossHallSnapshot.Id(r),kind=kind,sourceMaterial=source,meshPath=path,materialPath=CopyPath(source),min=mesh.bounds.min,max=mesh.bounds.max});
            }
        }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();foreach(var s in manifest.surfaces)s.meshSha=BossHallSurvey.Hash(s.meshPath);
        File.WriteAllText(ManifestPath,JsonUtility.ToJson(manifest,true)+"\n");AssetDatabase.ImportAsset(ManifestPath);cachedSha=null;
        return manifest;
    }
    static void Populate(Transform section,Manifest manifest)
    {
        foreach(var b in manifest.surfaces)
        {
            var g=new GameObject(b.name);Undo.RegisterCreatedObjectUndo(g,"Build Boss Hall skin");g.transform.SetParent(section,false);
            g.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(b.meshPath);
            var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(b.materialPath);
            r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=true;
        }
    }
    public static void Validate(BossHallFramework.Layout layout)
    {
        BossHallFramework.Validate(layout);var manifest=ReadManifest() ?? throw new InvalidOperationException("No rendering bindings");
        var section=BossHallFramework.FindSection(BossHallFramework.Section.Rendering);
        if(section.childCount!=manifest.surfaces.Count)throw new InvalidOperationException("Missing or extra Boss Hall skins");
        foreach(var r in section.GetComponentsInChildren<Renderer>(true))if(!IsApprovedSkin(r,layout))throw new InvalidOperationException("Unapproved skin " + r.name);
        foreach(string name in SourceNames)
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(CopyPath(Kit+name+".mat"));CheckMaterial(m,false);
            bool cutout=name.Contains("Trans"),glass=name=="Windows";
            var source=AssetDatabase.LoadAssetAtPath<Material>(Kit+name+".mat");
            bool authored=AuthoredTexture(Kit+name+".mat");
            var expected=authored?source.GetTexture("_MainTex"):AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_WornPlate.png");
            var scale=authored?source.GetTextureScale("_MainTex"):Vector2.one;
            var offset=authored?source.GetTextureOffset("_MainTex"):Vector2.zero;
            if(m.GetTexture("_BaseMap")!=expected||m.GetTextureScale("_BaseMap")!=scale||m.GetTextureOffset("_BaseMap")!=offset||
                m.GetFloat("_AlphaClip")!=(cutout?1:0)||m.IsKeywordEnabled("_ALPHATEST_ON")!=cutout||m.GetFloat("_Surface")!=(glass?1:0))
                throw new InvalidOperationException("Lost source alpha/tiling binding " + name);
        }
        for(int i=0;i<FinishNames.Length;i++)CheckMaterial(GetFinish(FinishNames[i]),i>=4);
        int count=BossHallFramework.FindRoot().GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count();
        if(count>20)throw new InvalidOperationException("Boss Hall material count >20");
        Debug.Log($"BOSS HALL RENDERING: {manifest.sourceRenderers} protected source renderers, {manifest.surfaces.Count} skins, {manifest.duplicateSubmeshes} coincident submeshes deduplicated, {count}/20 assigned materials; 20 prepared finishes.");
    }
    static void CheckMaterial(Material m,bool emissive)
    {
        if(!m||m.parent||m.shader.name!="Universal Render Pipeline/Lit"||m.GetFloat("_Metallic")>.4f||m.GetTextureScale("_BaseMap")==Vector2.zero)
            throw new InvalidOperationException("Invalid owned material " + (m?m.name:"missing"));
        if(emissive && (!m.IsKeywordEnabled("_EMISSION")||(m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive)==0))
            throw new InvalidOperationException("Emission lost on import: " + m.name);
        if(!emissive && m.GetFloat("_Surface")==0)
        {
            var c=m.GetColor("_BaseColor")*Mean(m.GetTexture("_BaseMap") as Texture2D);
            if(.2126f*c.r+.7152f*c.g+.0722f*c.b<.22f && !m.name.StartsWith("M_",StringComparison.Ordinal))throw new InvalidOperationException("Unreadable large surface " + m.name);
        }
    }
    [MenuItem("Tools/CoreBuster/Build Boss Hall Rendering")]
    public static void BuildMenu()
    {
        var layout=BossHallFramework.MeasureLayout();BuildMaterials();var manifest=BuildSurfaces(layout);
        BossHallFramework.RebuildSection(layout,BossHallFramework.Section.Rendering,"corebuster_render_010",t=>Populate(t,manifest));
        Validate(layout);Debug.Log("Boss Hall rendering built with Undo; save using the guarded batch workflow or review and save in the Editor.");
    }
    public static void BuildBatch()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Batch requires exclusive Editor access");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath,OpenSceneMode.Single);
        var layout=BossHallFramework.MeasureLayout();var before=BossHallSnapshot.Capture(SceneManager.GetActiveScene(),BossHallFramework.FindRoot());var assets=BossHallSnapshot.CaptureAssets();
        BossHallRenderingReview.NeighbourProof(layout,false);
        BuildMaterials();var manifest=BuildSurfaces(layout);
        BossHallFramework.RebuildSection(layout,BossHallFramework.Section.Rendering,"corebuster_render_010",t=>Populate(t,manifest));
        // A deliberate clear/Undo verifies section isolation using the framework's real transaction.
        VerifyGuards(layout);
        int count=BossHallFramework.FindSection(BossHallFramework.Section.Rendering).childCount;
        BossHallFramework.RebuildSection(layout,BossHallFramework.Section.Rendering,"corebuster_render_010",_=>{});Undo.PerformUndo();
        if(BossHallFramework.FindSection(BossHallFramework.Section.Rendering).childCount!=count)throw new InvalidOperationException("Skin clear/Undo failed");
        Validate(layout);layout=BossHallFramework.SaveAndReload(layout);Validate(layout);
        BossHallSnapshot.Compare(before,BossHallSnapshot.Capture(SceneManager.GetActiveScene(),BossHallFramework.FindRoot()),"Original scene after rendering reload");
        BossHallSnapshot.Compare(assets,BossHallSnapshot.CaptureAssets(),"Shared assets after rendering reload");
        var generated=Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>BossHallSurvey.Hash(p));
        BuildMaterials();BuildSurfaces(layout);
        BossHallSnapshot.Compare(generated,Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>BossHallSurvey.Hash(p)),"Deterministic owned assets rerun");
        BossHallRenderingReview.NeighbourProof(layout,true);BossHallRenderingReview.Capture(layout);
        BossHallRenderingReview.ValidateNeighbours();
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/Rendering_010.txt","corebuster_render_010 | Unity "+Application.unityVersion+" | 2026-10-04\nSaved scene: "+BossHallSurvey.Hash(BossHallSurvey.ScenePath)+
            "\nPASS: saved reload, exact skin bindings, alpha/UV persistence, parent/shader/emission checks, neutral captures, neighbour pixel proof, isolated Undo, "+before.Count+" original scene states, "+assets.Count+" protected assets/meta/settings.\n"+BossHallFramework.BudgetReport()+"\n");
        Debug.Log("BOSS HALL RENDERING BUILD VERIFIED");
    }
    public static void ValidateBatch()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use exclusive batch Editor");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath,OpenSceneMode.Single);string sha=BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var layout=BossHallFramework.MeasureLayout();Validate(layout);BossHallRenderingReview.ValidateNeighbours();
        if(SceneManager.GetActiveScene().isDirty||sha!=BossHallSurvey.Hash(BossHallSurvey.ScenePath))throw new InvalidOperationException("Read-only validation changed scene");
        Debug.Log("BOSS HALL RENDERING RELOAD VERIFIED");
    }
    public static void VerifyBatch()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use exclusive batch Editor");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath,OpenSceneMode.Single);string sha=BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var layout=BossHallFramework.MeasureLayout();Validate(layout);
        var before=BossHallSnapshot.Capture(SceneManager.GetActiveScene(),null);
        BossHallRenderingReview.NeighbourProof(layout,false);BossHallRenderingReview.NeighbourProof(layout,true);
        BossHallRenderingReview.Capture(layout);BossHallRenderingReview.ValidateNeighbours();
        BossHallSnapshot.Compare(before,BossHallSnapshot.Capture(SceneManager.GetActiveScene(),null),"Read-only capture state including skins");
        if(SceneManager.GetActiveScene().isDirty||sha!=BossHallSurvey.Hash(BossHallSurvey.ScenePath))throw new InvalidOperationException("Read-only proof changed scene");
        Debug.Log("BOSS HALL RENDERING FRESH ORIGINAL-SURFACE PROOF VERIFIED");
    }
    static void VerifyGuards(BossHallFramework.Layout layout)
    {
        var section=BossHallFramework.FindSection(BossHallFramework.Section.Rendering);
        var renderer=section.GetComponentsInChildren<MeshRenderer>().First();
        void Rejected(Action action,string label)
        {
            try { action(); } catch(InvalidOperationException) { Debug.Log("PASS: rendering guard " + label); return; }
            throw new InvalidOperationException("Accepted invalid rendering state: " + label);
        }
        Vector3 p=renderer.transform.localPosition;
        try { renderer.transform.localPosition=Vector3.up;Rejected(()=>BossHallFramework.Validate(layout),"moved skin"); }
        finally { renderer.transform.localPosition=p; }
        var material=renderer.sharedMaterial;
        try { renderer.sharedMaterial=GetFinish("WarningRed");Rejected(()=>BossHallFramework.Validate(layout),"wrong skin finish"); }
        finally { renderer.sharedMaterial=material; }
        var collider=renderer.gameObject.AddComponent<BoxCollider>();
        try { Rejected(()=>BossHallFramework.Validate(layout),"skin collision"); }
        finally { Object.DestroyImmediate(collider); }
        var filter=renderer.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
        try { filter.sharedMesh=null;Rejected(()=>BossHallFramework.Validate(layout),"missing skin mesh"); }
        finally { filter.sharedMesh=mesh; }
        var probe=new GameObject("__Material_Reapply_Probe");
        try
        {
            probe.transform.SetParent(section,false);var r=probe.AddComponent<MeshRenderer>();
            string source=Kit+"Walls.mat";r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(source);
            ApplyOwnedMaterials(probe.transform);
            if(AssetDatabase.GetAssetPath(r.sharedMaterial)!=CopyPath(source))throw new InvalidOperationException("Owned material reapplication failed");
        }
        finally { Object.DestroyImmediate(probe); }
        Validate(layout);
    }
}
