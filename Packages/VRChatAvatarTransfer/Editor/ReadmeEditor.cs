using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

[CustomEditor(typeof(Readme))]
[InitializeOnLoad]
public class ReadmeEditor : Editor
{
    static string s_ShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";

    const float k_Space = 16f;

    const string k_LanguagePrefKey = "Lilium.VRChatAvatarTransferTemplate.ReadmeLanguage";
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
        if (!SessionState.GetBool(s_ShowedReadmeSessionStateName, false))
        {
            SelectReadme();
            SessionState.SetBool(s_ShowedReadmeSessionStateName, true);
        }
    }

    static Readme SelectReadme()
    {
        var ids = AssetDatabase.FindAssets("Readme t:Readme");
        if (ids.Length == 1)
        {
            var readmeObject = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(ids[0]));

            Selection.objects = new UnityEngine.Object[] { readmeObject };

            return (Readme)readmeObject;
        }
        else
        {
            Debug.Log("Couldn't find a readme");
            return null;
        }
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
            case "openVRChatAvatarTransfer":
                Lilium.VRChatAvatarTransfer.Editor.VRChatAvatarTransferWindow.Open();
                break;
            default:
                Debug.LogWarning($"[Readme] Unknown buttonAction: '{actionId}'");
                break;
        }
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
