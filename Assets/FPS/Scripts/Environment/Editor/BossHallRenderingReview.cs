using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.FPS.Gameplay;
using Object = UnityEngine.Object;
using Unity.FPS.Game;

/// <summary>Unsaved neutral review and before/after boundary proof for corebuster_render_010.</summary>
public static class BossHallRenderingReview
{
    internal const int Width=960, Height=540;
    internal sealed class MaterialState
    {
        public Material material;public string json;public bool dirty;
    }
    internal static MaterialState[] PreserveMaterials()
    {
        var states=SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct()
            .Select(m=>new MaterialState{material=m,json=EditorJsonUtility.ToJson(m),dirty=EditorUtility.IsDirty(m)}).ToArray();
        // Project shaders already expose this hidden review control. Hold the reactor's procedural phase without
        // changing shader sources or saving a shared material; the complete material memento is restored below.
        foreach(var s in states)if(s.material.HasProperty("_ReviewTime"))s.material.SetFloat("_ReviewTime",0);
        return states;
    }
    internal static void RestoreMaterials(MaterialState[] states)
    {
        // URP may initialise keywords on imported FBX materials while drawing. Restore only the captured in-memory state;
        // never save/reimport a shared asset and never clear an edit that was already dirty before the review.
        foreach(var s in states)
        {
            if(EditorJsonUtility.ToJson(s.material)!=s.json)EditorJsonUtility.FromJsonOverwrite(s.json,s.material);
            if(!s.dirty)EditorUtility.ClearDirty(s.material);
            if(EditorJsonUtility.ToJson(s.material)!=s.json)throw new InvalidOperationException("Review changed material state " + s.material.name);
        }
    }
    internal static float EyeHeight()
    {
        var p=Object.FindAnyObjectByType<PlayerCharacterController>();
        if(!p)throw new InvalidOperationException("Missing saved player");
        return p.CapsuleHeightStanding*p.CameraHeightRatio;
    }
    static Vector3 DoorPosition()
    {
        var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        return BossHallFramework.Unique(all,BossHallSurvey.BossDoorName,true).position;
    }
    internal static Vector3 InclineEye(float distance,BossHallFramework.Layout layout)
    {
        var door=DoorPosition();var p=new Vector3(door.x-distance,layout.interior.max.y+2,door.z);
        var hits=Physics.RaycastAll(p,Vector3.down,40,~0,QueryTriggerInteraction.Ignore)
            .Where(h=>HierarchyFolder.PieceRoot(h.collider.transform).name==BossHallSurvey.InclineName &&
                (BossHallFramework.PathOf(h.collider.transform).Contains("Floor")||BossHallFramework.PathOf(h.collider.transform).Contains("Steps")))
            .OrderBy(h=>h.distance).ToArray();
        if(hits.Length==0)throw new InvalidOperationException("Missing measured incline floor at " + distance);
        return hits[0].point+Vector3.up*EyeHeight();
    }
    internal static Camera CameraForReview()
    {
        var go=new GameObject("Temporary Boss Hall review camera"){hideFlags=HideFlags.HideAndDontSave};
        var c=go.AddComponent<Camera>();c.enabled=false;c.fieldOfView=70;c.aspect=Width/(float)Height;c.nearClipPlane=.05f;c.farClipPlane=300;
        var data=c.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask|=1<<31;
        c.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
        // The saved global profile has random FilmGrain at 0.3. Override only grain on this temporary review camera.
        go.layer=31;var volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.hideFlags=HideFlags.HideAndDontSave;
        profile.Add<FilmGrain>(true).intensity.Override(0);volume.sharedProfile=profile;return c;
    }
    internal static void DestroyCamera(Camera camera)
    {
        var profile=camera.GetComponent<Volume>().sharedProfile;
        foreach(var component in profile.components)Object.DestroyImmediate(component);
        Object.DestroyImmediate(profile);Object.DestroyImmediate(camera.gameObject);
    }
    internal static byte[] Shoot(Camera camera,Vector3 position,Vector3 target)
    {
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var rt=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
        var t=new Texture2D(Width,Height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            if(camera.GetUniversalAdditionalCameraData().renderPostProcessing && VolumeManager.instance.stack.GetComponent<FilmGrain>().intensity.value!=0)
                throw new InvalidOperationException("Review camera grain override was not applied");
            t.ReadPixels(new Rect(0,0,Width,Height),0,0);t.Apply();return t.EncodeToPNG();
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(t);}
    }
    internal static (string,Vector3,Vector3)[] Neighbours(BossHallFramework.Layout layout)
    {
        var r=layout.Report;float eye=EyeHeight();var threshold=InclineEye(1,layout);
        Vector3 Incline(float distance)=>InclineEye(distance,layout);
        var focus=(r.turret.head.min+r.turret.head.max)*.5f;
        return new[]{
            ("Room3_Master",new Vector3(-225,1.62f,118),new Vector3(-235,2,110)),
            ("Room3_BossDoor",threshold+new Vector3(6,0,0),threshold+Vector3.up*.5f),
            ("Threshold_Forward",threshold,focus),
            ("Incline_Back",Incline(6),threshold+Vector3.up*.5f),
            ("Landing_Back",Incline(11),threshold+Vector3.up*1.5f),
            ("Incline_Reveal",Incline(8.5f),focus),
            ("Landing_Forward",Incline(11),focus),
            ("Centre_Room",new Vector3(-199,1.62f,55),new Vector3(-199,5,75)),
            ("Workshop",new Vector3(-170,1.62f,13),new Vector3(-161,3,30)),
        };
    }
    public static void NeighbourProof(BossHallFramework.Layout layout,bool compare)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string dir=BossHallRendering.Output+"/Neighbours/"+(compare?"After":"Before");Directory.CreateDirectory(dir);
        var materials=PreserveMaterials();var camera=CameraForReview();var lines=new List<string>();
        var skins=BossHallFramework.FindSection(BossHallFramework.Section.Rendering).GetComponentsInChildren<Renderer>(true);
        var enabled=skins.Select(r=>r.enabled).ToArray();
        // Always compare against the untouched original surfaces, including when a saved rendering pass already exists.
        if(!compare)foreach(var skin in skins)skin.enabled=false;
        try
        {
            foreach(var v in Neighbours(layout))
            {
                string path=dir+"/"+v.Item1+".png";File.WriteAllBytes(path,Shoot(camera,v.Item2,v.Item3));
                if(!compare)continue;
                var a=new Texture2D(2,2);var b=new Texture2D(2,2);
                try
                {
                    a.LoadImage(File.ReadAllBytes(BossHallRendering.Output+"/Neighbours/Before/"+v.Item1+".png"));b.LoadImage(File.ReadAllBytes(path));
                    var pa=a.GetPixels32();var pb=b.GetPixels32();var mask=new Color32[pa.Length];int changed=0,outside=0,large=0;
                    for(int i=0;i<pa.Length;i++)
                    {
                        int delta=Mathf.Max(Mathf.Abs(pa[i].r-pb[i].r),Mathf.Abs(pa[i].g-pb[i].g),Mathf.Abs(pa[i].b-pb[i].b));
                        bool hit=delta>4;mask[i]=hit?new Color32(255,0,0,255):new Color32((byte)(pa[i].r/3),(byte)(pa[i].g/3),(byte)(pa[i].b/3),255);
                        if(!hit)continue;changed++;
                        // An allowed pixel's ray must enter the measured hall. Backward/non-hall views have no exception.
                        Ray ray=camera.ViewportPointToRay(new Vector3((i%Width+.5f)/Width,(i/Width+.5f)/Height,0));
                        if(!layout.interior.IntersectRay(ray,out float d)||d<0){outside++;if(delta>32)large++;}
                    }
                    string diff=BossHallRendering.Output+"/Neighbours/Diff";Directory.CreateDirectory(diff);
                    var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);image.SetPixels32(mask);image.Apply();
                    File.WriteAllBytes(diff+"/"+v.Item1+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                    bool intoHall=v.Item1=="Threshold_Forward"||v.Item1=="Incline_Reveal"||v.Item1=="Landing_Forward";
                    lines.Add(v.Item1+": "+changed+" changed pixels, "+outside+" outside geometric hall projection ("+large+" above delta 32); "+
                        (intoHall?"permitted view into hall":"strict neighbour/backward view"));
                    // The task permits changes in views into the hall, including their existing transparent grating/post-processing.
                    // Every other view remains subject to the strict pixel check; source state/assets are independently protected.
                    if(!intoHall && (outside>32||large>0))throw new InvalidOperationException("Boundary image regression: "+lines.Last());
                }
                finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
            }
        }
        finally
        {
            for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];
            DestroyCamera(camera);RestoreMaterials(materials);
        }
        if(compare){File.WriteAllText(BossHallRendering.Output+"/Neighbours/Report.txt",string.Join("\n",lines)+"\n");Debug.Log(string.Join("\n",lines));}
        if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Capture dirtied MainScene");
    }
    [MenuItem("Tools/CoreBuster/Capture Boss Hall Rendering")]
    public static void CaptureMenu(){var layout=BossHallFramework.MeasureLayout();BossHallRendering.Validate(layout);Capture(layout);}
    public static void CaptureBatch(){EditorSceneManager.OpenScene(BossHallSurvey.ScenePath,OpenSceneMode.Single);CaptureMenu();}
    public static void Capture(BossHallFramework.Layout layout)
    {
        var r=layout.Report;var room=layout.interior;var focus=(r.turret.head.min+r.turret.head.max)*.5f;float eye=EyeHeight();
        var views=new[]{
            ("Entry",r.entry.centre+r.entry.inward*.5f+Vector3.up*eye,focus),
            ("Arena_SouthEast",new Vector3(room.max.x-8,r.floorTopY+eye,room.min.z+12),focus),
            ("Arena_NorthWest",new Vector3(room.min.x+8,r.floorTopY+eye,room.max.z-12),focus),
            ("Upper_Reverse",new Vector3(room.min.x+5,r.tier2TopY+eye,r.entry.centre.z),new Vector3(room.max.x,r.floorTopY+3,r.entry.centre.z)),
            ("Floor_Detail",new Vector3(room.max.x-12,r.floorTopY+2,room.min.z+18),new Vector3(room.max.x-17,r.floorTopY,room.min.z+23)),
            ("Wall_Detail",new Vector3(room.min.x+8,r.floorTopY+3,room.min.z+12),new Vector3(room.min.x,r.floorTopY+4,room.min.z+12)),
        };
        var materials=PreserveMaterials();var camera=CameraForReview();var lamps=new List<GameObject>();
        string dir=BossHallRendering.Output+"/Neutral";Directory.CreateDirectory(dir);
        try
        {
            foreach(var p in new[]{new Vector3(-16,8,-16),new Vector3(16,8,-16),new Vector3(-16,15,16),new Vector3(16,15,16)})
            {
                var g=new GameObject("Unsaved white review lamp"){hideFlags=HideFlags.HideAndDontSave};g.transform.position=r.centre+p;
                var l=g.AddComponent<Light>();l.type=LightType.Point;l.range=40;l.intensity=400;l.color=Color.white;l.shadows=LightShadows.None;lamps.Add(g);
            }
            foreach(var v in views)File.WriteAllBytes(dir+"/"+v.Item1+".png",Shoot(camera,v.Item2,v.Item3));
        }
        finally{foreach(var g in lamps)Object.DestroyImmediate(g);DestroyCamera(camera);RestoreMaterials(materials);}
        File.WriteAllText(dir+"/README.txt","NEUTRAL MATERIAL REVIEW: four unsaved white review lamps; existing scene ambient/grading remains, random film grain disabled on temporary camera only. These are not final env_055 lighting or performance captures. Original actors/materials are unchanged.\n");
        Samples();
        if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Neutral review dirtied MainScene");
    }
    static void Samples()
    {
        var main=SceneManager.GetActiveScene();var preview=EditorSceneManager.NewPreviewScene();var camera=CameraForReview();
        try
        {
            SceneManager.MoveGameObjectToScene(camera.gameObject,preview);camera.scene=preview;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.08f,.08f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var g=new GameObject("Neutral preview lamp"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(g,preview);
            var l=g.AddComponent<Light>();l.type=LightType.Directional;l.color=Color.white;l.intensity=1.5f;g.transform.rotation=Quaternion.Euler(30,-20,0);
            var materials=AssetDatabase.FindAssets("t:Material",new[]{BossHallRendering.Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            for(int i=0;i<materials.Length;i++)
            {
                var tile=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(tile,preview);
                tile.transform.position=new Vector3((i%5)*2.2f,(3-i/5)*1.7f,0);tile.transform.localScale=new Vector3(1.9f,1.4f,.15f);
                tile.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(materials[i]);
            }
            string dir=BossHallRendering.Output+"/Neutral";
            File.WriteAllBytes(dir+"/Material_Samples.png",Shoot(camera,new Vector3(4.4f,2.5f,-11),new Vector3(4.4f,2.5f,0)));
            File.WriteAllText(dir+"/Material_Samples.txt","ISOLATED NEUTRAL PREVIEW; left-to-right, top-to-bottom:\n"+
                string.Join("\n",materials.Select((p,i)=>(i+1)+". "+p+(p.Contains("/Copies/")?" <- "+AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(p)):"")))+"\n");
        }
        finally{DestroyCamera(camera);EditorSceneManager.ClosePreviewScene(preview);SceneManager.SetActiveScene(main);}
    }
    public static void ValidateNeighbours()
    {
        var centre=typeof(BossHallFramework).Assembly.GetType("CentreRoomFramework");
        if(centre!=null && !(bool)centre.GetMethod("Validate").Invoke(null,new[]{(object)SceneManager.GetActiveScene(),centre.GetMethod("MeasureLayout").Invoke(null,null),true}))
            throw new InvalidOperationException("Centre Room framework regression");
        if(!NorthWestRoomFramework.Validate(SceneManager.GetActiveScene(),NorthWestRoomFramework.MeasureLayout(),true))throw new InvalidOperationException("Room 3 framework regression");
        NorthWestRoomRendering.ValidateMenu();
        Debug.Log("PASS: existing-room framework and Room 3 rendering checks");
    }
}
