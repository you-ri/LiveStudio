// Copyright (c) You-Ri, 2026

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Scripting;

using Lilium.RemoteControl;

using GameObjectUtility = Lilium.RemoteControl.GameObjectUtility;

namespace Lilium.LiveStudio
{
    [ExposedClass]
    [Serializable]
    public class MeshState
    {
        [ExposedField, StringSelector(nameof(AvatarController.meshPaths))]
        public string name;

        [ExposedField]
        public bool visible = true;

        public SkinnedMeshRenderer skinnedMeshRenderer;

        public MeshFilter filter;

        public bool defaultVisible = true;
    }

    [ExposedClass]
    [Serializable]
    public class AnimationParameterOverride
    {
        [ExposedField, StringSelector(nameof(AvatarController.animationParameters))]
        public string name;

        // パラメータ名から自動判別された型。ShowIf がこのフィールドを参照して値フィールドの表示を切り替える。
        [ExposedField, Hide]
        public AnimatorControllerParameterType type;

        [ExposedField, ShowIf(nameof(type), (int)AnimatorControllerParameterType.Float)]
        public float floatValue;

        [ExposedField, ShowIf(nameof(type), (int)AnimatorControllerParameterType.Int)]
        public int intValue;

        [ExposedField, ShowIf(nameof(type), (int)AnimatorControllerParameterType.Bool)]
        public bool boolValue;

        [NonSerialized] public float defaultFloat;
        [NonSerialized] public int defaultInt;
        [NonSerialized] public bool defaultBool;
        [NonSerialized] public bool resolved;
    }

    [DefaultExecutionOrder(200)]
    [ExposedClass("Avatar", Category = "Avatar", Icon = "person")]
    public class AvatarController : MonoBehaviour, IAvatarService
    {
        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        public GameObject target => _target;

        public event Action onAvatarChanged;

        GameObject _target;

        [SerializeField]
        GameObject _defaultAvatarPrefab;

        [SerializeField]
        //[ExposedField(label = "AVATAR_RECEIVER"), ObjectSelector]
        MotionSourceBase _motionSource;

        public MotionSourceBase motionSource => _motionSource;

        [ExposedProperty, Hide]
        public string[] meshPaths
        {
            get
            {
                if (_target == null) return Array.Empty<string>();
                return _target.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true)
                    .Select(renderer => GameObjectUtility.GetRelativePath(_target.transform, renderer.transform))
                    .ToArray();
            }
        }


        [ExposedProperty, Hide]
        public string[] animationParameters
        {
            get
            {
                if (_target == null) return Array.Empty<string>();
                var animator = _target.GetComponent<Animator>();
                if (animator == null) return Array.Empty<string>();
                if (animator.runtimeAnimatorController == null) return Array.Empty<string>();
                return animator.parameters
                    .Where(param => param.type != AnimatorControllerParameterType.Trigger)
                    .Where(param => !animator.IsParameterControlledByCurve(param.nameHash))
                    .Select(param => param.name)
                    .ToArray();
            }
        }

        [SerializeField]
        [ExposedField(label="AVATAR_MESHSTATEOVERRIDES")]
        private MeshState[] meshStateOverrides = new MeshState[0];

        [SerializeField]
        [ExposedField(label="AVATAR_ANIMATIONPARAMETEROVERRIDES")]
        private AnimationParameterOverride[] animationParameterOverrides = new AnimationParameterOverride[0];

        [SerializeField]
        [ExposedField, Hide]
        [FormerlyExposedAs("_config")]
        private AvatarExpressionConfig _expressionConfig;

        public AvatarExpressionConfig config => _expressionConfig;

        // True when _expressionConfig points to a clone we created in Awake (vs. the original
        // asset reference). Guards OnDestroy so we never destroy the source asset.
        private bool _ownsExpressionConfig;

        IAvatarSource[] _avatarSources = Array.Empty<IAvatarSource>();


