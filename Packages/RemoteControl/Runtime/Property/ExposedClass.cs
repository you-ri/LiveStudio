using System;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    [System.Serializable]
    public struct ExposedPropertyDefine
    {
        public string name;

        public string path;

        public bool isPersistable;

        /// <summary>
        /// Shadow Field のメンバー名 (= Field の field.Name)。
        /// Property "X" に対する `[ExposedField, Hide, FormerlyExposedAs("X")]` Field を「Shadow Field」として
        /// 検出した場合にセットされる。Shadow Field は propertyTypes に独立 entry を作らず、
        /// 対応 Property の <see cref="ExposedPropertyType.shadowField"/> として束ねられる。
        /// JSON シリアライザは値を Property setter ではなく shadow field に直接書き込み、
        /// デシリアライズ時の setter 副作用 (Apply 系処理) をバイパスする。
        /// </summary>
        public string shadowFieldPath;
    }

    public class ExposedClass
    {
        public static IReadOnlyDictionary<System.Type, ExposedClass> all => _all;

        private static Dictionary<System.Type, ExposedClass> _all = new Dictionary<System.Type, ExposedClass>();

        private static Dictionary<string, ExposedClass> _byTypeName = new Dictionary<string, ExposedClass>();

        /// <summary>
        /// 型に対応する宣言順 index を取得。未登録なら -1。
        /// 実体は <see cref="ExposedClassDeclarationOrderTable"/> に分離されている
        /// (Source Gen の ModuleInitializer から ExposedClass.cctor を巻き込まないため)。
        /// </summary>
        internal static int GetDeclarationOrderIndex(System.Type type, string memberName)
            => ExposedClassDeclarationOrderTable.GetIndex(type, memberName);

        private static void _SetExposedClass(ExposedClass ec)
        {
            // 既存のExposedClassがある場合、イベント購読を新インスタンスに移行する
            // RegisterPropertiesが新インスタンスで差し替える際に、購読者が失われるのを防ぐ
            if (_all.TryGetValue(ec.type, out var old) && old != ec)
            {
                ec._MigrateEventsFrom(old);
                // 旧エイリアスは古いインスタンスに紐づいていたら除去してから再登録する
                _UnregisterTypeNameAliases(old);
            }

            _all[ec.type] = ec;
            _byTypeName[ec.typeName] = ec;
            _RegisterTypeNameAliases(ec);
        }

        private static void _RegisterTypeNameAliases(ExposedClass ec)
        {
            if (ec.formerTypeNames == null) return;
            for (int i = 0; i < ec.formerTypeNames.Length; i++)
            {
                var alias = ec.formerTypeNames[i];
                if (string.IsNullOrEmpty(alias)) continue;
                if (alias == ec.typeName) continue; // 現 typeName と同じなら重複登録しない
                if (_byTypeName.TryGetValue(alias, out var existing) && existing.type != ec.type)
                {
                    Debug.LogWarning($"[RemoteControl] FormerlyExposedAs alias '{alias}' on '{ec.typeName}' conflicts with existing type '{existing.typeName}'. Alias will override.");
                }
                _byTypeName[alias] = ec;
            }
        }

        private static void _UnregisterTypeNameAliases(ExposedClass ec)
        {
            if (ec?.formerTypeNames == null) return;
            for (int i = 0; i < ec.formerTypeNames.Length; i++)
            {
                var alias = ec.formerTypeNames[i];
                if (string.IsNullOrEmpty(alias)) continue;
                // 他クラスが上書き登録していない場合のみ削除
                if (_byTypeName.TryGetValue(alias, out var current) && current == ec)
                {
                    _byTypeName.Remove(alias);
                }
            }
        }


        static ExposedClass()
        {
            // アセンブリの初期化タイミングでExposedClassの登録を実行
            _RegisterAllTypesFromAttributes();
        }

        public static void Register<T>(string typeName, ExposedPropertyDefine[] defines, string category = null, string icon = null, bool hideInScene = false)
        {
            var properties = ExposedPropertyUtility.MakePropertyTypes(typeof(T), defines);
            var exposedClass = new ExposedClass(typeof(T), typeName, properties, category: category, icon: icon, hideInScene: hideInScene);
            _SetExposedClass(exposedClass);
        }

        public static ExposedClass RegisterClass(Type type)
        {
            var classAttribute = TypeReflectionSystem.GetCustomAttribute<ExposedClassAttribute>(type);
            if (classAttribute == null) return null;

            var typeName = classAttribute.typeName ?? type.Name;
            var category = classAttribute.Category;
            var icon = classAttribute.Icon;
            var hideInScene = classAttribute.HideInScene;
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<ExposedHelpAttribute>(type);
            var help = helpAttr?.text;
            var formerTypeNames = _CollectFormerNames(type);

            var exposedClass = new ExposedClass(type, typeName, category: category, help: help, icon: icon, hideInScene: hideInScene, formerTypeNames: formerTypeNames);
            _SetExposedClass(exposedClass);
            return exposedClass;
        }

        /// <summary>
        /// 型/メンバーに付与された全 [FormerlyExposedAs] 属性から旧名の配列を収集する。
        /// 空文字/null や重複は除外する。未付与なら空配列を返す。
        /// </summary>
        internal static string[] _CollectFormerNames(ICustomAttributeProvider provider)
        {
            var attrs = (FormerlyExposedAsAttribute[])provider.GetCustomAttributes(typeof(FormerlyExposedAsAttribute), inherit: false);
            if (attrs == null || attrs.Length == 0) return Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>(attrs.Length);
            for (int i = 0; i < attrs.Length; i++)
            {
                var n = attrs[i].name;
                if (string.IsNullOrEmpty(n)) continue;
                if (seen.Add(n)) list.Add(n);
            }
            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        internal static void RegisterProperties(Type type)
        {
            var classAttribute = TypeReflectionSystem.GetCustomAttribute<ExposedClassAttribute>(type);
            if (classAttribute == null) return;

            // typeNameがnullの場合はクラス名を使用
            var typeName = classAttribute.typeName ?? type.Name;

            // プロパティ、フィールド、メソッドを統合して収集（定義順を保持するため）
            // MemberType: 0=Property/Field, 1=Method
            var allMembers = new List<(MemberInfo member, int token, int memberType, object attr, bool isPersistable)>();

            // プロパティからExposedPropertyAttributeを検索（isPersistable は InlineReference の自動 true 化、
            // または Shadow Field との pairing で Field 側 persistable を継承する経路でのみ true になる）
            var propertyInfos = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var propInfo in propertyInfos)
            {
                var propAttribute = TypeReflectionSystem.GetCustomAttribute<ExposedPropertyAttribute>(propInfo);
                if (propAttribute != null)
                {
                    allMembers.Add((propInfo, propInfo.MetadataToken, 0, propAttribute, false));
                }
            }

            // フィールドからExposedFieldAttributeを検索（isPersistable = true）
            // .NET の GetFields は基底クラスの private フィールドを返さない。
            // 基底に Shadow Field を置いて派生で使うパターン (ExposedUnityObjectProxy._name 等) を
            // 拾うため、type.BaseType を辿って DeclaredOnly で各レベルを連結する。
            // 派生で同名の new フィールドを定義した場合は派生側が先勝ち。
            var fieldInfos = new List<FieldInfo>();
            var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var declared = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var fi in declared)
                {
                    if (seenFieldNames.Add(fi.Name)) fieldInfos.Add(fi);
                }
            }
            foreach (var fieldInfo in fieldInfos)
            {
                var fieldAttribute = TypeReflectionSystem.GetCustomAttribute<ExposedFieldAttribute>(fieldInfo);
                if (fieldAttribute != null)
                {
                    allMembers.Add((fieldInfo, fieldInfo.MetadataToken, 0, fieldAttribute, fieldAttribute.persistable));

                    //TODO: SelectedItemsAttributeの処理(WIP)
                    var selectableAddArrayAttribute = TypeReflectionSystem.GetCustomAttribute<SelectedItemsAttribute>(fieldInfo);
                    if (selectableAddArrayAttribute != null)
                    {
                        var property = TypeReflectionSystem.GetProperty(type, selectableAddArrayAttribute.selectionListName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (property != null && property.PropertyType == typeof(string[]))
                        {
                            Debug.Log(property.Name);
                        }
                    }
                }
            }

            // メソッドからExposedFunctionAttributeを検索
            var methodInfos = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var methodInfo in methodInfos)
            {
                var funcAttribute = TypeReflectionSystem.GetCustomAttribute<ExposedFunctionAttribute>(methodInfo);
                if (funcAttribute != null)
                {
                    allMembers.Add((methodInfo, methodInfo.MetadataToken, 1, funcAttribute, false));
                }
            }

            // Source Gen が型を登録していない場合は一度だけ警告。
            // 全 [ExposedClass] 型は generator が登録する前提なので、未登録は generator DLL 未ビルド
            // / バージョン不整合 / 動的ロード型のいずれか。
            if (allMembers.Count > 0 && !ExposedClassDeclarationOrderTable.IsRegistered(type)
                && ExposedClassDeclarationOrderTable.TryMarkWarned(type))
            {
                Debug.LogWarning(
                    $"[RemoteControl] Source Generator did not register declaration order for type '{type.FullName}'. " +
                    "Check that Plugins/Lilium.RemoteControl.SourceGenerator.dll is up to date. " +
                    "Without declaration order, member ordering may diverge from C# source order.");
            }

            // 明示 order 優先、同値の場合は Source Gen の宣言順で tiebreak。
            // 未登録型ではいずれも -1 を返すので並びは List.Sort の内部実装依存になる
            // (= 警告条件下の best-effort)。
            allMembers.Sort((a, b) =>
            {
                int orderA = _GetExplicitOrder(a.attr);
                int orderB = _GetExplicitOrder(b.attr);
                if (orderA != orderB) return orderA.CompareTo(orderB);

                var declA = GetDeclarationOrderIndex(type, a.member.Name);
                var declB = GetDeclarationOrderIndex(type, b.member.Name);
                return declA.CompareTo(declB);
            });

            // Shadow pair 検出:
            //   `[ExposedProperty] X { get; set; }` と
            //   `[ExposedField, Hide][FormerlyExposedAs("X")] _X` のペアを検出する。
            // Field 側は propertyTypes に独立 entry を作らず、Property の shadowField として束ねる。
            // 詳細は ExposedPropertyDefine.shadowFieldPath を参照。
            var propertyNamesByExposedName = new Dictionary<string, string>(StringComparer.Ordinal); // displayName -> Property member name
            for (int i = 0; i < allMembers.Count; i++)
            {
                var m = allMembers[i];
                if (m.memberType != 0 || !(m.member is PropertyInfo)) continue;
                if (!(m.attr is ExposedPropertyAttribute pa)) continue;
                var name = pa.name ?? m.member.Name;
                propertyNamesByExposedName[name] = m.member.Name;
            }

            // shadowField マップ: Property のメンバー名 (path) -> Shadow Field のメンバー名 (path)
            // および Field 側の persistable 値 (Property の isPersistable に継承)
            var shadowFieldByPropertyPath = new Dictionary<string, (string fieldPath, bool fieldPersistable)>(StringComparer.Ordinal);
            var shadowFieldMembers = new HashSet<MemberInfo>();
            for (int i = 0; i < allMembers.Count; i++)
            {
                var m = allMembers[i];
                if (m.memberType != 0 || !(m.member is FieldInfo fi)) continue;
                if (!(m.attr is ExposedFieldAttribute fieldAttr)) continue;
                // [Hide] が無いものは Shadow ではない (UI 表示用の独立 Field)
                if (TypeReflectionSystem.GetCustomAttribute<HideAttribute>(fi) == null) continue;
                // FormerlyExposedAs で参照される名前のいずれかが同クラスの Property に存在するかをチェック
                var formers = (FormerlyExposedAsAttribute[])fi.GetCustomAttributes(typeof(FormerlyExposedAsAttribute), inherit: false);
                if (formers == null || formers.Length == 0) continue;

                string targetPropertyPath = null;
                for (int j = 0; j < formers.Length; j++)
                {
                    var alias = formers[j].name;
                    if (string.IsNullOrEmpty(alias)) continue;
                    if (propertyNamesByExposedName.TryGetValue(alias, out var propPath))
                    {
                        targetPropertyPath = propPath;
                        break;
                    }
                }
                if (targetPropertyPath == null) continue;

                shadowFieldByPropertyPath[targetPropertyPath] = (fi.Name, fieldAttr.persistable);
                shadowFieldMembers.Add(fi);
            }

            // ソート順でproperties/functionsに追加し、orderを設定
            var properties = new List<ExposedPropertyDefine>();
            var functions = new List<ExposedFunctionType>();
            var propertyOrderMap = new Dictionary<string, int>(); // プロパティパス -> order

            for (int i = 0; i < allMembers.Count; i++)
            {
                var (member, _, memberType, attr, isPersistable) = allMembers[i];

                if (memberType == 0) // Property/Field
                {
                    // Shadow Field と判定された Field は propertyTypes に登録しない
                    if (shadowFieldMembers.Contains(member)) continue;

                    // ExposedPropertyAttribute または ExposedFieldAttribute から name を取得
                    string propName;
                    if (attr is ExposedPropertyAttribute propAttr)
                        propName = propAttr.name ?? member.Name;
                    else if (attr is ExposedFieldAttribute fieldAttr)
                        propName = fieldAttr.name ?? member.Name;
                    else
                        propName = member.Name;

                    string shadowFieldPath = null;
                    if (member is PropertyInfo
                        && shadowFieldByPropertyPath.TryGetValue(member.Name, out var shadowInfo))
                    {
                        shadowFieldPath = shadowInfo.fieldPath;
                        // Shadow Field 側の persistable を Property に継承
                        isPersistable = shadowInfo.fieldPersistable;
                    }

                    properties.Add(new ExposedPropertyDefine
                    {
                        name = propName,
                        path = member.Name,
                        isPersistable = isPersistable,
                        shadowFieldPath = shadowFieldPath
                    });
                    propertyOrderMap[member.Name] = i;
                }
                else // Method
                {
                    var funcAttr = (ExposedFunctionAttribute)attr;
                    var functionName = funcAttr.name ?? member.Name;
                    var funcType = new ExposedFunctionType(functionName, (MethodInfo)member);
                    funcType.order = i;
                    functions.Add(funcType);
                }
            }

            if (properties.Count == 0 && functions.Count == 0) return;

            // RegisterClassで登録済みのExposedClassからメタ情報を引き継ぐ
            var oldExposedClass = ExposedClass.Get(type);

            // プロパティ型を構築
            var propTypes = properties.Count > 0
                ? ExposedPropertyUtility.MakePropertyTypes(type, properties.ToArray())
                : (oldExposedClass?.propertyTypes ?? new ExposedPropertyType[0]);

            // orderを設定
            foreach (var propType in propTypes)
            {
                var memberName = propType.properyInfo?.Name ?? propType.fieldInfo?.Name;
                if (memberName != null && propertyOrderMap.TryGetValue(memberName, out var order))
                {
                    propType.order = order;
                }
            }

            var funcTypes = functions.Count > 0
                ? functions.ToArray()
                : (oldExposedClass?.functionTypes ?? new ExposedFunctionType[0]);

            // 1回のExposedClass生成で完了
            var newExposedClass = new ExposedClass(type, typeName, propTypes, funcTypes,
                oldExposedClass?.category, oldExposedClass?.help, oldExposedClass?.icon,
                oldExposedClass?.hideInScene ?? false, oldExposedClass?.formerTypeNames);
            _SetExposedClass(newExposedClass);
        }

        static int _GetExplicitOrder(object attr)
        {
            switch (attr)
            {
                case ExposedPropertyAttribute p: return p.order;
                case ExposedFieldAttribute f: return f.order;
                case ExposedFunctionAttribute fn: return fn.order;
                default: return 0;
            }
        }

        public static void RegisterFromAttributes<T>()
        {
            RegisterClass(typeof(T));
            RegisterProperties(typeof(T));
        }

        private static void _RegisterAllTypesFromAttributes()
        {
            // TypeReflectionSystemを使用して属性付きの型を検索
            var typesWithAttribute = new List<Type>();
            foreach (var type in TypeReflectionSystem.FindTypesWithAttribute<ExposedClassAttribute>())
            {
                typesWithAttribute.Add(type);
            }

            foreach (var type in typesWithAttribute)
            {
                RegisterClass(type);
            }

            foreach (var type in typesWithAttribute)
            {
                RegisterProperties(type);
            }
        }

        /// <summary>
        /// static classのExposedObjectを自動生成する。
        /// SubsystemRegistrationでExposedObjectRegistry.instancesがクリアされた後に実行する必要がある。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void _RegisterStaticExposedObjects()
        {
            foreach (var kvp in _all)
            {
                var exposedClass = kvp.Value;
                if (exposedClass.isStatic)
                {
                    // 既に登録済みならスキップ
                    if (ExposedObjectRegistry.FindById(exposedClass.typeName) != null)
                        continue;

                    new ExposedObject(exposedClass.typeName, exposedClass, null);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Edit モードのエディタ起動時 / ドメインリロード後にも静的 ExposedObject を登録する。
        /// `RuntimeInitializeOnLoadMethod` は Play 起動時のみ発火するため、Edit モードで動作する
        /// `RemoteControlServerManager` / `WebUISimulatorWindow` から静的クラスが引けるようにここで補完する。
        /// `_RegisterStaticExposedObjects` は FindById で重複スキップするため冪等。
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void _EditorRegisterStaticExposedObjects()
        {
            _RegisterStaticExposedObjects();
        }
#endif

        public static void Unregister(ExposedClass exposedClass)
        {
            if (_all.ContainsKey(exposedClass.type))
            {
                _all.Remove(exposedClass.type);
                _byTypeName.Remove(exposedClass.typeName);
                _UnregisterTypeNameAliases(exposedClass);
            }
        }

        /// <summary>
        /// 登録されたすべてのExposedClassをクリアします（テスト用）
        /// </summary>
        public static void Clear()
        {
            _all.Clear();
            _byTypeName.Clear();
        }

        /// <summary>
        /// すべてのExposedClassをクリアし、属性から再登録します
        /// </summary>
        public static void Reset()
        {
            Clear();
            _RegisterAllTypesFromAttributes();
            _RegisterStaticExposedObjects();
        }
        /// <summary>
        /// 型からExposedClassを取得、未登録の場合はExposedClassAttributeがあれば動的に登録を試みる
        /// </summary>
        private static ExposedClass _GetOrRegister(System.Type type)
        {
            if (_all.TryGetValue(type, out var exposedClass))
            {
                return exposedClass;
            }

            // 見つからない場合、該当型にExposedClassAttributeがあれば動的に登録を試みる
            var attr = TypeReflectionSystem.GetCustomAttribute<ExposedClassAttribute>(type);
            if (attr != null)
            {
                RegisterClass(type);
                RegisterProperties(type);
                if (_all.TryGetValue(type, out exposedClass))
                {
                    return exposedClass;
                }
            }

            return null;
        }

        public static ExposedClass Get<T>()
        {
            return Get(typeof(T));
        }

        public static ExposedClass Get(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            var exposedClass = _GetOrRegister(type);
            if (exposedClass != null) return exposedClass;

            throw new Exception($"ExposedClass for type `{type.Name}` is not registered.");
        }

        public static bool TryGet(System.Type type, out ExposedClass exposedClass)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _all.TryGetValue(type, out exposedClass);
        }

        public static bool Has(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _all.ContainsKey(type);
        }

        public static ExposedClass Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            _byTypeName.TryGetValue(typeName, out var result);
            return result;
        }

        public static ExposedClass Find(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _GetOrRegister(type);
        }


        public readonly System.Type type;

        public readonly string typeName;

        public readonly string category;

        public readonly bool isStatic;

        public readonly ExposedPropertyType[] propertyTypes;

        public readonly ExposedFunctionType[] functionTypes;

        public readonly string help;

        public readonly string icon;

        /// <summary>
        /// trueの場合、RemoteAppのシーンページ一覧から本型のオブジェクトを除外する。
        /// </summary>
        public readonly bool hideInScene;

        /// <summary>
        /// [FormerlyExposedAs] で指定された旧 typeName の配列。`ExposedClass.Find(alias)` 経由で解決可能。
        /// クラスのリネーム時に旧シーンファイルからの復元を可能にする。
        /// </summary>
        public readonly string[] formerTypeNames;

        private readonly Dictionary<string, ExposedPropertyType> _propertyByName;

        private readonly Dictionary<string, ExposedFunctionType> _functionByApiName;

        // Convention-based callbacks for static-classed ExposedObject (target == null).
        // C# 9 cannot put static abstract members on an interface, so we resolve a
        // public static parameterless method by name during registration.
        private readonly MethodInfo _staticOnAfterDeserialize;

        private readonly MethodInfo _staticOnBeforeSerialize;

        public delegate void PropertyChangingDelegate(ExposedProperty property, object newValue);

        public delegate void PropertyChangedDelegate(ExposedProperty property, object oldValue);

        public event PropertyChangingDelegate onPropertyChanging;

        public event PropertyChangedDelegate onPropertyChanged;

        private static Dictionary<string, ExposedFunctionType> _BuildFunctionApiNameIndex(ExposedFunctionType[] functionTypes)
        {
            var dict = new Dictionary<string, ExposedFunctionType>(functionTypes.Length);
            for (int i = 0; i < functionTypes.Length; i++)
            {
                dict[functionTypes[i].apiName] = functionTypes[i];
            }
            return dict;
        }

        private static Dictionary<string, ExposedPropertyType> _BuildPropertyNameIndex(ExposedPropertyType[] propertyTypes)
        {
            var dict = new Dictionary<string, ExposedPropertyType>(propertyTypes.Length);
            for (int i = 0; i < propertyTypes.Length; i++)
            {
                var pt = propertyTypes[i];
                dict[pt.name] = pt;

                // [FormerlyExposedAs] で指定された旧メンバー名もエイリアスとして引けるようにする
                if (pt.formerNames != null)
                {
                    for (int j = 0; j < pt.formerNames.Length; j++)
                    {
                        var alias = pt.formerNames[j];
                        if (string.IsNullOrEmpty(alias)) continue;
                        if (alias == pt.name) continue;
                        if (dict.TryGetValue(alias, out var existing) && existing != pt)
                        {
                            Debug.LogWarning($"[RemoteControl] FormerlyExposedAs alias '{alias}' on property '{pt.name}' conflicts with existing property '{existing.name}'. Alias will override.");
                        }
                        dict[alias] = pt;
                    }
                }
            }
            return dict;
        }

        private ExposedClass(System.Type type, string typeName,
            ExposedPropertyType[] propertyTypes = null, ExposedFunctionType[] functionTypes = null,
            string category = null, string help = null, string icon = null, bool hideInScene = false,
            string[] formerTypeNames = null)
        {
            this.type = type;
            this.typeName = typeName;
            this.category = category;
            this.isStatic = type.IsAbstract && type.IsSealed;
            this.propertyTypes = propertyTypes ?? new ExposedPropertyType[0];
            this.functionTypes = functionTypes ?? new ExposedFunctionType[0];
            this.help = help;
            this.icon = icon;
            this.hideInScene = hideInScene;
            this.formerTypeNames = formerTypeNames ?? Array.Empty<string>();
            this._propertyByName = _BuildPropertyNameIndex(this.propertyTypes);
            this._functionByApiName = _BuildFunctionApiNameIndex(this.functionTypes);

            if (this.isStatic && type != null)
            {
                const BindingFlags kStaticPublic = BindingFlags.Public | BindingFlags.Static;
                this._staticOnAfterDeserialize = type.GetMethod(
                    "OnAfterExposedDeserialize", kStaticPublic, null, Type.EmptyTypes, null);
                this._staticOnBeforeSerialize = type.GetMethod(
                    "OnBeforeExposedSerialize", kStaticPublic, null, Type.EmptyTypes, null);
            }
        }

        /// <summary>
        /// Fires the convention-based static deserialize callback if one exists.
        /// Used by <see cref="ExposedPropertySerializer"/> when the owning
        /// ExposedObject's target is null (static class).
        /// </summary>
        internal void InvokeStaticAfterDeserialize()
        {
            _staticOnAfterDeserialize?.Invoke(null, null);
        }

        /// <summary>
        /// Fires the convention-based static serialize callback if one exists.
        /// Used by <see cref="ExposedPropertySerializer.SerializeFullToJObject"/>
        /// when the owning ExposedObject's target is null (static class).
        /// </summary>
        internal void InvokeStaticBeforeSerialize()
        {
            _staticOnBeforeSerialize?.Invoke(null, null);
        }

        /// <summary>
        /// 旧ExposedClassインスタンスからイベント購読を移行する。
        /// RegisterPropertiesで新インスタンスに差し替える際に、既存の購読者が失われるのを防ぐ。
        /// </summary>
        internal void _MigrateEventsFrom(ExposedClass old)
        {
            if (old == null || old == this) return;
            this.onPropertyChanging = old.onPropertyChanging;
            this.onPropertyChanged = old.onPropertyChanged;
            old.onPropertyChanging = null;
            old.onPropertyChanged = null;
        }

        internal void RaisePropertyChanging(ExposedProperty property, object newValue)
        {
            onPropertyChanging?.Invoke(property, newValue);
        }

        internal void RaisePropertyChanged(ExposedProperty property, object oldValue)
        {
            onPropertyChanged?.Invoke(property, oldValue);
        }

        public ExposedPropertyType FindProperty(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _propertyByName.TryGetValue(name, out var result);
            return result;
        }

        public ExposedFunctionType FindFunction(string apiName)
        {
            if (string.IsNullOrEmpty(apiName)) return null;
            _functionByApiName.TryGetValue(apiName, out var result);
            return result;
        }
    }
    
    public class ExposedPropertyType
    {
        public readonly PropertyInfo properyInfo;

        public readonly FieldInfo fieldInfo;

        /// <summary>
        /// Shadow Field 経由のバッキングストレージ。non-null の場合、JSON シリアライザは
        /// <see cref="properyInfo"/> setter をバイパスしてここに直接書き込む。
        /// RemoteApp 経由の SetProperty は引き続き <see cref="properyInfo"/> setter を呼ぶ。
        /// 詳細は <see cref="ExposedPropertyDefine.shadowFieldPath"/> を参照。
        /// </summary>
        public readonly FieldInfo shadowField;

        public readonly ControlAttribute controlAttribute;

        public readonly ExposedClass exposedValueClass;

        public readonly string controlType;

        // 配列要素の型
        public readonly Type arrayElementType;

        public readonly int arrayIndex;

        public readonly bool isArrayElement;

        public readonly bool isReadOnly;

        public readonly bool isStatic;

        public readonly string help;

        public readonly string label;

        private readonly string _name;

        public readonly object defaultValue;

        /// <summary>
        /// 条件付き表示: 参照先プロパティ名
        /// </summary>
        public readonly string visibilityConditionProperty;

        /// <summary>
        /// 条件付き表示: 比較値
        /// </summary>
        public readonly object visibilityConditionValue;

        /// <summary>
        /// 条件付き表示: true=ShowIf（一致で表示）, false=HideIf（一致で非表示）
        /// </summary>
        public readonly bool visibilityShowWhenMatch;

        /// <summary>
        /// 条件付き表示が設定されているか
        /// </summary>
        public bool hasVisibilityCondition => visibilityConditionProperty != null;

        /// <summary>
        /// セクション属性（NavigatePageでのセクション表示用）
        /// </summary>
        public readonly SectionAttribute sectionAttribute;

        /// <summary>
        /// セクションに適用される最終アクセスレベル。
        /// <see cref="ExperimentalAttribute"/> / <see cref="DevelopmentAttribute"/> が
        /// 同じメンバーに付いていればそれを優先し、無ければ
        /// <see cref="SectionAttribute.accessLevel"/> を使う。
        /// </summary>
        public readonly AccessLevel sectionAccessLevel;

        /// <summary>
        /// [FormerlyExposedAs] で指定された旧メンバー名の配列。
        /// `ExposedClass.FindProperty(alias)` 経由で引けるようになり、フィールド/プロパティのリネーム後も
        /// 旧シーンファイルから復元できる。
        /// </summary>
        public readonly string[] formerNames;

        /// <summary>
        /// 宣言順序（プロパティと関数の混合表示用）
        /// </summary>
        public int order { get; internal set; }

        /// <summary>
        /// 名称
        /// </summary>
        public string name => _name;

        /// <summary>
        /// 値の型を取得します。
        /// </summary>
        public System.Type valueType => isArrayElement ? arrayElementType : (properyInfo?.PropertyType ?? fieldInfo?.FieldType);

        /// <summary>
        /// 永続化対象かどうか
        /// </summary>
        public readonly bool isPersistable;

        /// <summary>
        /// 有効なプロパティかどうか
        /// </summary>
        public bool isValid => isArrayElement || properyInfo != null || fieldInfo != null;

        /// <summary>
        /// 参照か？実態か？（class型は参照、struct/primitiveは実体。stringは例外で実体扱い）
        /// </summary>
        public bool isReference => valueType != null && valueType.IsClass && valueType != typeof(string);

        /// <summary>
        /// プリミティブ型か？
        /// </summary>
        /// <returns></returns>
        public bool isPrimitive => valueType != null && (valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) || valueType == typeof(decimal));

        /// <summary>
        /// Unityのプリミティブ型か？
        /// </summary>
        /// <returns></returns>
        public bool isUnityPrimtive => isPrimitive || valueType == typeof(Vector2) || valueType == typeof(Vector3) || valueType == typeof(Vector4) || valueType == typeof(Quaternion) || valueType == typeof(Color) || valueType == typeof(Color32) || valueType == typeof(Rect) || valueType == typeof(Bounds) || valueType == typeof(Vector2Int) || valueType == typeof(Vector3Int) || valueType == typeof(RectInt) || valueType == typeof(TransformValue);

        /// <summary>
        /// ExposedObject参照かどうか（UnityEngine.Objectのみ対象）
        /// </summary>
        public bool isExposedObjectReference => exposedValueClass != null && isReference && typeof(UnityEngine.Object).IsAssignableFrom(valueType);

        /// <summary>
        /// フィールドが他プロパティへの参照 (ExposedPropertyRef) かどうか。
        /// true のとき、値取得/設定/dirty/revert は参照先の ExposedProperty に委譲される。
        /// </summary>
        public bool isExposedPropertyReference => valueType == typeof(ExposedPropertyRef);

        /// <summary>
        /// 実効的な値型を取得する。
        /// 通常は <see cref="valueType"/> と同じだが、<see cref="isExposedPropertyReference"/> が true の場合は
        /// 参照先プロパティの値型 (例: float) を返す。TypeDefinition 出力でクライアントに伝える型として使う。
        /// 解決できない場合は <see cref="valueType"/> (= typeof(ExposedPropertyRef)) にフォールバック。
        /// </summary>
        public Type resolvedValueType
        {
            get
            {
                if (isExposedPropertyReference && fieldInfo != null && fieldInfo.IsStatic)
                {
                    // static readonly ExposedPropertyRef を前提に値を取得する。
                    // インスタンスフィールドの場合はインスタンス依存なのでここでは解決せず valueType を返す。
                    var refValue = fieldInfo.GetValue(null);
                    if (refValue is ExposedPropertyRef pr && pr.targetValueType != null)
                    {
                        return pr.targetValueType;
                    }
                }
                return valueType;
            }
        }

        /// <summary>
        /// 実効的な読み取り専用フラグ。PropertyRef の場合、参照先の isReadOnly を返す。
        /// 参照フィールドは static readonly として宣言されるのが通例だが、参照先が書き込み可能なら
        /// RemoteApp 側では編集可能として扱う必要がある。
        /// </summary>
        public bool resolvedIsReadOnly
        {
            get
            {
                if (isExposedPropertyReference && fieldInfo != null && fieldInfo.IsStatic)
                {
                    var refValue = fieldInfo.GetValue(null);
                    if (refValue is ExposedPropertyRef pr)
                    {
                        var resolved = pr.Resolve();
                        if (resolved.HasValue && resolved.Value.type != null)
                        {
                            return resolved.Value.type.isReadOnly;
                        }
                    }
                }
                return isReadOnly;
            }
        }

        /// <summary>
        /// 自身または配列要素がExposedObject参照を含むかどうか
        /// 永続化時のreadonly/dirty判定に使用
        /// exposedValueClassは登録順序に依存するため、ここではvalueTypeのみで判定する
        /// 配列の場合、要素型がUnityEngine.Object派生であれば実行時に派生型の
        /// ExposedObject参照を含む可能性があるため true を返す
        /// </summary>
        public bool containsExposedObjectReference
        {
            get
            {
                if (valueType == null) return false;
                if (!valueType.IsArray)
                {
                    return isReference && typeof(UnityEngine.Object).IsAssignableFrom(valueType);
                }
                var elemType = valueType.GetElementType();
                return elemType != null && typeof(UnityEngine.Object).IsAssignableFrom(elemType);
            }
        }

        /// <summary>
        /// 強制展開
        /// </summary>
        public bool forceValue => false;


        public ExposedPropertyType(string name, MemberInfo info, bool isPersistable = true, FieldInfo shadowField = null)
        {
            Debug.Assert(info != null, "PropertyInfo cannot be null");

            this.properyInfo = info as PropertyInfo;
            this.fieldInfo = info as FieldInfo;
            this.shadowField = shadowField;
            this._name = name;
            this.isPersistable = isPersistable;
            this.isArrayElement = false;
            this.arrayElementType = null;
            this.arrayIndex = -1;
            this.controlType = "default";

            // 読み取り専用判定
            if (this.properyInfo != null)
            {
                // PropertyInfoの場合: setterがない場合は読み取り専用
                this.isReadOnly = !this.properyInfo.CanWrite;
                this.isStatic = this.properyInfo.GetMethod?.IsStatic ?? false;
            }
            else if (this.fieldInfo != null)
            {
                // FieldInfoの場合: readonly または const の場合は読み取り専用
                this.isReadOnly = this.fieldInfo.IsInitOnly || this.fieldInfo.IsLiteral;
                this.isStatic = this.fieldInfo.IsStatic;
            }
            else
            {
                this.isReadOnly = true; // 無効な場合は読み取り専用扱い
                this.isStatic = false;
            }

            var valueType = properyInfo != null ? properyInfo.PropertyType : fieldInfo.FieldType;
            if (ExposedClass.Has(valueType))
            {
                this.exposedValueClass = ExposedClass.Get(valueType);
            }
            this.controlAttribute = TypeReflectionSystem.GetCustomAttribute<ControlAttribute>(info) ?? new ControlAttribute("default");

            // [InlineReference] は保存時に Component などを pending entry として書き出すための
            // マーカー。readonly Property でも isPersistable=true 扱いになる。
            if (this.controlAttribute is InlineReferenceAttribute)
            {
                this.isPersistable = true;
            }

            // TypeSelectorAttributeの場合、派生型を自動計算してoptionsを設定
            if (this.controlAttribute is TypeSelectorAttribute typeSelector)
            {
                var fieldType = this.properyInfo?.PropertyType ?? this.fieldInfo?.FieldType;
                if (fieldType != null)
                {
                    var derivedTypes = TypeReflectionSystem.FindDerivedTypes(fieldType);
                    var options = new List<string>();
                    foreach (var dt in derivedTypes)
                    {
                        var ec = ExposedClass.Find(dt);
                        if (ec != null) options.Add(ec.typeName);
                    }
                    typeSelector.options = options.ToArray();
                }
            }

            // CameraControllerAttributeの場合も同様に派生型のoptionsを自動計算
            if (this.controlAttribute is CameraControllerAttribute cameraController)
            {
                var fieldType = this.properyInfo?.PropertyType ?? this.fieldInfo?.FieldType;
                if (fieldType != null)
                {
                    var derivedTypes = TypeReflectionSystem.FindDerivedTypes(fieldType);
                    var options = new List<string>();
                    foreach (var dt in derivedTypes)
                    {
                        var ec = ExposedClass.Find(dt);
                        if (ec != null) options.Add(ec.typeName);
                    }
                    cameraController.options = options.ToArray();
                }
            }

            // DefaultValue属性を読み取り（TypeReflectionSystem経由でキャッシュ付き）
            var defaultValueAttr = TypeReflectionSystem.GetCustomAttribute<DefaultValueAttribute>(info);
            if (defaultValueAttr != null)
            {
                this.defaultValue = defaultValueAttr.value;
            }
            else
            {
                defaultValue = null;
            }

            // ExposedHelp属性を読み取り（TypeReflectionSystem経由でキャッシュ付き）
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<ExposedHelpAttribute>(info);
            if (helpAttr != null)
            {
                this.help = helpAttr.text;
            }
            else
            {
                this.help = null;
            }

            // label属性を読み取り（ExposedPropertyAttribute または ExposedFieldAttribute から）
            var labelPropAttr = TypeReflectionSystem.GetCustomAttribute<ExposedPropertyAttribute>(info);
            var labelFieldAttr = TypeReflectionSystem.GetCustomAttribute<ExposedFieldAttribute>(info);
            this.label = labelPropAttr?.label ?? labelFieldAttr?.label;

            // ShowIf/HideIf属性を読み取り
            var showIfAttr = TypeReflectionSystem.GetCustomAttribute<ShowIfAttribute>(info);
            var hideIfAttr = TypeReflectionSystem.GetCustomAttribute<HideIfAttribute>(info);
            if (showIfAttr != null)
            {
                this.visibilityConditionProperty = showIfAttr.propertyName;
                this.visibilityConditionValue = _ResolveConditionValue(info.DeclaringType, showIfAttr.propertyName, showIfAttr.value);
                this.visibilityShowWhenMatch = true;
            }
            else if (hideIfAttr != null)
            {
                this.visibilityConditionProperty = hideIfAttr.propertyName;
                this.visibilityConditionValue = _ResolveConditionValue(info.DeclaringType, hideIfAttr.propertyName, hideIfAttr.value);
                this.visibilityShowWhenMatch = false;
            }
            else
            {
                this.visibilityConditionProperty = null;
                this.visibilityConditionValue = null;
                this.visibilityShowWhenMatch = true;
            }

            // Section属性を読み取り
            this.sectionAttribute = TypeReflectionSystem.GetCustomAttribute<SectionAttribute>(info);

            // 効果的なアクセスレベルを解決:
            // [Development] > [Experimental] > Section.accessLevel > Public
            if (TypeReflectionSystem.GetCustomAttribute<DevelopmentAttribute>(info) != null)
            {
                this.sectionAccessLevel = AccessLevel.Development;
            }
            else if (TypeReflectionSystem.GetCustomAttribute<ExperimentalAttribute>(info) != null)
            {
                this.sectionAccessLevel = AccessLevel.Experimental;
            }
            else
            {
                this.sectionAccessLevel = this.sectionAttribute?.accessLevel ?? AccessLevel.Public;
            }

            // [FormerlyExposedAs] 旧メンバー名 (Property/Field 自身に付いた属性 + shadow field のメンバー名)
            var collected = ExposedClass._CollectFormerNames(info);
            if (shadowField != null)
            {
                // Phase 1〜2 で書かれた既存 JSON では shadow field のメンバー名 (`_X`) が
                // JSON キーに使われていたので、Property の旧名 alias として追加して読み込み互換を維持する。
                var sfName = shadowField.Name;
                if (!string.IsNullOrEmpty(sfName) && sfName != name && Array.IndexOf(collected, sfName) < 0)
                {
                    var merged = new string[collected.Length + 1];
                    Array.Copy(collected, merged, collected.Length);
                    merged[collected.Length] = sfName;
                    collected = merged;
                }
            }
            this.formerNames = collected;

            //Debug.Log($"Created ExposedPropertyEntity for {info.Name} of type {info.PropertyType.Name}, valueType: {(valueType != null ? valueType.typeName : "null")}");
        }

        /// <summary>
        /// 条件値を参照先プロパティの型に合わせて変換する。
        /// enum型の場合、int値をenum名文字列に変換する（JSONシリアライズ形式と一致させるため）。
        /// </summary>
        private static object _ResolveConditionValue(Type declaringType, string propertyName, object value)
        {
            if (declaringType == null || value == null) return value;

            // 参照先プロパティ/フィールドの型を取得
            Type targetType = null;
            var prop = declaringType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (prop != null)
            {
                targetType = prop.PropertyType;
            }
            else
            {
                var field = declaringType.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (field != null) targetType = field.FieldType;
            }

            // enum型の場合、int値をenum名に変換
            if (targetType != null && targetType.IsEnum && value is int intValue)
            {
                var enumName = Enum.GetName(targetType, intValue);
                if (enumName != null) return enumName;
            }

            return value;
        }

        // 配列要素用のコンストラクタ
        public ExposedPropertyType(System.Type elementType, int arrayIndex)
        {
            Debug.Assert(elementType != null, "ElementType cannot be null");
            Debug.Assert(arrayIndex >= 0, "ArrayIndex must be non-negative");

            this.properyInfo = null;
            this.fieldInfo = null;
            this.shadowField = null;
            this._name = $"[{arrayIndex}]";
            this.isArrayElement = true;
            this.arrayElementType = elementType;
            this.arrayIndex = arrayIndex;
            this.controlType = "default";
            this.isPersistable = true;
            this.isReadOnly = false; // 配列要素は通常書き込み可能
            this.isStatic = false; // 配列要素はstaticではない
            this.exposedValueClass = ExposedClass.Find(elementType);
            this.controlAttribute = new ControlAttribute("default");
            this.help = null;
            this.label = null;
            this.defaultValue = null;
            this.visibilityConditionProperty = null;
            this.visibilityConditionValue = null;
            this.visibilityShowWhenMatch = true;
            this.sectionAttribute = null;
            this.sectionAccessLevel = AccessLevel.Public;
            this.formerNames = Array.Empty<string>();
        }
    }

    public class ExposedFunctionType
    {
        public readonly MethodInfo methodInfo;

        private readonly string _name;

        private readonly string _apiName;

        private readonly ParameterInfo[] _parameters;

        public string name => _name;

        public string apiName => _apiName;

        public System.Type returnType => methodInfo?.ReturnType;

        public ParameterInfo[] parameters => _parameters;

        public bool isValid => methodInfo != null;

        public bool isStatic => methodInfo?.IsStatic ?? false;

        /// <summary>
        /// 宣言順序（プロパティと関数の混合表示用）
        /// </summary>
        public int order { get; internal set; }

        /// <summary>
        /// ヘルプテキスト
        /// </summary>
        public readonly string help;

        /// <summary>
        /// RemoteApp側で表示するラベル
        /// </summary>
        public readonly string label;

        /// <summary>
        /// コントローラー属性
        /// </summary>
        public readonly ControlAttribute controlAttribute;

        public ExposedFunctionType(string name, MethodInfo methodInfo)
        {
            Debug.Assert(methodInfo != null, "MethodInfo cannot be null");

            this.methodInfo = methodInfo;
            this._name = name;
            this._apiName = name.ToLowerInvariant();
            this._parameters = methodInfo.GetParameters();

            // ExposedHelp属性を読み取り（TypeReflectionSystem経由でキャッシュ付き）
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<ExposedHelpAttribute>(methodInfo);
            this.help = helpAttr?.text;

            // ExposedFunctionAttribute からlabelを読み取り
            var funcAttr = TypeReflectionSystem.GetCustomAttribute<ExposedFunctionAttribute>(methodInfo);
            this.label = funcAttr?.label;

            // ControlAttribute属性を読み取り
            this.controlAttribute = TypeReflectionSystem.GetCustomAttribute<ControlAttribute>(methodInfo);
        }

        public object Invoke(object target, object[] args)
        {
            if (!isValid) return null;
            // MethodInvokeSystemに委譲
            return MethodInvokeSystem.Invoke(target, MethodInvokeSystem.CreateInvokeData(methodInfo), args);
        }
    }

}
