// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// LiveObjectHandle の依存グラフ走査ユーティリティ。シーンのシリアライズとは独立した
    /// オブジェクトグラフ BFS で、Container の default 補足やシーン保存の到達集合構築に使う。
    /// （旧 LiveSceneSerializer から分離。シーン読み書きそのものではないため本体側に残す。）
    /// </summary>
    public static class LiveObjectGraph
    {
        /// <summary>
        /// 任意のオブジェクトリストからLiveObjectリストを構築する。
        /// 依存するLiveObjectも幅優先探索で自動的に追加される。
        /// </summary>
        public static List<LiveObjectHandle> ResolveLiveObjects(IReadOnlyList<object> objects, ILiveObjectResolver resolver)
        {
            var result = new List<LiveObjectHandle>();
            var visited = new HashSet<LiveObjectHandle>();
            var visitedTargets = new HashSet<object>(LiveObjectRegistry.ReferenceEqualityComparer.Instance);
            var queue = new Queue<LiveObjectHandle>();

            // 初期オブジェクトをLiveObjectに変換
            for (int i = 0; i < objects.Count; i++)
            {
                var target = objects[i];
                if (target == null) continue;

                // ILiveObjectの場合は直接liveObjectを取得
                LiveObjectHandle? exposed;
                if (target is ILiveObject ieo)
                {
                    exposed = ieo.liveObject;
                }
                else
                {
                    exposed = resolver.FindByTarget(target);
                }

                if (exposed == null) continue;
                var ex = exposed.Value;
                if (!visited.Add(ex)) continue;

                result.Add(ex);
                queue.Enqueue(ex);
            }

            // 幅優先探索で依存LiveObjectを収集
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!current.isValid) continue;

                var propertyTypes = current.propertyTypes;
                for (int i = 0; i < propertyTypes.Length; i++)
                {
                    var propType = propertyTypes[i];
                    if (!propType.containsLiveObjectReference) continue;

                    object value;
                    try
                    {
                        value = LivePropertyUtility.GetValueRaw(current.target, propType);
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

            // BFS完了後、static classのLiveObjectを追加
            foreach (var instance in LiveObjectRegistry.instances)
            {
                if (instance.targetType == null || !instance.targetType.isStatic) continue;
                if (!visited.Add(instance)) continue;
                result.Add(instance);
            }

            return result;
        }

        private static void _TryEnqueueDependency(object target, ILiveObjectResolver resolver,
            HashSet<LiveObjectHandle> visited, HashSet<object> visitedTargets, List<LiveObjectHandle> result, Queue<LiveObjectHandle> queue)
        {
            if (target == null) return;

            // targetベースの重複チェック（unregistered LiveObjectは毎回新規インスタンスのため）
            if (!visitedTargets.Add(target)) return;

            var exposed = resolver.FindByTarget(target);

            // レジストリ未登録の場合、LiveClass登録済みのUnityEngine.Objectなら一時LiveObjectを生成
            if (exposed == null && target is UnityEngine.Object unityObj)
            {
                var liveClass = LiveClass.Find(target.GetType());
                if (liveClass != null)
                {
                    exposed = LiveObjectHandle.CreateUnregistered(liveClass, target);
                }
            }

            if (exposed == null) return;
            var ex = exposed.Value;
            visited.Add(ex);

            // ID付き/ID無しの両方を result に含める。
            // - hasId: LiveSceneToJson のトップレベル出力対象。
            // - hasId無し: 呼び出し側が SetDefault/EnsureDefaultsCaptured で
            //   inline 子オブジェクトの defaults を登録できるように含める（pending delta 判定に必要）。
            //   LiveSceneToJson 側では hasId チェックで出力はスキップされる。
            result.Add(ex);
            queue.Enqueue(ex);
        }
    }
}