        void Awake()
        {
            // Clone the asset at Play start so runtime edits (RemoteApp, Inspector, etc.) do
            // not write back to disk. All subsequent SetExpressionConfig calls on swapped
            // avatars will receive this clone, keeping AvatarController the single source of
            // truth for expression configuration.
            if (_expressionConfig != null)
            {
                var sourceName = _expressionConfig.name;
                _expressionConfig = Instantiate(_expressionConfig);
                // Replace the trailing "(Clone)" appended by Instantiate with " (Instanced)" so
                // the clone is visibly distinguishable from the source asset in the Inspector
                // and any RemoteApp surface that displays the name.
                _expressionConfig.name = $"{sourceName} (Instanced)";
                _ownsExpressionConfig = true;
            }

            // Propagate the clone to the avatar already placed in the scene. Doing this in
            // Awake (AvatarController is [DefaultExecutionOrder(200)], IAvatar implementations
            // are [DefaultExecutionOrder(10)]) ensures the clone is in place before each
            // IAvatar.Start() runs expressionResolver.Setup(), so the very first Resolve()
            // reads from the clone rather than the source asset.
            var initialTarget = GetComponentInChildren<Animator>()?.gameObject;
            if (initialTarget != null)
            {
                initialTarget.GetComponent<IAvatar>()?.SetExpressionConfig(_expressionConfig);
            }
        }

        void OnEnable()
        {
            SelectableService<IAvatarService>.Register("current", this);
            SingletonService<IAvatarService>.Register(this);

            // アバターをこの AvatarController の位置・回転で配置し、移動にリアルタイム追従させる。
            if (_motionSource != null)
            {
                _motionSource.anchor = transform;
            }

            ExposedClass.Get<AvatarController>().onPropertyChanging += OnPropertyChanging;
            ExposedClass.Get<AvatarController>().onPropertyChanged += OnPropertyChanged;

            _avatarSources = GetComponents<IAvatarSource>();
            foreach (var source in _avatarSources)
            {
                source.onAvatarReady += _OnAvatarSourceReady;
            }
        }


        void OnDisable()
        {
            // 自分が設定した anchor のみ解除し、dangling 参照を残さない。
            if (_motionSource != null && _motionSource.anchor == transform)
            {
                _motionSource.anchor = null;
            }

            foreach (var source in _avatarSources)
            {
                source.onAvatarReady -= _OnAvatarSourceReady;
            }
            _avatarSources = Array.Empty<IAvatarSource>();

            ExposedClass.Get<AvatarController>().onPropertyChanging -= OnPropertyChanging;
            ExposedClass.Get<AvatarController>().onPropertyChanged -= OnPropertyChanged;
            SelectableService<IAvatarService>.Unregister("current", this);

            SingletonService<IAvatarService>.Unregister(this);

            // Notify dependents that this controller's hierarchy is no longer valid so they can
            // detach / clear their cached resolutions before the GameObject is torn down.
            TransformStructureService.NotifyStructureChanged(this.gameObject);
        }

        void Start()
        {
            _target = GetComponentInChildren<Animator>()?.gameObject;
            if (_target != null)
            {
                _PostSetupAvatar(_target);
                onAvatarChanged?.Invoke();
            }
        }

        void OnDestroy()
        {
            // Destroy the clone we created in Awake. The ownership flag prevents us from
            // touching the source asset when no clone was made (e.g. _expressionConfig was
            // null at Awake).
            if (_ownsExpressionConfig && _expressionConfig != null)
            {
                Destroy(_expressionConfig);
                _expressionConfig = null;
                _ownsExpressionConfig = false;
            }
        }

        void _OnAvatarSourceReady(GameObject newTarget)
        {
            if (_target != null)
            {
                ReleaseAvatar(_target);
                _target = null;
            }
            _ReplaceAvatar(newTarget);
        }

        public void SetupAvatar(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogError("[Studio] Avatar GameObject is null.");
                return;
            }

            var avatarTarget = avatar.GetComponent<Animator>();
            if (avatarTarget == null || avatarTarget.avatar == null)
            {
                Debug.LogError("[Studio] Avatar Animator or Avatar is null.");
                return;
            }

            var avatarComponent = avatar.GetComponent<IAvatar>();
            if (avatarComponent != null)
            {
                avatarComponent.BuildAvatar();
            }

