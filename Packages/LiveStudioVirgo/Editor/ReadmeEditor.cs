using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;
using System;
using System.IO;

namespace Lilium.LiveStudio.Virgo.Editor
{
[CustomEditor(typeof(Readme))]
[InitializeOnLoad]
public class ReadmeEditor : UnityEditor.Editor
{
    // The readme is a first-run welcome, so the "already shown" flag has to outlive the editor
    // session (SessionState would show it again on every launch). EditorPrefs is per user/machine
    // and shared by every project, so the project is folded into the key: a different project still
    // gets its own welcome. The prefix is package specific so packages never share the flag.
    const string k_ShownPrefKeyPrefix = "Lilium.LiveStudioVirgo.ReadmeShown.";

    const float k_Space = 16f;

    const string k_LanguagePrefKey = "Lilium.LiveStudioVirgo.ReadmeLanguage";
    static readonly string[] k_LanguageCodes = { "en", "ja", "zh-CN" };
    static readonly string[] k_LanguageLabels = { "English", "日本語", "中文" };

    static string CurrentLanguage
    {
        get
        {
            var stored = EditorPrefs.GetString(k_LanguagePrefKey, "");
            if (!string.IsNullOrEmpty(stored))
                return stored;
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese:
                    return "ja";
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.Chinese:
                    return "zh-CN";
                default:
                    return "en";
            }
        }
        set { EditorPrefs.SetString(k_LanguagePrefKey, value); }
    }

    static int LanguageIndex
    {
        get
        {
            var lang = CurrentLanguage;
            for (int i = 0; i < k_LanguageCodes.Length; i++)
            {
                if (k_LanguageCodes[i] == lang)
                    return i;
            }
            return 0;
        }
    }

    static ReadmeEditor()
    {
        EditorApplication.delayCall += SelectReadmeAutomatically;
    }

    static void SelectReadmeAutomatically()
    {
        var key = ShownPrefKey;
        if (EditorPrefs.GetBool(key, false))
            return;

        // Only remember it as shown once the asset was really found and selected, so a project where
        // the lookup fails (asset not imported yet, for instance) still gets its welcome later.
        if (SelectReadme() != null)
            EditorPrefs.SetBool(key, true);
    }

    static string ShownPrefKey
    {
        get
        {
            // Application.dataPath is "<project>/Assets". Hashed so the key stays a fixed length.
            var projectPath = (Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath)
                .Replace('\\', '/');
            return k_ShownPrefKeyPrefix + Hash128.Compute(projectPath);
        }
    }

    static Readme SelectReadme()
    {
        // Other packages and Unity's own project templates ship their own classes named Readme, so
        // "t:Readme" alone is ambiguous. Load each candidate and keep the first one that actually is
        // this package's Readme.
        foreach (var id in AssetDatabase.FindAssets("t:Readme"))
        {
            var readme = AssetDatabase.LoadAssetAtPath<Readme>(AssetDatabase.GUIDToAssetPath(id));
            if (readme == null)
                continue;

            Selection.objects = new UnityEngine.Object[] { readme };
            return readme;
        }
        return null;
    }

