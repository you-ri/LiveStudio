using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityEngine;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    [System.Serializable]
    public struct LivePropertyDefine
    {
        public string name;

        public string path;

        public bool isPersistable;

        /// <summary>
        /// 永続化先 (Scene=シーンファイル / Project=プロジェクト設定ファイル)。
        /// </summary>
        public PersistScope persistScope;

        /// <summary>
        /// Shadow Field のメンバー名 (= Field の field.Name)。
        /// Property "X" に対する `[LiveField, Hide, FormerlyNamedAs("X")]` Field を「Shadow Field」として
        /// 検出した場合にセットされる。Shadow Field は propertyTypes に独立 entry を作らず、
        /// 対応 Property の <see cref="LivePropertyType.shadowField"/> として束ねられる。
        /// JSON シリアライザは値を Property setter ではなく shadow field に直接書き込み、
        /// デシリアライズ時の setter 副作用 (Apply 系処理) をバイパスする。
        /// </summary>
        public string shadowFieldPath;

        /// <summary>
        /// Control metadata override (e.g. <see cref="SliderAttribute"/>). When set, it takes
        /// precedence over any [Control] attribute on the member. Used by attribute-less
        /// registration (presets) where metadata cannot come from attributes.
        /// </summary>
        public ControlAttribute control;

        /// <summary>Display label override. Takes precedence over the attribute-provided label.</summary>
        public string label;

        /// <summary>Help text override. Takes precedence over any [Help] attribute on the member.</summary>
        public string help;

        /// <summary>Section override. Takes precedence over any [Section] attribute on the member.</summary>
        public SectionAttribute section;

        /// <summary>
        /// Forbid writes through the API and show the member as display-only.
        /// This only ever adds the restriction: a member that reflection already reports as
        /// read-only (no setter, readonly/const field) stays read-only regardless of this flag.
        /// </summary>
        public bool isReadOnly;

        /// <summary>
        /// Which lane of the live data carries this member.
        ///
        /// Read at runtime, unlike the generator's use of the same declaration: a write to a member
        /// the state lane already carries does not need recording as an input as well, and the only
        /// place that can tell is where the write resolves.
        /// </summary>
        public FrameLane lane;
    }

    /// <summary>
    /// Attribute-less function (method) registration entry. Counterpart of
    /// <see cref="LivePropertyDefine"/> for [LiveFunction]-less methods, used by presets.
    /// </summary>
    [System.Serializable]
    public struct LiveFunctionDefine
    {
        /// <summary>Wire name of the function. Defaults to the method name when null.</summary>
        public string name;

        /// <summary>Method name on the target type.</summary>
        public string path;

        /// <summary>Display label override for the RemoteApp button.</summary>
        public string label;

        /// <summary>Material Icons name shown on the RemoteApp button.</summary>
        public string icon;

        /// <summary>Help text override.</summary>
        public string help;

        /// <summary>Section override (button-only sections).</summary>
        public SectionAttribute section;
    }

    public class LiveClass
    {
        public static IReadOnlyDictionary<System.Type, LiveClass> all => _all;

        private static Dictionary<System.Type, LiveClass> _all = new Dictionary<System.Type, LiveClass>();

        private static Dictionary<string, LiveClass> _byTypeName = new Dictionary<string, LiveClass>();

        /// <summary>
        /// 型に対応する宣言順 index を取得。未登録なら -1。
        /// 実体は <see cref="LiveClassDeclarationOrderTable"/> に分離されている
        /// (Source Gen の ModuleInitializer から LiveClass.cctor を巻き込まないため)。
        /// </summary>
        internal static int GetDeclarationOrderIndex(System.Type type, string memberName)
            => LiveClassDeclarationOrderTable.GetIndex(type, memberName);

        private static void _SetLiveClass(LiveClass ec)
        {
            // 既存のLiveClassがある場合、イベント購読を新インスタンスに移行する
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

            // Every registration path funnels through here, so this is the one place that has to
            // tell connected clients the type table moved. A client fetches /live/types once per
            // connection; without this, a type registered later (lazy resolution, a live class
            // asset arriving with a scene or bundle) would never reach it and its properties would
            // render as "no type info" for the rest of the session.
            LivePropertyBroadcast.BroadcastTypesUpdate();
        }

        private static void _RegisterTypeNameAliases(LiveClass ec)
        {
            if (ec.formerTypeNames == null) return;
            for (int i = 0; i < ec.formerTypeNames.Length; i++)
            {
                var alias = ec.formerTypeNames[i];
                if (string.IsNullOrEmpty(alias)) continue;
                if (alias == ec.typeName) continue; // 現 typeName と同じなら重複登録しない
                if (_byTypeName.TryGetValue(alias, out var existing) && existing.type != ec.type)
                {
                    Debug.LogWarning($"[RemoteControl] FormerlyNamedAs alias '{alias}' on '{ec.typeName}' conflicts with existing type '{existing.typeName}'. Alias will override.");
                }
                _byTypeName[alias] = ec;
            }
        }

        private static void _UnregisterTypeNameAliases(LiveClass ec)
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


        static LiveClass()
        {
            // アセンブリの初期化タイミングでLiveClassの登録を実行。
            // ここは HTTP ワーカースレッドからの型初期化でも走りうるため、エディタ API
            // (TypeCache) を引かないロード済みアセンブリのみの走査にする。取りこぼしは
            // _RegisterStaticLiveObjects の完全走査が埋める。
            _RegisterAllTypesFromAttributes(complete: false);
        }

        public static void Register<T>(string typeName, LivePropertyDefine[] defines, string category = null, string icon = null, bool hideInScene = false)
        {
            Register(typeof(T), typeName, defines, functionDefines: null, category: category, icon: icon, hideInScene: hideInScene);
        }

        /// <summary>
        /// Attribute-less registration for an arbitrary type (including compiled types such as
        /// UnityEngine components). Members and their UI metadata come entirely from the defines,
        /// so no [LiveClass]/[LiveProperty] attributes are required on the type.
        /// Registering the same type again replaces the previous registration (one member set per type).
        /// </summary>
        public static LiveClass Register(System.Type type, string typeName, LivePropertyDefine[] defines,
            LiveFunctionDefine[] functionDefines = null, string category = null, string icon = null, bool hideInScene = false)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var properties = LivePropertyUtility.MakePropertyTypes(type, defines ?? Array.Empty<LivePropertyDefine>());
            var functions = LivePropertyUtility.MakeFunctionTypes(type, functionDefines);
            var liveClass = new LiveClass(type, typeName ?? type.Name, properties, functions,
                category: category, icon: icon, hideInScene: hideInScene);
            _SetLiveClass(liveClass);
            return liveClass;
        }

        public static LiveClass RegisterClass(Type type)
        {
            var classAttribute = TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type);
            if (classAttribute == null) return null;

            var typeName = classAttribute.typeName ?? type.Name;
            var category = classAttribute.Category;
            var icon = classAttribute.Icon;
            var hideInScene = classAttribute.HideInScene;
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<HelpAttribute>(type);
            var help = helpAttr?.text;
            var formerTypeNames = _CollectFormerNames(type);

            var liveClass = new LiveClass(type, typeName, category: category, help: help, icon: icon, hideInScene: hideInScene, formerTypeNames: formerTypeNames);
            _SetLiveClass(liveClass);
            return liveClass;
        }

        /// <summary>
        /// 型/メンバーに付与された全 [FormerlyNamedAs] 属性から旧名の配列を収集する。
        /// 空文字/null や重複は除外する。未付与なら空配列を返す。
        /// </summary>
        internal static string[] _CollectFormerNames(ICustomAttributeProvider provider)
        {
            var attrs = (FormerlyNamedAsAttribute[])provider.GetCustomAttributes(typeof(FormerlyNamedAsAttribute), inherit: false);
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
            var classAttribute = TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type);
            if (classAttribute == null) return;

            // typeNameがnullの場合はクラス名を使用
            var typeName = classAttribute.typeName ?? type.Name;

            // プロパティ、フィールド、メソッドを統合して収集（定義順を保持するため）
            // MemberType: 0=Property/Field, 1=Method
            var allMembers = new List<(MemberInfo member, int token, int memberType, object attr, bool isPersistable)>();

            // プロパティからLivePropertyAttributeを検索（isPersistable は InlineReference の自動 true 化、
            // または Shadow Field との pairing で Field 側 persistable を継承する経路でのみ true になる）
            var propertyInfos = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var propInfo in propertyInfos)
            {
                var propAttribute = TypeReflectionSystem.GetCustomAttribute<LivePropertyAttribute>(propInfo);
                if (propAttribute != null)
                {
                    allMembers.Add((propInfo, propInfo.MetadataToken, 0, propAttribute, false));
                }
            }

            // フィールドからLiveFieldAttributeを検索（isPersistable = true）
            // .NET の GetFields は基底クラスの private フィールドを返さない。
            // 基底に Shadow Field を置いて派生で使うパターン (LiveUnityObjectProxy._name 等) を
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
                var fieldAttribute = TypeReflectionSystem.GetCustomAttribute<LiveFieldAttribute>(fieldInfo);
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

            // メソッドからLiveFunctionAttributeを検索
            var methodInfos = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var methodInfo in methodInfos)
            {
                var funcAttribute = TypeReflectionSystem.GetCustomAttribute<LiveFunctionAttribute>(methodInfo);
                if (funcAttribute != null)
                {
                    allMembers.Add((methodInfo, methodInfo.MetadataToken, 1, funcAttribute, false));
                }
            }

            // Source Gen が型を登録していない場合は一度だけ警告。
            // 全 [LiveClass] 型は generator が登録する前提なので、未登録は generator DLL 未ビルド
            // / バージョン不整合 / 動的ロード型のいずれか。
            if (allMembers.Count > 0 && !LiveClassDeclarationOrderTable.IsRegistered(type)
                && LiveClassDeclarationOrderTable.TryMarkWarned(type))
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
            //   `[LiveProperty] X { get; set; }` と
            //   `[LiveField, Hide][FormerlyNamedAs("X")] _X` のペアを検出する。
            // Field 側は propertyTypes に独立 entry を作らず、Property の shadowField として束ねる。
            // 詳細は LivePropertyDefine.shadowFieldPath を参照。
            var propertyNamesByLiveName = new Dictionary<string, string>(StringComparer.Ordinal); // displayName -> Property member name
            for (int i = 0; i < allMembers.Count; i++)
            {
                var m = allMembers[i];
                if (m.memberType != 0 || !(m.member is PropertyInfo)) continue;
                if (!(m.attr is LivePropertyAttribute pa)) continue;
                var name = pa.name ?? m.member.Name;
                propertyNamesByLiveName[name] = m.member.Name;
            }

            // shadowField マップ: Property のメンバー名 (path) -> Shadow Field のメンバー名 (path)
            // および Field 側の persistable / persistScope 値 (Property に継承)
            var shadowFieldByPropertyPath = new Dictionary<string, (string fieldPath, bool fieldPersistable, PersistScope fieldScope, FrameLane fieldLane)>(StringComparer.Ordinal);
            var shadowFieldMembers = new HashSet<MemberInfo>();
            for (int i = 0; i < allMembers.Count; i++)
            {
                var m = allMembers[i];
                if (m.memberType != 0 || !(m.member is FieldInfo fi)) continue;
                if (!(m.attr is LiveFieldAttribute fieldAttr)) continue;
                // [Hide] が無いものは Shadow ではない (UI 表示用の独立 Field)
                if (TypeReflectionSystem.GetCustomAttribute<HideAttribute>(fi) == null) continue;
                // FormerlyNamedAs で参照される名前のいずれかが同クラスの Property に存在するかをチェック
                var formers = (FormerlyNamedAsAttribute[])fi.GetCustomAttributes(typeof(FormerlyNamedAsAttribute), inherit: false);
                if (formers == null || formers.Length == 0) continue;

                string targetPropertyPath = null;
                for (int j = 0; j < formers.Length; j++)
                {
                    var alias = formers[j].name;
                    if (string.IsNullOrEmpty(alias)) continue;
                    if (propertyNamesByLiveName.TryGetValue(alias, out var propPath))
                    {
                        targetPropertyPath = propPath;
                        break;
                    }
                }
                if (targetPropertyPath == null) continue;

                shadowFieldByPropertyPath[targetPropertyPath] =
                    (fi.Name, fieldAttr.persistable, fieldAttr.persistScope, fieldAttr.lane);
                shadowFieldMembers.Add(fi);
            }

            // ソート順でproperties/functionsに追加し、orderを設定
            var properties = new List<LivePropertyDefine>();
            var functions = new List<LiveFunctionType>();
            var propertyOrderMap = new Dictionary<string, int>(); // プロパティパス -> order

            for (int i = 0; i < allMembers.Count; i++)
            {
                var (member, _, memberType, attr, isPersistable) = allMembers[i];

                if (memberType == 0) // Property/Field
                {
                    // Shadow Field と判定された Field は propertyTypes に登録しない
                    if (shadowFieldMembers.Contains(member)) continue;

                    // LivePropertyAttribute または LiveFieldAttribute から name / persistScope を取得
                    string propName;
                    PersistScope persistScope;
                    FrameLane lane;
                    if (attr is LivePropertyAttribute propAttr)
                    {
                        propName = propAttr.name ?? member.Name;
                        persistScope = propAttr.persistScope;
                        lane = propAttr.lane;
                    }
                    else if (attr is LiveFieldAttribute fieldAttr)
                    {
                        propName = fieldAttr.name ?? member.Name;
                        persistScope = fieldAttr.persistScope;
                        lane = fieldAttr.lane;
                    }
                    else
                    {
                        propName = member.Name;
                        persistScope = PersistScope.Scene;
                        lane = FrameLane.Input;
                    }

                    string shadowFieldPath = null;
                    if (member is PropertyInfo
                        && shadowFieldByPropertyPath.TryGetValue(member.Name, out var shadowInfo))
                    {
                        shadowFieldPath = shadowInfo.fieldPath;
                        // Shadow Field 側の persistable / persistScope / lane を Property に継承。
                        // lane を継承するのは、両者が同じ値の別の顔だから: フィールドが状態レーンに
                        // 載っているのにプロパティが入力レーンだと、同じ値が両方に記録される。
                        isPersistable = shadowInfo.fieldPersistable;
                        persistScope = shadowInfo.fieldScope;
                        lane = shadowInfo.fieldLane;
                    }

                    properties.Add(new LivePropertyDefine
                    {
                        name = propName,
                        path = member.Name,
                        isPersistable = isPersistable,
                        persistScope = persistScope,
                        shadowFieldPath = shadowFieldPath,
                        lane = lane
                    });
                    propertyOrderMap[member.Name] = i;
                }
                else // Method
                {
                    var funcAttr = (LiveFunctionAttribute)attr;
                    var functionName = funcAttr.name ?? member.Name;
                    var funcType = new LiveFunctionType(functionName, (MethodInfo)member);
                    funcType.order = i;
                    functions.Add(funcType);
                }
            }

            if (properties.Count == 0 && functions.Count == 0) return;

            // RegisterClassで登録済みのLiveClassからメタ情報を引き継ぐ
            var oldLiveClass = LiveClass.Get(type);

            // プロパティ型を構築
            var propTypes = properties.Count > 0
                ? LivePropertyUtility.MakePropertyTypes(type, properties.ToArray())
                : (oldLiveClass?.propertyTypes ?? new LivePropertyType[0]);

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
                : (oldLiveClass?.functionTypes ?? new LiveFunctionType[0]);

            // 1回のLiveClass生成で完了
            var newLiveClass = new LiveClass(type, typeName, propTypes, funcTypes,
                oldLiveClass?.category, oldLiveClass?.help, oldLiveClass?.icon,
                oldLiveClass?.hideInScene ?? false, oldLiveClass?.formerTypeNames);
            _SetLiveClass(newLiveClass);
        }

        static int _GetExplicitOrder(object attr)
        {
            switch (attr)
            {
                case LivePropertyAttribute p: return p.order;
                case LiveFieldAttribute f: return f.order;
                case LiveFunctionAttribute fn: return fn.order;
                default: return 0;
            }
        }

        public static void RegisterFromAttributes<T>()
        {
            RegisterClass(typeof(T));
            RegisterProperties(typeof(T));
        }

        /// <param name="complete">
        /// true なら、まだロードされていないアセンブリの型も含めて走査する
        /// (<see cref="TypeReflectionSystem.FindAllTypesWithAttribute{T}"/>)。エディタでは
        /// TypeCache を引くためメインスレッドからのみ渡してよい。static コンストラクタは
        /// ワーカースレッドから誘発されうるので false で呼ぶ。
        /// </param>
        private static void _RegisterAllTypesFromAttributes(bool complete)
        {
            // TypeReflectionSystemを使用して属性付きの型を検索
            var typesWithAttribute = new List<Type>();
            var found = complete
                ? TypeReflectionSystem.FindAllTypesWithAttribute<LiveClassAttribute>()
                : TypeReflectionSystem.FindTypesWithAttribute<LiveClassAttribute>();
            foreach (var type in found)
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
        /// static classのLiveObjectを自動生成する。
        /// SubsystemRegistrationでLiveObjectRegistry.instancesがクリアされた後に実行する必要がある。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void _RegisterStaticLiveObjects()
        {
            // 静的 cctor の _RegisterAllTypesFromAttributes() は LiveClass への最初の
            // アクセスで走るが、それがゲームアセンブリ (LiveStudio 等) の読み込み前に
            // 起きると走査が一部のアセンブリしか見られず、static [LiveClass] 型が
            // _all から欠落する。static 型は lazy 登録経路を持たない (Find(string) は
            // 属性から登録しない) ため、一度欠落するとドメイン生存中ずっと出てこない。
            // ここで走査をやり直す。本メソッドの呼び出し元 (再生時の AfterAssembliesLoaded /
            // エディタの InitializeOnLoadMethod) はどちらも全アセンブリ読み込み後にメインスレッドで
            // 走るため、完全走査 (エディタは TypeCache 併用) を掛けられる。ロード済みアセンブリだけを
            // 見る走査では、そのとき Mono がまだ読んでいないアセンブリの型がまるごと落ちる。
            _RegisterAllTypesFromAttributes(complete: true);

            // 静的クラスを先にスナップショットしておく。static LiveObjectHandle の生成は
            // 既定値キャプチャ (SetDefault) を伴い、その過程で別の型が lazy 登録されて
            // _all が列挙中に変化しうるため、直接 _all を foreach しない。
            var staticClasses = new List<LiveClass>();
            foreach (var kvp in _all)
            {
                if (kvp.Value.isStatic) staticClasses.Add(kvp.Value);
            }

            foreach (var liveClass in staticClasses)
            {
                // 既に登録済みならスキップ
                if (LiveObjectRegistry.FindById(liveClass.typeName) != null)
                    continue;

                new LiveObjectHandle(liveClass.typeName, liveClass, null);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Edit モードのエディタ起動時 / ドメインリロード後にも静的 LiveObjectHandle を登録する。
        /// `RuntimeInitializeOnLoadMethod` は Play 起動時のみ発火するため、Edit モードで動作する
        /// `RemoteControlServerManager` / `UIDesignerWindow` から静的クラスが引けるようにここで補完する。
        /// `_RegisterStaticLiveObjects` は FindById で重複スキップするため冪等。
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void _EditorRegisterStaticLiveObjects()
        {
            _RegisterStaticLiveObjects();
        }
#endif

        public static void Unregister(LiveClass liveClass)
        {
            if (_all.ContainsKey(liveClass.type))
            {
                _all.Remove(liveClass.type);
                _byTypeName.Remove(liveClass.typeName);
                _UnregisterTypeNameAliases(liveClass);
                // Same contract as _SetLiveClass: the table changed, so the client has to refetch.
                LivePropertyBroadcast.BroadcastTypesUpdate();
            }
        }

        /// <summary>
        /// 登録されたすべてのLiveClassをクリアします（テスト用）
        /// </summary>
        public static void Clear()
        {
            _all.Clear();
            _byTypeName.Clear();
            // 配列要素記述子は liveValueClass 経由でレジストリに依存するため、ここで一緒に破棄する。
            LivePropertyType.ClearArrayElementCache();
        }

        /// <summary>
        /// すべてのLiveClassをクリアし、属性から再登録します
        /// </summary>
        public static void Reset()
        {
            Clear();
            // _RegisterStaticLiveObjects() が先頭で _RegisterAllTypesFromAttributes()
            // を呼ぶため、ここで重ねて走査しない。
            _RegisterStaticLiveObjects();
        }
        /// <summary>
        /// 型からLiveClassを取得、未登録の場合はLiveClassAttributeがあれば動的に登録を試みる
        /// </summary>
        private static LiveClass _GetOrRegister(System.Type type)
        {
            if (_all.TryGetValue(type, out var liveClass))
            {
                return liveClass;
            }

            // 見つからない場合、該当型にLiveClassAttributeがあれば動的に登録を試みる
            var attr = TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type);
            if (attr != null)
            {
                RegisterClass(type);
                RegisterProperties(type);
                if (_all.TryGetValue(type, out liveClass))
                {
                    return liveClass;
                }
            }

            return null;
        }

        public static LiveClass Get<T>()
        {
            return Get(typeof(T));
        }

        public static LiveClass Get(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            var liveClass = _GetOrRegister(type);
            if (liveClass != null) return liveClass;

            throw new Exception($"LiveClass for type `{type.Name}` is not registered.");
        }

        public static bool TryGet(System.Type type, out LiveClass liveClass)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _all.TryGetValue(type, out liveClass);
        }

        public static bool Has(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _all.ContainsKey(type);
        }

        public static LiveClass Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            _byTypeName.TryGetValue(typeName, out var result);
            return result;
        }

        public static LiveClass Find(System.Type type)
        {
            Debug.Assert(type != null, "Type cannot be null");

            return _GetOrRegister(type);
        }


        public readonly System.Type type;

        public readonly string typeName;

        public readonly string category;

        public readonly bool isStatic;

        public readonly LivePropertyType[] propertyTypes;

        public readonly LiveFunctionType[] functionTypes;

        public readonly string help;

        public readonly string icon;

        /// <summary>
        /// trueの場合、RemoteAppのシーンページ一覧から本型のオブジェクトを除外する。
        /// </summary>
        public readonly bool hideInScene;

        /// <summary>
        /// [FormerlyNamedAs] で指定された旧 typeName の配列。`LiveClass.Find(alias)` 経由で解決可能。
        /// クラスのリネーム時に旧シーンファイルからの復元を可能にする。
        /// </summary>
        public readonly string[] formerTypeNames;

        /// <summary>
        /// True when <paramref name="name"/> is this class's current <see cref="typeName"/> or one of
        /// its <see cref="formerTypeNames"/>. Files written before a type rename carry the old name,
        /// so any @type validation must accept the aliases that <c>LiveClass.Find</c> already resolves.
        /// </summary>
        public bool IsCurrentOrFormerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == typeName) return true;
            if (formerTypeNames == null) return false;
            for (int i = 0; i < formerTypeNames.Length; i++)
            {
                if (name == formerTypeNames[i]) return true;
            }
            return false;
        }

        // baseTypeNames の遅延キャッシュ (型ごとに一度だけ算出)。
        private string[] _baseTypeNames;

        /// <summary>
        /// ユーザー定義の基底クラス名の連鎖 (最も近い祖先が先頭、自身は含まない)。祖先が [LiveClass]
        /// ならその <see cref="typeName"/>、そうでなければ素の C# 名を使う (共有抽象基底のように
        /// [LiveClass] を付けない型もグループ判定キーとして有用)。<c>System.*</c> / <c>UnityEngine.*</c>
        /// の最初のフレームワーク型で打ち切るので、エンジン基底型は含まれない。
        /// クライアントは <c>GET /live/types</c> の <c>baseTypes</c> でこれを受け取り、具象 <c>@type</c>
        /// を列挙せずに多態グループ (例: すべての <c>AvatarAssetBase</c> 派生) を判定できる。
        /// </summary>
        public IReadOnlyList<string> baseTypeNames
        {
            get
            {
                if (_baseTypeNames == null)
                {
                    var names = new List<string>();
                    for (var t = type?.BaseType; t != null && !_IsFrameworkType(t); t = t.BaseType)
                    {
                        // Non-registering lookup (_all, not Find): reading a base type's name must not
                        // register it as a side effect. Find -> _GetOrRegister would add an [LiveClass]
                        // ancestor to _all, which HandleGetTypes enumerates live — a collection-modified
                        // hazard during type serialization. A non-registered base falls back to its C# name.
                        names.Add(_all.TryGetValue(t, out var ec) ? ec.typeName : t.Name);
                    }
                    _baseTypeNames = names.ToArray();
                }
                return _baseTypeNames;
            }
        }

        /// <summary>
        /// この型が <paramref name="baseTypeName"/> の (厳密な) サブクラスか。<see cref="baseTypeNames"/>
        /// に <paramref name="baseTypeName"/> を含むかで判定する。.NET の <see cref="System.Type.IsSubclassOf"/>
        /// と同じく厳密 (自分自身は false)・interface は対象外。クライアント側の同名判定 (RemoteApp
        /// <c>isSubclassOf</c>) はこの by-name 定義を wire 越しに鏡写しにするため、両者の語義が一致する。
        /// </summary>
        public bool IsSubclassOf(string baseTypeName)
        {
            if (string.IsNullOrEmpty(baseTypeName)) return false;
            var names = baseTypeNames;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == baseTypeName) return true;
            }
            return false;
        }

        // フレームワーク (BCL / Unity / エンジン) 由来の型か。基底クラス連鎖はここで打ち切るので、
        // baseTypeNames には利用側が判定に使うユーザー定義の基底のみが残る。UnityEngine に加え
        // Unity.* / UnityEditor.* も除外し、エンジン内部やエディタ基底型が wire 契約に漏れないようにする。
        private static bool _IsFrameworkType(System.Type t)
        {
            if (t == typeof(object)) return true;
            var ns = t.Namespace;
            if (string.IsNullOrEmpty(ns)) return false;
            return ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal)
                || ns == "UnityEngine" || ns.StartsWith("UnityEngine.", System.StringComparison.Ordinal)
                || ns == "UnityEditor" || ns.StartsWith("UnityEditor.", System.StringComparison.Ordinal)
                || ns == "Unity" || ns.StartsWith("Unity.", System.StringComparison.Ordinal);
        }

        private readonly Dictionary<string, LivePropertyType> _propertyByName;

        private readonly Dictionary<string, LiveFunctionType> _functionByApiName;

        // [LiveKey] が付いた唯一のプロパティ (無ければ null)。配列要素をインデックスでなく
        // 安定キーで引く ("expressions[Joy].weight") ための識別子。
        private readonly LivePropertyType _keyProperty;

        // Convention-based callbacks for static-classed LiveObjectHandle (target == null).
        // C# 9 cannot put static abstract members on an interface, so we resolve a
        // public static parameterless method by name during registration.
        private readonly MethodInfo _staticOnAfterDeserialize;

        private readonly MethodInfo _staticOnBeforeSerialize;

        public delegate void PropertyChangingDelegate(LiveProperty property, object newValue);

        public delegate void PropertyChangedDelegate(LiveProperty property, object oldValue);

        public event PropertyChangingDelegate onPropertyChanging;

        public event PropertyChangedDelegate onPropertyChanged;

        /// <summary>
        /// onPropertyChanging / onPropertyChanged のいずれかに購読者がいるか。
        /// 購読者がいなければ <see cref="LiveProperty.TrySetValue{T}(T)"/> は oldValue の
        /// 読み取り (boxing を伴う) とイベント発火を丸ごと省略できる。
        /// </summary>
        internal bool hasPropertyChangeListeners => onPropertyChanging != null || onPropertyChanged != null;

        private static Dictionary<string, LiveFunctionType> _BuildFunctionApiNameIndex(LiveFunctionType[] functionTypes)
        {
            var dict = new Dictionary<string, LiveFunctionType>(functionTypes.Length);
            for (int i = 0; i < functionTypes.Length; i++)
            {
                dict[functionTypes[i].apiName] = functionTypes[i];
            }
            return dict;
        }

        private static Dictionary<string, LivePropertyType> _BuildPropertyNameIndex(LivePropertyType[] propertyTypes)
        {
            var dict = new Dictionary<string, LivePropertyType>(propertyTypes.Length);
            for (int i = 0; i < propertyTypes.Length; i++)
            {
                var pt = propertyTypes[i];
                dict[pt.name] = pt;

                // [FormerlyNamedAs] で指定された旧メンバー名もエイリアスとして引けるようにする
                if (pt.formerNames != null)
                {
                    for (int j = 0; j < pt.formerNames.Length; j++)
                    {
                        var alias = pt.formerNames[j];
                        if (string.IsNullOrEmpty(alias)) continue;
                        if (alias == pt.name) continue;
                        if (dict.TryGetValue(alias, out var existing) && existing != pt)
                        {
                            Debug.LogWarning($"[RemoteControl] FormerlyNamedAs alias '{alias}' on property '{pt.name}' conflicts with existing property '{existing.name}'. Alias will override.");
                        }
                        dict[alias] = pt;
                    }
                }
            }
            return dict;
        }

        // propertyTypes から [LiveKey] の付いたメンバーを1個特定する。
        // 複数付いていた場合は宣言順で最初の1個を採用し警告する。
        private static LivePropertyType _FindKeyProperty(LivePropertyType[] propertyTypes)
        {
            LivePropertyType found = null;
            for (int i = 0; i < propertyTypes.Length; i++)
            {
                MemberInfo member = (MemberInfo)propertyTypes[i].properyInfo ?? propertyTypes[i].fieldInfo;
                if (member == null) continue;
                if (!Attribute.IsDefined(member, typeof(LiveKeyAttribute))) continue;

                if (found != null)
                {
                    Debug.LogWarning($"[RemoteControl] Multiple [LiveKey] members found; using '{found.name}' and ignoring '{propertyTypes[i].name}'.");
                    continue;
                }
                found = propertyTypes[i];
            }
            return found;
        }

        /// <summary>
        /// [LiveKey] が付いたプロパティ。配列要素を安定キーで引く際に使う。無ければ null。
        /// </summary>
        public LivePropertyType keyProperty => _keyProperty;

        private LiveClass(System.Type type, string typeName,
            LivePropertyType[] propertyTypes = null, LiveFunctionType[] functionTypes = null,
            string category = null, string help = null, string icon = null, bool hideInScene = false,
            string[] formerTypeNames = null)
        {
            this.type = type;
            this.typeName = typeName;
            this.category = category;
            this.isStatic = type.IsAbstract && type.IsSealed;
            this.propertyTypes = propertyTypes ?? new LivePropertyType[0];
            this.functionTypes = functionTypes ?? new LiveFunctionType[0];
            this.help = help;
            this.icon = icon;
            this.hideInScene = hideInScene;
            this.formerTypeNames = formerTypeNames ?? Array.Empty<string>();
            this._propertyByName = _BuildPropertyNameIndex(this.propertyTypes);
            this._functionByApiName = _BuildFunctionApiNameIndex(this.functionTypes);
            this._keyProperty = _FindKeyProperty(this.propertyTypes);

            if (this.isStatic && type != null)
            {
                const BindingFlags kStaticPublic = BindingFlags.Public | BindingFlags.Static;
                this._staticOnAfterDeserialize = type.GetMethod(
                    "OnAfterLiveDeserialize", kStaticPublic, null, Type.EmptyTypes, null);
                this._staticOnBeforeSerialize = type.GetMethod(
                    "OnBeforeLiveSerialize", kStaticPublic, null, Type.EmptyTypes, null);
            }
        }

        /// <summary>
        /// Fires the convention-based static deserialize callback if one exists.
        /// Used by <see cref="LivePropertySerializer"/> when the owning
        /// LiveObjectHandle's target is null (static class).
        /// </summary>
        internal void InvokeStaticAfterDeserialize()
        {
            _staticOnAfterDeserialize?.Invoke(null, null);
        }

        /// <summary>
        /// Fires the convention-based static serialize callback if one exists.
        /// Used by <see cref="LivePropertySerializer.SerializeFullToJObject"/>
        /// when the owning LiveObjectHandle's target is null (static class).
        /// </summary>
        internal void InvokeStaticBeforeSerialize()
        {
            _staticOnBeforeSerialize?.Invoke(null, null);
        }

        /// <summary>
        /// 旧LiveClassインスタンスからイベント購読を移行する。
        /// RegisterPropertiesで新インスタンスに差し替える際に、既存の購読者が失われるのを防ぐ。
        /// </summary>
        internal void _MigrateEventsFrom(LiveClass old)
        {
            if (old == null || old == this) return;
            this.onPropertyChanging = old.onPropertyChanging;
            this.onPropertyChanged = old.onPropertyChanged;
            old.onPropertyChanging = null;
            old.onPropertyChanged = null;
        }

        internal void RaisePropertyChanging(LiveProperty property, object newValue)
        {
            onPropertyChanging?.Invoke(property, newValue);
        }

        internal void RaisePropertyChanged(LiveProperty property, object oldValue)
        {
            onPropertyChanged?.Invoke(property, oldValue);
        }

        public LivePropertyType FindProperty(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _propertyByName.TryGetValue(name, out var result);
            return result;
        }

        /// <summary>
        /// <see cref="FindProperty(string)"/> の span 版。<c>name.ToString()</c> を挟まずに引けるため、
        /// パス解決のホットパス (<see cref="LiveObjectHandle.GetProperty(ReadOnlySpan{char})"/>) で
        /// 文字列確保を避けられる。_propertyByName (Dictionary) は span でキー検索できないため
        /// propertyTypes を走査する (要素数は通常数十なのでホットパスでも無視できる)。
        /// _propertyByName のキー空間は propertyTypes の name + [FormerlyNamedAs] エイリアスと一致するので、
        /// string 版と同じ結果を返す (名前が競合しない前提。競合時は _BuildPropertyNameIndex が警告する)。
        /// </summary>
        public LivePropertyType FindProperty(ReadOnlySpan<char> name)
        {
            if (name.IsEmpty) return null;

            // まず正規名で照合
            for (int i = 0; i < propertyTypes.Length; i++)
            {
                if (name.SequenceEqual(propertyTypes[i].name.AsSpan()))
                    return propertyTypes[i];
            }

            // 次に [FormerlyNamedAs] エイリアスで照合
            for (int i = 0; i < propertyTypes.Length; i++)
            {
                var formerNames = propertyTypes[i].formerNames;
                if (formerNames == null) continue;
                for (int j = 0; j < formerNames.Length; j++)
                {
                    if (!string.IsNullOrEmpty(formerNames[j]) && name.SequenceEqual(formerNames[j].AsSpan()))
                        return propertyTypes[i];
                }
            }

            return null;
        }

        public LiveFunctionType FindFunction(string apiName)
        {
            if (string.IsNullOrEmpty(apiName)) return null;
            _functionByApiName.TryGetValue(apiName, out var result);
            return result;
        }
    }
    
    /// <summary>
    /// 条件付き表示 (ShowIf/HideIf) の 1 条件。1 メンバーに複数条件が付く場合は AND で評価される
    /// (RemoteApp 側)。値の一致判定に使うため <see cref="value"/> はシリアライズ形式に揃えて保持する
    /// (enum は名前文字列)。
    /// </summary>
    public readonly struct LiveVisibilityCondition
    {
        /// <summary>参照先プロパティ名。</summary>
        public readonly string property;

        /// <summary>比較値。</summary>
        public readonly object value;

        /// <summary>true=ShowIf (一致で表示), false=HideIf (一致で非表示)。</summary>
        public readonly bool showWhenMatch;

        public LiveVisibilityCondition(string property, object value, bool showWhenMatch)
        {
            this.property = property;
            this.value = value;
            this.showWhenMatch = showWhenMatch;
        }
    }

    public class LivePropertyType
    {
        public readonly PropertyInfo properyInfo;

        public readonly FieldInfo fieldInfo;

        /// <summary>
        /// Shadow Field 経由のバッキングストレージ。non-null の場合、JSON シリアライザは
        /// <see cref="properyInfo"/> setter をバイパスしてここに直接書き込む。
        /// RemoteApp 経由の SetProperty は引き続き <see cref="properyInfo"/> setter を呼ぶ。
        /// 詳細は <see cref="LivePropertyDefine.shadowFieldPath"/> を参照。
        /// </summary>
        public readonly FieldInfo shadowField;

        /// <summary>
        /// Which lane of the live data carries this member. See <see cref="FrameLane"/>.
        ///
        /// <see cref="FrameLane.State"/> means the value is copied every frame regardless, so a
        /// write to it does not also need to be kept as an input -- the same value in both lanes is
        /// the input record paying 548 bytes to say what the state lane already said.
        /// </summary>
        public readonly FrameLane lane;

        /// <summary>
        /// Source Generator が生成した高速 get/set アクセサ。non-null の場合、
        /// <see cref="LivePropertyUtility.GetValueRaw"/> / <see cref="LivePropertyUtility.SetValueRaw"/>
        /// は reflection の代わりにこれを呼ぶ。未登録 (struct 型 / 非アクセスメンバー等) では null で、
        /// reflection にフォールバックする。read-only メンバーでは <see cref="setter"/> が null。
        /// </summary>
        public readonly Func<object, object> getter;
        public readonly Action<object, object> setter;

        /// <summary>
        /// 値型メンバー向けの型付きアクセサ。<c>Func&lt;object,T&gt;</c> / <c>Action&lt;object,T&gt;</c> で、
        /// object を経由しないため boxing を避けられる。<see cref="LiveProperty.TryGetValue{T}"/> /
        /// <see cref="LiveProperty.TrySetValue{T}(T)"/> がキャストして呼ぶ。参照型メンバー・配列要素・
        /// 未登録メンバーでは null (呼び出し側は object 経路にフォールバックする)。
        /// </summary>
        public readonly System.Delegate typedGetter;
        public readonly System.Delegate typedSetter;

        public readonly ControlAttribute controlAttribute;

        public readonly LiveClass liveValueClass;

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
        /// 条件付き表示の条件一覧 (ShowIf/HideIf)。複数指定時は AND で評価される。
        /// 条件が無いときは空配列 (null にはしない)。
        /// </summary>
        public readonly LiveVisibilityCondition[] visibilityConditions;

        /// <summary>
        /// 条件付き表示が設定されているか
        /// </summary>
        public bool hasVisibilityCondition => visibilityConditions != null && visibilityConditions.Length > 0;

        /// <summary>
        /// セクション属性（NavigatePageでのセクション表示用）
        /// </summary>
        public readonly SectionAttribute sectionAttribute;

        /// <summary>
        /// レイアウトグループ属性（セクション内での縦横配置用）。未指定なら null。
        /// </summary>
        public readonly LayoutAttribute layoutAttribute;

        /// <summary>
        /// [Collapsed] が付いていれば true。RemoteApp が配列・構造体を初期折りたたみで描画するヒント。
        /// </summary>
        public readonly bool collapsed;

        /// <summary>
        /// セクションに適用される最終アクセスレベル。
        /// <see cref="ExperimentalAttribute"/> / <see cref="DevelopmentAttribute"/> が
        /// 同じメンバーに付いていればそれを優先し、無ければ
        /// <see cref="SectionAttribute.accessLevel"/> を使う。
        /// </summary>
        public readonly AccessLevel sectionAccessLevel;

        /// <summary>
        /// [FormerlyNamedAs] で指定された旧メンバー名の配列。
        /// `LiveClass.FindProperty(alias)` 経由で引けるようになり、フィールド/プロパティのリネーム後も
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
        /// <see cref="RawJsonAttribute"/> が付いた string メンバーかどうか。true の場合、シリアライザは
        /// 値文字列を JSON として埋め込み (エスケープ文字列にしない)、読込時に再文字列化する。
        /// </summary>
        public readonly bool isRawJson;

        /// <summary>
        /// 永続化先 (Scene=シーンファイル / Project=プロジェクト設定ファイル)。
        /// <see cref="isPersistable"/> が true の場合にのみ意味を持つ。
        /// </summary>
        public readonly PersistScope persistScope;

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
        /// LiveObject参照かどうか（UnityEngine.Objectのみ対象）
        /// </summary>
        public bool isLiveObjectReference => liveValueClass != null && isReference && typeof(UnityEngine.Object).IsAssignableFrom(valueType);

        /// <summary>
        /// フィールドが他プロパティへの参照 (LivePropertyRef) かどうか。
        /// true のとき、値取得/設定/dirty/revert は参照先の LiveProperty に委譲される。
        /// </summary>
        public bool isLivePropertyReference => valueType == typeof(LivePropertyRef);

        /// <summary>
        /// 実効的な値型を取得する。
        /// 通常は <see cref="valueType"/> と同じだが、<see cref="isLivePropertyReference"/> が true の場合は
        /// 参照先プロパティの値型 (例: float) を返す。TypeDefinition 出力でクライアントに伝える型として使う。
        /// 解決できない場合は <see cref="valueType"/> (= typeof(LivePropertyRef)) にフォールバック。
        /// </summary>
        public Type resolvedValueType
        {
            get
            {
                if (isLivePropertyReference && fieldInfo != null && fieldInfo.IsStatic)
                {
                    // static readonly LivePropertyRef を前提に値を取得する。
                    // インスタンスフィールドの場合はインスタンス依存なのでここでは解決せず valueType を返す。
                    var refValue = fieldInfo.GetValue(null);
                    if (refValue is LivePropertyRef pr && pr.targetValueType != null)
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
                if (isLivePropertyReference && fieldInfo != null && fieldInfo.IsStatic)
                {
                    var refValue = fieldInfo.GetValue(null);
                    if (refValue is LivePropertyRef pr)
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
        /// 自身または配列要素がLiveObject参照を含むかどうか
        /// 永続化時のreadonly/dirty判定に使用
        /// liveValueClassは登録順序に依存するため、ここではvalueTypeのみで判定する
        /// 配列の場合、要素型がUnityEngine.Object派生であれば実行時に派生型の
        /// LiveObject参照を含む可能性があるため true を返す
        /// </summary>
        public bool containsLiveObjectReference
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


        public LivePropertyType(string name, MemberInfo info, bool isPersistable = true, FieldInfo shadowField = null, PersistScope persistScope = PersistScope.Scene,
            ControlAttribute controlOverride = null, string labelOverride = null, string helpOverride = null, SectionAttribute sectionOverride = null,
            bool readOnlyOverride = false, FrameLane lane = FrameLane.Input)
        {
            this.lane = lane;
            Debug.Assert(info != null, "PropertyInfo cannot be null");

            this.properyInfo = info as PropertyInfo;
            this.fieldInfo = info as FieldInfo;
            this.shadowField = shadowField;
            this._name = name;
            this.isPersistable = isPersistable;
            this.persistScope = persistScope;
            this.isArrayElement = false;
            this.arrayElementType = null;
            this.arrayIndex = -1;
            this.controlType = "default";

            // Source Generator が登録した高速アクセサを引く (キー = メンバー宣言型 + メンバー名)。
            // 未登録なら getter/setter は null のままで、Get/SetValueRaw は reflection にフォールバックする。
            // typedGetter/typedSetter は値型メンバーのみ non-null (box 回避経路)。
            if (info != null)
            {
                LiveMemberAccessorTable.TryGet(info.DeclaringType, info.Name,
                    out this.getter, out this.setter, out this.typedGetter, out this.typedSetter);
            }

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

            // Declared read-only (attribute-less registration) can only add the restriction.
            // Never clear a reflection-derived one — there is no setter to call.
            if (readOnlyOverride) this.isReadOnly = true;

            var valueType = properyInfo != null ? properyInfo.PropertyType : fieldInfo.FieldType;
            if (LiveClass.Has(valueType))
            {
                this.liveValueClass = LiveClass.Get(valueType);
            }
            // Define-side override (attribute-less registration) takes precedence over member attributes.
            this.controlAttribute = controlOverride ?? TypeReflectionSystem.GetCustomAttribute<ControlAttribute>(info) ?? new ControlAttribute("default");

            // [RawJson]: string メンバーの値を埋め込み JSON として出力する (二重エンコード回避)。
            this.isRawJson = TypeReflectionSystem.GetCustomAttribute<RawJsonAttribute>(info) != null;

            // [InlineReference] は保存時に Component などを pending entry として書き出すための
            // マーカー。readonly Property でも isPersistable=true 扱いになる。
            if (this.controlAttribute is InlineReferenceAttribute)
            {
                this.isPersistable = true;
            }

            // Scene 以外の PersistScope を指定しても persistable でなければどこにも保存されない。
            // (shadow field / [InlineReference] を持たない [LiveProperty] などが該当)。誤用を早期検知する。
            if (this.persistScope != PersistScope.Scene && !this.isPersistable)
            {
                Debug.LogWarning($"[RemoteControl] Member '{info?.DeclaringType?.Name}.{_name}' is marked PersistScope.{this.persistScope} but is not persistable, so it will not be saved anywhere. Pair it with a shadow field or make it a persistable [LiveField].");
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
                        var ec = LiveClass.Find(dt);
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
                        var ec = LiveClass.Find(dt);
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

            // Help属性を読み取り（TypeReflectionSystem経由でキャッシュ付き）。Define側の上書きを優先。
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<HelpAttribute>(info);
            this.help = helpOverride ?? helpAttr?.text;

            // label属性を読み取り（LivePropertyAttribute または LiveFieldAttribute から）。Define側の上書きを優先。
            var labelPropAttr = TypeReflectionSystem.GetCustomAttribute<LivePropertyAttribute>(info);
            var labelFieldAttr = TypeReflectionSystem.GetCustomAttribute<LiveFieldAttribute>(info);
            this.label = labelOverride ?? labelPropAttr?.label ?? labelFieldAttr?.label;

            // ShowIf/HideIf属性を読み取り (複数付与可、AND 評価)。
            // ShowIf を先に並べることで、単一条件を "visibility" として出力する際の優先順を従来と揃える。
            this.visibilityConditions = _CollectVisibilityConditions(info, info.DeclaringType);

            // Section属性を読み取り。Define側の上書きを優先。
            this.sectionAttribute = sectionOverride ?? TypeReflectionSystem.GetCustomAttribute<SectionAttribute>(info);

            // Layout属性を読み取り（セクション内の縦横グループ）
            this.layoutAttribute = TypeReflectionSystem.GetCustomAttribute<LayoutAttribute>(info);

            // [Collapsed] を読み取り（RemoteApp の初期折りたたみヒント）
            this.collapsed = TypeReflectionSystem.GetCustomAttribute<CollapsedAttribute>(info) != null;

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

            // [FormerlyNamedAs] 旧メンバー名 (Property/Field 自身に付いた属性 + shadow field のメンバー名)
            var collected = LiveClass._CollectFormerNames(info);
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

            //Debug.Log($"Created LivePropertyEntity for {info.Name} of type {info.PropertyType.Name}, valueType: {(valueType != null ? valueType.typeName : "null")}");
        }

        /// <summary>
        /// メンバーに付いた ShowIf/HideIf 属性を全て読み取り、条件配列を構築する (AND 評価用)。
        /// ShowIf を先に並べ、単一条件を "visibility" として出力する際の優先順を従来実装 (ShowIf 優先) に合わせる。
        /// </summary>
        internal static LiveVisibilityCondition[] _CollectVisibilityConditions(MemberInfo info, Type declaringType)
        {
            List<LiveVisibilityCondition> conditions = null;

            foreach (var attr in info.GetCustomAttributes<ShowIfAttribute>())
            {
                conditions ??= new List<LiveVisibilityCondition>();
                conditions.Add(new LiveVisibilityCondition(
                    attr.propertyName, _ResolveConditionValue(declaringType, attr.propertyName, attr.value), true));
            }

            foreach (var attr in info.GetCustomAttributes<HideIfAttribute>())
            {
                conditions ??= new List<LiveVisibilityCondition>();
                conditions.Add(new LiveVisibilityCondition(
                    attr.propertyName, _ResolveConditionValue(declaringType, attr.propertyName, attr.value), false));
            }

            return conditions != null ? conditions.ToArray() : Array.Empty<LiveVisibilityCondition>();
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
        // 配列要素用 LivePropertyType のキャッシュ。記述子は (コレクション型, index) に対して
        // 不変なので使い回し、GetPropertyIndex のループ呼び出しでの GC を抑える。
        // liveValueClass が LiveClass レジストリに依存するため、レジストリを作り直す
        // LiveClass.Clear() で無効化する。RemoteControl ハンドラ (ワーカースレッド) から
        // 読み書きされるため ConcurrentDictionary を使う。
        private static readonly ConcurrentDictionary<(System.Type collectionType, int index), LivePropertyType> _arrayElementCache
            = new ConcurrentDictionary<(System.Type, int), LivePropertyType>();

        /// <summary>
        /// コレクション型と index に対応する配列要素の <see cref="LivePropertyType"/> を返す。
        /// 記述子は (collectionType, index) に対して不変なのでキャッシュして再利用する。
        /// </summary>
        internal static LivePropertyType GetArrayElement(System.Type collectionType, int index)
        {
            // ラムダは key しか参照しない (キャプチャ無し) ため、コンパイラが delegate を
            // 静的キャッシュし、呼び出しごとの closure 確保は発生しない。
            return _arrayElementCache.GetOrAdd((collectionType, index),
                key => new LivePropertyType(
                    LivePropertyUtility.GetCollectionElementType(key.collectionType), key.index));
        }

        /// <summary>配列要素記述子キャッシュを破棄する (レジストリ再構築時に呼ぶ)。</summary>
        internal static void ClearArrayElementCache() => _arrayElementCache.Clear();

        public LivePropertyType(System.Type elementType, int arrayIndex)
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
            this.persistScope = PersistScope.Scene;
            this.lane = FrameLane.Input;
            this.isRawJson = false; // 配列要素自体は RawJson 対象外 (要素内の string メンバーが個別に判定される)
            this.isReadOnly = false; // 配列要素は通常書き込み可能
            this.isStatic = false; // 配列要素はstaticではない
            this.liveValueClass = LiveClass.Find(elementType);
            this.controlAttribute = new ControlAttribute("default");
            this.help = null;
            this.label = null;
            this.defaultValue = null;
            this.visibilityConditions = Array.Empty<LiveVisibilityCondition>();
            this.sectionAttribute = null;
            this.layoutAttribute = null;
            this.sectionAccessLevel = AccessLevel.Public;
            this.collapsed = false;
            this.formerNames = Array.Empty<string>();
        }
    }

    public class LiveFunctionType
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
        /// RemoteApp のボタンに表示するアイコン名 (Material Icons)。未指定なら null。
        /// </summary>
        public readonly string icon;

        /// <summary>
        /// コントローラー属性
        /// </summary>
        public readonly ControlAttribute controlAttribute;

        /// <summary>
        /// レイアウトグループ属性（セクション内での縦横配置用）。未指定なら null。
        /// ボタンを横一列に並べる用途で、プロパティと同じ仕組みを関数にも適用する。
        /// </summary>
        public readonly LayoutAttribute layoutAttribute;

        /// <summary>
        /// セクション属性。関数にも付与でき、ボタンだけで構成されるセクションを宣言できる。
        /// </summary>
        public readonly SectionAttribute sectionAttribute;

        /// <summary>
        /// セクションに適用される最終アクセスレベル。プロパティ側と同じ解決規則
        /// ([Development] > [Experimental] > Section.accessLevel > Public)。
        /// </summary>
        public readonly AccessLevel sectionAccessLevel;

        /// <summary>
        /// 条件付き表示の条件一覧 (ShowIf/HideIf)。複数指定時は AND で評価される。
        /// 条件が無いときは空配列 (null にはしない)。関数ボタンをプロパティと同様に出し分けるために使う。
        /// </summary>
        public readonly LiveVisibilityCondition[] visibilityConditions;

        /// <summary>条件付き表示が設定されているか。</summary>
        public bool hasVisibilityCondition => visibilityConditions != null && visibilityConditions.Length > 0;

        public LiveFunctionType(string name, MethodInfo methodInfo,
            string labelOverride = null, string iconOverride = null, string helpOverride = null, SectionAttribute sectionOverride = null)
        {
            Debug.Assert(methodInfo != null, "MethodInfo cannot be null");

            this.methodInfo = methodInfo;
            this._name = name;
            this._apiName = name.ToLowerInvariant();
            this._parameters = methodInfo.GetParameters();

            // Help属性を読み取り（TypeReflectionSystem経由でキャッシュ付き）。Define側の上書きを優先。
            var helpAttr = TypeReflectionSystem.GetCustomAttribute<HelpAttribute>(methodInfo);
            this.help = helpOverride ?? helpAttr?.text;

            // LiveFunctionAttribute から label / icon を読み取り。Define側の上書きを優先。
            var funcAttr = TypeReflectionSystem.GetCustomAttribute<LiveFunctionAttribute>(methodInfo);
            this.label = labelOverride ?? funcAttr?.label;
            this.icon = iconOverride ?? funcAttr?.icon;

            // ControlAttribute属性を読み取り
            this.controlAttribute = TypeReflectionSystem.GetCustomAttribute<ControlAttribute>(methodInfo);

            // Layout属性を読み取り（セクション内の縦横グループ）
            this.layoutAttribute = TypeReflectionSystem.GetCustomAttribute<LayoutAttribute>(methodInfo);

            // Section属性を読み取り（ボタンだけのセクション用）。アクセスレベルの解決規則は
            // LivePropertyType と同一に揃える。Define側の上書きを優先。
            this.sectionAttribute = sectionOverride ?? TypeReflectionSystem.GetCustomAttribute<SectionAttribute>(methodInfo);
            if (TypeReflectionSystem.GetCustomAttribute<DevelopmentAttribute>(methodInfo) != null)
            {
                this.sectionAccessLevel = AccessLevel.Development;
            }
            else if (TypeReflectionSystem.GetCustomAttribute<ExperimentalAttribute>(methodInfo) != null)
            {
                this.sectionAccessLevel = AccessLevel.Experimental;
            }
            else
            {
                this.sectionAccessLevel = this.sectionAttribute?.accessLevel ?? AccessLevel.Public;
            }

            // ShowIf/HideIf属性を読み取り (複数付与可、AND 評価)。プロパティと同じく AND 評価する。
            this.visibilityConditions = LivePropertyType._CollectVisibilityConditions(methodInfo, methodInfo.DeclaringType);
        }

        public object Invoke(object target, object[] args)
        {
            if (!isValid) return null;
            // MethodInvokeSystemに委譲
            return MethodInvokeSystem.Invoke(target, MethodInvokeSystem.CreateInvokeData(methodInfo), args);
        }
    }

}
