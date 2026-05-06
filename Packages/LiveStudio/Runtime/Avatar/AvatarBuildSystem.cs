using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Runtimeでヒューマノイド用Avatarを構築するシステム
    /// </summary>
    public static class AvatarBuildSystem
    {

        /// <summary>
        /// TransformとHumanDescriptionからAvatarBuildDataを作成する
        /// </summary>
        /// <param name="root">ルートTransform</param>
        /// <param name="humanDescription">HumanDescription</param>
        /// <returns>作成されたAvatarBuildData</returns>
        public static AvatarBuildData CreateAvatarBuildData(Transform root, HumanDescription humanDescription)
        {
            if (root == null)
            {
                Debug.LogError("[Core] Root transform is null.");
                return default;
            }

            if (humanDescription.skeleton == null || humanDescription.skeleton.Length == 0)
            {
                Debug.LogError("[Core] HumanDescription skeleton is null or empty.");
                return default;
            }

            // Humanoidで管理されているボーンのみをフィルタリング
            //TODO: 本当は_FilterHumanoidBones()を使って、Humanoidボーンのみを週出したいが、現状の実装では座標系が再構築しない問題があるため、一旦コメントアウト
            var filteredSkeletonBones = humanDescription.skeleton; //_FilterHumanoidBones(humanDescription, root);

            // 親子関係を抽出
            var parentIndices = _ExtractParentIndices(root, filteredSkeletonBones);

            // UnityEngine型からLilium.Virgo型に変換
            var virgoHumanBones = _ConvertHumanBones(humanDescription.human);
            var virgoSkeletonBones = _ConvertSkeletonBones(filteredSkeletonBones);

            var data = new AvatarBuildData
            {
                humanBones = virgoHumanBones,
                skeletonBones = virgoSkeletonBones,
                skeletonBoneParentIndices = parentIndices,
                upperArmTwist = humanDescription.upperArmTwist,
                lowerArmTwist = humanDescription.lowerArmTwist,
                upperLegTwist = humanDescription.upperLegTwist,
                lowerLegTwist = humanDescription.lowerLegTwist,
                armStretch = humanDescription.armStretch,
                legStretch = humanDescription.legStretch,
                feetSpacing = humanDescription.feetSpacing,
                hasTranslationDoF = humanDescription.hasTranslationDoF
            };

            return data;
        }

        /// <summary>
        /// AvatarBuildDataをHumanDescriptionに変換する
        /// </summary>
        /// <param name="data">変換元のAvatarBuildData</param>
        /// <returns>変換されたHumanDescription</returns>
        public static HumanDescription ToHumanDescription(in AvatarBuildData data)
        {
            // Lilium.Virgo型からUnityEngine型に変換
            var unityHumanBones = _ConvertToUnityHumanBones(data.humanBones);
            var unitySkeletonBones = _ConvertToUnitySkeletonBones(data.skeletonBones);

            var humanDescription = new HumanDescription
            {
                human = unityHumanBones,
                skeleton = unitySkeletonBones,
                upperArmTwist = data.upperArmTwist,
                lowerArmTwist = data.lowerArmTwist,
                upperLegTwist = data.upperLegTwist,
                lowerLegTwist = data.lowerLegTwist,
                armStretch = data.armStretch,
                legStretch = data.legStretch,
                feetSpacing = data.feetSpacing,
                hasTranslationDoF = data.hasTranslationDoF
            };

            return humanDescription;
        }


        /// <summary>
        /// AvatarBuildDataからスケルトン構造を構築する
        /// </summary>
        /// <param name="data">ビルドデータ</param>
        /// <param name="rootGameObject">スケルトンのルートとして使用するGameObject（必須）</param>
        /// <returns>構築されたスケルトンのルートGameObject</returns>
        public static GameObject BuildSkeleton(in AvatarBuildData data, GameObject rootGameObject)
        {
            if (rootGameObject == null)
            {
                Debug.LogError("[Core] rootGameObject is null.");
                return null;
            }

            if (data.skeletonBones == null || data.skeletonBones.Length == 0)
            {
                Debug.LogError("[Core] SkeletonBones is null or empty.");
                return null;
            }

            if (data.skeletonBoneParentIndices == null || data.skeletonBoneParentIndices.Length != data.skeletonBones.Length)
            {
                Debug.LogError("[Core] SkeletonBoneParentIndices is null or length mismatch.");
                return null;
            }

            // 既存の子階層をクリア
            var childCount = rootGameObject.transform.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = rootGameObject.transform.GetChild(i);
                Object.DestroyImmediate(child.gameObject);
            }

            var boneObjects = new GameObject[data.skeletonBones.Length];

            // 全てのボーンGameObjectを新規作成
            for (int i = 0; i < data.skeletonBones.Length; i++)
            {
                var bone = data.skeletonBones[i];
                var boneObj = new GameObject(bone.name);

                boneObj.transform.localPosition = bone.position;
                boneObj.transform.localRotation = bone.rotation;
                boneObj.transform.localScale = bone.scale;
                boneObjects[i] = boneObj;
            }

            // 親子関係を設定
            for (int i = 0; i < data.skeletonBones.Length; i++)
            {
                var parentIndex = data.skeletonBoneParentIndices[i];
                if (parentIndex >= 0 && parentIndex < boneObjects.Length)
                {
                    // 親が存在する場合は親の子として配置
                    boneObjects[i].transform.SetParent(boneObjects[parentIndex].transform, false);
                }
                else
                {
                    // ルートボーンはrootGameObjectの直下に配置
                    boneObjects[i].transform.SetParent(rootGameObject.transform, false);
                }
            }

            // Animatorコンポーネントを追加（まだ存在しない場合）
            if (rootGameObject.GetComponent<Animator>() == null)
            {
                rootGameObject.AddComponent<Animator>();
            }

            return rootGameObject;
        }


        /// <summary>
        /// 明示的なHumanDescriptionを使用してAvatarを作成する
        /// </summary>
        /// <param name="root">アバターのルートGameObject</param>
        /// <param name="humanDescription">ヒューマノイド定義</param>
        /// <param name="avatarName">作成するAvatarの名前</param>
        /// <returns>作成されたAvatar。失敗時はnull</returns>
        public static Avatar BuildHumanAvatar(GameObject root, HumanDescription humanDescription, string avatarName = "RuntimeAvatar")
        {
            if (root == null)
            {
                Debug.LogError("[Core] Root GameObject is null.");
                return null;
            }

            var avatar = AvatarBuilder.BuildHumanAvatar(root, humanDescription);
            if (avatar == null)
            {
                Debug.LogError("[Core] AvatarBuilder.BuildHumanAvatar returned null.");
                return null;
            }

            avatar.name = avatarName;

            if (!avatar.isValid)
            {
                Debug.LogError("[Core] Created avatar is invalid.");
                GameObjectUtility.Destroy(avatar);
                return null;
            }

            if (!avatar.isHuman)
            {
                Debug.LogError("[Core] Created avatar is not humanoid.");
                GameObjectUtility.Destroy(avatar);
                return null;
            }

            return avatar;
        }

        /// <summary>
        /// AvatarBuildDataを使用してAvatarを作成する
        /// </summary>
        /// <param name="root">アバターのルートGameObject</param>
        /// <param name="data">ビルドデータ</param>
        /// <param name="avatarName">作成するAvatarの名前</param>
        /// <returns>作成されたAvatar。失敗時はnull</returns>
        public static Avatar BuildHumanAvatar(GameObject root, in AvatarBuildData data, string avatarName = "RuntimeAvatar")
        {
            var humanDescription = ToHumanDescription(data);
            return BuildHumanAvatar(root, humanDescription, avatarName);
        }

        /// <summary>
        /// UnityEngine.HumanLimitをLilium.Virgo.HumanLimitに変換
        /// </summary>
        private static HumanLimit _ConvertHumanLimit(UnityEngine.HumanLimit unityLimit)
        {
            return new HumanLimit
            {
                min = unityLimit.min,
                max = unityLimit.max,
                center = unityLimit.center,
                axisLength = unityLimit.axisLength,
                useDefaultValues = unityLimit.useDefaultValues
            };
        }

        /// <summary>
        /// Lilium.Virgo.HumanLimitをUnityEngine.HumanLimitに変換
        /// </summary>
        private static UnityEngine.HumanLimit _ConvertToUnityHumanLimit(HumanLimit virgoLimit)
        {
            return new UnityEngine.HumanLimit
            {
                min = virgoLimit.min,
                max = virgoLimit.max,
                center = virgoLimit.center,
                axisLength = virgoLimit.axisLength,
                useDefaultValues = virgoLimit.useDefaultValues
            };
        }

        /// <summary>
        /// UnityEngine.HumanBone配列をLilium.Virgo.HumanBone配列に変換
        /// </summary>
        private static HumanBone[] _ConvertHumanBones(UnityEngine.HumanBone[] unityBones)
        {
            if (unityBones == null) return null;

            var result = new HumanBone[unityBones.Length];
            for (int i = 0; i < unityBones.Length; i++)
            {
                result[i] = new HumanBone
                {
                    boneName = unityBones[i].boneName,
                    humanName = unityBones[i].humanName,
                    limit = _ConvertHumanLimit(unityBones[i].limit)
                };
            }
            return result;
        }

        /// <summary>
        /// UnityEngine.SkeletonBone配列をLilium.Virgo.SkeletonBone配列に変換
        /// </summary>
        private static SkeletonBone[] _ConvertSkeletonBones(UnityEngine.SkeletonBone[] unityBones)
        {
            if (unityBones == null) return null;

            var result = new SkeletonBone[unityBones.Length];
            for (int i = 0; i < unityBones.Length; i++)
            {
                result[i] = new SkeletonBone
                {
                    name = unityBones[i].name,
                    position = unityBones[i].position,
                    rotation = unityBones[i].rotation,
                    scale = unityBones[i].scale
                };
            }
            return result;
        }

        /// <summary>
        /// Lilium.Virgo.HumanBone配列をUnityEngine.HumanBone配列に変換
        /// </summary>
        private static UnityEngine.HumanBone[] _ConvertToUnityHumanBones(HumanBone[] virgoBones)
        {
            if (virgoBones == null) return null;

            var result = new UnityEngine.HumanBone[virgoBones.Length];
            for (int i = 0; i < virgoBones.Length; i++)
            {
                result[i] = new UnityEngine.HumanBone
                {
                    boneName = virgoBones[i].boneName,
                    humanName = virgoBones[i].humanName,
                    limit = _ConvertToUnityHumanLimit(virgoBones[i].limit)
                };
            }
            return result;
        }

        /// <summary>
        /// Lilium.Virgo.SkeletonBone配列をUnityEngine.SkeletonBone配列に変換
        /// </summary>
        private static UnityEngine.SkeletonBone[] _ConvertToUnitySkeletonBones(SkeletonBone[] virgoBones)
        {
            if (virgoBones == null) return null;

            var result = new UnityEngine.SkeletonBone[virgoBones.Length];
            for (int i = 0; i < virgoBones.Length; i++)
            {
                result[i] = new UnityEngine.SkeletonBone
                {
                    name = virgoBones[i].name,
                    position = virgoBones[i].position,
                    rotation = virgoBones[i].rotation,
                    scale = virgoBones[i].scale
                };
            }
            return result;
        }

        /// <summary>
        /// HumanDescriptionからHumanoidで管理されているボーンのみをフィルタリング
        /// </summary>
        /// <param name="humanDescription">HumanDescription</param>
        /// <param name="root">アバターのルートTransform</param>
        /// <returns>フィルタリングされたSkeletonBone配列</returns>
        private static SkeletonBone[] _FilterHumanoidBones(HumanDescription humanDescription, Transform root)
        {
            // HumanBoneで使用されている全ボーン名を収集
            var humanBoneNames = new HashSet<string>();
            foreach (var humanBone in humanDescription.human)
            {
                humanBoneNames.Add(humanBone.boneName);
            }

            // Transformの階層情報を収集
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var transformDict = new Dictionary<string, Transform>();
            foreach (var t in allTransforms)
            {
                if (!transformDict.ContainsKey(t.name))
                {
                    transformDict[t.name] = t;
                }
            }

            // HumanBoneとその親階層に必要な全てのボーン名を収集
            var requiredBoneNames = new HashSet<string>();
            foreach (var boneName in humanBoneNames)
            {
                if (transformDict.TryGetValue(boneName, out var boneTransform))
                {
                    // ボーン自身を追加
                    requiredBoneNames.Add(boneName);

                    // 親階層を辿ってルートまで追加
                    var current = boneTransform.parent;
                    while (current != null && current != root)
                    {
                        requiredBoneNames.Add(current.name);
                        current = current.parent;
                    }
                }
            }

            // skeleton配列から必要なボーンのみをフィルタリング
            var filteredBones = new List<SkeletonBone>();
            foreach (var skeletonBone in humanDescription.skeleton)
            {
                if (requiredBoneNames.Contains(skeletonBone.name))
                {
                    // UnityEngine.SkeletonBoneからLilium.Virgo.SkeletonBoneに変換
                    var virgoBone = new SkeletonBone
                    {
                        name = skeletonBone.name,
                        position = skeletonBone.position,
                        rotation = skeletonBone.rotation,
                        scale = skeletonBone.scale
                    };
                    filteredBones.Add(virgoBone);
                }
            }

            return filteredBones.ToArray();
        }

        /// <summary>
        /// Transform階層からSkeletonBoneの親インデックスを抽出する
        /// </summary>
        private static int[] _ExtractParentIndices(Transform root, UnityEngine.SkeletonBone[] skeletonBones)
        {
            var parentIndices = new int[skeletonBones.Length];

            // 全てのTransformを収集
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var transformDict = new Dictionary<string, Transform>();
            foreach (var t in allTransforms)
            {
                if (!transformDict.ContainsKey(t.name))
                {
                    transformDict[t.name] = t;
                }
            }

            // 各SkeletonBoneの親インデックスを計算
            for (int i = 0; i < skeletonBones.Length; i++)
            {
                var boneName = skeletonBones[i].name;

                // Transformを検索
                if (!transformDict.TryGetValue(boneName, out var boneTransform))
                {
                    // Transform階層に存在しないボーンは親なしとして扱う
                    //Debug.LogWarning($"[Core] Transform not found for bone '{boneName}'. Treating as root bone.");
                    parentIndices[i] = -1;
                    continue;
                }

                // 親を取得
                var parentTransform = boneTransform.parent;
                if (parentTransform == null || parentTransform == root)
                {
                    // ルートまたは親なし
                    parentIndices[i] = -1;
                }
                else
                {
                    // 親のインデックスを検索
                    int parentIndex = System.Array.FindIndex(skeletonBones, sb => sb.name == parentTransform.name);
                    if (parentIndex < 0)
                    {
                        // 親がSkeletonBone配列に存在しない場合はルートとして扱う
                        //Debug.LogWarning($"[Core] Parent Transform '{parentTransform.name}' not found in skeleton bones for '{boneName}'. Treating as root bone.");
                        parentIndices[i] = -1;
                    }
                    else
                    {
                        parentIndices[i] = parentIndex;
                    }
                }
            }

            return parentIndices;
        }

        public static void PrintHumanDescription(HumanDescription humanDescription)
        {
            string text = "";
            text += "Human Bones:\n";
            foreach (var humanBone in humanDescription.human)
            {
                text += $"- Bone Name: {humanBone.boneName}, Human Name: {humanBone.humanName}\n";
            }
            text += "\n";
            text += "\n";

            text += "Skeleton Bones:\n";
            foreach (var skeletonBone in humanDescription.skeleton)
            {
                text += $"- Name: {skeletonBone.name}, Position: {skeletonBone.position}, Rotation: {skeletonBone.rotation}, Scale: {skeletonBone.scale}\n";
            }
            Debug.Log(text);
        }
    }
}
