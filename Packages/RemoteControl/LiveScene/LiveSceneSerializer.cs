// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// シーン全体のシリアライズ/デシリアライズを担当するユーティリティクラス。
    /// 依存グラフ走査は本体側 <see cref="LiveObjectGraph"/> に分離済み。
    /// </summary>
    public static class LiveSceneSerializer
    {
        // Written into the "format" field of new files. Loading does not validate this string
        // (only formatVersion is inspected), so legacy files carrying the old
        // "jp.lilium.remotecontrol.scene" identifier still deserialize unchanged.
        public const string FormatIdentifier = "jp.lilium.remotecontrol.live";
        public const int CurrentFormatVersion = 1;

        // Oldest version this reader accepts. Files below are rejected; at/above CurrentFormatVersion are
        // read best-effort. See FormatHeader for the shared policy. Incompatible breaks use a new format id.
        public const int MinSupportedVersion = 1;

        public static string LiveSceneToJson(IReadOnlyList<LiveObjectHandle> objects, ILiveObjectResolver resolver, SerializeMode filter = SerializeMode.Snapshot, ExcludeFilter exclude = ExcludeFilter.None, string baseSceneName = null)
        {
            bool onlyDirty = filter == SerializeMode.Delta;
            bool excludeStatic = (exclude & ExcludeFilter.Static) != 0;

            // ファイル保存では fileid を再採番せず、同一 Object に安定 id を付けたいので
            // 新セッションでは Registry をクリアする（呼び出し元が既存ファイルを
            // 事前ロードしていればマッピングが維持される）。
            var fileResolver = new FileScopedResolver(resolver);

            var jRoot = new JObject();

            FormatHeader.Write(jRoot, FormatIdentifier, CurrentFormatVersion);
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
            var goPrefabMap = new Dictionary<long, string>();
            var jArray = new JArray();
            foreach (var obj in objects)
            {
                if (!_IsValidObject(obj, excludeStatic)) continue;

                // LiveObjectContainer はシーンJSONには書き出さない。
                // 1 live.json = 1 Container 前提のため、Container 自身のエントリは冗長。
                // 配下のオブジェクトは @prefab 付き（@source なし）でトップレベルに直接書き出される。
                if (obj.target is LiveObjectContainer) continue;

                // Prefab追跡情報を収集 (値は Asset GUID)
                if (obj.target is LiveUnityObjectBase unityObj && !string.IsNullOrEmpty(unityObj.prefabSourceKey))
                {
                    var prefabGo = LiveSceneObjectUtil.GetGameObject(obj);
                    if (prefabGo != null)
                    {
                        goPrefabMap[LiveObjectUtility.GetInstanceID(prefabGo)] = unityObj.prefabSourceKey;
                    }
                }

                if (!obj.hasId) continue;

                // file-scope resolver を渡すことで、走査中の UnityEngine.Object 参照が
                // fileid ベースの @ref に置き換わる。
                fileResolver.SetCurrentRoot(obj);
                // Scene scope のメンバーのみ live.json に書き出す (Project scope は settings.json へ別出力)。
                var json = LivePropertySerializer.ToJson(obj, fileResolver, onlyDirty, forPersistence: true, scopeFilter: PersistScope.Scene);
                var jObj = JObject.Parse(json);

                // Prefab から生成された新規エントリは「存在そのものが default からの差分」なので
                // Delta モードでもプロパティ差分の有無に関わらず必ず出力する。
                var go = LiveSceneObjectUtil.GetGameObject(obj);
                bool isPrefabNew = go != null && goPrefabMap.ContainsKey(LiveObjectUtility.GetInstanceID(go));

                // onlyDirty の場合、メタデータ(@type/@id/@name)以外のプロパティがなければスキップ
                // （ただし prefab-new エントリは存在情報を保持するため例外）
                if (onlyDirty && !isPrefabNew && !LivePropertySerializer.HasNonMetaProperties(jObj))
                    continue;

                // root が UnityEngine.Object なら FileRegistry に登録して他エントリから参照可能にする
                // （`@id` を file-scope id として再利用する。追加の `@fileid` は書かない）
                if (obj.target is UnityEngine.Object rootUnityObj && rootUnityObj != null)
                {
                    fileResolver.AssignFileId(rootUnityObj, registerPending: false);
                }

                if (isPrefabNew && goPrefabMap.TryGetValue(LiveObjectUtility.GetInstanceID(go), out var pfKey))
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

                // 本体を書き出せる場合: LiveClass があればプロパティを展開、なければメタだけ
                var pendingLiveClass = LiveClass.Find(pending.target.GetType());
                JObject entry;

                if (pendingLiveClass != null)
                {
                    // 再起点として、元の root/path をベースに展開する
                    // （ここで pending 内部の Unity.Object 参照が更なる pending として追加されうる）
                    var originalRoot = !string.IsNullOrEmpty(pending.rootId) ? resolver.FindById(pending.rootId) : null;
                    var tempLive = LiveObjectHandle.CreateUnregistered(pendingLiveClass, pending.target);

                    fileResolver.SetCurrentRoot(originalRoot);
                    fileResolver.SetBasePath(pending.path);
                    // pending も scene 全体の SerializeMode に従う。
                    // LiveObjectDefaultRegistry は target 参照で defaults を保持するため、
                    // inline child として事前に EnsureDefaultsCaptured/SetDefault されていれば
                    // tempLive 経由でも同じ defaults が参照でき、正しく delta が計算できる。
                    // defaults 未登録の場合は _ToJsonDelta のフォールバックで「差分ゼロ＝
                    // metadata only」となり、HasNonMetaProperties チェックでスキップされる。
                    var subJson = LivePropertySerializer.ToJson(tempLive, fileResolver, isDirtyOnly: onlyDirty, forPersistence: true, scopeFilter: PersistScope.Scene);
                    entry = JObject.Parse(subJson);

                    // delta モードでメタデータ以外のプロパティを含まない pending は出力しない
                    // （root エントリの L61-62 と同じロジック）。
                    if (onlyDirty && !LivePropertySerializer.HasNonMetaProperties(entry))
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

            // Re-emit any pending-store entries that were NOT bound this session (their owning prop /
            // avatar never finished loading), so a load→save cycle does not drop their saved state.
            // Bound entries were removed from the store on ApplyFor, and a loaded object re-emits its own
            // diff above, so an entry already present here is never re-added.
            _AppendUnconsumedPendingEntries(jArray);
            // Re-emit deferred @prefab entries whose instance was never created this session (their asset
            // never registered), so a load→save cycle does not drop them.
            _AppendUnconsumedPrefabEntries(jArray);

            fileResolver.SetCurrentRoot(null);
            jRoot["objects"] = jArray;

            return jRoot.ToString(Formatting.Indented);
        }

        // Appends pending-store entries whose @source is not already output. Their @source is preserved
        // verbatim so the next load re-queues them identically. Cloned because a JObject cannot be
        // attached to two parents (the store keeps its copy for a later save).
        private static void _AppendUnconsumedPendingEntries(JArray jArray)
        {
            HashSet<string> emitted = null;
            foreach (var pendingEntry in LiveScenePendingStore.UnconsumedEntries)
            {
                var source = pendingEntry["@source"]?.Value<string>();
                if (string.IsNullOrEmpty(source)) continue;

                if (emitted == null)
                {
                    emitted = new HashSet<string>();
                    foreach (var item in jArray)
                    {
                        if (item is JObject o && o["@source"]?.Value<string>() is string s) emitted.Add(s);
                    }
                }
                if (!emitted.Add(source)) continue;
                jArray.Add(pendingEntry.DeepClone());
            }
        }

        // Appends deferred @prefab entries (PendingPrefabStore) whose @id is not already output. These carry
        // @id + @prefab (no @source), so they are keyed and deduplicated by @id — distinct from the @source
        // keying above. Cloned because a JObject cannot be attached to two parents.
        private static void _AppendUnconsumedPrefabEntries(JArray jArray)
        {
            HashSet<string> emittedIds = null;
            foreach (var pendingEntry in PendingPrefabStore.UnconsumedEntries)
            {
                var id = pendingEntry["@id"]?.Value<string>();
                if (string.IsNullOrEmpty(id)) continue;

                if (emittedIds == null)
                {
                    emittedIds = new HashSet<string>();
                    foreach (var item in jArray)
                    {
                        if (item is JObject o && o["@id"]?.Value<string>() is string s) emittedIds.Add(s);
                    }
                }
                if (!emittedIds.Add(id)) continue;
                jArray.Add(pendingEntry.DeepClone());
            }
        }

        /// <summary>
        /// 指定 Container の現在の状態を、変更検出用の Delta JSON として生成する。
        /// LiveSceneToJson の Delta モード・既定フィルタを呼ぶ薄いラッパー。container が null なら null。
        /// </summary>
        public static string BuildLiveSceneJson(LiveObjectContainer container, string baseSceneName = null)
        {
            if (container == null) return null;
            // Snapshot main + all merged sources so objects carried in from other scenes
            // (RemoteControlContainer worlds) are serialized into the single live scene file.
            var allObjects = new List<ILiveObject>(container.EnumerateAllObjects());
            return LiveSceneToJson(
                LiveObjectGraph.ResolveLiveObjects(allObjects, container),
                container,
                SerializeMode.Delta,
                ExcludeFilter.None,
                baseSceneName);
        }

        // Unsaved-change detection used to live here as HasChanges(container, baselineJson): serialize
        // the whole scene and diff it against the file. That could not distinguish "the user edited
        // something" from "this build serializes the scene differently than the build that wrote the
        // file", so a scene saved by an older version reported unsaved changes forever and blocked
        // quit. It now lives in LiveSceneSaveSystem.HasUnsavedChanges, which asks the per-object dirty
        // tracking instead.

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

        public static void LiveSceneFromJson(string json, ILiveObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(json)) return;

            var jRoot = JObject.Parse(json);

            // フォーマット検証 (バージョン方針は FormatHeader に集約)。
            if (!FormatHeader.TryReadVersion(jRoot, "Scene", CurrentFormatVersion, MinSupportedVersion, out _)) return;

            var jArray = jRoot["objects"] as JArray;
            if (jArray == null)
            {
                Debug.LogWarning("[RemoteControl] Invalid file format: 'objects' array not found.");
                return;
            }

            // ファイル読み込みの起点では FileRegistry を初期化する。
            // 以降のエントリ走査で source-key ベースに再登録される。
            LiveObjectFileRegistry.Clear();
            // 同じく、未解決エントリの遅延バインドキューも読み込みごとに初期化する。
            // 解決できなかったエントリは以降のパスで再登録される。
            LiveScenePendingStore.Clear();
            // Deferred prefab instantiation queue (external props whose asset is not registered yet).
            PendingPrefabStore.Clear();

            // 復元は 4 パスで行う（各パスは jArray を順走査する。詳細は各メソッド参照）。
            _InstantiatePrefabs(jArray, resolver);   // Pass 1: @prefab からインスタンス化
            _RegisterRoots(jArray, resolver);        // Pass 2: ルートを Registry へ反映
            _RegisterFileObjects(jArray, resolver);  // Pass 2.5: source-key → UnityEngine.Object
            _ApplyProperties(jArray, resolver);      // Pass 3: プロパティ適用
        }

        // -------------------------------------------------------
        // Restore passes (LiveSceneFromJson から分割。挙動は不変)
        // -------------------------------------------------------

        // Pass 1: @prefab 付き(かつ @source なし)エントリから Prefab インスタンス化
        // @prefab の値は Asset GUID。同じ GUID のエントリが複数あっても、求めるコンポーネントが
        // 既に「消費済み」なら新規GOを生成する。
        // - 同一GO上の異なる型のLiveObject (例: TestAdditionsComponent + TestAdditionsComponent2) → 同じGOを共有
        // - 同一プレハブの別インスタンス (例: 4個のCamera) → それぞれ別のGOを生成
        private static void _InstantiatePrefabs(JArray jArray, ILiveObjectResolver resolver)
        {
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
                        // The prefab is not resolvable now — e.g. an external prop whose owning asset is
                        // discovered by the project crawl only AFTER this restore finishes. Queue for a
                        // deferred instantiation (drained once the asset registers) instead of dropping it,
                        // so it is not silently lost on the next save (its @prefab entry carries no @source,
                        // which the property pending-store re-emit keys on).
                        PendingPrefabStore.Add(id, prefabKey, jObject);
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

                var wrapper = _RegisterComponentLiveObject(go, typeName, id, prefabKey, out var claimedTarget);
                if (claimedTarget != null) claimedTargets.Add(claimedTarget);
                if (wrapper != null)
                {
                    wrapper.OnEnable();

                    // 復元したプレハブ インスタンスはベースシーンの RemoteControlContainer に登録する。
                    // こうするとベースシーン リロード時にそのシーンと一緒に破棄され、常駐ホストの
                    // LiveObjectContainer に stale エントリが蓄積しない (リロードのたびに再生成される
                    // ため)。シーンに RemoteControlContainer が無い構成 (テスト等) では従来どおり
                    // resolver 自身のコンテナにフォールバックする。
                    var sceneContainer = _ResolveSceneContainer();
                    if (sceneContainer != null)
                    {
                        if (!sceneContainer._objects.Contains(wrapper))
                            sceneContainer._objects.Add(wrapper);
                    }
                    else if (resolver is LiveObjectContainer container)
                    {
                        if (!container._objects.Contains(wrapper))
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
        private static void _RegisterRoots(JArray jArray, ILiveObjectResolver resolver)
        {
            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;
                if (_IsPendingEntry(jObject)) continue; // pending はスキップ
                var typeName = jObject["@type"]?.Value<string>();
                var id = _GetEntryKey(jObject);
                if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(id)) continue;
                // A deferred @prefab entry is instantiated later on drain; do not let the type-name
                // fallback below mis-bind it to an unrelated same-typed object.
                if (PendingPrefabStore.Contains(id)) continue;
                if (resolver.FindById(id) != null) continue;

                var liveClass = LiveClass.Find(typeName);
                if (liveClass == null) continue;

                var objectName = jObject["@name"]?.Value<string>();

                // LiveUnityObjectFactory登録済みのProxy/Reference型はシーン上に実体が存在するため、
                // Activator.CreateInstanceで空インスタンスを作らず、型名で既存オブジェクトを探索する
                if (_IsFactoryRegisteredType(typeName))
                {
                    _TryResolveByTypeName(typeName, id, objectName);
                    continue;
                }

                var created = LiveObjectRegistry.GetOrCreate(id, liveClass);

                // MonoBehaviour/ScriptableObject型はGetOrCreateで生成できないため、
                // シーン上のコンポーネントを検索してLiveObjectを作成
                if (created == null && liveClass.type != null
                    && typeof(Component).IsAssignableFrom(liveClass.type))
                {
                    var found = GameObject.FindObjectsByType(liveClass.type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var comp in found)
                    {
                        if (comp is Component c && (objectName == null || c.gameObject.name == objectName))
                        {
                            LiveObjectRegistry.GetOrCreate(id, liveClass, comp);
                            break;
                        }
                    }
                }

                // IDミスマッチのフォールバック: 型名（+@name）で既存LiveObjectを検索
                if (resolver.FindById(id) == null)
                {
                    _TryResolveByTypeName(typeName, id, objectName);
                }
            }
        }

        // Pass 2.5: source-key → UnityEngine.Object を FileRegistry に登録する。
        // @source があれば rootId+path で解決 (path 空ならルート target)、無ければ @id から Registry 経由で解決。
        private static void _RegisterFileObjects(JArray jArray, ILiveObjectResolver resolver)
        {
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
                    LiveObjectFileRegistry.Register(entryKey, target);
                }
                else if (_IsPendingEntry(jObject))
                {
                    // Target not in the scene yet: the owning prop / avatar loads asynchronously after
                    // this restore. Queue for a deferred bind (see LiveScenePendingStore) rather than
                    // warn — the entry is applied once its asset finishes loading, or round-trips on the
                    // next save if it never does.
                    _ParseSourceKey(sourceKey, out var pendingRootId, out _);
                    LiveScenePendingStore.Add(sourceKey, pendingRootId, jObject);
                }
            }
        }

        // Pass 3: プロパティデシリアライズ
        // pending 子参照 (@source に path あり) は FileRegistry の target に一時 LiveObjectHandle で適用、
        // それ以外は entryKey で登録済み LiveObjectHandle を引いて適用する。
        private static void _ApplyProperties(JArray jArray, ILiveObjectResolver resolver)
        {
            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;

                var entryKey = _GetEntryKey(jObject);
                var entryTypeName = jObject["@type"]?.Value<string>();
                if (string.IsNullOrEmpty(entryKey) || string.IsNullOrEmpty(entryTypeName)) continue;

                var propertyJson = jEntry.ToString();

                if (_IsPendingEntry(jObject))
                {
                    // Child reference (e.g. a loaded prop's component). Apply now if its target is already
                    // in the scene; if it isn't, the entry was queued in the pending store (Pass 2.5) for
                    // a deferred bind once its owning asset loads. ApplyPendingEntry carries the same
                    // defaults-then-apply handling the inline logic used before, shared with ApplyFor.
                    ApplyPendingEntry(entryKey, jObject, resolver);
                    continue;
                }

                // A deferred @prefab root: its instance (and this entry's application) happen on drain, and
                // the store owns re-emitting it on save — so do NOT also queue it in the property store.
                if (PendingPrefabStore.Contains(entryKey)) continue;

                // ルート上書き or 新規 (Pass 1/2 で登録済み): entryKey で引いて適用
                var liveObject = resolver.FindById(entryKey);
                if (liveObject != null)
                {
                    // captureDefaults: false — デフォルトはContainer.Initializeで既にキャプチャ済み。
                    // LiveSceneFromJson中にキャプチャすると、DeserializeLiveObject内のSetValueRawで
                    // 変更された値がデフォルトとして記録され、Delta保存で差分が検出されなくなる。
                    LivePropertySerializer.FromJson(propertyJson, liveObject.Value, resolver, captureDefaults: false);
                }
                else
                {
                    // Root object not in the scene yet: an asset-backed wrapper (prop / avatar) whose
                    // GameObject loads asynchronously after this restore, or a stale reference. Queue for a
                    // deferred bind — ApplyFor applies it when the asset loads, and an entry that never
                    // binds round-trips on the next save rather than being dropped.
                    LiveScenePendingStore.Add(entryKey, entryKey, jObject);
                }
            }
        }

        // -------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------

        /// <summary>
        /// LiveUnityObjectFactory に登録済みの型名かを判定する。
        /// Proxy/Reference型はシーンに実体が存在し、Activatorで生成しても空になるため区別が必要。
        /// </summary>
        private static bool _IsFactoryRegisteredType(string typeName)
        {
            var registrations = LiveUnityObjectFactory.GetRegisteredTypes();
            for (int i = 0; i < registrations.Count; i++)
            {
                if (registrations[i].displayName == typeName) return true;
            }
            return false;
        }

        // The base-scene RemoteControlContainer that restored prefab instances register into, so they
        // share the (reloadable) base scene's lifecycle rather than lingering in the persistent host
        // container. Prefers a container in a build (base) scene; additively-loaded set-bundle scenes
        // have buildIndex -1. Returns null when no RemoteControlContainer exists (e.g. headless tests).
        private static RemoteControlContainer _ResolveSceneContainer()
        {
            var all = RemoteControlContainer.all;
            RemoteControlContainer fallback = null;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                if (fallback == null) fallback = c;
                if (c.gameObject.scene.buildIndex >= 0) return c;
            }
            return fallback;
        }

        private static bool _IsValidObject(LiveObjectHandle obj, bool excludeStatic)
        {
            if (obj == null) return false;
            if (excludeStatic && obj.targetType != null && obj.targetType.isStatic) return false;
            // Skip wrappers whose backing scene object has been destroyed (Unity "fake null"). This
            // happens transiently during a base-scene reload: a prop/avatar GameObject is destroyed with
            // the scene while its wrapper briefly lingers in a source container not yet unregistered.
            // Serializing it would dereference the destroyed object and throw MissingReferenceException.
            // Distinguish this from a pure proxy that legitimately never has a backing reference
            // (ReferenceEquals(reference, null) == true, e.g. id supplied from its own field) — those must
            // still be serialized. So require a real reference (not C# null) that is now destroyed.
            if (obj.target is LiveUnityObjectBase unityObj
                && !ReferenceEquals(unityObj.reference, null)  // a real reference was assigned...
                && unityObj.reference == null)                 // ...but it is now a destroyed (fake null)
                return false;
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
        /// Applies one live-scene entry onto its (now resolvable) target, and returns false when the
        /// target still cannot be resolved so the caller can keep the entry queued. Shared by Pass 3 of
        /// the restore and by <see cref="LiveScenePendingStore.ApplyFor"/> (deferred bind once a prop /
        /// avatar has loaded).
        ///
        /// A root entry (<c>@source</c> is a bare id) is applied directly onto the registered object. A
        /// child entry (<c>@source</c> carries a path) resolves its target via
        /// <see cref="_ResolveObjectBySource"/> and applies the JSON on an unregistered temp handle — the
        /// per-load id is not persisted — capturing the target's current values as the delta baseline
        /// first (mirrors the original Pass 3 pending handling).
        /// </summary>
        internal static bool ApplyPendingEntry(string sourceKey, JObject entry, ILiveObjectResolver resolver)
        {
            if (entry == null || resolver == null) return false;

            var typeName = entry["@type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName)) return false;

            _ParseSourceKey(sourceKey, out var rootId, out var path);
            if (string.IsNullOrEmpty(rootId)) return false;

            var entryJson = entry.ToString();

            if (string.IsNullOrEmpty(path))
            {
                var handle = resolver.FindById(rootId);
                if (handle == null) return false;
                LivePropertySerializer.FromJson(entryJson, handle.Value, resolver, captureDefaults: false);
                // Deferred apply of saved state, not a user edit: this lands after the load-time
                // re-baseline, so adopt it as the user-change baseline or the quit check reports the
                // restored values as unsaved changes.
                handle.Value.MarkClean();
                return true;
            }

            var target = _ResolveObjectBySource(sourceKey, resolver);
            if (target == null) return false;

            var pendingClass = LiveClass.Find(typeName);
            if (pendingClass == null) return false;

            var tempLive = LiveObjectHandle.CreateUnregistered(pendingClass, target);
            LiveObjectDefaultRegistry.EnsureDefaultsCaptured(tempLive, resolver);
            LivePropertySerializer.FromJson(entryJson, tempLive, resolver, captureDefaults: false);
            // Same as the root branch. The handle is unregistered, but the dirty baselines are keyed by
            // target reference, so this cleans the state the registered handle reports too.
            tempLive.MarkClean();
            return true;
        }

        /// <summary>
        /// @source (rootId と path を "." で結合した文字列) から UnityEngine.Object を引き当てる。
        /// 登録済み LiveObjectHandle を起点に PropertyPath で辿る。
        /// </summary>
        private static UnityEngine.Object _ResolveObjectBySource(string sourceKey, ILiveObjectResolver resolver)
        {
            _ParseSourceKey(sourceKey, out var rootId, out var path);
            if (string.IsNullOrEmpty(rootId)) return null;

            var rootLive = resolver.FindById(rootId);
            if (rootLive == null) return null;
            var rootLiveObj = rootLive.Value;

            // path が空なら root 自身を返す
            if (string.IsNullOrEmpty(path))
            {
                return rootLiveObj.target as UnityEngine.Object;
            }

            // Named component element ("components[Chair]"): resolve by exposed type name rather than array
            // index, so a source key survives bundle re-export / component reordering. Numeric keys
            // ("components[0]", legacy files) and any other path shape fall through to index-based
            // FindProperty below.
            if (_TryResolveNamedComponent(rootLiveObj, path, out var namedComponent))
            {
                return namedComponent;
            }

            var property = rootLiveObj.FindProperty(path);
            if (!property.HasValue) return null;

            return property.Value.GetValue() as UnityEngine.Object;
        }

        // The exposed property name of the GameObject wrapper's component list (see LiveGameObject).
        private const string kComponentsPrefix = "components[";

        /// <summary>
        /// Resolves a single named component element path ("components[&lt;TypeName&gt;]") against the root's
        /// GameObject by matching each component's exposed type name (then its [FormerlyNamedAs] former
        /// names, mirroring AssetStateSnapshot). Returns false — so the caller falls back to index-based
        /// resolution — for numeric keys (legacy "components[0]"), multi-segment paths, or non-GameObject
        /// roots. First match wins when several components share a type (same constraint as the state
        /// snapshot envelope).
        /// </summary>
        private static bool _TryResolveNamedComponent(LiveObjectHandle root, string path, out UnityEngine.Object result)
        {
            result = null;
            if (!path.StartsWith(kComponentsPrefix, StringComparison.Ordinal)) return false;
            if (!path.EndsWith("]", StringComparison.Ordinal)) return false;

            var key = path.Substring(kComponentsPrefix.Length, path.Length - kComponentsPrefix.Length - 1);
            if (key.Length == 0) return false;
            // Numeric key → let index-based FindProperty handle it (legacy compatibility).
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] >= '0' && key[i] <= '9') continue;
                goto named; // contains a non-digit → treat as a type name
            }
            return false;

        named:
            var go = LiveSceneObjectUtil.GetGameObject(root);
            if (go == null) return false;

            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                if (comp == null) continue;
                var liveClass = LiveClass.Find(comp.GetType());
                if (liveClass != null && liveClass.typeName == key) { result = comp; return true; }
            }
            // Former type names fallback (component type renamed via [FormerlyNamedAs]).
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                if (comp == null) continue;
                var formers = LiveClass.Find(comp.GetType())?.formerTypeNames;
                if (formers == null) continue;
                for (int f = 0; f < formers.Length; f++)
                {
                    if (formers[f] == key) { result = comp; return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// 最初の '.' または '[' を区切りとして rootId と path を分解する
        /// （FileScopedResolver._ComposeSourceKey の逆操作）。
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
        /// 一致するLiveObjectが見つかればReplaceIdでIDを復元する。
        /// LiveUnityObjectProxy等がPlay mode再入時に新しいGUIDを生成するケースに対応。
        /// </summary>
        private static void _TryResolveByTypeName(string typeName, string savedId, string objectName)
        {
            LiveObjectHandle? match = null;
            int matchCount = 0;

            foreach (var instance in LiveObjectRegistry.instances)
            {
                if (!instance.isValid) continue;
                if (instance.targetTypeName != typeName) continue;

                // @nameが指定されている場合はname一致も必須
                if (!string.IsNullOrEmpty(objectName) && instance.name != objectName) continue;

                match = instance;
                matchCount++;
            }

            // 一意に特定できた場合のみIDを復元（曖昧な場合は安全のためスキップ）
            if (matchCount == 1 && match != null)
            {
                if (match.Value.target is LiveUnityObjectBase wrapper)
                {
                    wrapper.ReplaceId(savedId);
                }
                else
                {
                    // 非UnityObject型: LiveObjectのIDを直接更新（struct なので Registry 経由で再キー）
                    LiveObjectRegistry.ReplaceId(match.Value, savedId);
                }
            }
            else if (matchCount > 1 && !string.IsNullOrEmpty(objectName))
            {
                // 曖昧 (同型・同名が複数) なため安全側でスキップするが、復元漏れになり得るので可視化する。
                // ただし @name を持つ live/API 経路の曖昧のみ警告する。永続ファイルのエントリは @name を
                // 持たず型だけでは特定できないため、警告は出さず未解決のまま残し、RemoteApp 側で削除可能な
                // "missing" シーンアイテムとして提示する (LiveScenePendingStore.GetOrphans 参照)。
                Debug.LogWarning($"[RemoteControl] Ambiguous restore: {matchCount} objects match type '{typeName}'" +
                    (string.IsNullOrEmpty(objectName) ? "" : $" name '{objectName}'") +
                    $" for saved id '{savedId}'; skipped id restore to avoid mis-binding.");
            }
        }

        /// <summary>
        /// Prefabインスタンス上のコンポーネントからLiveObjectを生成・登録する。
        /// Component型の場合はLiveObjectのみ作成。
        /// LiveUnityObjectBase派生型（Proxy/Reference）の場合はFactoryでラッパーも生成して返す。
        /// </summary>
        /// <param name="claimedTarget">このエントリで「消費」したUnityObject（Component または GameObject）。Pass 1での重複検出に使用。</param>
        /// <returns>コンテナに追加すべきLiveUnityObjectBase。Component型の場合はnull。</returns>
        /// <summary>
        /// Instantiates one deferred @prefab entry (queued by Pass 1 because its prefab was not resolvable
        /// then) now that <paramref name="prefab"/> is available: registers a fresh instance under the saved
        /// <paramref name="id"/>, moves it into the base scene, and applies the entry's own plus its child
        /// (component) overrides. Mirrors the Pass 1 + Pass 3 handling for a single entry. Returns false when
        /// it still cannot be registered, so <see cref="PendingPrefabStore"/> keeps it queued.
        /// </summary>
        internal static bool TryInstantiateDeferredPrefab(string id, string prefabKey, JObject jObject, GameObject prefab)
        {
            if (prefab == null || string.IsNullOrEmpty(id) || jObject == null) return false;
            var typeName = jObject["@type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName)) return false;

#if UNITY_2022_3_OR_NEWER
            var host = UnityEngine.Object.FindFirstObjectByType<RemoteControlBehaviour>();
#else
            var host = UnityEngine.Object.FindObjectOfType<RemoteControlBehaviour>();
#endif
            var resolver = host != null ? host.objectContainer : null;
            if (resolver == null) return false;
            if (resolver.FindById(id) != null) return true; // already restored (e.g. a raced drain)

            var go = UnityEngine.Object.Instantiate(prefab);
            var savedName = jObject["@name"]?.Value<string>();
            if (!string.IsNullOrEmpty(savedName)) go.name = savedName;

            // Place the instance in the base scene (its RemoteControlContainer's scene), not the active scene
            // which may be an additively-loaded set-bundle scene that would take the instance on unload.
            var sceneContainer = _ResolveSceneContainer();
            if (sceneContainer != null)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, sceneContainer.gameObject.scene);

            var wrapper = _RegisterComponentLiveObject(go, typeName, id, prefabKey, out _);
            if (wrapper == null)
            {
                UnityEngine.Object.Destroy(go);
                return false;
            }
            wrapper.OnEnable();

            if (sceneContainer != null)
            {
                if (!sceneContainer._objects.Contains(wrapper)) sceneContainer._objects.Add(wrapper);
            }
            else if (!resolver._objects.Contains(wrapper))
            {
                resolver._objects.Add(wrapper);
            }

            // Apply this entry's own saved properties, then its deferred child (component) overrides, then
            // re-baseline so the restored values are not reported as unsaved changes at quit time.
            var handle = resolver.FindById(id);
            if (handle != null)
            {
                LivePropertySerializer.FromJson(jObject.ToString(), handle.Value, resolver, captureDefaults: false);
                LiveScenePendingStore.ApplyFor(id, resolver);
                handle.Value.MarkClean();
            }
            return true;
        }

        private static LiveUnityObjectBase _RegisterComponentLiveObject(GameObject go, string typeName, string id, string prefabKey, out UnityEngine.Object claimedTarget)
        {
            claimedTarget = null;
            var liveClass = LiveClass.Find(typeName);
            if (liveClass == null || liveClass.type == null) return null;

            // Case 1: LiveClass型がComponent（直接Componentに[LiveClass]がついている場合）
            if (typeof(Component).IsAssignableFrom(liveClass.type))
            {
                var component = go.GetComponent(liveClass.type);
                if (component != null)
                {
                    LiveObjectRegistry.GetOrCreate(id, liveClass, component);
                    claimedTarget = component;
                }
                return null;
            }

            // Case 2: LiveUnityObjectBase派生型（LiveCamera等のProxy/Reference型）
            // LiveUnityObjectFactoryからdisplayNameが一致する登録を探す
            var registrations = LiveUnityObjectFactory.GetRegisteredTypes();
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

                // ファクトリでラッパー生成（コンストラクタで自動生成IDのLiveObjectが作られる）
                var wrapper = registration.factory(target);

                // 自動生成のLiveObjectを破棄し、保存済みIDで再登録
                wrapper.ReplaceId(id);

                // Prefab復元時はソースキー (Asset GUID) を設定（LiveSceneToJsonで@op/@prefab付きで出力するため）
                if (prefabKey != null)
                    wrapper.prefabSourceKey = prefabKey;

                claimedTarget = target;
                return wrapper;
            }

            return null;
        }

        /// <summary>
        /// typeNameから、Pass 1で「ターゲットが既に消費されたか」を判定するためのUnityObject型を返す。
        /// LiveClassがComponent派生ならその型、Proxy/Reference型なら登録の componentType を返す。
        /// 該当するUnityObject型が無い場合（POCO等）は null を返す。
        /// </summary>
        private static Type _ResolveRequiredUnityObjectType(string typeName)
        {
            var liveClass = LiveClass.Find(typeName);
            if (liveClass != null && liveClass.type != null
                && typeof(UnityEngine.Object).IsAssignableFrom(liveClass.type))
            {
                return liveClass.type;
            }
            var registrations = LiveUnityObjectFactory.GetRegisteredTypes();
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
