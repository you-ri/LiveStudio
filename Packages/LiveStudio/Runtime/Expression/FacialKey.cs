using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    public enum ExpressionPreset
    {
        custom,
        happy,
        angry,
        sad,
        relaxed,
        surprised,
        aa,
        ih,
        ou,
        ee,
        oh,
        blink,
        blinkLeft,
        blinkRight,
        lookUp,
        lookDown,
        lookLeft,
        lookRight,
        neutral,
    }

    [Serializable]
    public struct FacialKey : IEquatable<FacialKey>, IComparable<FacialKey>, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Enum.ToString() のGC回避用キャッシュ
        /// </summary>
        private static readonly Dictionary<ExpressionPreset, string> _presetNameCache =
            new Dictionary<ExpressionPreset, string>();

        /// <summary>
        ///  ExpressionPreset と同名の名前を持つ独自に追加した Expression を区別するための prefix
        /// </summary>
        private static readonly string kCustomKeyPrefix = "Custom_";

        /// <summary>
        /// Preset of this ExpressionKey.
        /// </summary>
        public readonly ExpressionPreset preset;

        /// <summary>
        /// Custom Name of this ExpressionKey.
        /// This works if preset was Custom.
        /// </summary>
        public string name;

        /// <summary>
        /// Key's hashcode for comparison.
        /// </summary>
        private int _hashCode;

        public bool isBlink
        {
            get
            {
                switch (preset)
                {
                    case ExpressionPreset.blink:
                    case ExpressionPreset.blinkLeft:
                    case ExpressionPreset.blinkRight:
                        return true;
                }
                return false;
            }
        }

        public bool isLookAt
        {
            get
            {
                switch (preset)
                {
                    case ExpressionPreset.lookUp:
                    case ExpressionPreset.lookDown:
                    case ExpressionPreset.lookLeft:
                    case ExpressionPreset.lookRight:
                        return true;
                }
                return false;
            }
        }

        public bool isMouth
        {
            get
            {
                switch (preset)
                {
                    case ExpressionPreset.aa:
                    case ExpressionPreset.ih:
                    case ExpressionPreset.ou:
                    case ExpressionPreset.ee:
                    case ExpressionPreset.oh:
                        return true;
                }
                return false;
            }
        }

        public bool isProcedural => isBlink || isLookAt || isMouth;

        public FacialKey(ExpressionPreset preset, string customName = null)
        {
            this.preset = preset;

            if (this.preset != ExpressionPreset.custom)
            {
                if (_presetNameCache.ContainsKey((this.preset)))
                {
                    name = _presetNameCache[this.preset];
                    _hashCode = name.GetHashCode();
                }
                else
                {
                    _presetNameCache.Add(this.preset, this.preset.ToString());
                    name = _presetNameCache[this.preset];
                    _hashCode = name.GetHashCode();
                }
            }
            else
            {
                if (string.IsNullOrEmpty(customName))
                {
                    throw new ArgumentException("name is required for ExpressionPreset.Custom");
                }

                name = customName;
                _hashCode = $"{kCustomKeyPrefix}{customName}".GetHashCode();
            }
        }

        public static FacialKey CreateCustom(String key)
        {
            return new FacialKey(ExpressionPreset.custom, key);
        }

        public static FacialKey CreateFromPreset(ExpressionPreset preset)
        {
            return new FacialKey(preset);
        }

        public static FacialKey happy => CreateFromPreset(ExpressionPreset.happy);
        public static FacialKey angry => CreateFromPreset(ExpressionPreset.angry);
        public static FacialKey sad => CreateFromPreset(ExpressionPreset.sad);
        public static FacialKey relaxed => CreateFromPreset(ExpressionPreset.relaxed);
        public static FacialKey surprised => CreateFromPreset(ExpressionPreset.surprised);
        public static FacialKey aa => CreateFromPreset(ExpressionPreset.aa);
        public static FacialKey ih => CreateFromPreset(ExpressionPreset.ih);
        public static FacialKey ou => CreateFromPreset(ExpressionPreset.ou);
        public static FacialKey ee => CreateFromPreset(ExpressionPreset.ee);
        public static FacialKey oh => CreateFromPreset(ExpressionPreset.oh);
        public static FacialKey blink => CreateFromPreset(ExpressionPreset.blink);
        public static FacialKey blinkLeft => CreateFromPreset(ExpressionPreset.blinkLeft);
        public static FacialKey blinkRight => CreateFromPreset(ExpressionPreset.blinkRight);
        public static FacialKey lookUp => CreateFromPreset(ExpressionPreset.lookUp);
        public static FacialKey lookDown => CreateFromPreset(ExpressionPreset.lookDown);
        public static FacialKey lookLeft => CreateFromPreset(ExpressionPreset.lookLeft);
        public static FacialKey lookRight => CreateFromPreset(ExpressionPreset.lookRight);
        public static FacialKey neutral => CreateFromPreset(ExpressionPreset.neutral);

        /// <summary>
        /// ARKitブレンドシェイプ名のHashSet（高速検索用）
        /// </summary>
        private static readonly HashSet<string> _arkitBlendShapeNames = new HashSet<string>
        {
            "BrowDownLeft", "BrowDownRight", "BrowInnerUp", "BrowOuterUpLeft", "BrowOuterUpRight",
            "CheekPuff", "CheekSquintLeft", "CheekSquintRight",
            "EyeBlinkLeft", "EyeBlinkRight",
            "EyeLookDownLeft", "EyeLookDownRight", "EyeLookInLeft", "EyeLookInRight",
            "EyeLookOutLeft", "EyeLookOutRight", "EyeLookUpLeft", "EyeLookUpRight",
            "EyeSquintLeft", "EyeSquintRight", "EyeWideLeft", "EyeWideRight",
            "JawForward", "JawLeft", "JawOpen", "JawRight",
            "MouthClose", "MouthDimpleLeft", "MouthDimpleRight",
            "MouthFrownLeft", "MouthFrownRight", "MouthFunnel",
            "MouthLeft", "MouthLowerDownLeft", "MouthLowerDownRight",
            "MouthPressLeft", "MouthPressRight", "MouthPucker", "MouthRight",
            "MouthRollLower", "MouthRollUpper", "MouthShrugLower", "MouthShrugUpper",
            "MouthSmileLeft", "MouthSmileRight", "MouthStretchLeft", "MouthStretchRight",
            "MouthUpperUpLeft", "MouthUpperUpRight",
            "NoseSneerLeft", "NoseSneerRight",
            "TongueOut"
        };

        /// <summary>
        /// 指定したFacialKeyがARKitのブレンドシェイプキーかどうかを判定する
        /// </summary>
        /// <param name="key">判定対象のFacialKey</param>
        /// <returns>ARKitキーの場合true、それ以外はfalse</returns>
        public static bool IsARKitKey(FacialKey key)
        {
            return !string.IsNullOrEmpty(key.name) && _arkitBlendShapeNames.Contains(key.name);
        }

        public override string ToString()
        {
            return name;
        }

        public bool Equals(FacialKey other)
        {
            // Early pruning
            if (_hashCode != other._hashCode) return false;

            if (preset != other.preset) return false;
            if (preset != ExpressionPreset.custom) return true;
            return name.Equals(other.name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            if (obj is FacialKey key)
            {
                return Equals(key);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public int CompareTo(FacialKey other)
        {
            if (preset != other.preset)
            {
                return preset - other.preset;
            }

            return 0;
        }

        internal sealed class EqualityComparer : IEqualityComparer<FacialKey>
        {
            public bool Equals(FacialKey x, FacialKey y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(FacialKey obj)
            {
                return obj.GetHashCode();
            }
        }

        // ISerializationCallbackReceiver implementation
        public void OnBeforeSerialize()
        {
            // シリアライゼーション前の処理（特になし）
        }

        public void OnAfterDeserialize()
        {
            // デシリアライゼーション後にnameとhashCodeを復元
            if (preset != ExpressionPreset.custom)
            {
                // プリセットの場合は名前を復元
                if (!_presetNameCache.ContainsKey(preset))
                {
                    _presetNameCache[preset] = preset.ToString();
                }
                name = _presetNameCache[preset];
                _hashCode = name.GetHashCode();
            }
            else
            {
                // カスタムの場合はハッシュコードを復元
                if (!string.IsNullOrEmpty(name))
                {
                    _hashCode = $"{kCustomKeyPrefix}{name}".GetHashCode();
                }
            }
        }
    }

}
