using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl
{
    
    public class ExposedEnumValue
    {
        public readonly string name;
        public readonly int value;
        public readonly string displayName;

        public ExposedEnumValue(string name, int value, string displayName = null)
        {
            this.name = name;
            this.value = value;
            this.displayName = displayName ?? name;
        }
    }

    public class ExposedEnum
    {
        public static Dictionary<Type, ExposedEnum> all = new Dictionary<Type, ExposedEnum>();

        static ExposedEnum()
        {
            // アセンブリの初期化タイミングで自動登録を実行
            RegisterAllFromAttributes();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor のドメインリロード直後に static ctor を誘発させて、
        /// `RegisterAllFromAttributes` を確実に走らせる。
        /// `ExposedClass._EditorRegisterStaticExposedObjects` と同じ役割。
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void _EditorEnsureInitialized()
        {
            // `all` に触ることで static ctor がトリガされる
            _ = all;
        }
#endif

        private static void RegisterEnumInternal(Type type, string typeName, string help = null)
        {
            if (!type.IsEnum)
            {
                Debug.LogError($"[RemoteControl] Type {type.Name} is not an enum type");
                return;
            }

            var exposedEnum = ExposedEnum.Create(type, typeName, help);
            all[exposedEnum.type] = exposedEnum;

        }

        public static void Register<T>(string typeName = null) where T : Enum
        {
            var type = typeof(T);
            var exposedEnum = ExposedEnum.Create(type, typeName ?? type.Name);
            all[exposedEnum.type] = exposedEnum;
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

        private static void RegisterAllFromAttributes()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsEnum && t.GetCustomAttribute<ExposedEnumAttribute>() != null);

                    foreach (var type in types)
                    {
                        var enumAttribute = type.GetCustomAttribute<ExposedEnumAttribute>();
                        var typeName = enumAttribute?.typeName ?? type.Name;

                        // ExposedHelpAttributeを読み取り
                        var helpAttr = type.GetCustomAttribute<ExposedHelpAttribute>();
                        var help = helpAttr?.text;

                        RegisterEnumInternal(type, typeName, help);
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // アセンブリの読み込みエラーは無視
                    Debug.LogWarning($"[RemoteControl] Failed to load types from assembly: {assembly.FullName}");
                }
            }
        }

        /// <summary>
        /// 登録されたすべてのExposedEnumをクリアします
        /// </summary>
        public static void Clear()
        {
            all.Clear();
        }

        /// <summary>
        /// すべてのExposedEnumをクリアし、属性から再登録します
        /// </summary>
        public static void Reset()
        {
            Clear();
            RegisterAllFromAttributes();
        }

        public static void Unregister(ExposedEnum exposedEnum)
        {
            if (all.ContainsKey(exposedEnum.type))
            {
                all.Remove(exposedEnum.type);
            }
        }

        public static ExposedEnum Get<T>() where T : Enum
        {
            var type = typeof(T);
            if (all.TryGetValue(type, out var exposedEnum))
            {
                return exposedEnum;
            }
            return null;
        }

        public static ExposedEnum Get(Type type)
        {
            if (all.TryGetValue(type, out var exposedEnum))
            {
                return exposedEnum;
            }
            return null;
        }

        private static ExposedEnum Create(Type type, string typeName, string help = null)
        {
            if (!type.IsEnum)
            {
                Debug.LogError($"[RemoteControl] Type {type.Name} is not an enum type");
                return null;
            }

            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            var enumValues = new List<ExposedEnumValue>();

            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var value = Convert.ToInt32(values.GetValue(i));
                enumValues.Add(new ExposedEnumValue(name, value));
            }

            return new ExposedEnum(type, typeName, enumValues.ToArray(), help);
        }

        public readonly Type type;
        public readonly string typeName;
        public readonly ExposedEnumValue[] values;
        public readonly string help;

        private ExposedEnum(Type type, string typeName, ExposedEnumValue[] values, string help = null)
        {
            this.type = type;
            this.typeName = typeName;
            this.values = values;
            this.help = help;
        }
    }
}