    protected override void OnHeaderGUI()
    {
        var readme = (Readme)target;
        Init();

        var iconWidth = Mathf.Min(EditorGUIUtility.currentViewWidth / 3f - 20f, 128f);

        GUILayout.BeginHorizontal("In BigTitle");
        {
            if (readme.icon != null)
            {
                GUILayout.Space(k_Space);
                GUILayout.Label(readme.icon, GUILayout.Width(iconWidth), GUILayout.Height(iconWidth));
            }
            GUILayout.Space(k_Space);
            GUILayout.BeginVertical();
            {

                GUILayout.FlexibleSpace();
                GUILayout.Label(ResolveTitle(readme, CurrentLanguage), TitleStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();
    }

    public override void OnInspectorGUI()
    {
        var readme = (Readme)target;
        Init();

        // The Readme ships inside the package, so when the package is consumed as an immutable
        // (registry / git) dependency Unity draws its inspector read-only (GUI.enabled = false),
        // which would make the language toggle, section actions and Install buttons unclickable.
        // This inspector only triggers actions and never edits the asset's serialized fields, so
        // force-enable the GUI to keep the buttons usable regardless of the package's mutability.
        GUI.enabled = true;

        var currentIndex = LanguageIndex;
        var newIndex = GUILayout.Toolbar(currentIndex, k_LanguageLabels);
        if (newIndex != currentIndex)
        {
            CurrentLanguage = k_LanguageCodes[newIndex];
            Repaint();
        }
        GUILayout.Space(k_Space);

        var lang = CurrentLanguage;
        foreach (var section in readme.sections)
        {
            var resolved = ResolveSection(section, lang);

            if (!string.IsNullOrEmpty(resolved.heading))
            {
                GUILayout.Label(resolved.heading, HeadingStyle);
            }

            if (!string.IsNullOrEmpty(resolved.text))
            {
                GUILayout.Label(resolved.text, BodyStyle);
            }

            if (!string.IsNullOrEmpty(resolved.linkText))
            {
                if (LinkLabel(new GUIContent(resolved.linkText)))
                {
                    Application.OpenURL(section.url);
                }
            }

            if (!string.IsNullOrEmpty(resolved.buttonText))
            {
                if (GUILayout.Button(resolved.buttonText, ButtonStyle, GUILayout.ExpandWidth(false)))
                {
                    InvokeSectionAction(section.buttonAction);
                }
            }

            GUILayout.Space(k_Space);
        }

        DrawProjectSettings(lang);

        DrawOptionalPackages(readme, lang);
    }

    void OnEnable()
    {
        OptionalPackageInstaller.RefreshInstalled(Repaint);
    }

    static string Localize(string lang, string en, string ja, string zh)
    {
        switch (lang)
        {
            case "ja": return ja;
            case "zh-CN": return zh;
            default: return en;
        }
    }

    void DrawOptionalPackages(Readme readme, string lang)
    {
        var packages = readme.optionalPackages;
        if (packages == null || packages.Length == 0)
            return;

        GUILayout.Label(Localize(lang, "Optional Packages", "オプションパッケージ", "可选软件包"), HeadingStyle);
        GUILayout.Label(
            Localize(lang,
                "Install the following packages as needed.",
                "必要に合わせて以下のパッケージをインストールしてください。",
                "请根据需要安装以下软件包。"),
            BodyStyle);
        GUILayout.Space(4f);

        EditorGUI.BeginDisabledGroup(OptionalPackageInstaller.IsBusy);

        var known = OptionalPackageInstaller.IsInstalledKnown;
        var missing = new List<Readme.OptionalPackage>();
        foreach (var pkg in packages)
        {
            if (pkg == null || string.IsNullOrEmpty(pkg.id))
                continue;

            GUILayout.BeginHorizontal();
            var label = string.IsNullOrEmpty(pkg.displayName) ? pkg.id : pkg.displayName;
            if (!string.IsNullOrEmpty(pkg.version))
                label += " " + pkg.version;
            GUILayout.Label(label, BodyStyle);
            GUILayout.FlexibleSpace();

            if (!known)
            {
                GUILayout.Label(Localize(lang, "Checking...", "確認中...", "检查中..."), BodyStyle);
            }
            else if (OptionalPackageInstaller.IsInstalled(pkg.id))
            {
                GUILayout.Label(Localize(lang, "Installed", "インストール済み", "已安装"), BodyStyle);
            }
            else
            {
                missing.Add(pkg);
                if (GUILayout.Button(Localize(lang, "Install", "インストール", "安装"), ButtonStyle, GUILayout.ExpandWidth(false)))
                {
                    OptionalPackageInstaller.Install(new[] { pkg }, Repaint);
                }
            }
            GUILayout.EndHorizontal();
        }

        if (known && missing.Count > 0)
        {
            GUILayout.Space(4f);
            if (GUILayout.Button(Localize(lang, "Install All", "すべてインストール", "全部安装"), ButtonStyle, GUILayout.ExpandWidth(false)))
            {
                OptionalPackageInstaller.Install(missing.ToArray(), Repaint);
            }
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.Space(k_Space);
    }

    // Draws the "Project Settings Verification" section. Each project-readiness check is a sub-item
    // under this heading; more checks are expected to be added here over time.
    void DrawProjectSettings(string lang)
    {
        GUILayout.Label(
            Localize(lang, "Project Settings Verification", "プロジェクト設定の検証", "项目设置验证"),
            HeadingStyle);

        DrawBackgroundInputCheck(lang);
    }

    // Verifies the project settings required for gamepad input to keep working while the built app
    // is not the foreground window. Two settings must be enabled together:
    //   - Player Settings "Run In Background" so the player loop keeps running without focus.
    //   - Input System "Background Behavior = Ignore Focus" so the Input System does not disable
    //     devices when focus is lost.
    // When either is off, a warning and a one-click fix button are shown.
    void DrawBackgroundInputCheck(string lang)
    {
        bool runInBackground = PlayerSettings.runInBackground;
        bool ignoreFocus = InputSystem.settings != null &&
            InputSystem.settings.backgroundBehavior == InputSettings.BackgroundBehavior.IgnoreFocus;
        bool ok = runInBackground && ignoreFocus;

        // Item title on the left, status/fix button right-aligned (same layout as Optional Packages).
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            Localize(lang, "Gamepad Background Input", "ゲームパッドのバックグラウンド入力", "手柄后台输入"),
            BodyStyle);
        GUILayout.FlexibleSpace();
        if (ok)
        {
            GUILayout.Label(Localize(lang, "Enabled", "有効", "已启用"), BodyStyle);
        }
        else if (GUILayout.Button(
                     Localize(lang, "Enable", "有効化", "启用"),
                     ButtonStyle, GUILayout.ExpandWidth(false)))
        {
            EnableBackgroundInput();
            Repaint();
        }
        GUILayout.EndHorizontal();

        if (!ok)
        {
            EditorGUILayout.HelpBox(
                Localize(lang,
                    "Background input requires Player Settings \"Run In Background\" and Input System \"Background Behavior = Ignore Focus\". They are currently disabled, so gamepad input stops when the built app is not the foreground window.",
                    "バックグラウンド入力には Player Settings の「Run In Background」と Input System の「Background Behavior = Ignore Focus」が必要です。現在無効のため、ビルドしたアプリが非アクティブだとゲームパッド入力が止まります。",
                    "后台输入需要 Player Settings 的“Run In Background”以及 Input System 的“Background Behavior = Ignore Focus”。当前已禁用，因此构建后的应用在非前台时手柄输入会停止。"),
                MessageType.Warning);
        }

        GUILayout.Space(k_Space);
    }

    static void EnableBackgroundInput()
    {
        PlayerSettings.runInBackground = true;

        var settings = InputSystem.settings;
        // The InputSystem.settings setter only registers the value in EditorBuildSettings (so it
        // ships with the build) when the value is an on-disk asset. The default in-memory settings
        // would not persist, so make sure an asset exists and is registered as the active settings
        // (this mirrors the Input System's own "Create settings asset" flow).
        if (settings == null || !AssetDatabase.Contains(settings))
        {
            var guids = AssetDatabase.FindAssets("t:InputSettings");
            if (guids.Length > 0)
            {
                settings = AssetDatabase.LoadAssetAtPath<InputSettings>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            else
            {
                settings = ScriptableObject.CreateInstance<InputSettings>();
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");
                AssetDatabase.CreateAsset(settings, "Assets/Settings/InputSystem.inputsettings.asset");
            }
            InputSystem.settings = settings;
        }

        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    bool m_Initialized;

    GUIStyle LinkStyle
    {
        get { return m_LinkStyle; }
    }

    [SerializeField]
    GUIStyle m_LinkStyle;

    GUIStyle TitleStyle
    {
        get { return m_TitleStyle; }
    }

    [SerializeField]
    GUIStyle m_TitleStyle;

    GUIStyle HeadingStyle
    {
        get { return m_HeadingStyle; }
    }

    [SerializeField]
    GUIStyle m_HeadingStyle;

    GUIStyle BodyStyle
    {
        get { return m_BodyStyle; }
    }

    [SerializeField]
    GUIStyle m_BodyStyle;

    GUIStyle ButtonStyle
    {
        get { return m_ButtonStyle; }
    }

    [SerializeField]
    GUIStyle m_ButtonStyle;

    void Init()
    {
        if (m_Initialized)
            return;
        m_BodyStyle = new GUIStyle(EditorStyles.label);
        m_BodyStyle.wordWrap = true;
        m_BodyStyle.fontSize = 14;
        m_BodyStyle.richText = true;

        m_TitleStyle = new GUIStyle(m_BodyStyle);
        m_TitleStyle.fontSize = 26;

        m_HeadingStyle = new GUIStyle(m_BodyStyle);
        m_HeadingStyle.fontStyle = FontStyle.Bold;
        m_HeadingStyle.fontSize = 18;

        m_LinkStyle = new GUIStyle(m_BodyStyle);
        m_LinkStyle.wordWrap = false;

        // Match selection color which works nicely for both light and dark skins
        m_LinkStyle.normal.textColor = new Color(0x00 / 255f, 0x78 / 255f, 0xDA / 255f, 1f);
        m_LinkStyle.stretchWidth = false;

        m_ButtonStyle = new GUIStyle(EditorStyles.miniButton);
        m_ButtonStyle.fontStyle = FontStyle.Bold;

        m_Initialized = true;
    }

    static (string heading, string text, string linkText, string buttonText) ResolveSection(Readme.Section s, string lang)
    {
        Readme.SectionTranslation t = null;
        if (s.translations != null)
        {
            foreach (var x in s.translations)
            {
                if (x != null && x.language == lang)
                {
                    t = x;
                    break;
                }
            }
        }
        if (t == null)
            return (s.heading, s.text, s.linkText, s.buttonText);
        return (
            string.IsNullOrEmpty(t.heading) ? s.heading : t.heading,
            string.IsNullOrEmpty(t.text) ? s.text : t.text,
            string.IsNullOrEmpty(t.linkText) ? s.linkText : t.linkText,
            string.IsNullOrEmpty(t.buttonText) ? s.buttonText : t.buttonText
        );
    }

    static string ResolveTitle(Readme readme, string lang)
    {
        if (readme.titleTranslations != null)
        {
            foreach (var t in readme.titleTranslations)
            {
                if (t != null && t.language == lang && !string.IsNullOrEmpty(t.title))
                    return t.title;
            }
        }
        return readme.title;
    }

    static void InvokeSectionAction(string actionId)
    {
        switch (actionId)
        {
            case "openSampleStudio":
                OpenSampleStudio();
                break;
            case "openUIDesigner":
                EditorApplication.ExecuteMenuItem("Window/Lilium Remote Control/UI Designer");
                break;
            case "launchRemoteApp":
                LaunchRemoteApp();
                break;
            case "buildAndRun":
                BuildAndRun();
                break;
            case "allowCapturePorts":
                ToolMenu.AllowCapturePortsThroughFirewall();
                break;
            default:
                Debug.LogWarning($"[Readme] Unknown buttonAction: '{actionId}'");
                break;
        }
    }

    static void OpenSampleStudio()
    {
        var ids = AssetDatabase.FindAssets("SampleStudio t:Scene");
        if (ids.Length == 0)
        {
            Debug.LogWarning("[Readme] SampleStudio scene not found.");
            return;
        }
        var path = AssetDatabase.GUIDToAssetPath(ids[0]);
        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        }
    }

    static void BuildAndRun()
    {
        EditorApplication.ExecuteMenuItem("File/Build And Run");
    }

    static void LaunchRemoteApp()
    {
        var path = ResolveRemoteAppPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError($"[Readme] Remote App not found: {path}");
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
        };
        if (System.Diagnostics.Process.Start(startInfo) == null)
        {
            Debug.LogError($"[Readme] Failed to launch Remote App: {path}");
        }
    }

    static string ResolveRemoteAppPath()
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilium.remotecontrol");
        if (packageInfo != null)
            return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "Tools~/VirgoMotionRemote/VirgoMotionRemote.exe"));
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/VirgoMotionRemote/VirgoMotionRemote.exe"));
    }

    bool LinkLabel(GUIContent label, params GUILayoutOption[] options)
    {
        var position = GUILayoutUtility.GetRect(label, LinkStyle, options);

        Handles.BeginGUI();
        Handles.color = LinkStyle.normal.textColor;
        Handles.DrawLine(new Vector3(position.xMin, position.yMax), new Vector3(position.xMax, position.yMax));
        Handles.color = Color.white;
        Handles.EndGUI();

        EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);

        return GUI.Button(position, label, LinkStyle);
    }
}
}
