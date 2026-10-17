using System.Linq;
using TMPro;
using Unity.FPS.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using K = MenuSceneBuilder;

/// <summary>
/// Reskins the in-game HUD and pause menu with the Gentleland Steampunk UI pack, matching the main
/// menu: brass-framed health, jetpack and ammo gauges, a brass reticle, a framed compass, themed toasts,
/// and a rebuilt pause menu (Resume / Controls / Main Menu, settings, and in-game key rebinding).
/// Edits the UI prefabs in place, so every level using the GameManager prefab picks it up. Safe to re-run.
/// </summary>
public static class HudSkinBuilder
{
    const string UIFolder = "Assets/FPS/Prefabs/UI/";
    const string HudPath = UIFolder + "GameHUD.prefab";
    const string AmmoPath = UIFolder + "AmmoCounter.prefab";
    const string PauseMenuPath = UIFolder + "InGameMenu.prefab";

    static readonly Color k_Icon = new Color(1f, .88f, .68f);
    static readonly Color k_EmptyFlash = new Color(1f, .35f, .3f);

    [MenuItem("Tools/CoreBuster/Reskin HUD and Pause Menu")]
    public static void BuildAll()
    {
        Edit(AmmoPath, SkinAmmoCounter);
        Edit(HudPath, SkinHud);
        Edit(UIFolder + "NotificationToast.prefab", SkinNotification);
        Edit(UIFolder + "ObjectiveToastPrimary.prefab", SkinObjective);
        Edit(UIFolder + "ObjectiveToastSecondary.prefab", SkinObjective);
        Edit(UIFolder + "GameMessageGeneric.prefab", SkinGameMessage);
        Edit(UIFolder + "CompassMarkerDirection.prefab", SkinCompassMarker);
        Edit(PauseMenuPath, BuildPauseMenu);
        AssetDatabase.SaveAssets();
        Debug.Log("HUD and pause menu reskinned.");
    }

    static void Edit(string path, System.Action<GameObject, K.Kit> skin)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var kit = K.LoadKit();
            if (kit == null) return;
            skin(root, kit);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ HUD

