using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    
    public class LiveEnumValue
    {
        public readonly string name;
        public readonly int value;
        public readonly string displayName;

        public LiveEnumValue(string name, int value, string displayName = null)
        {
            this.name = name;
            this.value = value;
            this.displayName = displayName ?? name;
        }
    }

    public class LiveEnum
    {
        public static Dictionary<Type, LiveEnum> all = new Dictionary<Type, LiveEnum>();

        static LiveEnum()
        {
            // アセンブリの初期化タイミングで自動登録を実行。LiveClass と同じく、ここは
            // ワーカースレッドから誘発されうるのでエディタ API を引かない走査にする。
            RegisterAllFromAttributes(complete: false);
        }

        /// <summary>
        /// 全アセンブリ読み込み後に走査をやり直す。static ctor は最初に LiveEnum に触れた
        /// タイミングで走るため、それがゲームアセンブリの読み込み前だと enum がまるごと落ちる。
        /// enum には LiveClass の <c>_GetOrRegister</c> にあたる遅延登録経路が無く、一度落ちると
        /// ドメイン生存中ずっと出てこない。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void _RegisterAllFromAttributesAfterAssembliesLoaded()
        {
            RegisterAllFromAttributes(complete: true);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor のドメインリロード直後に static ctor を誘発させて、
        /// `RegisterAllFromAttributes` を確実に走らせる。
        /// `LiveClass._EditorRegisterStaticLiveObjects` と同じ役割。
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void _EditorEnsureInitialized()
        {
            // `all` に触ることで static ctor がトリガされる。そのうえで、まだロードされていない
            // アセンブリの enum を拾い直す (InitializeOnLoadMethod はメインスレッド)。
            _ = all;
            RegisterAllFromAttributes(complete: true);
        }
#endif

        private static void RegisterEnumInternal(Type type, string typeName, string help = null, string[] excludeNames = null)
        {
            if (!type.IsEnum)
            {
                Debug.LogError($"[RemoteControl] Type {type.Name} is not an enum type");
                return;
            }

            var liveEnum = LiveEnum.Create(type, typeName, help, excludeNames);
            all[liveEnum.type] = liveEnum;

            // Every registration path funnels through here. /live/enums is fetched together with
            // /live/types, so the same pseudo id covers both (see LiveClass._SetLiveClass).
            LivePropertyBroadcast.BroadcastTypesUpdate();
        }

        public static void Register<T>(string typeName = null) where T : Enum
        {
            RegisterEnumInternal(typeof(T), typeName ?? typeof(T).Name);
        }

        public static void Register(Type type, string typeName = null)
        {
            if (!type.IsEnum)
            {
                Debug.LogError($"[RemoteControl] Type {type.Name} is not an enum type");
                return;
            }

            RegisterEnumInternal(type, typeName ?? type.Name);
        }

        /// <param name="complete">
        /// true なら、まだロードされていないアセンブリの enum も走査対象にする。
        /// エディタでは TypeCache を引くのでメインスレッドからのみ。
        /// </param>
        private static void RegisterAllFromAttributes(bool complete)
        {
            var found = complete
                ? TypeReflectionSystem.FindAllTypesWithAttribute<LiveEnumAttribute>()
                : TypeReflectionSystem.FindTypesWithAttribute<LiveEnumAttribute>();
            foreach (var type in found)
            {
                if (type == null || !type.IsEnum) continue;

                var enumAttribute = type.GetCustomAttribute<LiveEnumAttribute>();
                var typeName = enumAttribute?.typeName ?? type.Name;

                // HelpAttributeを読み取り
                var helpAttr = type.GetCustomAttribute<HelpAttribute>();
                var help = helpAttr?.text;

                RegisterEnumInternal(type, typeName, help);
            }

            // 外部 enum (UnityEngine 組み込み等、属性を付けられない型) の宣言的登録。
            // これはアセンブリ属性なので TypeCache では引けず、ロード済みアセンブリを走査する。
            // 取りこぼしても、後から登録された時点で @types が記録されクライアントが取り直す。
            foreach (var assembly in AssemblyUtility.GetLoadedAssemblies())
            {
                foreach (var attr in assembly.GetCustomAttributes<LiveExternalEnumAttribute>())
                {
                    if (attr.type == null) continue;
                    RegisterEnumInternal(attr.type, attr.typeName ?? attr.type.Name, null, attr.excludeNames);
                }
            }
        }

        /// <summary>
        /// 登録されたすべてのLiveEnumをクリアします
        /// </summary>
        public static void Clear()
        {
            all.Clear();
        }

        /// <summary>
        /// Clears all LiveEnum entries and re-registers them from attributes.
        /// The re-registration publishes the types change itself (see RegisterEnumInternal), so
        /// connected RemoteApp clients refetch /live/types and /live/enums after a manual rebuild
        /// (e.g. UI Designer Reset, which calls LiveClass.Reset() then LiveEnum.Reset()).
        /// </summary>
        public static void Reset()
        {
            Clear();
            RegisterAllFromAttributes(complete: true);
        }

        public static void Unregister(LiveEnum liveEnum)
        {
            if (all.ContainsKey(liveEnum.type))
            {
                all.Remove(liveEnum.type);
                LivePropertyBroadcast.BroadcastTypesUpdate();
            }
        }

        public static LiveEnum Get<T>() where T : Enum
        {
            var type = typeof(T);
            if (all.TryGetValue(type, out var liveEnum))
            {
                return liveEnum;
            }
            return null;
        }

        public static LiveEnum Get(Type type)
        {
            if (all.TryGetValue(type, out var liveEnum))
            {
                return liveEnum;
            }
            return null;
        }

        private static LiveEnum Create(Type type, string typeName, string help = null, string[] excludeNames = null)
        {
            if (!type.IsEnum)
            {
                Debug.LogError($"[RemoteControl] Type {type.Name} is not an enum type");
                return null;
            }

            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            var enumValues = new List<LiveEnumValue>();

            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                // 除外指定されたメンバー (件数番兵値など) は公開しない。
                if (excludeNames != null && Array.IndexOf(excludeNames, name) >= 0) continue;
                var value = Convert.ToInt32(values.GetValue(i));
                enumValues.Add(new LiveEnumValue(name, value));
            }

            return new LiveEnum(type, typeName, enumValues.ToArray(), help);
        }

        public readonly Type type;
        public readonly string typeName;
        public readonly LiveEnumValue[] values;
        public readonly string help;

        private LiveEnum(Type type, string typeName, LiveEnumValue[] values, string help = null)
        {
            this.type = type;
            this.typeName = typeName;
            this.values = values;
            this.help = help;
        }
    }
}
