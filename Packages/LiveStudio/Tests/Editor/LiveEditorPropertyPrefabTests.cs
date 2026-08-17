// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// The editor rule for "changed" / revert, on the one path RemoteControl's own tests cannot reach:
    /// an actual prefab instance. A component defined in a test assembly is dropped when the object is
    /// saved as a prefab, so this fixture uses a real shipped component instead.
    /// </summary>
    public class LiveEditorPropertyPrefabTests
    {
        private const string kPrefabPath = "Assets/__LiveEditorPropertyPrefabTests.prefab";
        private const string kMemberPath = "_followRotation";

        private GameObject _sceneObject;
        private GameObject _instance;
        private LiveObjectHandle _handle;

        [SetUp]
        public void SetUp()
        {
            _sceneObject = new GameObject(nameof(LiveEditorPropertyPrefabTests));
            _sceneObject.AddComponent<BoneFollower>();

            var source = PrefabUtility.SaveAsPrefabAsset(_sceneObject, kPrefabPath);
            Assert.IsNotNull(source, "テストの前提: プレハブを保存できること");

            _instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            var component = _instance.GetComponent<BoneFollower>();
            Assert.IsNotNull(component, "テストの前提: プレハブにコンポーネントが残ること");

            _handle = new LiveObjectHandle(
                "editor-prefab-test", LiveClass.Find(typeof(BoneFollower)), component);
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectRegistry.Unregister(_handle);
            if (_instance != null) Object.DestroyImmediate(_instance);
            if (_sceneObject != null) Object.DestroyImmediate(_sceneObject);
            AssetDatabase.DeleteAsset(kPrefabPath);
        }

        private LiveProperty Property()
        {
            var property = _handle.FindProperty(kMemberPath);
            Assert.IsTrue(property.HasValue, "テストの前提: " + kMemberPath + " が公開されていること");
            return property.Value;
        }

        [Test]
        public void RemoteWrite_ShowsAsChangedImmediately()
        {
            Assert.IsFalse(LiveEditorProperty.IsChanged(Property()), "書く前は上書きなし");

            // ⚠ ここが本題。プレハブインスタンスへスクリプトから書いた分は、放っておくと
            // 上書き一覧に載らない。載るのは次に直列化されたときなので、書いた直後の応答だけが
            // 「変更なし」になり、あとで開き直すと「変更あり」になる — 実際にそうなっていた。
            var property = Property();
            using (new LiveEditorWriteScope(_handle.target, property.obj))
            {
                property.SetValue(true);
            }

            Assert.IsTrue(LiveEditorProperty.IsChanged(Property()),
                "書いた直後の読み取りで変更ありになること");
        }

        [Test]
        public void Revert_PutsThePrefabValueBack()
        {
            var property = Property();
            using (new LiveEditorWriteScope(_handle.target, property.obj))
            {
                property.SetValue(true);
            }

            Assert.IsTrue(LiveEditorProperty.TryRevert(Property()));

            var reverted = Property();
            Assert.AreEqual(false, reverted.GetValue(), "プレハブ側の値まで戻ること");
            Assert.IsFalse(LiveEditorProperty.IsChanged(reverted), "戻した後は変更なし");
        }
    }
}
