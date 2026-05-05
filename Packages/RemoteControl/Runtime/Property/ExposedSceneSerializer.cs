// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// シーン全体のシリアライズ/デシリアライズと、ExposedObjectの依存解決を担当するユーティリティクラス。
    /// </summary>
    public static class ExposedSceneSerializer
    {
        public const string FormatIdentifier = "jp.lilium.remotecontrol.scene";
        public const int CurrentFormatVersion = 1;

        public static string SceneToJson(IReadOnlyList<ExposedObject> objects, IExposedObjectResolver resolver, SerializeMode filter = SerializeMode.Snapshot, ExcludeFilter exclude = ExcludeFilter.None, string baseSceneName = null)
        {
            bool onlyDirty = filter == SerializeMode.Delta;
            bool excludeStatic = (exclude & ExcludeFilter.Static) != 0;

            // ファイル保存では fileid を再採番せず、同一 Object に安定 id を付けたいので
            // 新セッションでは Registry をクリアする（呼び出し元が既存ファイルを
            // 事前ロードしていればマッピングが維持される）。
            var fileResolver = new FileScopedResolver(resolver);

            var jRoot = new JObject();

            jRoot["format"] = FormatIdentifier;
            jRoot["formatVersion"] = CurrentFormatVersion;
            // Top-level field that drives load-time behavior: name of the Unity scene to load
            // before applying objects[]. Older files without this field fall back to re-loading
            // the current active scene. Kept outside `metadata` because it is functional, not
            // informational like packageVersion / appVersion.
            if (!string.IsNullOrEmpty(baseSceneName)) jRoot["baseSceneName"] = baseSceneName;
            var jMetadata = new JObject();
            jMetadata["packageVersion"] = RemoteControlSettings.PackageVersion;
            jMetadata["appName"] = Application.productName;
            jMetadata["appVersion"] = Application.version;
            jMetadata["unityVersion"] = Application.unityVersion;
            jRoot["metadata"] = jMetadata;

            // Prefab GUID マップ構築とシリアライズを1パスで処理
            // ID付きオブジェクトのみトップレベルに出力（IDなしは親プロパティ経由でネスト出力）
            var goPrefabMap = new Dictionary<int, string>();
            var jArray = new JArray();
            foreach (var obj in objects)
            {
                if (!_IsValidObject(obj, excludeStatic)) continue;

                // ExposedObjectContainer はシーンJSONには書き出さない。
                // 1 scene.json = 1 Container 前提のため、Container 自身のエントリは冗長。
                // 配下のオブジェクトは @prefab 付き（@source なし）でトップレベルに直接書き出される。
                if (obj.target is ExposedObjectContainer) continue;

                // Prefab追跡情報を収集 (値は Asset GUID)
                if (obj.target is ExposedUnityObjectBase unityObj && !string.IsNullOrEmpty(unityObj.prefabSourceKey))
                {
                    var prefabGo = _GetGameObject(obj);
                    if (prefabGo != null)
                    {
                        goPrefabMap[prefabGo.GetInstanceID()] = unityObj.prefabSourceKey;
                    }
                }

                if (!obj.hasId) continue;

                // file-scope resolver を渡すことで、走査中の UnityEngine.Object 参照が
                // fileid ベースの @ref に置き換わる。
                fileResolver.SetCurrentRoot(obj);
                var json = ExposedPropertySerializer.ToJson(obj, fileResolver, onlyDirty, forPersistence: true);
                var jObj = JObject.Parse(json);

                // Prefab から生成された新規エントリは「存在そのものが default からの差分」なので
                // Delta モードでもプロパティ差分の有無に関わらず必ず出力する。
                var go = _GetGameObject(obj);
                bool isPrefabNew = go != null && goPrefabMap.ContainsKey(go.GetInstanceID());

                // onlyDirty の場合、メタデータ(@type/@id/@name)以外のプロパティがなければスキップ
                // （ただし prefab-new エントリは存在情報を保持するため例外）
                if (onlyDirty && !isPrefabNew && !ExposedPropertySerializer.HasNonMetaProperties(jObj))
                    continue;

                // root が UnityEngine.Object なら FileRegistry に登録して他エントリから参照可能にする
                // （`@id` を file-scope id として再利用する。追加の `@fileid` は書かない）
                if (obj.target is UnityEngine.Object rootUnityObj && rootUnityObj != null)
                {
                    fileResolver.AssignFileId(rootUnityObj, registerPending: false);
                }

                if (isPrefabNew && goPrefabMap.TryGetValue(go.GetInstanceID(), out var pfKey))
                {
                    // Prefab 新規: @id を残したまま先頭に @prefab を差し込む (@source は付けない)。
                    // @prefab の値は prefab の Asset GUID。
                    jObj.AddFirst(new JProperty("@prefab", pfKey));
                }
                else
                {
                    // 既存ルート上書き: @id を外し、@source = obj.id (path 空) を先頭に置く。
                    jObj.Remove("@id");
                    jObj.AddFirst(new JProperty("@source", obj.id));
                }
                jArray.Add(jObj);
            }

            // 未登録 UnityEngine.Object 参照エントリを objects[] に書き出す。
            // 本体プロパティは元のプロパティと同じく展開し、先頭に @source を添える。
            // 処理中に新規 pending が追加されるため worklist 方式でループする。
            var processedSourceKeys = new HashSet<string>();
            int processIndex = 0;
            while (processIndex < fileResolver.pending.Count)
            {
                var pending = fileResolver.pending[processIndex++];
                if (string.IsNullOrEmpty(pending.sourceKey)) continue;
                if (!processedSourceKeys.Add(pending.sourceKey)) continue;
                if (pending.target == null) continue;

                // 本体を書き出せる場合: ExposedClass があればプロパティを展開、なければメタだけ
                var pendingExposedClass = ExposedClass.Find(pending.target.GetType());
                JObject entry;

                if (pendingExposedClass != null)
                {
                    // 再起点として、元の root/path をベースに展開する
                    // （ここで pending 内部の Unity.Object 参照が更なる pending として追加されうる）
                    var originalRoot = !string.IsNullOrEmpty(pending.rootId) ? resolver.FindById(pending.rootId) : null;
                    var tempExposed = ExposedObject.CreateUnregistered(pendingExposedClass, pending.target);

                    fileResolver.SetCurrentRoot(originalRoot);
                    fileResolver.SetBasePath(pending.path);
                    // pending も scene 全体の SerializeMode に従う。
                    // ExposedObjectDefaultRegistry は target 参照で defaults を保持するため、
                    // inline child として事前に EnsureDefaultsCaptured/SetDefault されていれば
                    // tempExposed 経由でも同じ defaults が参照でき、正しく delta が計算できる。
                    // defaults 未登録の場合は _ToJsonDelta のフォールバックで「差分ゼロ＝
                    // metadata only」となり、HasNonMetaProperties チェックでスキップされる。
                    var subJson = ExposedPropertySerializer.ToJson(tempExposed, fileResolver, isDirtyOnly: onlyDirty, forPersistence: true);
                    entry = JObject.Parse(subJson);

                    // delta モードでメタデータ以外のプロパティを含まない pending は出力しない
                    // （root エントリの L61-62 と同じロジック）。
                    if (onlyDirty && !ExposedPropertySerializer.HasNonMetaProperties(entry))
                        continue;
                }
                else
                {
                    entry = new JObject
                    {
                        ["@type"] = pending.typeName,
                    };
                }

                // pending エントリは @source のみを持つ (@id は書かない)。
                entry.Remove("@id");
                entry.Remove("@source");
                entry.AddFirst(new JProperty("@source", pending.sourceKey));
                jArray.Add(entry);
            }

            fileResolver.SetCurrentRoot(null);
            jRoot["objects"] = jArray;

            return jRoot.ToString(Formatting.Indented);
        }

        /// <summary>
        /// 任意のオブジェクトリストからExposedObjectリストを構築する。
        /// 依存するExposedObjectも幅優先探索で自動的に追加される。
        /// </summary>
        public static List<ExposedObject> ResolveExposedObjects(IReadOnlyList<object> objects, IExposedObjectResolver resolver)
        {
            var result = new List<ExposedObject>();
            var visited = new HashSet<ExposedObject>();
            var visitedTargets = new HashSet<object>(ExposedObjectRegistry.ReferenceEqualityComparer.Instance);
            var queue = new Queue<ExposedObject>();

            // 初期オブジェクトをExposedObjectに変換
            for (int i = 0; i < objects.Count; i++)
            {
                var target = objects[i];
                if (target == null) continue;

                // IExposedObjectの場合は直接exposedObjectを取得
                ExposedObject exposed;
                if (target is IExposedObject ieo)
                {
                    exposed = ieo.exposedObject;
                }
                else
                {
                    exposed = resolver.FindByTarget(target);
                }

                if (exposed == null) continue;
                if (!visited.Add(exposed)) continue;

                result.Add(exposed);
                queue.Enqueue(exposed);
            }

            // 幅優先探索で依存ExposedObjectを収集
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!current.isValid) continue;

                var propertyTypes = current.propertyTypes;
                for (int i = 0; i < propertyTypes.Length; i++)
                {
                    var propType = propertyTypes[i];
                    if (!propType.containsExposedObjectReference) continue;

                    object value;
                    try
                    {
                        value = ExposedPropertyUtility.GetValueRaw(current.target, propType);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null) continue;

                    if (value is System.Collections.IList list)
                    {
                        for (int j = 0; j < list.Count; j++)
                        {
                            _TryEnqueueDependency(list[j], resolver, visited, visitedTargets, result, queue);
                        }
                    }
                    else if (value is System.Array array)
                    {
                        for (int j = 0; j < array.Length; j++)
                        {
                            _TryEnqueueDependency(array.GetValue(j), resolver, visited, visitedTargets, result, queue);
                        }
                    }
                    else
                    {
                        _TryEnqueueDependency(value, resolver, visited, visitedTargets, result, queue);
                    }
                }
            }

            // BFS完了後、static classのExposedObjectを追加
            foreach (var instance in ExposedObjectRegistry.instances)
            {
                if (instance == null) continue;
                if (instance.targetType == null || !instance.targetType.isStatic) continue;
                if (!visited.Add(instance)) continue;
                result.Add(instance);
            }

            return result;
        }

        /// <summary>
        /// 指定 Container の現在の状態を、変更検出用の Delta JSON として生成する。
        /// SceneToJson の Delta モード・既定フィルタを呼ぶ薄いラッパー。container が null なら null。
        /// </summary>
        public static string BuildSceneJson(ExposedObjectContainer container, string baseSceneName = null)
        {
            if (container == null) return null;
            return SceneToJson(
                ResolveExposedObjects(container.objects, container),
                container,
                SerializeMode.Delta,
                ExcludeFilter.None,
                baseSceneName);
        }

        /// <summary>
        /// 現在の Container 状態と基準 JSON を比較し、差分があれば true。
        /// baselineJson または container が null の場合は false。
        /// baseSceneName は baselineJson から抽出して比較対象 JSON にも反映する
        /// （baseline 生成時と同じフィールドが揃うように）。
        /// </summary>
        public static bool HasChanges(ExposedObjectContainer container, string baselineJson)
        {
            if (container == null) return false;
            if (baselineJson == null) return false;
            var baseSceneName = ExtractBaseSceneName(baselineJson);
            return !string.Equals(BuildSceneJson(container, baseSceneName), baselineJson, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads the top-level <c>baseSceneName</c> from a scene JSON without performing a full deserialization.
        /// Returns <c>null</c> when the field is absent (legacy files) or the JSON cannot be parsed.
        /// </summary>
        public static string ExtractBaseSceneName(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var jRoot = JObject.Parse(json);
                return jRoot["baseSceneName"]?.Value<string>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static void SceneFromJson(string json, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(json)) return;

            var jRoot = JObject.Parse(json);

            // フォーマット検証
            int formatVersion = _DetectFormatVersion(jRoot);
            if (formatVersion < 0)
            {
                Debug.LogWarning("[RemoteControl] Unknown scene format.");
                return;
            }
            if (formatVersion > CurrentFormatVersion)
            {
                Debug.LogWarning($"[RemoteControl] Scene format version {formatVersion} is newer than supported version {CurrentFormatVersion}. Some data may not load correctly.");
            }

            var jArray = jRoot["objects"] as JArray;
            if (jArray == null)
            {
                Debug.LogWarning("[RemoteControl] Invalid file format: 'objects' array not found.");
                return;
            }

            // ファイル読み込みの起点では FileRegistry を初期化する。
            // 以降のエントリ走査で source-key ベースに再登録される。
            ExposedObjectFileRegistry.Clear();

            // Pass 1: @prefab 付き(かつ @source なし)エントリから Prefab インスタンス化
            // @prefab の値は Asset GUID。同じ GUID のエントリが複数あっても、求めるコンポーネントが
            // 既に「消費済み」なら新規GOを生成する。
            // - 同一GO上の異なる型のExposedObject (例: TestAdditionsComponent + TestAdditionsComponent2) → 同じGOを共有
            // - 同一プレハブの別インスタンス (例: 4個のCamera) → それぞれ別のGOを生成
            var prefabInstances = new List<(string prefabKey, GameObject go)>();
            var claimedTargets = new HashSet<UnityEngine.Object>();
            foreach (var jEntry in jArray)
            {
                if (!_TryGetMetadata(jEntry, out var typeName, out var id)) continue;
                var jObject = (JObject)jEntry;
                // @source が付いていれば既存オブジェクト上書き対象 → Pass 1 では扱わない。
                if (jObject["@source"] != null) continue;
                var prefabKey = jObject["@prefab"]?.ToString();
                if (string.IsNullOrEmpty(prefabKey)) continue;

                // 既に存在するかチェック
                if (resolver.FindById(id) != null) continue;

                // typeName から必要な Unity Object 型を解決
                var requiredType = _ResolveRequiredUnityObjectType(typeName);

                // 既存インスタンスから「同じprefab かつ requiredTypeのターゲットが未消費」のGOを探す
                GameObject go = null;
                for (int i = 0; i < prefabInstances.Count; i++)
                {
                    if (prefabInstances[i].prefabKey != prefabKey) continue;
                    var candidate = prefabInstances[i].go;
                    var candidateTarget = _GetTargetOnGameObject(candidate, requiredType);
                    if (candidateTarget != null && !claimedTargets.Contains(candidateTarget))
                    {
                        go = candidate;
                        break;
                    }
                }

                // 見つからなければ新規インスタンス化
                if (go == null)
                {
                    go = PrefabRegistry.Instantiate(prefabKey);
                    if (go == null)
                    {
                        Debug.LogWarning($"[RemoteControl] Failed to instantiate prefab (guid='{prefabKey}')");
                        continue;
                    }
                    // @name が保存されていればインスタンス名に反映する
                    // （StandardObjectFactory._GenerateUniqueName で付与されたユニーク名を復元するため）
                    var savedName = jObject["@name"]?.Value<string>();
                    if (!string.IsNullOrEmpty(savedName))
                    {
                        go.name = savedName;
                    }
                    prefabInstances.Add((prefabKey, go));
                }

                var wrapper = _RegisterComponentExposedObject(go, typeName, id, prefabKey, out var claimedTarget);
                if (claimedTarget != null) claimedTargets.Add(claimedTarget);
                if (wrapper != null)
                {
                    wrapper.OnEnable();

                    // resolver が ExposedObjectContainer の場合、wrapper を _objects に追加する。
                    // Container の _objects は JSON 永続化対象外のため、ロード時にここで復元する。
                    // 1 scene.json = 1 Container 前提のため、resolver 自身を「そのシーンのContainer」として扱う。
                    if (resolver is ExposedObjectContainer container)
                    {
                        if (!container._objects.Contains(wrapper))
                        {
                            container._objects.Add(wrapper);
                        }
                    }
                }
            }

            // Pass 2: ルートエントリ (pending 以外) を Registry に反映する。
            // - 既に id で登録済み → 何もしない
            // - Factory 登録型 → typename フォールバックで解決
            // - POCO → GetOrCreate で Activator 生成
            // - Component 型 → シーン上を探す
            // - それでも無ければ typename フォールバック
            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;
                if (_IsPendingEntry(jObject)) continue; // pending はスキップ
                var typeName = jObject["@type"]?.Value<string>();
                var id = _GetEntryKey(jObject);
                if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(id)) continue;
                if (resolver.FindById(id) != null) continue;

                var exposedClass = ExposedClass.Find(typeName);
                if (exposedClass == null) continue;

                var objectName = jObject["@name"]?.Value<string>();

                // ExposedUnityObjectFactory登録済みのProxy/Reference型はシーン上に実体が存在するため、
                // Activator.CreateInstanceで空インスタンスを作らず、型名で既存オブジェクトを探索する
                if (_IsFactoryRegisteredType(typeName))
                {
                    _TryResolveByTypeName(typeName, id, objectName);
                    continue;
                }

                var created = ExposedObjectRegistry.GetOrCreate(id, exposedClass);

                // MonoBehaviour/ScriptableObject型はGetOrCreateで生成できないため、
                // シーン上のコンポーネントを検索してExposedObjectを作成
                if (created == null && exposedClass.type != null
                    && typeof(Component).IsAssignableFrom(exposedClass.type))
                {
                    var found = GameObject.FindObjectsByType(exposedClass.type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var comp in found)
                    {
                        if (comp is Component c && (objectName == null || c.gameObject.name == objectName))
                        {
                            ExposedObjectRegistry.GetOrCreate(id, exposedClass, comp);
                            break;
                        }
                    }
                }

                // IDミスマッチのフォールバック: 型名（+@name）で既存ExposedObjectを検索
                if (resolver.FindById(id) == null)
                {
                    _TryResolveByTypeName(typeName, id, objectName);
                }
            }

            // Pass 2.5: source-key → UnityEngine.Object を FileRegistry に登録する。
            // @source があれば rootId+path で解決 (path 空ならルート target)、無ければ @id から Registry 経由で解決。
            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;
                var entryKey = _GetEntryKey(jObject);
                if (string.IsNullOrEmpty(entryKey)) continue;

                var sourceKey = jObject["@source"]?.Value<string>();
                UnityEngine.Object target = sourceKey != null
                    ? _ResolveObjectBySource(sourceKey, resolver)
                    : resolver.FindById(entryKey)?.target as UnityEngine.Object;

                if (target != null)
                {
                    ExposedObjectFileRegistry.Register(entryKey, target);
                }
                else if (_IsPendingEntry(jObject))
                {
                    Debug.LogWarning($"[RemoteControl] Failed to resolve pending entry '@source={sourceKey}' to UnityEngine.Object.");
                }
            }

            // Pass 3: プロパティデシリアライズ
            // pending 子参照 (@source に path あり) は FileRegistry の target に一時 ExposedObject で適用、
            // それ以外は entryKey で登録済み ExposedObject を引いて適用する。
            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;

                var entryKey = _GetEntryKey(jObject);
                var entryTypeName = jObject["@type"]?.Value<string>();
                if (string.IsNullOrEmpty(entryKey) || string.IsNullOrEmpty(entryTypeName)) continue;

                var propertyJson = jEntry.ToString();

                if (_IsPendingEntry(jObject))
                {
                    if (!ExposedObjectFileRegistry.TryGetObject(entryKey, out var pendingTarget) || pendingTarget == null)
                    {
                        Debug.LogWarning($"[RemoteControl] Pending entry '@source={entryKey}' not resolved at Pass 3.");
                        continue;
                    }

                    var pendingClass = ExposedClass.Find(entryTypeName);
                    if (pendingClass == null) continue;

                    var tempExposed = ExposedObject.CreateUnregistered(pendingClass, pendingTarget);

                    // FromJson で値を書き換える前に、現在の（プレハブ初期値相当の）状態を
                    // defaults として登録する。Container.Initialize 時点で存在しなかった
                    // プレハブ経由生成コンポーネントは defaults が未登録のため、保存時
                    // _ToJsonDelta で default==current フォールバックとなり pending エントリ全体が
                    // metadata-only 扱いで破棄されてしまう。ここで target 参照キーで登録しておけば
                    // 保存時 BFS で別 tempExposed が作られても同じ defaults が引け、delta が正しく計算される。
                    ExposedObjectDefaultRegistry.EnsureDefaultsCaptured(tempExposed, resolver);

                    ExposedPropertySerializer.FromJson(propertyJson, tempExposed, resolver, captureDefaults: false);
                    continue;
                }

                // ルート上書き or 新規 (Pass 1/2 で登録済み): entryKey で引いて適用
                var exposedObject = resolver.FindById(entryKey);
                if (exposedObject != null)
                {
                    // captureDefaults: false — デフォルトはContainer.Initializeで既にキャプチャ済み。
                    // SceneFromJson中にキャプチャすると、DeserializeExposedObject内のSetValueRawで
                    // 変更された値がデフォルトとして記録され、Delta保存で差分が検出されなくなる。
                    ExposedPropertySerializer.FromJson(propertyJson, exposedObject, resolver, captureDefaults: false);
                }
                else
                {
                    Debug.LogWarning($"[RemoteControl] ExposedObject with key '{entryKey}@{entryTypeName}' not found in the scene.");
                }
            }
        }

        // -------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------

        /// <summary>
        /// ExposedUnityObjectFactory に登録済みの型名かを判定する。
        /// Proxy/Reference型はシーンに実体が存在し、Activatorで生成しても空になるため区別が必要。
        /// </summary>
        private static bool _IsFactoryRegisteredType(string typeName)
        {
            var registrations = ExposedUnityObjectFactory.GetRegisteredTypes();
            for (int i = 0; i < registrations.Count; i++)
            {
                if (registrations[i].displayName == typeName) return true;
            }
            return false;
        }

        private static bool _IsValidObject(ExposedObject obj, bool excludeStatic)
        {
            if (obj == null) return false;
            if (excludeStatic && obj.targetType != null && obj.targetType.isStatic) return false;
            return true;
        }

        /// <summary>
        /// エントリ識別子を返す。override エントリは @source、新規エントリは @id。
        /// 両方 null ならエントリ不正。
        /// </summary>
        private static string _GetEntryKey(JObject jObject)
        {
            return jObject["@source"]?.Value<string>() ?? jObject["@id"]?.Value<string>();
        }

        /// <summary>
        /// pending 子参照エントリかどうかを判定する。
        /// @source が path 情報 ('.' または '[') を含めば pending 子参照。
        /// </summary>
        private static bool _IsPendingEntry(JObject jObject)
        {
            var sourceKey = jObject["@source"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceKey)) return false;
            return sourceKey.IndexOf('.') >= 0 || sourceKey.IndexOf('[') >= 0;
        }

        /// <summary>
        /// @source (rootId と path を "." で結合した文字列) から UnityEngine.Object を引き当てる。
        /// 登録済み ExposedObject を起点に PropertyPath で辿る。
        /// </summary>
        private static UnityEngine.Object _ResolveObjectBySource(string sourceKey, IExposedObjectResolver resolver)
        {
            _ParseSourceKey(sourceKey, out var rootId, out var path);
            if (string.IsNullOrEmpty(rootId)) return null;

            var rootExposed = resolver.FindById(rootId);
            if (rootExposed == null) return null;

            // path が空なら root 自身を返す
            if (string.IsNullOrEmpty(path))
            {
                return rootExposed.target as UnityEngine.Object;
            }

            var property = rootExposed.FindProperty(path);
            if (!property.HasValue) return null;

            return property.Value.GetValue() as UnityEngine.Object;
        }

        /// <summary>
        /// rootId と DotBracket 形式 path を 1 本の文字列 @source キーに結合する。
        /// 前提: rootId は '.' や '[' を含まない (GUID や typeName ベース id)。
        /// path が空 → "rootId"。path が "[" から始まる → "rootId[0]..."。それ以外 → "rootId.foo..."。
        /// </summary>
        internal static string _ComposeSourceKey(string rootId, string path)
        {
            if (string.IsNullOrEmpty(path)) return rootId;
            return path[0] == '[' ? rootId + path : rootId + "." + path;
        }

        /// <summary>
        /// _ComposeSourceKey の逆。最初の '.' または '[' を区切りとして rootId と path を分解する。
        /// </summary>
        private static void _ParseSourceKey(string sourceKey, out string rootId, out string path)
        {
            rootId = sourceKey;
            path = string.Empty;
            if (string.IsNullOrEmpty(sourceKey)) return;

            int dotIndex = sourceKey.IndexOf('.');
            int bracketIndex = sourceKey.IndexOf('[');
            int splitIndex = -1;
            if (dotIndex >= 0 && bracketIndex >= 0) splitIndex = System.Math.Min(dotIndex, bracketIndex);
            else if (dotIndex >= 0) splitIndex = dotIndex;
            else if (bracketIndex >= 0) splitIndex = bracketIndex;

            if (splitIndex < 0) return;

            rootId = sourceKey.Substring(0, splitIndex);
            // '.' 区切りはパスから落とす、'[' はパスの一部として残す
            path = sourceKey[splitIndex] == '.'
                ? sourceKey.Substring(splitIndex + 1)
                : sourceKey.Substring(splitIndex);
        }

        /// <summary>
        /// フォーマットバージョンを検出する。
        /// `formatVersion` が無い JSON は format ヘッダ未導入時代のレガシー入力として
        /// CurrentFormatVersion 互換とみなす（読み込み自体は試行する）。
        /// </summary>
        private static int _DetectFormatVersion(JObject jRoot)
        {
            var jFormatVersion = jRoot["formatVersion"];
            if (jFormatVersion != null)
            {
                return jFormatVersion.Value<int>();
            }

            // `formatVersion` 未指定はヘッダー導入前のJSONとみなす。
            // objects 配列が無いケースは呼び出し側で別途警告される。
            return CurrentFormatVersion;
        }

        private static bool _TryGetMetadata(JToken token, out string typeName, out string id)
        {
            typeName = null;
            id = null;
            if (!(token is JObject jObject)) return false;

            typeName = jObject["@type"]?.Value<string>();
            id = jObject["@id"]?.Value<string>();
            return !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(id);
        }

        /// <summary>
        /// IDで解決できなかったオブジェクトを型名（+@name）で検索し、
        /// 一致するExposedObjectが見つかればReplaceIdでIDを復元する。
        /// ExposedUnityObjectProxy等がPlay mode再入時に新しいGUIDを生成するケースに対応。
        /// </summary>
        private static void _TryResolveByTypeName(string typeName, string savedId, string objectName)
        {
            ExposedObject match = null;
            int matchCount = 0;

            foreach (var instance in ExposedObjectRegistry.instances)
            {
                if (instance == null || !instance.isValid) continue;
                if (instance.targetTypeName != typeName) continue;

                // @nameが指定されている場合はname一致も必須
                if (!string.IsNullOrEmpty(objectName) && instance.name != objectName) continue;

                match = instance;
                matchCount++;
            }

            // 一意に特定できた場合のみIDを復元（曖昧な場合は安全のためスキップ）
            if (matchCount == 1 && match != null)
            {
                if (match.target is ExposedUnityObjectBase wrapper)
                {
                    wrapper.ReplaceId(savedId);
                }
                else
                {
                    // 非UnityObject型: ExposedObjectのIDを直接更新
                    match.ReplaceId(savedId);
                }
            }
        }

        private static void _TryEnqueueDependency(object target, IExposedObjectResolver resolver,
            HashSet<ExposedObject> visited, HashSet<object> visitedTargets, List<ExposedObject> result, Queue<ExposedObject> queue)
        {
            if (target == null) return;

            // targetベースの重複チェック（unregistered ExposedObjectは毎回新規インスタンスのため）
            if (!visitedTargets.Add(target)) return;

            var exposed = resolver.FindByTarget(target);

            // レジストリ未登録の場合、ExposedClass登録済みのUnityEngine.Objectなら一時ExposedObjectを生成
            if (exposed == null && target is UnityEngine.Object unityObj)
            {
                var exposedClass = ExposedClass.Find(target.GetType());
                if (exposedClass != null)
                {
                    exposed = ExposedObject.CreateUnregistered(exposedClass, target);
                }
            }

            if (exposed == null) return;
            visited.Add(exposed);

            // ID付き/ID無しの両方を result に含める。
            // - hasId: SceneToJson のトップレベル出力対象。
            // - hasId無し: 呼び出し側が SetDefault/EnsureDefaultsCaptured で
            //   inline 子オブジェクトの defaults を登録できるように含める（pending delta 判定に必要）。
            //   SceneToJson 側では L52 の hasId チェックで出力はスキップされる。
            result.Add(exposed);
            queue.Enqueue(exposed);
        }

        private static GameObject _GetGameObject(ExposedObject obj)
        {
            if (obj.target is Component comp) return comp.gameObject;
            if (obj.target is GameObject g) return g;
            if (obj.target is ExposedUnityObjectBase unityObj)
            {
                if (unityObj.reference is GameObject gRef) return gRef;
                if (unityObj.reference is Component cRef) return cRef.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Prefabインスタンス上のコンポーネントからExposedObjectを生成・登録する。
        /// Component型の場合はExposedObjectのみ作成。
        /// ExposedUnityObjectBase派生型（Proxy/Reference）の場合はFactoryでラッパーも生成して返す。
        /// </summary>
        /// <param name="claimedTarget">このエントリで「消費」したUnityObject（Component または GameObject）。Pass 1での重複検出に使用。</param>
        /// <returns>コンテナに追加すべきExposedUnityObjectBase。Component型の場合はnull。</returns>
        private static ExposedUnityObjectBase _RegisterComponentExposedObject(GameObject go, string typeName, string id, string prefabKey, out UnityEngine.Object claimedTarget)
        {
            claimedTarget = null;
            var exposedClass = ExposedClass.Find(typeName);
            if (exposedClass == null || exposedClass.type == null) return null;

            // Case 1: ExposedClass型がComponent（直接Componentに[ExposedClass]がついている場合）
            if (typeof(Component).IsAssignableFrom(exposedClass.type))
            {
                var component = go.GetComponent(exposedClass.type);
                if (component != null)
                {
                    ExposedObjectRegistry.GetOrCreate(id, exposedClass, component);
                    claimedTarget = component;
                }
                return null;
            }

            // Case 2: ExposedUnityObjectBase派生型（ExposedCamera等のProxy/Reference型）
            // ExposedUnityObjectFactoryからdisplayNameが一致する登録を探す
            var registrations = ExposedUnityObjectFactory.GetRegisteredTypes();
            for (int i = 0; i < registrations.Count; i++)
            {
                if (registrations[i].displayName != typeName) continue;

                var registration = registrations[i];
                UnityEngine.Object target;
                if (typeof(Component).IsAssignableFrom(registration.componentType))
                    target = go.GetComponent(registration.componentType);
                else if (typeof(GameObject).IsAssignableFrom(registration.componentType))
                    target = go;
                else
                    continue;

                if (target == null) continue;

                // ファクトリでラッパー生成（コンストラクタで自動生成IDのExposedObjectが作られる）
                var wrapper = registration.factory(target);

                // 自動生成のExposedObjectを破棄し、保存済みIDで再登録
                wrapper.ReplaceId(id);

                // Prefab復元時はソースキー (Asset GUID) を設定（SceneToJsonで@op/@prefab付きで出力するため）
                if (prefabKey != null)
                    wrapper.prefabSourceKey = prefabKey;

                claimedTarget = target;
                return wrapper;
            }

            return null;
        }

        /// <summary>
        /// typeNameから、Pass 1で「ターゲットが既に消費されたか」を判定するためのUnityObject型を返す。
        /// ExposedClassがComponent派生ならその型、Proxy/Reference型なら登録の componentType を返す。
        /// 該当するUnityObject型が無い場合（POCO等）は null を返す。
        /// </summary>
        private static Type _ResolveRequiredUnityObjectType(string typeName)
        {
            var exposedClass = ExposedClass.Find(typeName);
            if (exposedClass != null && exposedClass.type != null
                && typeof(UnityEngine.Object).IsAssignableFrom(exposedClass.type))
            {
                return exposedClass.type;
            }
            var registrations = ExposedUnityObjectFactory.GetRegisteredTypes();
            for (int i = 0; i < registrations.Count; i++)
            {
                if (registrations[i].displayName == typeName)
                    return registrations[i].componentType;
            }
            return null;
        }

        /// <summary>
        /// GO上から指定型のUnityObjectを取得する。Component型ならGetComponent、GameObject型ならGO自身を返す。
        /// requiredTypeがnullの場合はGO自身を返す（Pass 1で全エントリが共有可能なフォールバック扱い）。
        /// </summary>
        private static UnityEngine.Object _GetTargetOnGameObject(GameObject go, Type requiredType)
        {
            if (go == null) return null;
            if (requiredType == null) return go;
            if (typeof(Component).IsAssignableFrom(requiredType)) return go.GetComponent(requiredType);
            if (typeof(GameObject).IsAssignableFrom(requiredType)) return go;
            return null;
        }
    }
}