    static void SkinHud(GameObject root, K.Kit kit)
    {
        var theme = Get<HudTheme>(root);
        theme.TitleFont = kit.Bebas;
        theme.TitleMaterial = kit.BebasGold;
        theme.BodyFont = kit.Roboto;

        // Health: the core as the gauge's dial, a copper fill in a gold frame, and a readout.
        var health = Find(root, "PlayerHealth");
        Size(health, 250, 34);
        Gauge(kit, health, Find(root, "BackgroundImage", health), Find(root, "FillImageHealth", health), kit.SliderFill, 30f);
        var icon = Find(root, "HealthIcon", health);
        var iconImage = icon.GetComponent<Image>();
        iconImage.sprite = kit.Logo;
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        Place(icon, new Vector2(0f, .5f), new Vector2(14f, 0f), new Vector2(42f, 42f));
        icon.SetAsLastSibling();
        var healthText = Child<TextMeshProUGUI>(health, "HealthText");
        Fill(healthText.rectTransform, new Vector2(34f, 0f), new Vector2(-12f, 0f));
        Style(healthText, kit.Bebas, null, 22f, K.k_Cream);
        healthText.alignment = TextAlignmentOptions.MidlineRight;
        healthText.text = "100";
        var healthBar = root.GetComponent<PlayerHealthBar>();
        healthBar.HealthText = healthText;
        healthBar.HealthyColor = Color.white;
        healthBar.CriticalColor = new Color(1f, .3f, .22f);

        var stance = Find(root, "Stance");
        Size(stance, 34, 34);
        var stanceBack = stance.GetComponent<Image>();
        stanceBack.sprite = K.PackSprite("Slots_Arrows", 12);
        stanceBack.color = Color.white;
        var stanceIcon = Find(root, "StanceIcon", stance);
        Place(stanceIcon, new Vector2(.5f, .5f), Vector2.zero, new Vector2(24f, 24f));
        stanceIcon.GetComponent<Image>().color = k_Icon;
        Size(Find(root, "HorizontalContent"), 289, 34);

        // Jetpack: a silver steam gauge in the same gold frame.
        var jetpack = Find(root, "JetpackCounter");
        Size(jetpack, 180, 22);
        Gauge(kit, jetpack, Find(root, "BackgroundImage", jetpack), Find(root, "FillImageJetpack", jetpack),
            K.PackSprite("Dialogue_2", 2), 3f);
        Find(root, "JetpackIcon", jetpack).GetComponent<Image>().color = k_Icon;
        Flash(jetpack.GetComponent<FillBarColorChange>(), new Color(.6f, .92f, 1f));

        DisassemblerGauge(kit, (RectTransform)jetpack.parent);

        // Crosshair: a brass ring with a fixed centre pip; the weapon still sets its size.
        var crosshairs = root.GetComponent<CrosshairManager>();
        crosshairs.ThemeSprite = K.PackSprite("Slots_Arrows", 13);
        var pip = Child<Image>(Find(root, "Crosshair"), "Centre Pip");
        pip.sprite = K.PackSprite("Slots_Arrows", 14);
        pip.color = new Color(1f, .9f, .7f, .9f);
        pip.raycastTarget = false;
        Place(pip.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(4f, 4f));

        // Compass: a dark brass-framed strip.
        var compassBack = Find(root, "CompassBackground").GetComponent<Image>();
        compassBack.sprite = kit.FooterFill;
        compassBack.color = new Color(1f, 1f, 1f, .8f);
        Find(root, "LineImage").GetComponent<Image>().color = new Color(1f, .8f, .45f, .45f);
        var compassFrame = Child<Image>(Find(root, "Compass"), "Frame");
        compassFrame.sprite = kit.SliderFrame;
        compassFrame.raycastTarget = false;
        Fill(compassFrame.rectTransform, Vector2.zero, Vector2.zero);

        // Pause hint and framerate readout.
        var pauseInfo = Find(root, "PauseMenuInfo");
        var tab = Find(root, "TABImage", pauseInfo).GetComponent<Image>();
        tab.sprite = kit.ScrollHandle;
        tab.type = Image.Type.Sliced;
        tab.pixelsPerUnitMultiplier = 6f;
        tab.color = Color.white;
        foreach (var text in pauseInfo.GetComponentsInChildren<TMP_Text>(true))
        {
            bool key = text.transform.parent == tab.transform;
            Style(text, key ? kit.Bebas : kit.Roboto, null, key ? 13f : 11f, key ? K.k_Cream : K.k_Parchment);
        }
        Style(Find(root, "FramerateCounter").GetComponent<TMP_Text>(), kit.Roboto, null, 16f, K.k_Cream);
    }

    // The Disassembler's charge: a gold-framed gauge split into one cell per execution, above the health bar.
    static void DisassemblerGauge(K.Kit kit, RectTransform corner)
    {
        const int cells = 3;
        var existing = corner.Find("DisassemblerCharge");
        if (existing) Object.DestroyImmediate(existing.gameObject);

        var root = K.Rect(corner, "DisassemblerCharge", Vector2.zero, new Vector2(250f, 44f));
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.SetAsFirstSibling();
        var body = K.Stretch(root, "Body");

        var label = K.Text(body, "Label", "DISASSEMBLER", kit.Bebas, null, 17f, Color.white, Vector2.zero, Vector2.zero);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.characterSpacing = 4f;
        Fill(label.rectTransform, new Vector2(4f, 22f), new Vector2(0f, 0f));
        var status = K.Text(body, "Status", "0/3 CORES", kit.Bebas, null, 15f, K.k_Cream, Vector2.zero, Vector2.zero);
        status.alignment = TextAlignmentOptions.BottomRight;
        status.characterSpacing = 2f;
        Fill(status.rectTransform, new Vector2(0f, 22f), new Vector2(-4f, 0f));

        var bar = K.Rect(body, "Bar", Vector2.zero, Vector2.zero);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 2f);
        bar.sizeDelta = new Vector2(0f, 18f);
        var background = K.Stretch(bar, "BackgroundImage");
        background.gameObject.AddComponent<Image>().raycastTarget = false;
        var fill = K.Stretch(bar, "FillImage");
        fill.gameObject.AddComponent<Image>().raycastTarget = false;
        // The silver fill (as the jetpack's) takes the gauge's blue and yellow tints cleanly.
        Gauge(kit, bar, background, fill, K.PackSprite("Dialogue_2", 2), 3f);

