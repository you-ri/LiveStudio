using System;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
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

        // An optional UPM package surfaced in the readme with an install button. UniVRM
        // (com.vrmc.gltf / com.vrmc.vrm) is not published to any public registry, so it is
        // installed from a Git URL. When gitUrl is empty the package is added by id@version
        // (or bare id) instead.
        [Serializable]
        public class OptionalPackage
        {
            public string id;
            public string gitUrl;
            public string version;
            public string displayName;
        }
    }
}
