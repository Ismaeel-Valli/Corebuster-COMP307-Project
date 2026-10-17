using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.FPS.Game;
using Unity.FPS.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the front-end scenes from the Gentleland Steampunk UI pack: the Cosmic Studios boot screen,
/// the main menu (Start / Options / Quit), and the lose and win screens. Each run rebuilds the scenes
/// from scratch, so tweak the layout here rather than in the scenes.
/// </summary>
public static class MenuSceneBuilder
{
    const string PackArt = "Assets/Gentleland/SteampunkUI/Art/Steampunk_UI_";
    const string PackFonts = "Assets/Gentleland/SteampunkUI/Fonts/";
    const string UIArtFolder = "Assets/FPS/Art/UI";
    const string LogoPath = UIArtFolder + "/CoreBuster_Logo.png";
    const string GlintPath = UIArtFolder + "/UI_Glint.png";
    const string MaterialFolder = "Assets/FPS/Art/Materials/UI";
    const string GalaxySourcePath = "Assets/Materials/Textured.mat";

    const string BootScenePath = "Assets/FPS/Scenes/BootScreen.unity";
    const string MenuScenePath = "Assets/FPS/Scenes/IntroMenu.unity";
    const string LoseScenePath = "Assets/FPS/Scenes/LoseScene.unity";
    const string WinScenePath = "Assets/FPS/Scenes/WinScene.unity";

    const string Credits =
        "COMP307 Graphics & Modelling  ||  Cosmic Studios\n" +
        "Created by Mahomed Valli Mahomed (224005878), Jawad Kachbal (223037045), Abiel Govender (224058851), " +
        "Andiswa Hlengwa (221002430), Joshua Blom (224039813)";

    const string DisclosureHeading = "GENERATIVE AI & ASSET DISCLOSURE";
    const string DisclosureBody =
        "CoreBuster was made by Cosmic Studios for COMP307 Graphics & Modelling.\n\n" +
        "Generative AI tools assisted with parts of this game's code, shaders, procedural art and documentation. " +
        "Every idea, design decision and piece of guidance given to the AI was our own, and the team reviewed, " +
        "tested and integrated everything it produced.\n\n" +
        "Some models, textures, sounds and interface art come from third-party asset packs, used under their licences.";

    internal static readonly Color k_Parchment = new Color(.93f, .86f, .72f);
    internal static readonly Color k_Cream = new Color(1f, .95f, .85f);
    internal static readonly Color k_Ember = new Color(1f, .78f, .7f);

    // Everything loaded once per build. Shared with HudSkinBuilder.
    internal class Kit
    {
        public TMP_FontAsset Bebas, Roboto;
        public Material BebasGold, RobotoGold;
        public Sprite Logo, Glint;
        public Sprite ButtonNormal, ButtonHover, ButtonPressed;
        public Sprite KeyNormal, KeyHover, KeyPressed;
        public Sprite ArrowNormal, ArrowHover, ArrowPressed;
        public Sprite HeaderBar, ValueBar, FooterFrame, FooterFill;
        public Sprite SliderBack, SliderFill, SliderFrame, SliderKnob, ScrollHandle;
        public Sprite OptionsFrame, OptionsPlate, DialogFrame, DialogPlate;
        public Material Galaxy, GalaxyLose, Glow, GlowLose, GlowWin;
    }

    [MenuItem("Tools/CoreBuster/Build Menu Scenes")]
    public static void BuildAll()
    {
        var open = EditorSceneManager.GetActiveScene();
        if (open.isDirty)
        {
            Debug.LogError("Menu builder: save or discard the changes in " + open.name + " first.");
            return;
        }
        string reopen = open.path;

        if (LoadKit() == null) return;

        BuildBoot();
        BuildMainMenu();
        BuildEndScreen(LoseScenePath, false);
        BuildEndScreen(WinScenePath, true);
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();

        if (!string.IsNullOrEmpty(reopen)) EditorSceneManager.OpenScene(reopen);
        Debug.Log("Menu scenes built: BootScreen, IntroMenu, LoseScene, WinScene.");
    }

    // ------------------------------------------------------------------ assets

    // Called after each NewScene: opening a scene unloads unused assets, which would leave font
    // references loaded earlier dangling (TMP then silently falls back to its default font).
    internal static Kit LoadKit()
    {
        EnsureFolder(MaterialFolder);
        var kit = new Kit
        {
            Bebas = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PackFonts + "BebasNeue/BebasNeue SDF.asset"),
            Roboto = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PackFonts + "RobotoSlab/RobotoSlab SDF.asset"),
            BebasGold = AssetDatabase.LoadAssetAtPath<Material>(PackFonts + "BebasNeue/BebasNeue SDF Gold.mat"),
            RobotoGold = AssetDatabase.LoadAssetAtPath<Material>(PackFonts + "RobotoSlab/RobotoSlab SDF Gold.mat"),
            Logo = ImportSprite(LogoPath),
            Glint = ImportSprite(CreateGlintTexture()),

