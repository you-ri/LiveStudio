// Copyright (c) You-Ri, 2026

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Scene;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// AvatarInput.settings プロパティ (Persistable/POCO) のシリアライズ経路のテスト。
    /// キーバインド情報が JSON 出力に含まれ、ラウンドトリップで復元されることを検証する。
    /// </summary>
    public class AvatarInputSettingsTests
    {
        private GameObject _gameObject;
        private AvatarInput _controller;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Reset();

            // OnEnable (ExecuteAlways) で _inputActionMap.Enable() が呼ばれるので、
            // 先に無効化した状態で AddComponent → フィールド設定 → 再有効化する。
            _gameObject = new GameObject("AvatarInputTest");
            _gameObject.SetActive(false);
            _controller = _gameObject.AddComponent<AvatarInput>();

            var field = typeof(AvatarInput).GetField(
                "_inputActionMap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "_inputActionMap field must exist");
            field.SetValue(_controller, new InputActionMap("TestMap"));

            _gameObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
                _controller = null;
            }
        }

        [Test]
        public void Settings_Getter_IncludesBindings()
        {
            var action = InputActionMapUtils.SafeCreateAction(_controller.inputActionMap, "Jump");
            Assert.IsNotNull(action);
            action.AddBinding("<Keyboard>/space");

            var settings = _controller.settings;

            Assert.IsNotNull(settings);
            Assert.IsNotNull(settings.bindings);
            Assert.AreEqual(1, settings.bindings.Length, "bindings array should contain one entry");
            Assert.AreEqual("Jump", settings.bindings[0].actionName);
            Assert.Contains("<Keyboard>/space", settings.bindings[0].bindingPaths);
        }

        [Test]
        public void Settings_SerializedForPersistence_IncludesBindingPath()
        {
            // Scene 保存と同じ経路 (forPersistence=true Snapshot) で JSON 化される
            var action = InputActionMapUtils.SafeCreateAction(_controller.inputActionMap, "Jump");
            Assert.IsNotNull(action);
            action.AddBinding("<Keyboard>/space");

            var exposedClass = ExposedClass.Find(typeof(AvatarInput));
            Assert.IsNotNull(exposedClass, "AvatarInput must have ExposedClass registered");

            var exposedObject = new ExposedObject("test-input-actions", exposedClass, _controller);
            try
            {
                var json = ExposedSceneSerializer.SceneToJson(
                    new System.Collections.Generic.List<ExposedObject> { exposedObject },
                    DefaultExposedObjectResolver.Instance,
                    SerializeMode.Snapshot);

                StringAssert.Contains("\"settings\"", json);
                StringAssert.Contains("\"Jump\"", json);
                StringAssert.Contains("<Keyboard>/space", json);
            }
            finally
            {
                exposedObject.Unregister();
            }
        }

        [Test]
        public void Settings_RoundTrip_RestoresBindingPath()
        {
            // Save → 別のコントローラに Load → バインディングが復元されること。
            // SetDefault は空状態でキャプチャし、その後にバインディングを追加して
            // delta に bindings を載せる。
            var exposedClass = ExposedClass.Find(typeof(AvatarInput));
            var source = new ExposedObject("test-input-actions-rt", exposedClass, _controller);
            try
            {
                ExposedPropertyUtility.SetDefault(source);

                var action = InputActionMapUtils.SafeCreateAction(_controller.inputActionMap, "Crouch");
                Assert.IsNotNull(action);
                action.AddBinding("<Keyboard>/c");

                var json = ExposedSceneSerializer.SceneToJson(
                    new System.Collections.Generic.List<ExposedObject> { source },
                    DefaultExposedObjectResolver.Instance,
                    SerializeMode.Delta);

                // 復元先の別コントローラを用意
                source.Unregister();

                var targetGo = new GameObject("AvatarInputTarget");
                targetGo.SetActive(false);
                var target = targetGo.AddComponent<AvatarInput>();
                var field = typeof(AvatarInput).GetField(
                    "_inputActionMap", BindingFlags.Instance | BindingFlags.NonPublic);
                field.SetValue(target, new InputActionMap("TargetMap"));
                targetGo.SetActive(true);

                var targetExposed = new ExposedObject("test-input-actions-rt", exposedClass, target);
                try
                {
                    ExposedSceneSerializer.SceneFromJson(json, DefaultExposedObjectResolver.Instance);

                    var restoredAction = target.inputActionMap.FindAction("Crouch");
                    Assert.IsNotNull(restoredAction, "Crouch action should be restored");
                    Assert.AreEqual(1, restoredAction.bindings.Count, "one binding should be restored");
                    Assert.AreEqual("<Keyboard>/c", restoredAction.bindings[0].effectivePath);
                }
                finally
                {
                    targetExposed.Unregister();
                    Object.DestroyImmediate(targetGo);
                }
            }
            finally
            {
                if (ExposedObjectRegistry.FindById("test-input-actions-rt") != null)
                {
                    ExposedObjectRegistry.FindById("test-input-actions-rt").Unregister();
                }
            }
        }

        [Test]
        public void Settings_DeltaForPersistence_IncludesBindingPath()
        {
            // Delta モードでバインディング変更を反映した settings が出力されること。
            // SetDefault で空状態を固定してから追加し、差分として現れることを確認する。
            var exposedClass = ExposedClass.Find(typeof(AvatarInput));
            var exposedObject = new ExposedObject("test-input-actions-delta", exposedClass, _controller);
            try
            {
                ExposedPropertyUtility.SetDefault(exposedObject);

                var action = InputActionMapUtils.SafeCreateAction(_controller.inputActionMap, "Fire");
                Assert.IsNotNull(action);
                action.AddBinding("<Mouse>/leftButton");

                var json = ExposedSceneSerializer.SceneToJson(
                    new System.Collections.Generic.List<ExposedObject> { exposedObject },
                    DefaultExposedObjectResolver.Instance,
                    SerializeMode.Delta);

                StringAssert.Contains("\"settings\"", json);
                StringAssert.Contains("\"Fire\"", json);
                StringAssert.Contains("<Mouse>/leftButton", json);
            }
            finally
            {
                exposedObject.Unregister();
            }
        }

        [Test]
        public void Settings_DeltaForPersistence_NoChange_EmitsNoSettings()
        {
            // SetDefault 後に何も変更しなければ delta から settings が消え、
            // objects[] が空になること (delta 空化の回帰テスト)。
            var exposedClass = ExposedClass.Find(typeof(AvatarInput));
            var exposedObject = new ExposedObject("test-input-actions-no-change", exposedClass, _controller);
            try
            {
                ExposedPropertyUtility.SetDefault(exposedObject);

                var json = ExposedSceneSerializer.SceneToJson(
                    new System.Collections.Generic.List<ExposedObject> { exposedObject },
                    DefaultExposedObjectResolver.Instance,
                    SerializeMode.Delta);

                StringAssert.DoesNotContain("\"settings\"", json);
                StringAssert.Contains("\"objects\": []", json);
            }
            finally
            {
                exposedObject.Unregister();
            }
        }
    }
}
