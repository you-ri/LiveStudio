using System;
using UnityEngine;

namespace Lilium.LiveStudio.Virgo.Editor
{
    public class Readme : ScriptableObject
    {
        public Texture2D icon;
        public string title;
        public TitleTranslation[] titleTranslations;
        public Section[] sections;
        public OptionalPackage[] optionalPackages;

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

        // An optional UPM package surfaced in the readme with an install button. The package is
        // pulled from a scoped registry (OpenUPM by default); the registry and its scope are
        // ensured in the project manifest before the package is added.
        [Serializable]
        public class OptionalPackage
        {
            public string id;
            public string version;
            public string displayName;
            public string registryName;
            public string registryUrl;
            public string scope;
        }
    }
}