            // Brass-capped long buttons (the pack's Button 10).
            ButtonNormal = PackSprite("Buttons_4", 1), ButtonHover = PackSprite("Buttons_4", 0),
            ButtonPressed = PackSprite("Buttons_4", 2),
            // Slate slanted buttons (Button 11) for the key boxes.
            KeyNormal = PackSprite("Buttons_4", 4), KeyHover = PackSprite("Buttons_4", 3),
            KeyPressed = PackSprite("Buttons_4", 5),
            // Riveted copper arrow plates.
            ArrowNormal = PackSprite("Slots_Arrows", 4), ArrowHover = PackSprite("Slots_Arrows", 6),
            ArrowPressed = PackSprite("Slots_Arrows", 5),

            HeaderBar = PackSprite("Dialogue_2", 1),
            ValueBar = PackSprite("Dialogue_3", 5),
            FooterFrame = PackSprite("Dialogue_3", 4),
            FooterFill = PackSprite("Dialogue_3", 5),
            // The gold slider (the pack's Bar 9) with a brass ring for the knob.
            SliderBack = PackSprite("HP_Bars", 13), SliderFill = PackSprite("HP_Bars", 14),
            SliderFrame = PackSprite("HP_Bars", 12), SliderKnob = PackSprite("Slots_Arrows", 13),
            ScrollHandle = PackSprite("Slots_Arrows", 9),
            OptionsFrame = PackSprite("Dialogue_1", 2), OptionsPlate = PackSprite("Dialogue_1", 0),
            DialogFrame = PackSprite("Dialogue_2", 0), DialogPlate = PackSprite("Dialogue_2", 3),
        };

        var galaxySource = AssetDatabase.LoadAssetAtPath<Material>(GalaxySourcePath);
        kit.Galaxy = GalaxyMaterial("M_UIGalaxy", galaxySource, Color.white, .55f);
        kit.GalaxyLose = GalaxyMaterial("M_UIGalaxy_Lose", galaxySource, new Color(1f, .42f, .42f), .75f);
        kit.Glow = GlowMaterial("M_UICoreGlow", new Color(.55f, .75f, 1.6f), new Color(1.1f, .35f, .9f));
        kit.GlowLose = GlowMaterial("M_UICoreGlow_Lose", new Color(1.6f, .35f, .25f), new Color(.9f, .1f, .2f));
        kit.GlowWin = GlowMaterial("M_UICoreGlow_Win", new Color(.6f, 1.3f, 1.6f), new Color(.9f, .7f, .3f));

        if (!kit.Bebas || !kit.Roboto || !kit.Logo || !kit.ButtonNormal || !kit.Galaxy)
        {
            Debug.LogError("Menu builder: missing Steampunk UI pack fonts/sprites, the logo, or the galaxy shader.");
            return null;
        }
        return kit;
    }

    internal static Sprite PackSprite(string sheet, int index)
    {
        string name = "Steampunk_UI_" + sheet + "_" + index;
        var sprite = AssetDatabase.LoadAllAssetsAtPath(PackArt + sheet + ".png").OfType<Sprite>()
            .FirstOrDefault(s => s.name == name);
        if (!sprite) Debug.LogWarning("Menu builder: no sprite " + name);
        return sprite;
    }

    static Sprite ImportSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer && (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // A soft vertical streak for the shine across "Cosmic Studios".
    static string CreateGlintTexture()
    {
        if (File.Exists(GlintPath)) return GlintPath;
        const int w = 64, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int x = 0; x < w; x++)
        {
            float d = Mathf.Abs(x / (w - 1f) * 2f - 1f);
            float a = Mathf.Exp(-d * d * 9f);
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        File.WriteAllBytes(GlintPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GlintPath);
        return GlintPath;
    }

    static Material GalaxyMaterial(string name, Material source, Color tint, float vignette)
    {
        var material = LoadOrCreate(name, Shader.Find("CoreBuster/UI Galaxy"));
        if (source)
        {
            material.SetTexture("_NebulaTexA", source.GetTexture("_NebulaTexA"));
            material.SetTexture("_NebulaTexB", source.GetTexture("_NebulaTexB"));
        }
        material.SetColor("_Tint", tint);
        material.SetFloat("_Vignette", vignette);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material GlowMaterial(string name, Color core, Color rim)
    {
        var material = LoadOrCreate(name, Shader.Find("CoreBuster/UI Core Glow"));
        material.SetColor("_Color", core);
        material.SetColor("_RimColor", rim);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material LoadOrCreate(string name, Shader shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    static void UpdateBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(s => s.path == BootScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ------------------------------------------------------------------ scenes

    static void BuildBoot()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var kit = LoadKit();
        var canvas = CreateCanvasRig();

        var galaxy = FullScreen<RawImage>(canvas, "Galaxy");
        galaxy.material = kit.Galaxy;

        var studio = Rect(canvas, "Cosmic Studios", Vector2.zero, new Vector2(1600, 360));
        var studioGroup = studio.gameObject.AddComponent<CanvasGroup>();
        var halo = Rect(studio, "Halo", Vector2.zero, new Vector2(1500, 900)).gameObject.AddComponent<Image>();
        halo.material = kit.Glow;
        halo.color = new Color(.8f, .6f, 1f, .45f);
        halo.raycastTarget = false;

        // The glint is clipped to the title's box.
        var clip = Rect(studio, "Title Clip", new Vector2(0, 10), new Vector2(1500, 220));
        clip.gameObject.AddComponent<RectMask2D>();
        var title = Text(clip, "Title", "COSMIC STUDIOS", kit.Bebas, kit.BebasGold, 160, Color.white, Vector2.zero, new Vector2(1500, 220));
        title.characterSpacing = 14;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        var glint = Rect(clip, "Glint", Vector2.zero, new Vector2(90, 420));
        glint.localRotation = Quaternion.Euler(0, 0, -20);
        var glintImage = glint.gameObject.AddComponent<Image>();
        glintImage.sprite = kit.Glint;
        glintImage.color = new Color(1f, .97f, .85f, .75f);
        glintImage.raycastTarget = false;
        var shine = title.gameObject.AddComponent<TextShine>();
        shine.Glint = glint;
        Text(studio, "Tagline", "PRESENTS", kit.Roboto, null, 30, k_Parchment, new Vector2(0, -135), new Vector2(600, 50))
            .characterSpacing = 30;

        // Shown first, over a dimmed sky; a dark panel keeps the body text readable on any part of the nebula.
        var disclosure = Rect(canvas, "AI Disclosure", Vector2.zero, new Vector2(1440, 720));
        var disclosureGroup = disclosure.gameObject.AddComponent<CanvasGroup>();
        disclosureGroup.blocksRaycasts = false;
        var panel = Stretch(disclosure, "Panel").gameObject.AddComponent<Image>();
        panel.color = new Color(.02f, .02f, .05f, .78f);
        panel.raycastTarget = false;
        Text(disclosure, "Heading", DisclosureHeading, kit.Bebas, kit.BebasGold, 70, Color.white, new Vector2(0, 270),
            new Vector2(1340, 90)).characterSpacing = 8;
        var body = Text(disclosure, "Body", DisclosureBody, kit.Roboto, null, 34, k_Parchment, new Vector2(0, 0),
            new Vector2(1240, 400));
        body.lineSpacing = 10;
        var hint = Text(disclosure, "Continue", "PRESS ANY KEY TO CONTINUE", kit.Roboto, null, 24, k_Cream,
            new Vector2(0, -290), new Vector2(900, 40));
        hint.characterSpacing = 20;
        var hintGroup = hint.gameObject.AddComponent<CanvasGroup>();

        var flash = FullScreen<Image>(canvas, "Flash");
        flash.color = new Color(.92f, .95f, 1f, 1f);
        var flashGroup = flash.gameObject.AddComponent<CanvasGroup>();
        flashGroup.blocksRaycasts = false;
        var fade = FullScreen<Image>(canvas, "Fade");
        fade.color = Color.black;
        var fadeGroup = fade.gameObject.AddComponent<CanvasGroup>();
        fadeGroup.blocksRaycasts = false;

        var boot = new GameObject("Boot Sequence").AddComponent<BootSequence>();
        boot.Galaxy = galaxy;
        boot.Flash = flashGroup;
        boot.Fade = fadeGroup;
        boot.Disclosure = disclosureGroup;
        boot.DisclosureHint = hintGroup;
        boot.StudioGroup = studioGroup;
        boot.StudioTitle = clip;
        boot.Shine = shine;
        boot.NextScene = Path.GetFileNameWithoutExtension(MenuScenePath);

        EditorSceneManager.SaveScene(scene, BootScenePath);
    }

    static void BuildMainMenu()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var kit = LoadKit();
        var canvas = CreateCanvasRig();
        FullScreen<RawImage>(canvas, "Galaxy").material = kit.Galaxy;

        var menu = canvas.gameObject.AddComponent<MainMenu>();
        menu.GameSceneName = "MainScene";

        // Header: the core glowing above the title.
        var header = Rect(canvas, "Header", Vector2.zero, new Vector2(1920, 1080));
        CoreLogo(kit, header, new Vector2(0, 300), 330f, kit.Glow, false);
        var title = Text(header, "Title", "COREBUSTER", kit.Bebas, kit.BebasGold, 130, Color.white, new Vector2(0, 92), new Vector2(1200, 150));
        title.characterSpacing = 14;
        menu.Header = header.gameObject;

        // Start / Options / Quit.
        var main = Rect(canvas, "Main Buttons", Vector2.zero, new Vector2(1920, 1080));
        var start = MenuButton(kit, main, "Start", "START", new Vector2(0, -60), new Vector2(520, 120));
        var options = MenuButton(kit, main, "Options", "OPTIONS", new Vector2(0, -190), new Vector2(520, 120));
        var quit = MenuButton(kit, main, "Quit", "QUIT", new Vector2(0, -320), new Vector2(520, 120));
        UnityEventTools.AddPersistentListener(start.onClick, menu.ShowDifficulty);
        UnityEventTools.AddPersistentListener(options.onClick, menu.ShowOptions);
        UnityEventTools.AddPersistentListener(quit.onClick, menu.ShowQuitConfirm);
        Navigate(start, options, quit);
        menu.MainPanel = main.gameObject;
        menu.MainFirst = start;

        // Difficulty choice, in the order Invincible (testing), Easy, Hard.
        var difficulty = Rect(canvas, "Difficulty", Vector2.zero, new Vector2(1920, 1080));
        Text(difficulty, "Heading", "SELECT DIFFICULTY", kit.Bebas, kit.BebasGold, 56, Color.white, new Vector2(0, -5), new Vector2(900, 70))
            .characterSpacing = 8;
        var invincible = MenuButton(kit, difficulty, "Invincible", "INVINCIBLE\n<size=50%>TESTING - YOU CANNOT DIE</size>",
            new Vector2(0, -80), new Vector2(560, 92));
        var easy = MenuButton(kit, difficulty, "Easy", "EASY\n<size=50%>ENEMIES HIT SOFTER</size>",
            new Vector2(0, -178), new Vector2(560, 92));
        var hard = MenuButton(kit, difficulty, "Hard", "HARD\n<size=50%>TOUGHER ENEMIES, HARDER HITS</size>",
            new Vector2(0, -276), new Vector2(560, 92));
        var difficultyBack = MenuButton(kit, difficulty, "Back", "BACK", new Vector2(0, -360), new Vector2(300, 70));
        UnityEventTools.AddIntPersistentListener(invincible.onClick, menu.StartGame, (int)GameDifficulty.Invincible);
        UnityEventTools.AddIntPersistentListener(easy.onClick, menu.StartGame, (int)GameDifficulty.Easy);
        UnityEventTools.AddIntPersistentListener(hard.onClick, menu.StartGame, (int)GameDifficulty.Hard);
        UnityEventTools.AddPersistentListener(difficultyBack.onClick, menu.ShowMain);
        Navigate(invincible, easy, hard, difficultyBack);
        menu.DifficultyPanel = difficulty.gameObject;
        menu.DifficultyFirst = easy;

        // Options.
        var optionsPanel = BuildOptionsPanel(kit, canvas, menu, out Selectable optionsFirst);
        menu.OptionsPanel = optionsPanel.gameObject;
        menu.OptionsFirst = optionsFirst;

        // Quit confirmation.
        var dialog = Rect(canvas, "Quit Dialog", new Vector2(0, -190), new Vector2(780, 440));
        var dialogPlate = Rect(dialog, "Plate", new Vector2(0, -10), new Vector2(660, 330)).gameObject.AddComponent<Image>();
        dialogPlate.sprite = kit.DialogPlate;
        var dialogFrame = Stretch(dialog, "Frame").gameObject.AddComponent<Image>();
        dialogFrame.sprite = kit.DialogFrame;
        dialogFrame.raycastTarget = false;
        Text(dialog, "Title", "QUIT", kit.Bebas, kit.BebasGold, 44, Color.white, new Vector2(0, 161), new Vector2(320, 60))
            .characterSpacing = 10;
        Text(dialog, "Question", "Are you sure you want to quit?", kit.Roboto, null, 34, k_Cream, new Vector2(0, 40), new Vector2(620, 100));
        var yes = MenuButton(kit, dialog, "Yes", "YES", new Vector2(-145, -95), new Vector2(250, 84));
        var no = MenuButton(kit, dialog, "No", "NO", new Vector2(145, -95), new Vector2(250, 84));
        UnityEventTools.AddPersistentListener(yes.onClick, menu.ConfirmQuit);
        UnityEventTools.AddPersistentListener(no.onClick, menu.CancelQuit);
        NavigateHorizontal(yes, no);
        menu.QuitDialog = dialog.gameObject;
        menu.QuitFirst = no;

        // Credits along the bottom.
        var footer = Rect(canvas, "Credits", Vector2.zero, new Vector2(1780, 118));
        Anchor(footer, new Vector2(.5f, 0f), new Vector2(0, 70));
        var footerFill = Rect(footer, "Backing", Vector2.zero, new Vector2(1640, 86)).gameObject.AddComponent<Image>();
        footerFill.sprite = kit.FooterFill;
        footerFill.color = new Color(1f, 1f, 1f, .92f);
        var footerFrame = Stretch(footer, "Frame").gameObject.AddComponent<Image>();
        footerFrame.sprite = kit.FooterFrame;
        footerFrame.raycastTarget = false;
        var credits = Text(footer, "Text", Credits, kit.Roboto, null, 21, k_Parchment, new Vector2(0, 2), new Vector2(1500, 80));
        credits.lineSpacing = 12;
        credits.enableAutoSizing = true;
        credits.fontSizeMin = 14;
        credits.fontSizeMax = 21;

        menu.Fader = CreateFader(canvas);

        foreach (var panel in new[] { difficulty, optionsPanel, dialog }) panel.gameObject.SetActive(false);
        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }

    static RectTransform BuildOptionsPanel(Kit kit, RectTransform canvas, MainMenu menu, out Selectable first)
    {
        var panel = Rect(canvas, "Options", new Vector2(0, 30), new Vector2(1600, 880));
        var plate = Rect(panel, "Plate", new Vector2(0, -20), new Vector2(1500, 790)).gameObject.AddComponent<Image>();
        plate.sprite = kit.OptionsPlate;
        plate.color = new Color(.78f, .76f, .82f, .96f);
        var frame = Stretch(panel, "Frame").gameObject.AddComponent<Image>();
        frame.sprite = kit.OptionsFrame;
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 1.1f;
        frame.raycastTarget = false;
        Text(panel, "Title", "OPTIONS", kit.Bebas, kit.BebasGold, 64, Color.white, new Vector2(0, 385), new Vector2(600, 80))
            .characterSpacing = 12;

        var options = panel.gameObject.AddComponent<OptionsMenu>();

        // Left column: graphics and mouse.
        var left = Rect(panel, "Settings", new Vector2(-385, 0), new Vector2(660, 880));
        Header(kit, left, "GRAPHICS", 280);
        options.Quality = Stepper(kit, left, "Quality", "Quality", 210);
        options.Resolution = Stepper(kit, left, "Resolution", "Resolution", 150);
        options.DisplayMode = Stepper(kit, left, "Display Mode", "Display", 90);
        options.VSync = Stepper(kit, left, "VSync", "VSync", 30);
        options.FrameLimit = Stepper(kit, left, "Frame Limit", "Frame Limit", -30);
        Header(kit, left, "MOUSE", -115);
        options.Sensitivity = SensitivitySlider(kit, left, -185, out options.SensitivityValue);

        // Right column: key bindings.
        var right = Rect(panel, "Key Bindings", new Vector2(385, 0), new Vector2(660, 880));
        Header(kit, right, "KEY BINDINGS", 280);
        var rows = BindingList(kit, right);

        var back = MenuButton(kit, left, "Back", "BACK", new Vector2(0, -330), new Vector2(320, 84));
        UnityEventTools.AddPersistentListener(back.onClick, menu.ShowMain);
        var reset = MenuButton(kit, right, "Reset Keys", "RESET KEYS", new Vector2(0, -330), new Vector2(360, 84));
        options.ResetBindingsButton = reset;

        // Keyboard/gamepad order: the graphics arrows, the slider, then back.
        var column = new List<Selectable>
        {
            options.Quality.NextButton, options.Resolution.NextButton, options.DisplayMode.NextButton,
            options.VSync.NextButton, options.FrameLimit.NextButton, options.Sensitivity, back,
        };
        Navigate(column.ToArray());
        Navigate(rows.Concat(new Selectable[] { reset }).ToArray());
        first = options.Quality.NextButton;
        return panel;
    }

    internal static List<Selectable> BindingList(Kit kit, RectTransform column)
    {
        var bindings = new (string label, string action, string part)[]
        {
            ("Move Forward", "Player/Move", "up"),
            ("Move Back", "Player/Move", "down"),
            ("Strafe Left", "Player/Move", "left"),
            ("Strafe Right", "Player/Move", "right"),
            ("Jump", "Player/Jump", ""),
            ("Sprint / Dodge", "Player/Sprint", ""),
            ("Crouch", "Player/Crouch", ""),
            ("Slide", "Player/Slide", ""),
            ("Fire", "Player/Fire", ""),
            ("Aim", "Player/Aim", ""),
            ("Reload", "Player/Reload", ""),
            ("Execute Core", "Player/Interact", ""),
            ("Disassembler", "Player/Disassembler", ""),
            ("Next Weapon", "Player/NextWeapon", "negative"),
            ("Previous Weapon", "Player/NextWeapon", "positive"),
            ("Pause Menu", "UI/Menu", ""),
        };

        const float rowHeight = 54f;
        var view = Rect(column, "Scroll View", new Vector2(-10, -25), new Vector2(620, 520));
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        var viewport = Stretch(view, "Viewport");
        viewport.offsetMax = new Vector2(-30, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = new Color(0, 0, 0, 0); // catches the mouse wheel between rows
        var content = Rect(viewport, "Content", Vector2.zero, new Vector2(590, bindings.Length * rowHeight));
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0, bindings.Length * rowHeight);

        var selectables = new List<Selectable>();
        for (int i = 0; i < bindings.Length; i++)
        {
            var row = Rect(content, bindings[i].label, Vector2.zero, new Vector2(0, rowHeight - 6));
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(.5f, 1);
            row.anchoredPosition = new Vector2(0, -i * rowHeight);
            row.sizeDelta = new Vector2(0, rowHeight - 6);

            var label = Text(row, "Label", bindings[i].label, kit.Roboto, null, 27, k_Parchment, new Vector2(-120, 0), new Vector2(340, rowHeight));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var key = SmallButton(kit, row, "Key", "-", new Vector2(175, 0), new Vector2(230, 48));
            var rebind = row.gameObject.AddComponent<KeyRebindRow>();
            rebind.ActionPath = bindings[i].action;
            rebind.CompositePart = bindings[i].part;
            rebind.RebindButton = key;
            rebind.KeyLabel = key.GetComponentInChildren<TMP_Text>();
            selectables.Add(key);
        }

        // Thin copper scrollbar.
        var bar = Rect(view, "Scrollbar", Vector2.zero, new Vector2(18, 0));
        bar.anchorMin = new Vector2(1, 0);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(1, .5f);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(18, 0);
        var track = bar.gameObject.AddComponent<Image>();
        track.color = new Color(.08f, .05f, .04f, .75f);
        var handleArea = Stretch(bar, "Sliding Area");
        var handle = Stretch(handleArea, "Handle");
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = kit.ScrollHandle;
        handleImage.type = Image.Type.Sliced;
        handleImage.pixelsPerUnitMultiplier = 3f;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return selectables;
    }

    static void BuildEndScreen(string path, bool win)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var kit = LoadKit();
        var canvas = CreateCanvasRig();
        FullScreen<RawImage>(canvas, "Galaxy").material = win ? kit.Galaxy : kit.GalaxyLose;

        var screen = canvas.gameObject.AddComponent<EndScreen>();
        CoreLogo(kit, canvas, new Vector2(0, 270), 300f, win ? kit.GlowWin : kit.GlowLose, !win);
        var title = Text(canvas, "Title", win ? "YOU WIN" : "YOU LOSE", kit.Bebas, kit.BebasGold, 140, Color.white,
            new Vector2(0, 55), new Vector2(1200, 160));
        title.characterSpacing = 14;
        // Two lines tall: a scripted ending (the finale) replaces it with its own, longer line.
        screen.Subtitle = Text(canvas, "Subtitle", win ? "Every core busted. The facility is clear." : "Your core collapsed.",
            kit.Roboto, null, 38, win ? k_Cream : k_Ember, new Vector2(0, -60), new Vector2(1500, 110));
        screen.DifficultyLabel = Text(canvas, "Difficulty", "", kit.Roboto, null, 26, k_Parchment,
            new Vector2(0, -135), new Vector2(800, 40));

        var again = MenuButton(kit, canvas, "Play Again", "PLAY AGAIN", new Vector2(0, -225), new Vector2(520, 120));
        var menuButton = MenuButton(kit, canvas, "Main Menu", "MAIN MENU", new Vector2(0, -355), new Vector2(520, 120));
        UnityEventTools.AddPersistentListener(again.onClick, screen.PlayAgain);
        UnityEventTools.AddPersistentListener(menuButton.onClick, screen.MainMenu);
        Navigate(again, menuButton);
        screen.FirstSelection = again;
        screen.MenuSceneName = Path.GetFileNameWithoutExtension(MenuScenePath);
        screen.Fader = CreateFader(canvas);

        EditorSceneManager.SaveScene(scene, path);
    }

    // ------------------------------------------------------------------ widgets

    static RectTransform CreateCanvasRig()
    {
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.gameObject.AddComponent<AudioListener>();

        var canvasObject = new GameObject("Canvas", typeof(RectTransform));
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        var events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        return (RectTransform)canvasObject.transform;
    }

    static SceneFader CreateFader(RectTransform canvas)
    {
        var overlay = FullScreen<Image>(canvas, "Fade");
        overlay.color = Color.black;
        var group = overlay.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        var fader = canvas.gameObject.AddComponent<SceneFader>();
        fader.Overlay = group;
        return fader;
    }

    static RectTransform CoreLogo(Kit kit, RectTransform parent, Vector2 position, float size, Material glowMaterial, bool unstable)
    {
        var root = Rect(parent, "Core Logo", position, new Vector2(size, size));
        var glow = Rect(root, "Glow", Vector2.zero, new Vector2(size * 2.3f, size * 2.3f)).gameObject.AddComponent<Image>();
        glow.material = glowMaterial;
        glow.raycastTarget = false;
        var logo = Rect(root, "Logo", Vector2.zero, new Vector2(size, size));
        var logoImage = logo.gameObject.AddComponent<Image>();
        logoImage.sprite = kit.Logo;
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;
        if (unstable) logoImage.color = new Color(1f, .78f, .78f);

        var animator = root.gameObject.AddComponent<CoreLogoGlow>();
        animator.Glow = glow;
        animator.Logo = logo;
        return root;
    }

    internal static Button MenuButton(Kit kit, RectTransform parent, string name, string label, Vector2 position, Vector2 size)
    {
        var button = SpriteButton(parent, name, position, size, kit.ButtonNormal, kit.ButtonHover, kit.ButtonPressed, 1.6f);
        var text = Text(button.transform, "Label", label, kit.Bebas, kit.BebasGold, Mathf.Min(58f, size.y * .5f), Color.white,
            Vector2.zero, size - new Vector2(120, 16));
        text.characterSpacing = 6;
        text.lineSpacing = -18;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20;
        text.fontSizeMax = text.fontSize;
        button.gameObject.AddComponent<MenuButtonFX>();
        return button;
    }

    internal static Button SmallButton(Kit kit, RectTransform parent, string name, string label, Vector2 position, Vector2 size)
    {
        var button = SpriteButton(parent, name, position, size, kit.KeyNormal, kit.KeyHover, kit.KeyPressed, 3.2f);
        Text(button.transform, "Label", label, kit.Bebas, null, 30, k_Cream, new Vector2(8, 0), size - new Vector2(40, 6));
        button.gameObject.AddComponent<MenuButtonFX>().HoverScale = 1.04f;
        return button;
    }

    internal static Button SpriteButton(RectTransform parent, string name, Vector2 position, Vector2 size,
        Sprite normal, Sprite hover, Sprite pressed, float pixelsPerUnit)
    {
        var rect = Rect(parent, name, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = normal;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = pixelsPerUnit;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = hover, selectedSprite = hover, pressedSprite = pressed, disabledSprite = normal,
        };
        return button;
    }

    internal static void Header(Kit kit, RectTransform column, string label, float y)
    {
        var bar = Rect(column, label + " Header", new Vector2(0, y), new Vector2(600, 62)).gameObject.AddComponent<Image>();
        bar.sprite = kit.HeaderBar;
        bar.type = Image.Type.Sliced;
        bar.pixelsPerUnitMultiplier = 2.2f;
        Text(bar.transform, "Label", label, kit.Bebas, kit.BebasGold, 40, Color.white, new Vector2(0, 2), new Vector2(480, 56))
            .characterSpacing = 8;
    }

    static UIStepper Stepper(Kit kit, RectTransform column, string name, string label, float y)
    {
        var row = Rect(column, name, new Vector2(0, y), new Vector2(600, 58));
        var text = Text(row, "Label", label, kit.Roboto, null, 29, k_Parchment, new Vector2(-195, 0), new Vector2(210, 58));
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var valueBack = Rect(row, "Value", new Vector2(105, 0), new Vector2(260, 46)).gameObject.AddComponent<Image>();
        valueBack.sprite = kit.ValueBar;
        var value = Text(valueBack.transform, "Text", "-", kit.Roboto, null, 24, k_Cream, Vector2.zero, new Vector2(250, 44));
        value.enableAutoSizing = true;
        value.fontSizeMin = 16;
        value.fontSizeMax = 24;

        var previous = SpriteButton(row, "Previous", new Vector2(-55, 0), new Vector2(54, 44), kit.ArrowNormal, kit.ArrowHover, kit.ArrowPressed, 1f);
        previous.transform.localScale = new Vector3(-1, 1, 1);
        ((Image)previous.targetGraphic).type = Image.Type.Simple;
        var next = SpriteButton(row, "Next", new Vector2(265, 0), new Vector2(54, 44), kit.ArrowNormal, kit.ArrowHover, kit.ArrowPressed, 1f);
        ((Image)next.targetGraphic).type = Image.Type.Simple;

        var stepper = row.gameObject.AddComponent<UIStepper>();
        stepper.PreviousButton = previous;
        stepper.NextButton = next;
        stepper.ValueLabel = value;
        // Left/right on the focused arrow steps the value, up/down moves between rows.
        next.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = previous };
        previous.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = next };
        return stepper;
    }

    internal static Slider SensitivitySlider(Kit kit, RectTransform column, float y, out TMP_Text valueLabel)
    {
        var row = Rect(column, "Sensitivity", new Vector2(0, y), new Vector2(600, 58));
        var text = Text(row, "Label", "Sensitivity", kit.Roboto, null, 29, k_Parchment, new Vector2(-195, 0), new Vector2(210, 58));
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var sliderRect = Rect(row, "Slider", new Vector2(85, 0), new Vector2(280, 30));
        var back = Stretch(sliderRect, "Background").gameObject.AddComponent<Image>();
        back.sprite = kit.SliderBack;
        var fillArea = Stretch(sliderRect, "Fill Area");
        fillArea.offsetMin = new Vector2(4, 4);
        fillArea.offsetMax = new Vector2(-4, -4);
        var fill = Stretch(fillArea, "Fill");
        fill.gameObject.AddComponent<Image>().sprite = kit.SliderFill;
        var frame = Stretch(sliderRect, "Frame").gameObject.AddComponent<Image>();
        frame.sprite = kit.SliderFrame;
        frame.raycastTarget = false;
        ((RectTransform)frame.transform).offsetMin = new Vector2(-10, -6);
        ((RectTransform)frame.transform).offsetMax = new Vector2(10, 6);
        var handleArea = Stretch(sliderRect, "Handle Slide Area");
        var handle = Rect(handleArea, "Handle", Vector2.zero, new Vector2(46, 42));
        var knob = handle.gameObject.AddComponent<Image>();
        knob.sprite = kit.SliderKnob;
        knob.preserveAspect = true;

        var slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = knob;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = GameSettings.MinSensitivity;
        slider.maxValue = GameSettings.MaxSensitivity;
        slider.value = GameSettings.DefaultSensitivity;
        slider.transition = Selectable.Transition.ColorTint;
        var colors = slider.colors;
        colors.highlightedColor = new Color(1f, .85f, .55f);
        colors.selectedColor = new Color(1f, .85f, .55f);
        slider.colors = colors;

        valueLabel = Text(row, "Value", "1.00", kit.Roboto, null, 26, k_Cream, new Vector2(282, 0), new Vector2(80, 50));
        return slider;
    }

    // ------------------------------------------------------------------ layout helpers

    internal static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    internal static RectTransform Stretch(Transform parent, string name)
    {
        var rect = Rect(parent, name, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    internal static T FullScreen<T>(Transform parent, string name) where T : Graphic
    {
        var graphic = Stretch(parent, name).gameObject.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, Material material,
        float size, Color color, Vector2 position, Vector2 box)
    {
        var tmp = Rect(parent, name, position, box).gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        if (material) tmp.fontSharedMaterial = material;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    internal static void Navigate(params Selectable[] column)
    {
        for (int i = 0; i < column.Length; i++)
        {
            var nav = column[i].navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = column[(i + column.Length - 1) % column.Length];
            nav.selectOnDown = column[(i + 1) % column.Length];
            column[i].navigation = nav;
        }
    }

    internal static void NavigateHorizontal(Selectable left, Selectable right)
    {
        left.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = right };
        right.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = left };
    }
}
