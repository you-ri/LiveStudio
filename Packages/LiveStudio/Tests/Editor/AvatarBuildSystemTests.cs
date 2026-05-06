using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// AvatarBuildSystemのUnity Test Framework テスト
    /// </summary>
    public class AvatarBuildSystemTests
    {
        private GameObject _testSourceModel;
        private Animator _testAnimator;
        private GameObject _testTargetRoot;

        /// <summary>
        /// 各テスト実行前のセットアップ
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // テスト用のソースモデルを作成（プリミティブで代用）
            _testSourceModel = new GameObject("TestSourceModel");
            _testAnimator = _testSourceModel.AddComponent<Animator>();

            // テスト用のターゲットルートを作成
            _testTargetRoot = new GameObject("TestTargetRoot");
        }

        /// <summary>
        /// 各テスト実行後のクリーンアップ
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // テスト用オブジェクトを削除
            if (_testSourceModel != null)
            {
                Object.DestroyImmediate(_testSourceModel);
            }

            if (_testTargetRoot != null)
            {
                Object.DestroyImmediate(_testTargetRoot);
            }
        }

        /// <summary>
        /// テスト: CreateAvatarBuildData - Transformがnullの場合
        /// </summary>
        [Test]
        public void CreateAvatarBuildData_WithNullTransform_ReturnsDefault()
        {
            // Arrange
            var humanDesc = new HumanDescription
            {
                skeleton = new UnityEngine.SkeletonBone[] { new UnityEngine.SkeletonBone { name = "Test" } }
            };
            LogAssert.Expect(LogType.Error, "[Core] Root transform is null.");

            // Act
            var result = AvatarBuildSystem.CreateAvatarBuildData(null, humanDesc);

            // Assert
            Assert.IsNull(result.humanBones);
            Assert.IsNull(result.skeletonBones);
        }

        /// <summary>
        /// テスト: CreateAvatarBuildData - HumanDescriptionのskeletonが空の場合
        /// </summary>
        [Test]
        public void CreateAvatarBuildData_WithEmptySkeleton_ReturnsDefault()
        {
            // Arrange
            var humanDesc = new HumanDescription
            {
                skeleton = new UnityEngine.SkeletonBone[0]
            };
            LogAssert.Expect(LogType.Error, "[Core] HumanDescription skeleton is null or empty.");

            // Act
            var result = AvatarBuildSystem.CreateAvatarBuildData(_testSourceModel.transform, humanDesc);

            // Assert
            Assert.IsNull(result.humanBones);
            Assert.IsNull(result.skeletonBones);
        }

        /// <summary>
        /// テスト: CreateAvatarBuildData - 有効なHumanoid Avatarの場合
        /// </summary>
        [Test]
        public void CreateAvatarBuildData_WithValidHumanoidAvatar_ExtractsData()
        {
            // Arrange
            // プロジェクト内のテスト用Humanoidモデルを読み込む
            var testModel = _LoadTestHumanoidModel();
            if (testModel == null)
            {
                Assert.Ignore("Test humanoid model not found. Skipping test.");
                return;
            }

            var animator = testModel.GetComponent<Animator>();
            Assert.IsNotNull(animator, "Test model should have Animator component");
            Assert.IsNotNull(animator.avatar, "Test model should have Avatar");
            Assert.IsTrue(animator.avatar.isHuman, "Test model should be humanoid");

            var humanDescription = animator.avatar.humanDescription;

            // Act
            var result = AvatarBuildSystem.CreateAvatarBuildData(testModel.transform, humanDescription);

            // Assert
            Assert.IsNotNull(result.humanBones, "HumanBones should not be null");
            Assert.IsNotNull(result.skeletonBones, "SkeletonBones should not be null");
            Assert.IsNotNull(result.skeletonBoneParentIndices, "ParentIndices should not be null");
            Assert.Greater(result.humanBones.Length, 0, "Should extract human bones");
            Assert.Greater(result.skeletonBones.Length, 0, "Should extract skeleton bones");
            Assert.AreEqual(result.skeletonBones.Length, result.skeletonBoneParentIndices.Length,
                "ParentIndices length should match SkeletonBones length");

            // Cleanup
            Object.DestroyImmediate(testModel);
        }

        /// <summary>
        /// テスト: BuildSkeleton - rootGameObjectがnullの場合
        /// </summary>
        [Test]
        public void BuildSkeleton_WithNullRootGameObject_ReturnsNull()
        {
            // Arrange
            var data = new AvatarBuildData
            {
                skeletonBones = new SkeletonBone[1],
                skeletonBoneParentIndices = new int[1]
            };
            LogAssert.Expect(LogType.Error, "[Core] rootGameObject is null.");

            // Act
            var result = AvatarBuildSystem.BuildSkeleton(data, null);

            // Assert
            Assert.IsNull(result, "Should return null when rootGameObject is null");
        }

        /// <summary>
        /// テスト: BuildSkeleton - skeletonBonesが空の場合
        /// </summary>
        [Test]
        public void BuildSkeleton_WithEmptySkeletonBones_ReturnsNull()
        {
            // Arrange
            var data = new AvatarBuildData
            {
                skeletonBones = new SkeletonBone[0],
                skeletonBoneParentIndices = new int[0]
            };
            LogAssert.Expect(LogType.Error, "[Core] SkeletonBones is null or empty.");

            // Act
            var result = AvatarBuildSystem.BuildSkeleton(data, _testTargetRoot);

            // Assert
            Assert.IsNull(result, "Should return null when skeletonBones is empty");
        }

        /// <summary>
        /// テスト: BuildSkeleton - 有効なデータで正常に構築
        /// </summary>
        [Test]
        public void BuildSkeleton_WithValidData_BuildsHierarchy()
        {
            // Arrange
            var data = new AvatarBuildData
            {
                skeletonBones = new SkeletonBone[]
                {
                    new SkeletonBone { name = "Root", position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one },
                    new SkeletonBone { name = "Child1", position = Vector3.up, rotation = Quaternion.identity, scale = Vector3.one },
                    new SkeletonBone { name = "Child2", position = Vector3.right, rotation = Quaternion.identity, scale = Vector3.one }
                },
                skeletonBoneParentIndices = new int[] { -1, 0, 0 } // Root, Child1->Root, Child2->Root
            };

            // Act
            var result = AvatarBuildSystem.BuildSkeleton(data, _testTargetRoot);

            // Assert
            Assert.IsNotNull(result, "Should return valid GameObject");
            Assert.AreEqual(_testTargetRoot, result, "Should return the same rootGameObject");
            Assert.AreEqual(1, result.transform.childCount, "Root should have 1 direct child (the skeleton root bone)");

            var skeletonRoot = result.transform.GetChild(0);
            Assert.AreEqual("Root", skeletonRoot.name, "First child should be the skeleton root bone");
            Assert.AreEqual(2, skeletonRoot.childCount, "Skeleton root should have 2 children");

            // Animatorコンポーネントの確認
            var animator = result.GetComponent<Animator>();
            Assert.IsNotNull(animator, "Should add Animator component");
        }

        /// <summary>
        /// テスト: BuildSkeleton - 既存の子がクリアされる
        /// </summary>
        [Test]
        public void BuildSkeleton_ClearsExistingChildren()
        {
            // Arrange
            var existingChild = new GameObject("ExistingChild");
            existingChild.transform.SetParent(_testTargetRoot.transform);
            Assert.AreEqual(1, _testTargetRoot.transform.childCount, "Should have 1 existing child");

            var data = new AvatarBuildData
            {
                skeletonBones = new SkeletonBone[]
                {
                    new SkeletonBone { name = "NewRoot", position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one }
                },
                skeletonBoneParentIndices = new int[] { -1 }
            };

            // Act
            var result = AvatarBuildSystem.BuildSkeleton(data, _testTargetRoot);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.transform.childCount, "Should have only new skeleton hierarchy");
            Assert.AreEqual("NewRoot", result.transform.GetChild(0).name, "New hierarchy should be built");
        }

        /// <summary>
        /// テスト: ToHumanDescription - データを正しく変換
        /// </summary>
        [Test]
        public void ToHumanDescription_ConvertsDataCorrectly()
        {
            // Arrange
            var humanBones = new HumanBone[]
            {
                new HumanBone { humanName = "Hips", boneName = "J_Bip_C_Hips" }
            };
            var skeletonBones = new SkeletonBone[]
            {
                new SkeletonBone { name = "J_Bip_C_Hips" }
            };

            var data = new AvatarBuildData
            {
                humanBones = humanBones,
                skeletonBones = skeletonBones,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0.0f,
                hasTranslationDoF = false
            };

            // Act
            var result = AvatarBuildSystem.ToHumanDescription(data);

            // Assert - HumanBones
            Assert.AreEqual(humanBones.Length, result.human.Length, "HumanBones count should match");
            Assert.AreEqual(humanBones[0].humanName, result.human[0].humanName, "HumanBone humanName should match");
            Assert.AreEqual(humanBones[0].boneName, result.human[0].boneName, "HumanBone boneName should match");

            // Assert - SkeletonBones
            Assert.AreEqual(skeletonBones.Length, result.skeleton.Length, "SkeletonBones count should match");
            Assert.AreEqual(skeletonBones[0].name, result.skeleton[0].name, "SkeletonBone name should match");

            // Assert - Other properties
            Assert.AreEqual(0.5f, result.upperArmTwist);
            Assert.AreEqual(0.5f, result.lowerArmTwist);
            Assert.AreEqual(0.05f, result.armStretch);
            Assert.IsFalse(result.hasTranslationDoF);
        }

        /// <summary>
        /// テスト: CreateAvatarBuildData → ToHumanDescription で元のデータが保持される
        /// </summary>
        [Test]
        public void CreateAvatarBuildData_ToHumanDescription_PreservesOriginalData()
        {
            // Arrange
            var testModel = _LoadTestHumanoidModel();
            if (testModel == null)
            {
                Assert.Ignore("Test humanoid model not found. Skipping test.");
                return;
            }

            var animator = testModel.GetComponent<Animator>();
            var originalHumanDescription = animator.avatar.humanDescription;

            try
            {
                // Act - Step 1: CreateAvatarBuildData
                var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(testModel.transform, originalHumanDescription);

                // Act - Step 2: ToHumanDescription
                var reconstructedHumanDescription = AvatarBuildSystem.ToHumanDescription(avatarBuildData);

                // Assert - 基本パラメーターが保持される
                Assert.AreEqual(originalHumanDescription.upperArmTwist, reconstructedHumanDescription.upperArmTwist, 0.0001f, "upperArmTwist should be preserved");
                Assert.AreEqual(originalHumanDescription.lowerArmTwist, reconstructedHumanDescription.lowerArmTwist, 0.0001f, "lowerArmTwist should be preserved");
                Assert.AreEqual(originalHumanDescription.upperLegTwist, reconstructedHumanDescription.upperLegTwist, 0.0001f, "upperLegTwist should be preserved");
                Assert.AreEqual(originalHumanDescription.lowerLegTwist, reconstructedHumanDescription.lowerLegTwist, 0.0001f, "lowerLegTwist should be preserved");
                Assert.AreEqual(originalHumanDescription.armStretch, reconstructedHumanDescription.armStretch, 0.0001f, "armStretch should be preserved");
                Assert.AreEqual(originalHumanDescription.legStretch, reconstructedHumanDescription.legStretch, 0.0001f, "legStretch should be preserved");
                Assert.AreEqual(originalHumanDescription.feetSpacing, reconstructedHumanDescription.feetSpacing, 0.0001f, "feetSpacing should be preserved");
                Assert.AreEqual(originalHumanDescription.hasTranslationDoF, reconstructedHumanDescription.hasTranslationDoF, "hasTranslationDoF should be preserved");

                // Assert - HumanBones配列が保持される
                Assert.AreEqual(originalHumanDescription.human.Length, reconstructedHumanDescription.human.Length, "HumanBones count should be preserved");
                for (int i = 0; i < originalHumanDescription.human.Length; i++)
                {
                    Assert.AreEqual(originalHumanDescription.human[i].humanName, reconstructedHumanDescription.human[i].humanName,
                        $"HumanBone[{i}].humanName should be preserved");
                    Assert.AreEqual(originalHumanDescription.human[i].boneName, reconstructedHumanDescription.human[i].boneName,
                        $"HumanBone[{i}].boneName should be preserved");
                }

                // Assert - SkeletonBones配列が保持される
                Assert.AreEqual(originalHumanDescription.skeleton.Length, reconstructedHumanDescription.skeleton.Length, "SkeletonBones count should be preserved");
                for (int i = 0; i < originalHumanDescription.skeleton.Length; i++)
                {
                    Assert.AreEqual(originalHumanDescription.skeleton[i].name, reconstructedHumanDescription.skeleton[i].name,
                        $"SkeletonBone[{i}].name should be preserved");
                    Assert.AreEqual(originalHumanDescription.skeleton[i].position, reconstructedHumanDescription.skeleton[i].position,
                        $"SkeletonBone[{i}].position should be preserved");
                    Assert.AreEqual(originalHumanDescription.skeleton[i].rotation, reconstructedHumanDescription.skeleton[i].rotation,
                        $"SkeletonBone[{i}].rotation should be preserved");
                    Assert.AreEqual(originalHumanDescription.skeleton[i].scale, reconstructedHumanDescription.skeleton[i].scale,
                        $"SkeletonBone[{i}].scale should be preserved");
                }
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(testModel);
            }
        }

        /// <summary>
        /// 統合テスト: CreateAvatarBuildData → BuildSkeleton → BuildAvatar フロー
        /// </summary>
        [Test]
        public void IntegrationTest_CreateBuildSkeletonBuildAvatar()
        {
            // Arrange
            var testModel = _LoadTestHumanoidModel();
            if (testModel == null)
            {
                Assert.Ignore("Test humanoid model not found. Skipping integration test.");
                return;
            }

            var sourceAnimator = testModel.GetComponent<Animator>();
            var humanDescription = sourceAnimator.avatar.humanDescription;
            var targetRoot = new GameObject("IntegrationTestTarget");

            try
            {
                // Act - Step 1: CreateAvatarBuildData
                var extractedData = AvatarBuildSystem.CreateAvatarBuildData(testModel.transform, humanDescription);
                Assert.IsNotNull(extractedData.humanBones, "CreateAvatarBuildData failed");

                // Act - Step 2: Build Skeleton
                var rebuiltSkeleton = AvatarBuildSystem.BuildSkeleton(extractedData, targetRoot);
                Assert.IsNotNull(rebuiltSkeleton, "BuildSkeleton failed");

                // Act - Step 3: Build Avatar
                var rebuiltAvatar = AvatarBuildSystem.BuildHumanAvatar(rebuiltSkeleton, extractedData, "TestAvatar");

                // Assert
                Assert.IsNotNull(rebuiltAvatar, "BuildHumanAvatar should create Avatar");
                Assert.IsTrue(rebuiltAvatar.isValid, "Created Avatar should be valid");
                Assert.IsTrue(rebuiltAvatar.isHuman, "Created Avatar should be humanoid");
                Assert.AreEqual("TestAvatar", rebuiltAvatar.name, "Avatar name should match");
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(testModel);
                Object.DestroyImmediate(targetRoot);
            }
        }

        /// <summary>
        /// テスト用Humanoidモデルを読み込む
        /// </summary>
        private GameObject _LoadTestHumanoidModel()
        {
            // プロジェクト内のHumanoidモデルを検索
            var guids = AssetDatabase.FindAssets("t:GameObject");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab != null)
                {
                    var animator = prefab.GetComponent<Animator>();
                    if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                    {
                        // Humanoidモデルを見つけた
                        var instance = Object.Instantiate(prefab);
                        return instance;
                    }
                }
            }

            return null;
        }
    }
}