            _PostSetupAvatar(avatar);
        }

        private void _PostSetupAvatar(GameObject avatar)
        {
            var avatarComponent = avatar.GetComponent<IAvatar>();
            if (avatarComponent != null)
            {
                avatarComponent.SetMotionSource(motionSource);
            }

            var avatarTransform = avatar.transform;
            var meshes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);

            var avatarTarget = avatar.GetComponent<Animator>();

            // Keep the avatar animating even when its renderers are off-screen / culled. The pose is
            // motion-driven (sockets, prop attachments, expression) and must stay correct regardless of
            // camera view, so disable Animator culling on every setup pass.
            if (avatarTarget != null) avatarTarget.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var avatarRotation = avatarTransform.rotation; // root rotation; independent of bone pose

            // Standard prop attachment sockets. Each is normalized to the avatar-root rotation so a
            // single prop offset works across models with different bone axis conventions.
            //
            // Sockets bake the bone-to-root orientation delta at creation time, so they must be created
            // from a pose that is identical across models; otherwise an A-pose import yields a different
            // socket frame than a T-pose import and a single prop offset cannot fit both. The avatar is
            // driven to its configured T-pose (stored per-bone in Avatar.humanDescription.skeleton, which
            // for VRChat / VRM avatars is an exact T-pose: arms horizontal and straight to the sides), the
            // sockets are created, then the original pose is restored.
            var restorePose = _ApplyConfiguredTPose(avatarTarget, avatarTransform);

            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.Hips, "Hips", avatarRotation);
            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.Spine, "Spine", avatarRotation);
            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.Chest, "Chest", avatarRotation);
            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.Head, "Head", avatarRotation);
            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.LeftHand, "LeftWrist", avatarRotation);
            _CreateSocketIfBonePresent(avatarTarget, HumanBodyBones.RightHand, "RightWrist", avatarRotation);

            _RestorePose(restorePose); // the motion driver re-poses next frame anyway, but avoid a 1-frame artifact

            foreach (var meshInfo in meshStateOverrides)
            {
                var prevSkinnedMeshRenderer = meshInfo.skinnedMeshRenderer;
                var renderer = meshes.FirstOrDefault(r => GameObjectUtility.GetRelativePath(avatarTransform, r.transform) == meshInfo.name);
                meshInfo.skinnedMeshRenderer = renderer;

                if (prevSkinnedMeshRenderer != renderer)
                {
                    meshInfo.defaultVisible = renderer != null ? renderer.gameObject.activeSelf : true;
                }

                if (meshInfo.skinnedMeshRenderer != null)
                {
                    meshInfo.skinnedMeshRenderer.gameObject.SetActive(meshInfo.visible);
                }
            }

            _ApplyAnimationParameterOverrides(avatar);

            // Notify TransformRef subscribers that the hierarchy under this GameObject may have
            // changed (avatar swap, BuildAvatar, etc.). Single notification point unifies the
            // Start path and _ReplaceAvatar path since both flow through here. The argument is
            // the GameObject whose name corresponds to TransformRef.ownerName ("Main Avatar" etc.).
            TransformStructureService.NotifyStructureChanged(this.gameObject);
        }

        // Create a normalized Socket on a humanoid bone, skipping optional bones the rig lacks
        // (e.g. Chest) so Socket.CreateSocket does not log a null-parent error. The caller is expected
        // to have driven the avatar to the canonical T-pose first, so the bone's current world rotation
        // is the T-pose orientation that CreateSocket bakes against.
        private static void _CreateSocketIfBonePresent(Animator animator, HumanBodyBones bone, string socketName, Quaternion referenceWorldRotation)
        {
            var boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform == null) return;
            Socket.CreateSocket(boneTransform, socketName, referenceWorldRotation);
        }

        // Force the avatar into its configured T-pose by writing each bone's local rotation from
        // Avatar.humanDescription.skeleton (the per-bone reference TRS captured when the avatar was built;
        // for VRChat / VRM avatars an exact T-pose with arms horizontal and straight to the sides). This is
        // exact and rig-independent, unlike a guessed muscle config. Only local rotations are written (bone
        // local positions are pose-invariant, so they are left untouched). A socket reads the world rotation
        // of its bone, which depends on the whole parent chain, so the pose is applied to every matching
        // transform under the avatar. The avatar root (referenceTransform) is skipped so the captured
        // reference rotation stays valid. Returns a snapshot for _RestorePose; null if no skeleton data.
        private static Dictionary<Transform, Quaternion> _ApplyConfiguredTPose(Animator animator, Transform referenceTransform)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isValid) return null;

            var skeleton = animator.avatar.humanDescription.skeleton;
            if (skeleton == null || skeleton.Length == 0) return null;

            var byName = new Dictionary<string, Quaternion>(skeleton.Length);
            foreach (var bone in skeleton)
            {
                if (!byName.ContainsKey(bone.name)) byName[bone.name] = bone.rotation;
            }

            var restore = new Dictionary<Transform, Quaternion>();
            var transforms = animator.transform.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var t in transforms)
            {
                if (t == referenceTransform) continue;
                if (byName.TryGetValue(t.name, out var rotation))
                {
                    restore[t] = t.localRotation;
                    t.localRotation = rotation;
                }
            }
            return restore;
        }

        // Restore the local rotations captured by _ApplyConfiguredTPose.
        private static void _RestorePose(Dictionary<Transform, Quaternion> restore)
        {
            if (restore == null) return;
            foreach (var kv in restore)
            {
                if (kv.Key != null) kv.Key.localRotation = kv.Value;
            }
        }

        private void _ApplyAnimationParameterOverrides(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null) return;
            if (animator.runtimeAnimatorController == null) return;

            var paramByName = animator.parameters
                .GroupBy(p => p.name)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var paramOverride in animationParameterOverrides)
            {
                if (paramOverride == null || string.IsNullOrEmpty(paramOverride.name)) continue;
                if (!paramByName.TryGetValue(paramOverride.name, out var param)) continue;
                if (param.type == AnimatorControllerParameterType.Trigger) continue;

                paramOverride.type = param.type;

                if (!paramOverride.resolved)
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            paramOverride.defaultFloat = animator.GetFloat(param.nameHash);
                            break;
                        case AnimatorControllerParameterType.Int:
                            paramOverride.defaultInt = animator.GetInteger(param.nameHash);
                            break;
                        case AnimatorControllerParameterType.Bool:
                            paramOverride.defaultBool = animator.GetBool(param.nameHash);
                            break;
                    }
                    paramOverride.resolved = true;
                }

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(param.nameHash, paramOverride.floatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(param.nameHash, paramOverride.intValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(param.nameHash, paramOverride.boolValue);
                        break;
                }
            }
        }

        public void ReleaseAvatar(GameObject avatar)
        {

            if (avatar != null)
            {
                // ユーザーが TransformRef("Main Avatar/Head" 等) でアバターのボーン配下に
                // アタッチした managed な ExposedGameObjectWithTransform 系子 GO を、
                // 破棄前に ControllerGO 直下に退避させる。退避中の hierarchy 変更で
                // user-set TransformRef が clobber されないよう suspendHierarchySync を立てる。
                // 新アバター生成後 _PostSetupAvatar 末尾の TransformStructureService.NotifyStructureChanged で再アタッチする。
                ExposedGameObjectWithTransform.suspendHierarchySync = true;
                try
                {
                    _RescueExposedManagedDescendants(avatar);
                    // Object.Destroy はフレーム末まで遅延されるため、階層に残ったままだと
                    // 直後に走る GetComponentsInChildren (TransformRef.Resolve など) が
                    // 破棄予定の Transform を拾い、フレーム末で参照が "Missing" になる。
                    // 親から切り離して以降の検索に乗らないようにする。
                    avatar.transform.SetParent(null, worldPositionStays: false);
                    GameObjectUtility.Destroy(avatar);
                }
                finally
                {
                    ExposedGameObjectWithTransform.suspendHierarchySync = false;
                }
            }
        }

        /// <summary>
        /// avatar の子孫の中から ExposedUnityObjectBase で wrap されている GO (= ユーザーが
        /// RemoteApp 経由で配置した prefab 等) を探し出し、avatar 破棄に巻き込まれないよう
        /// ControllerGO (this.transform) 直下に退避させる。
        /// 子も再帰的に救出されるが wrapper 単位で見れば最も外側のものだけ拾えば足りる。
        /// </summary>
        void _RescueExposedManagedDescendants(GameObject avatar)
        {
            if (avatar == null) return;
            var managed = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
            foreach (var instance in ExposedObjectRegistry.instances)
            {
                if (instance == null || !instance.isValid) continue;
                if (!(instance.target is ExposedUnityObjectBase proxy)) continue;
                var refObj = proxy.reference;
                if (refObj == null) continue;
                GameObject go = refObj as GameObject;
                if (go == null && refObj is Component comp) go = comp.gameObject;
                if (go == null) continue;
                managed.Add(go);
            }
            if (managed.Count == 0) return;

            // 走査中に SetParent するため一旦集める。subtree 重複も避けるためトップダウンで
            // 「最初に見つかった managed」だけ拾い、その配下の探索は止める。
            var rescue = new List<Transform>();
            var stack = new Stack<Transform>();
            for (int i = 0; i < avatar.transform.childCount; i++)
            {
                stack.Push(avatar.transform.GetChild(i));
            }
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null || t.gameObject == null) continue;
                if (managed.Contains(t.gameObject))
                {
                    rescue.Add(t);
                    continue; // 子はこの subtree ごと巻き取られる
                }
                for (int i = 0; i < t.childCount; i++)
                {
                    stack.Push(t.GetChild(i));
                }
            }

            for (int i = 0; i < rescue.Count; i++)
            {
                // TransformRef はユーザーがボーン配下の localPosition で配置する想定の API なので、
                // 退避〜再アタッチの間で保持すべきは local TRS の値そのもの。
                // worldPositionStays=true にすると退避時点で localPosition が recompute され、
                // 再アタッチ (TransformAttachment.Attach は worldPositionStays=false) で
                // 壊れた値がそのまま新ボーン下に持ち越されてしまう。
                rescue[i].SetParent(this.transform, worldPositionStays: false);
            }
        }

        sealed class ReferenceEqualityComparer : IEqualityComparer<GameObject>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public bool Equals(GameObject x, GameObject y) => ReferenceEquals(x, y);
            public int GetHashCode(GameObject obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }


        public void RequestLoad(string filepath)
        {
            var avatarSource = GetComponent<ExternalAvatarSource>();
            if (avatarSource == null)
            {
                Debug.LogError("[Studio] ExternalAvatarSource component is required to load an avatar.");
                return;
            }
            avatarSource.RequestLoad(filepath);
        }

        [ContextMenu("Reset Camera")]
        public void ResetCamera()
        {
            motionSource?.ResetCamera();
        }

        [ContextMenu("Reset Avatar")]
        public void ResetAvatar()
        {
            if (_target != null)
            {
                ReleaseAvatar(_target);
                _target = null;
            }
            if (_defaultAvatarPrefab != null)
            {
                // 外部アバター（ExternalAvatarSource）と同様に AvatarController GO 直下へ配置する。
                var newTarget = GameObjectUtility.CreateInstanceFromPrefab(_defaultAvatarPrefab, this.transform);
                _ReplaceAvatar(newTarget);
            }
        }

        private void _ReplaceAvatar(GameObject newTarget)
        {
#if VRMC_VRM10
            var setupSettings = LiveStudioProjectSettings.Instance?.vrmAvatarSetupSettings;
            if (setupSettings != null)
            {
                VRMAvatarSetupSystem.SetupVRMTargetAvatar(newTarget, setupSettings);
            }
            else
            {
                Debug.LogWarning("[Studio] VRMAvatarSetupSettings is not assigned in LiveStudioProjectSettings.");
            }
#endif
            if (newTarget != null)
            {
                newTarget.GetComponent<IAvatar>()?.SetExpressionConfig(_expressionConfig);
                _target = newTarget;
            }

            // SetupAvatar → _PostSetupAvatar 末尾で TransformStructureService.NotifyStructureChanged(this)
            // が走り、ReleaseAvatar でこの ControllerGO 直下に退避した managed な子 GO が、
            // 新しいアバターの対応するボーン (TransformRef で指定された "Main Avatar/Head" 等)
            // に自動で再アタッチされる。
            SetupAvatar(newTarget);

            // A runtime-loaded avatar applies its parameter overrides here (via the synchronous
            // _PostSetupAvatar above), but the avatar's own Start() runs one frame later and calls
            // AvatarBodyDriver.Initialize(), which creates the AnimatorControllerPlayable and resets
            // every parameter to the controller defaults — wiping what we just applied. Re-apply once
            // the playable exists. Avatars present from scene start are unaffected because execution
            // order (IAvatar=10 < AvatarController=200) already builds the playable before we apply.
            if (newTarget != null)
            {
                StartCoroutine(_ReapplyAnimationParameterOverridesNextFrame());
            }

            onAvatarChanged?.Invoke();
        }

        // Re-apply parameter overrides after the loaded avatar's PlayableGraph is built. Waiting one
        // frame guarantees the avatar's Start() (and thus AvatarBodyDriver.Initialize) has run.
        // resolved is intentionally left untouched so the defaults captured before any override (during
        // the earlier _PostSetupAvatar pass) are preserved; we only need to re-write the override values.
        IEnumerator _ReapplyAnimationParameterOverridesNextFrame()
        {
            yield return null;
            if (_target != null)
            {
                _ApplyAnimationParameterOverrides(_target);
            }
        }

        private void OnPropertyChanging(ExposedProperty property, object newValue)
        {
            // オーバーライドしているメッシュの表示状態を初期設定に戻す
            if (property.PathContains(nameof(meshStateOverrides)))
            {
                foreach (var meshInfo in meshStateOverrides)
                {
                    if (meshInfo.skinnedMeshRenderer != null)
                    {
                        meshInfo.skinnedMeshRenderer.gameObject.SetActive(meshInfo.defaultVisible);
                    }
                }
            }

            // オーバーライドしているAnimatorパラメータを初期値に戻す
            if (property.PathContains(nameof(animationParameterOverrides)) && _target != null)
            {
                var animator = _target.GetComponent<Animator>();
                if (animator != null)
                {
                    foreach (var paramOverride in animationParameterOverrides)
                    {
                        if (paramOverride == null || !paramOverride.resolved) continue;
                        switch (paramOverride.type)
                        {
                            case AnimatorControllerParameterType.Float:
                                animator.SetFloat(paramOverride.name, paramOverride.defaultFloat);
                                break;
                            case AnimatorControllerParameterType.Int:
                                animator.SetInteger(paramOverride.name, paramOverride.defaultInt);
                                break;
                            case AnimatorControllerParameterType.Bool:
                                animator.SetBool(paramOverride.name, paramOverride.defaultBool);
                                break;
                        }
                        paramOverride.resolved = false;
                    }
                }
            }
        }

        private void OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (_target != null)
            {
                _PostSetupAvatar(_target);
            }
        }

        [Preserve]
        [ExposedFunction, Hide]
        IEnumerable<ControlExpressionInfo> GetExpressionBindings()
        {
            if (_expressionConfig == null)
            {
                return Array.Empty<ControlExpressionInfo>();
            }
            var expressionKeys = ExpressionService.GetAvailableExpressions();
            return _expressionConfig.expressions.Where(exp => expressionKeys.Any(key => key.name == exp.name)).Select(t => new ControlExpressionInfo
            {
                name = t.name,
                bindings = InputActionService.FindInputAction("Expression." + t.name)?.bindings.Select(b =>
                    UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(b.effectivePath, UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.UseShortNames)
                ).ToArray()
            });
        }

        [Preserve]
        [ExposedFunction("getavailableexpressions"), Hide]
        IEnumerable<string> GetAvailableExpressions()
        {
            var expressionKeys = ExpressionService.GetAvailableExpressions();
            return expressionKeys.Select(t => t.name);
        }

        [Preserve]
        [ExposedFunction(label="AVATAR_RESETPHYSICS")]
        [ExposedHelp("AVATAR_RESETPHYSICS_HELP")]
        void ResetPhysics()
        {
            if (_target == null)
            {
                return;
            }

            _target.GetComponent<IAvatar>().ResetPhysics();
        }
    }
}
