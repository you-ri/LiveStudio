// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObject の依存グラフ走査ユーティリティ。シーンのシリアライズとは独立した
    /// オブジェクトグラフ BFS で、Container の default 補足やシーン保存の到達集合構築に使う。
    /// （旧 ExposedSceneSerializer から分離。シーン読み書きそのものではないため本体側に残す。）
    /// </summary>
    public static class ExposedObjectGraph
    {
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
            //   SceneToJson 側では hasId チェックで出力はスキップされる。
            result.Add(exposed);
            queue.Enqueue(exposed);
        }
    }
}
