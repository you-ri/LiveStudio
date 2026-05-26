using System;
using UnityEngine;

public class Readme : ScriptableObject
{
    public Texture2D icon;
    public string title;
    public TitleTranslation[] titleTranslations;
    public Section[] sections;

    [Serializable]
    public class TitleTranslation
    {
        public string language;
        public string title;
    }

    [Serializable]
    public class Section
    {
        public string heading, text, linkText, url;
        public string buttonText;
        public string buttonAction;
        public SectionTranslation[] translations;
    }

    [Serializable]
    public class SectionTranslation
    {
        public string language;
        public string heading;
        public string text;
        public string linkText;
        public string buttonText;
    }
}