        // Dividers between the cells, over the fill but under the frame.
        for (int i = 1; i < cells; i++)
        {
            var tick = K.Rect(bar, "Tick " + i, Vector2.zero, new Vector2(3f, -6f)).gameObject.AddComponent<Image>();
            tick.color = new Color(.1f, .07f, .05f, .9f);
            tick.raycastTarget = false;
            tick.rectTransform.anchorMin = new Vector2(i / (float)cells, 0f);
            tick.rectTransform.anchorMax = new Vector2(i / (float)cells, 1f);
            tick.rectTransform.anchoredPosition = Vector2.zero;
            tick.transform.SetSiblingIndex(fill.GetSiblingIndex() + 1);
        }

        var hud = root.gameObject.AddComponent<DisassemblerChargeHUD>();
        hud.Body = body;
        hud.Fill = fill.GetComponent<Image>();
        hud.Frame = bar.Find("Frame").GetComponent<Image>();
        hud.Label = label;
        hud.Status = status;
    }

    static void SkinAmmoCounter(GameObject root, K.Kit kit)
    {
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(210f, 30f);
        Gauge(kit, rect, Find(root, "BackgroundImage"), Find(root, "FillImageAmmo"), kit.SliderFill, 3f);
        Flash(root.GetComponent<FillBarColorChange>(), Color.white);

        foreach (var name in new[] { "Bullet Number", "Image Number", "Reload" })
        {
            var box = Find(root, name).GetComponent<Image>();
            box.sprite = kit.ScrollHandle;
            box.type = Image.Type.Sliced;
            box.pixelsPerUnitMultiplier = 8f;
            box.color = Color.white;
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            bool label = text.name == "Reload";
            Style(text, kit.Bebas, null, label ? 18f : Mathf.Min(text.fontSize, 16f), label ? K.k_Ember : K.k_Cream);
        }
        Find(root, "WeaponIcon").GetComponent<Image>().color = k_Icon;
        Size(Find(root, "CanvasGroupIgnoreParent"), 200, 30);
    }

    // Background, inset fill and a gold frame on top, from the pack's gold bar (Bar 9).
    static void Gauge(K.Kit kit, RectTransform bar, RectTransform background, RectTransform fill, Sprite fillSprite, float insetLeft)
    {
        var back = background.GetComponent<Image>();
        back.sprite = kit.SliderBack;
        back.type = Image.Type.Simple;
        back.color = Color.white;
        Fill(background, Vector2.zero, Vector2.zero);

        var fillImage = fill.GetComponent<Image>();
        fillImage.sprite = fillSprite;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.color = Color.white;
        Fill(fill, new Vector2(insetLeft, 3f), new Vector2(-3f, -3f));

        var frame = Child<Image>(bar, "Frame");
        frame.sprite = kit.SliderFrame;
        frame.raycastTarget = false;
        Fill(frame.rectTransform, new Vector2(-6f, -3f), new Vector2(6f, 3f));
        frame.transform.SetSiblingIndex(fill.GetSiblingIndex() + 1);
    }

    static void Flash(FillBarColorChange colors, Color foreground)
    {
        if (!colors) return;
        colors.DefaultForegroundColor = foreground;
        colors.FlashForegroundColorFull = new Color(1f, 1f, .8f);
        colors.DefaultBackgroundColor = Color.white;
        colors.FlashBackgroundColorEmpty = k_EmptyFlash;
    }

    static void SkinNotification(GameObject root, K.Kit kit)
    {
        var back = Find(root, "BackgroundImage").GetComponent<Image>();
        back.sprite = kit.FooterFill;
        back.color = new Color(1f, 1f, 1f, .92f);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            Style(text, kit.Roboto, null, text.fontSize, K.k_Parchment);
    }

    static void SkinObjective(GameObject root, K.Kit kit)
    {
        var title = Find(root, "BackgroundImageTitle").GetComponent<Image>();
        title.sprite = kit.FooterFill;
        title.color = new Color(1f, 1f, 1f, .92f);
        Find(root, "BackgroundImageSubTitle").GetComponent<Image>().color = new Color(.12f, .08f, .06f, .7f);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            // Titles keep their colour coding (amber primary, teal secondary) in the display font.
            if (text.name.StartsWith("TitleText")) Style(text, kit.Bebas, null, text.fontSize * 1.25f, text.color);
            else Style(text, kit.Roboto, null, text.fontSize, K.k_Parchment);
        }
    }

    static void SkinGameMessage(GameObject root, K.Kit kit)
    {
        Find(root, "BackgroundImage").GetComponent<Image>().color = new Color(.14f, .07f, .03f, .6f);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            Style(text, kit.Bebas, kit.BebasGold, text.fontSize, Color.white);
    }

    static void SkinCompassMarker(GameObject root, K.Kit kit)
    {
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            Style(text, kit.Bebas, null, 16f, K.k_Cream);
    }

    // ------------------------------------------------------------------ pause menu

    static void BuildPauseMenu(GameObject root, K.Kit kit)
    {
        var menu = root.GetComponent<InGameMenuManager>();
        var canvas = (RectTransform)menu.MenuRoot.transform;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        var dim = Find(root, "Background");
        if (dim) dim.GetComponent<Image>().color = new Color(.02f, .01f, .04f, .72f);

        // The old grid panel, controls picture and screenshot tool are replaced outright.
        foreach (var name in new[] { "MenuPanel", "ControlsImage" })
            while (Find(root, name)) Object.DestroyImmediate(Find(root, name).gameObject);
        var screenshot = root.GetComponent<TakeScreenshot>();
        if (screenshot) Object.DestroyImmediate(screenshot);

        // Main pause panel.
        var panel = Panel(kit, canvas, "MenuPanel", new Vector2(1180f, 760f), "PAUSED");
        var left = K.Rect(panel, "Actions", new Vector2(-290f, 0f), new Vector2(480f, 760f));
        var resume = K.MenuButton(kit, left, "Resume", "RESUME", new Vector2(0f, 150f), new Vector2(440f, 110f));
        var controls = K.MenuButton(kit, left, "Controls", "CONTROLS", new Vector2(0f, 20f), new Vector2(440f, 110f));
        var exit = K.MenuButton(kit, left, "Main Menu", "MAIN MENU", new Vector2(0f, -110f), new Vector2(440f, 110f));
        UnityEventTools.AddPersistentListener(resume.onClick, menu.ClosePauseMenu);
        UnityEventTools.AddBoolPersistentListener(controls.onClick, menu.OnShowControlButtonClicked, true);
        UnityEventTools.AddPersistentListener(exit.onClick, menu.QuitToMainMenu);
        menu.DifficultyLabel = K.Text(left, "Difficulty", "", kit.Roboto, null, 26f, K.k_Parchment,
            new Vector2(0f, -235f), new Vector2(440f, 40f));

        var right = K.Rect(panel, "Settings", new Vector2(265f, 0f), new Vector2(600f, 760f));
        K.Header(kit, right, "SETTINGS", 200f);
        menu.LookSensitivitySlider = K.SensitivitySlider(kit, right, 120f, out TMP_Text sensitivityValue);
        var valueLabel = menu.LookSensitivitySlider.gameObject.AddComponent<SliderValueLabel>();
        valueLabel.Slider = menu.LookSensitivitySlider;
        valueLabel.Label = sensitivityValue;
        menu.ShadowsToggle = GearToggle(kit, right, "Shadows", 45f);
        menu.InvincibilityToggle = GearToggle(kit, right, "Invincible", -30f);
        menu.FramerateToggle = GearToggle(kit, right, "Show FPS", -105f);
        K.Navigate(resume, controls, exit, menu.LookSensitivitySlider, menu.ShadowsToggle, menu.InvincibilityToggle,
            menu.FramerateToggle);

        // Controls: the same rebindable key list as the main menu's Options, over the pause panel.
        var controlsRoot = K.Stretch(canvas, "ControlsImage");
        var backdrop = controlsRoot.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, .55f);
        var controlsPanel = Panel(kit, controlsRoot, "Controls Panel", new Vector2(1000f, 900f), "CONTROLS");
        var column = K.Rect(controlsPanel, "Key Bindings", new Vector2(0f, -25f), new Vector2(660f, 880f));
        K.Header(kit, column, "KEY BINDINGS", 280f);
        var rows = K.BindingList(kit, column);
        var back = K.MenuButton(kit, controlsPanel, "Back", "BACK", new Vector2(-200f, -372f), new Vector2(320f, 84f));
        var reset = K.MenuButton(kit, controlsPanel, "Reset Keys", "RESET KEYS", new Vector2(200f, -372f), new Vector2(360f, 84f));
        UnityEventTools.AddBoolPersistentListener(back.onClick, menu.OnShowControlButtonClicked, false);
        UnityEventTools.AddPersistentListener(reset.onClick, menu.ResetKeyBindings);
        K.Navigate(rows.Concat(new Selectable[] { back, reset }).ToArray());
        menu.ControlImage = controlsRoot.gameObject;
        controlsRoot.gameObject.SetActive(false);
    }

    // A pipe-framed slate panel with a title on its copper bar, like the main menu's Options.
    static RectTransform Panel(K.Kit kit, RectTransform parent, string name, Vector2 size, string title)
    {
        var panel = K.Rect(parent, name, Vector2.zero, size);
        var plate = K.Rect(panel, "Plate", new Vector2(0f, -20f), size - new Vector2(90f, 90f)).gameObject.AddComponent<Image>();
        plate.sprite = kit.OptionsPlate;
        plate.color = new Color(.78f, .76f, .82f, .97f);
        var frame = K.Stretch(panel, "Frame").gameObject.AddComponent<Image>();
        frame.sprite = kit.OptionsFrame;
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 1.1f;
        frame.raycastTarget = false;
        K.Text(panel, "Title", title, kit.Bebas, kit.BebasGold, 64f, Color.white, new Vector2(0f, size.y * .5f - 48f),
            new Vector2(600f, 80f)).characterSpacing = 12;
        return panel;
    }

    // A labelled on/off switch: a grey gear that turns copper when on.
    static Toggle GearToggle(K.Kit kit, RectTransform column, string label, float y)
    {
        var row = K.Rect(column, label, new Vector2(0f, y), new Vector2(600f, 62f));
        var text = K.Text(row, "Label", label, kit.Roboto, null, 29f, K.k_Parchment, new Vector2(-150f, 0f), new Vector2(300f, 58f));
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var background = K.Rect(row, "Gear", new Vector2(180f, 0f), new Vector2(58f, 58f)).gameObject.AddComponent<Image>();
        background.sprite = K.PackSprite("settings_icons", 11);
        background.preserveAspect = true;
        var check = K.Stretch(background.transform, "On").gameObject.AddComponent<Image>();
        check.sprite = K.PackSprite("settings_icons", 8);
        check.preserveAspect = true;
        check.raycastTarget = false;

        var toggle = row.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = check;
        var colors = toggle.colors;
        colors.highlightedColor = new Color(1f, .88f, .6f);
        colors.selectedColor = new Color(1f, .88f, .6f);
        toggle.colors = colors;
        row.gameObject.AddComponent<MenuButtonFX>().HoverScale = 1.03f;
        return toggle;
    }

    // ------------------------------------------------------------------ helpers

    static RectTransform Find(GameObject root, string name, Transform within = null)
    {
        var scope = within ? within : root.transform;
        return scope.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == name);
    }

    // Unity's "missing" components aren't C# null, so no ?? here.
    static T Get<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component ? component : go.AddComponent<T>();
    }

    // Finds or creates a child UI element with the component, so re-running doesn't stack copies.
    static T Child<T>(Transform parent, string name) where T : Component
    {
        var existing = parent.Find(name);
        if (existing) return Get<T>(existing.gameObject);
        var rect = K.Rect(parent, name, Vector2.zero, Vector2.zero);
        return rect.gameObject.AddComponent<T>();
    }

    static void Size(RectTransform rect, float width, float height) => rect.sizeDelta = new Vector2(width, height);

    static void Fill(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Style(TMP_Text text, TMP_FontAsset font, Material material, float size, Color color)
    {
        text.font = font;
        if (material) text.fontSharedMaterial = material;
        text.fontSize = size;
        text.color = color;
    }
}
