using System;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 永続化メンバーの保存先を指定する。
    /// <see cref="Scene"/> はシーンファイル (*.live.json) に、<see cref="Project"/> は
    /// プロジェクト全体の設定ファイル ({projectPath}/Settings/{クラス名}.settings.json) に保存する。
    /// <see cref="Custom"/> はどちらにも書かず、所有者が自前のファイルへ書き出す。
    /// 既定は <see cref="Scene"/> (=0) のため、未指定時は従来どおりシーンに保存される。
    /// </summary>
    public enum PersistScope
    {
        Scene = 0,
        Project = 1,

        /// <summary>
        /// The owner persists these members into its own file, so neither the live scene nor the
        /// project settings write them. Serialization is still available — the owner asks for this
        /// scope explicitly (see <c>LiveObjectSnapshot.Capture(handle, scope)</c>) — but the built-in
        /// writers (scene / project settings) and the dirty comparison, which both filter on
        /// <see cref="Scene"/> or <see cref="Project"/>, skip them. Such an owner is therefore
        /// responsible for its own save and unsaved-state reporting; see
        /// <c>LiveSceneSaveSystem.onAfterSave</c> / <c>unsavedChangesProbe</c>.
        /// Deserialization ignores the scope, so a legacy file that still holds these members inline
        /// keeps restoring them.
        /// </summary>
        Custom = 2,
    }

    /// <summary>
    /// structのカスタムデフォルト値を提供するstaticプロパティに付与するAttribute。
    /// </summary>
    /// <example>
    /// public struct MyData
    /// {
    ///     public float value;
    ///
    ///     [LiveDefault]
    ///     public static MyData Default => new MyData { value = 1.0f };
    /// }
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class LiveDefaultAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class LiveClassAttribute : Attribute
    {
        public string typeName { get; }

        /// <summary>
        /// オブジェクトの分類カテゴリ。RemoteApp側でフィルタリングに使用する。
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// RemoteApp側で表示するアイコン名。Material Iconsの名前を指定する。
        /// </summary>
        public string Icon { get; set; } = "deployed_code";

        /// <summary>
        /// trueを指定するとRemoteAppのシーンページ一覧から本型のオブジェクトを非表示にする。
        /// システム内部オブジェクトなど、ユーザに提示したくない登録型に使用する。
        /// </summary>
        public bool HideInScene { get; set; }

        public LiveClassAttribute()
        {
            this.typeName = null;
        }

        public LiveClassAttribute(string typeName)
        {
            this.typeName = typeName;
        }
    }

    /// <summary>
    /// 旧名互換属性。リネーム前の typeName / フィールド名 / プロパティ名を宣言して、
    /// 旧名で書かれたシーンファイルからの復元を可能にする。Unity の FormerlySerializedAs と同等の役割。
    /// </summary>
    /// <example>
    /// [LiveClass("NewPlug")]
    /// [FormerlyNamedAs("OldPlug")]
    /// [FormerlyNamedAs("AncientPlug")]
    /// public class NewPlug : MonoBehaviour
    /// {
    ///     [LiveField]
    ///     [FormerlyNamedAs("oldCount")]
    ///     public int count;
    /// }
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class FormerlyNamedAsAttribute : Attribute
    {
        /// <summary>
        /// 旧名。クラスなら旧 typeName、フィールド/プロパティなら旧メンバー名。
        /// </summary>
        public string name { get; }

        public FormerlyNamedAsAttribute(string name)
        {
            this.name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false)]
    public class LiveEnumAttribute : Attribute
    {
        public string typeName { get; }

        public LiveEnumAttribute()
        {
            this.typeName = null;
        }

        public LiveEnumAttribute(string typeName)
        {
            this.typeName = typeName;
        }
    }

    /// <summary>
    /// Declares an external enum (one you cannot decorate with <see cref="LiveEnumAttribute"/>,
    /// e.g. a UnityEngine built-in like <c>HumanBodyBones</c>) so it is registered as an
    /// <see cref="LiveEnum"/> and shows as a dropdown in the remote app. Applied at assembly level;
    /// picked up by <c>LiveEnum.RegisterAllFromAttributes</c>, so it survives <c>LiveEnum.Reset()</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class LiveExternalEnumAttribute : Attribute
    {
        public Type type { get; }
        public string typeName { get; }

        /// <summary>Enum member names to omit from the exposed values (e.g. count sentinels like
        /// <c>HumanBodyBones.LastBone</c>). Set via named argument.</summary>
        public string[] excludeNames { get; set; }

        public LiveExternalEnumAttribute(Type type, string typeName = null)
        {
            this.type = type;
            this.typeName = typeName;
        }
    }

    // TODO: 実験中
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class KeyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class LivePropertyAttribute : Attribute
    {
        public string name { get; }

        /// <summary>
        /// RemoteApp側で表示するラベル。翻訳キーまたは表示文字列を指定する。
        /// 未設定時はRemoteApp側でプロパティ名から自動生成される。
        /// </summary>
        public string label { get; set; }

        /// <summary>
        /// RemoteApp側での表示順。負で前、正で後ろ、同値は宣言順。既定 0。
        /// 派生クラスで基底プロパティより前に表示したい場合などに使用する。
        /// </summary>
        public int order { get; set; } = 0;

        /// <summary>
        /// 永続化先。既定は <see cref="PersistScope.Scene"/>。
        /// プロパティが実際に保存されるのは shadow field とのペアまたは [InlineReference] で
        /// persistable になっている場合のみで、その場合に本値が保存先を決める
        /// (shadow field を持つ場合は field 側の persistScope が優先される)。
        /// </summary>
        public PersistScope persistScope { get; set; } = PersistScope.Scene;

        public LivePropertyAttribute()
        {
            this.name = null;
        }

        public LivePropertyAttribute(string name)
        {
            this.name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class LiveFieldAttribute : Attribute
    {
        public string name { get; }

        /// <summary>
        /// RemoteApp側で表示するラベル。翻訳キーまたは表示文字列を指定する。
        /// 未設定時はRemoteApp側でプロパティ名から自動生成される。
        /// </summary>
        public string label { get; set; }

        /// <summary>
        /// JSON永続化の対象にするかどうか。既定は true。
        /// false の場合、RemoteApp には公開されるが scene/studio.json には書き出されない。
        /// </summary>
        public bool persistable { get; set; } = true;

        /// <summary>
        /// RemoteApp側での表示順。負で前、正で後ろ、同値は宣言順。既定 0。
        /// 派生クラスで基底プロパティより前に表示したい場合などに使用する。
        /// </summary>
        public int order { get; set; } = 0;

        /// <summary>
        /// 永続化先。既定は <see cref="PersistScope.Scene"/>。
        /// <see cref="PersistScope.Project"/> を指定すると、このフィールドはシーンファイルではなく
        /// プロジェクト全体の設定ファイル ({クラス名}.settings.json) に保存される。
        /// shadow field として使う場合、ペアとなる property の保存先も本値を継承する。
        /// </summary>
        public PersistScope persistScope { get; set; } = PersistScope.Scene;

        public LiveFieldAttribute()
        {
            this.name = null;
        }

        public LiveFieldAttribute(string name)
        {
            this.name = name;
        }
    }

    /// <summary>
    /// Marks a <c>string</c> [LiveField]/[LiveProperty] whose value is itself a JSON document.
    /// The serializer embeds the parsed JSON inline (as an object/array) instead of writing it as an
    /// escaped string, and re-stringifies it on read, so a JSON-in-JSON blob is stored readably and is
    /// not double-encoded. Opt-in: untagged members are unaffected.
    ///
    /// Backward compatible on read: a legacy file that stored the value as a JSON string still
    /// deserializes (the string is taken verbatim). A value that is not valid JSON falls back to plain
    /// string serialization. Only meaningful on <c>string</c>-typed members.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class RawJsonAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks the [LiveProperty]/[LiveField] of an array/list element type that serves as its
    /// stable identity. A property path may then address an element by this key instead of by index
    /// (e.g. "expressions[Joy].weight"), so a binding survives reordering of the collection.
    ///
    /// Exactly one member per element type should be tagged. The key value is compared as its
    /// invariant-culture string form. Key addressing only resolves against reference-type elements
    /// (writes to a value-type element would hit a boxed copy), matching the existing array-element
    /// write rules.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class LiveKeyAttribute : Attribute
    {
    }

    /// <summary>
    /// RemoteApp 側で配列・ネスト構造体を初期状態で折りたたんで表示させるヒント属性。
    /// 展開トグル自体は残り、ユーザーが手動で開ける。controller とは独立した
    /// メタデータ (collapsed) として emit される。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class CollapsedAttribute : Attribute
    {
    }


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class LiveFunctionAttribute : Attribute
    {
        public string name { get; }

        /// <summary>
        /// RemoteApp側で表示するラベル。翻訳キーまたは表示文字列を指定する。
        /// 未設定時はRemoteApp側で関数名から自動生成される。
        /// </summary>
        public string label { get; set; }

        /// <summary>
        /// RemoteApp側での表示順。負で前、正で後ろ、同値は宣言順。既定 0。
        /// </summary>
        public int order { get; set; } = 0;

        /// <summary>
        /// RemoteApp のボタンに表示するアイコン名 (Material Icons)。未設定ならアイコンなし。
        /// </summary>
        public string icon { get; set; }

        public LiveFunctionAttribute()
        {
            this.name = null;
        }

        public LiveFunctionAttribute(string name)
        {
            this.name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Parameter, AllowMultiple = false)]
    public class HelpAttribute : Attribute
    {
        public string text { get; }


        public HelpAttribute()
        {
            this.text = null;
        }

        public HelpAttribute(string text)
        {
            this.text = text;
        }
    }


    // TODO: 実験中
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class SelectedItemsAttribute : Attribute
    {
        public string selectionListName { get; }

        public SelectedItemsAttribute(string selectionListName = null)
        {
            this.selectionListName = selectionListName;
        }
    }

    public class ControlAttribute : Attribute
    {
        public string controlName { get; }

        public ControlAttribute(string controlName)
        {
            this.controlName = controlName;
        }

        public virtual JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = "Default",
            };
        }
    }

    /// <summary>
    /// RemoteApp側で非表示にするコントローラー属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    public class HideAttribute : ControlAttribute
    {
        public HideAttribute() : base("Hide") { }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
            };
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class SliderAttribute : ControlAttribute
    {
        public float minValue { get; }

        public float maxValue { get; }

        public float stepValue { get; set; }

        public SliderAttribute(float minValue = 0f, float maxValue = 1f, float stepValue = 0f)
            : base("Slider")
        {
            this.minValue = minValue;
            this.maxValue = maxValue;
            if (stepValue == 0f)
            {
                stepValue = (maxValue - minValue) / 20f;
            }
            this.stepValue = stepValue;
        }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
                ["minValue"] = minValue,
                ["maxValue"] = maxValue,
                ["step"] = stepValue,
            };
        }
    }

    /// <summary>
    /// カメラコントローラーを表示するコントローラー属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class CameraControllerAttribute : ControlAttribute
    {
        public string[] options { get; internal set; }

        public CameraControllerAttribute()
            : base("CameraController")
        {
            this.options = Array.Empty<string>();
        }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
                ["options"] = new JArray(options),
            };
        }
    }

    /// <summary>
    /// SerializeReference型のフィールドで派生型を選択するコントローラー属性。
    /// optionsは LivePropertyType コンストラクタで自動計算される。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class TypeSelectorAttribute : ControlAttribute
    {
        public string[] options { get; internal set; }

        public TypeSelectorAttribute() : base("TypeSelector")
        {
            this.options = Array.Empty<string>();
        }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
                ["options"] = new JArray(options),
            };
        }
    }

    /// <summary>
    /// 限られたstring配列から選択するコントローラー属性。
    /// sourcePropertyNameには選択肢となるstring[]を返すプロパティ名を指定する。
    /// Parameter にも付与でき、LiveFunction の引数入力をドロップダウン化する
    /// (選択肢は関数が属するオブジェクトの sourcePropertyName プロパティから取得)。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false)]
    public class StringSelectorAttribute : ControlAttribute
    {
        public string sourcePropertyName { get; }

        public StringSelectorAttribute(string sourcePropertyName)
            : base("StringSelector")
        {
            this.sourcePropertyName = sourcePropertyName;
        }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
                ["sourceProperty"] = sourcePropertyName,
            };
        }
    }

    /// <summary>
    /// Control attribute that renders a live image preview.
    /// The property value is a server-relative image URL path (e.g. "/api/camera/image").
    /// The client polls the URL while the control is visible and renders the response image.
    /// The property is read-only on the client.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class ImagePreviewAttribute : ControlAttribute
    {
        /// <summary>Polling interval in milliseconds. 0 uses the client default.</summary>
        public int intervalMs { get; }

        public ImagePreviewAttribute(int intervalMs = 0)
            : base("ImagePreview")
        {
            this.intervalMs = intervalMs;
        }

        public override JObject ToJObject()
        {
            var obj = new JObject
            {
                ["type"] = controlName,
            };
            if (intervalMs > 0)
            {
                obj["intervalMs"] = intervalMs;
            }
            return obj;
        }
    }

    /// <summary>
    /// Control attribute for an <see cref="LiveFunctionAttribute"/> that must not fire the instant the
    /// button is pressed. The client opens a modal, counts down, and only then invokes the function, so the
    /// operator has time to get into position (calibration, staged captures, and similar).
    ///
    /// The countdown is a client-side presentation concern: the server still exposes a plain function and
    /// receives a single ordinary invocation once the countdown reaches zero. Cancelling never reaches
    /// the server.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CountdownAttribute : ControlAttribute
    {
        /// <summary>Countdown length in seconds. 0 defers to the client's configured default.</summary>
        public int seconds { get; }

        /// <summary>Localization key for the instruction shown while counting down.</summary>
        public string message { get; set; }

        /// <summary>Localization key for the text shown while the invocation is in flight.</summary>
        public string runningMessage { get; set; }

        /// <summary>Material Icons name shown in the modal. Falls back to a client default when unset.</summary>
        public string icon { get; set; }

        public CountdownAttribute(int seconds = 0)
            : base("Countdown")
        {
            this.seconds = seconds;
        }

        // message / runningMessage は表示文字列なので、他の翻訳対象 (label / help / section title) と同じく
        // ここで解決してから送る。LocalizationSystem は同一アセンブリ内にある。
        public override JObject ToJObject()
        {
            var obj = new JObject
            {
                ["type"] = controlName,
            };
            if (seconds > 0) obj["seconds"] = seconds;
            if (!string.IsNullOrEmpty(message)) obj["message"] = LocalizationSystem.Translate(message);
            if (!string.IsNullOrEmpty(runningMessage)) obj["runningMessage"] = LocalizationSystem.Translate(runningMessage);
            if (!string.IsNullOrEmpty(icon)) obj["icon"] = icon;
            return obj;
        }
    }

    /// <summary>
    /// Control attribute that renders a read-only <c>string</c> member as a button which opens its value
    /// as a URL in the operator's external browser. Use it for documentation and support links so the page
    /// can declare them instead of the client hard-coding them.
    ///
    /// The value is the URL itself; the button caption comes from the member's label. Return a
    /// <see cref="LocalizationSystem.Translate"/>d key from the getter when the destination differs per language.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class UrlButtonAttribute : ControlAttribute
    {
        /// <summary>Material Icons name shown on the button.</summary>
        public string icon { get; set; } = "open_in_new";

        public UrlButtonAttribute() : base("UrlButton") { }

        public override JObject ToJObject()
        {
            var obj = new JObject
            {
                ["type"] = controlName,
            };
            if (!string.IsNullOrEmpty(icon)) obj["icon"] = icon;
            return obj;
        }
    }

    /// <summary>
    /// アセット参照フィールド (AnimationClip 等の UnityEngine.Object 派生) 用のセレクタ属性。
    /// 値はアセット GUID (<see cref="AssetRegistry"/> 経由) でシリアライズされるため、
    /// 対象アセットは AssetRegistry に登録されている必要がある。
    /// sourcePropertyName には候補 GUID の string[] を返すプロパティ名を指定する
    /// (先頭の空文字は「未選択」を表す)。クライアントは各 GUID の表示名と型を
    /// <c>GET /api/asset?guid=...</c> で解決して表示する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class AssetSelectorAttribute : ControlAttribute
    {
        public string sourcePropertyName { get; }

        /// <summary>
        /// Optional sibling string property that stores an external asset reference key (a scheme-prefixed
        /// key such as LiveStudio's <c>file:&lt;path&gt;#&lt;clip&gt;</c>) which cannot be resolved to a
        /// live <see cref="UnityEngine.Object"/> synchronously at deserialize time (its bundle loads
        /// lazily). When set, the client routes a chosen external key here (and clears the Object field),
        /// while a baked in-app asset still writes the Object field's <c>{"@guid"}</c> (and clears this).
        /// The owner resolves the external key asynchronously so scene-restore survives the lazy load.
        /// </summary>
        public string refPropertyName { get; }

        public AssetSelectorAttribute(string sourcePropertyName = null, string refPropertyName = null)
            : base("AssetSelector")
        {
            this.sourcePropertyName = sourcePropertyName;
            this.refPropertyName = refPropertyName;
        }

        public override JObject ToJObject()
        {
            var obj = new JObject
            {
                ["type"] = controlName,
            };
            // Optional: the client sources its options from GET /api/assets?type=<assetType> (emitted
            // separately), so a fixed candidate-GUID sourceProperty is no longer required.
            if (!string.IsNullOrEmpty(sourcePropertyName)) obj["sourceProperty"] = sourcePropertyName;
            if (!string.IsNullOrEmpty(refPropertyName)) obj["refProperty"] = refPropertyName;
            return obj;
        }
    }

    /// <summary>
    /// UnityEngine.Object 派生フィールド用のセレクタ属性。
    /// フィールド型から自動的にシーン内の同型オブジェクトを列挙して RemoteApp にドロップダウンとして表示する。
    /// 候補名 (string[]) と現在値のインデックスを送出し、RemoteApp からはインデックス (int) を受け取って再列挙→代入する。
    /// options の計算は LivePropertySerializer 側で行うため、属性自身は controlName のみ保持する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class ObjectSelectorAttribute : ControlAttribute
    {
        public ObjectSelectorAttribute() : base("ObjectSelector") { }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
            };
        }
    }

    /// <summary>
    /// glTF系モデルファイル（.gltf / .glb / .vrm など）の選択UIを表示するコントローラー属性。
    /// RemoteApp側でファイル選択+プレビュー+ロード機能を提供する。
    /// VRMはglTFの拡張仕様のため、この属性で共通的に扱う。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class GLTFFileSelectorAttribute : ControlAttribute
    {
        public string[] extensions { get; }

        public GLTFFileSelectorAttribute(params string[] extensions) : base("GLTFFileSelector")
        {
            this.extensions = (extensions != null && extensions.Length > 0) ? extensions : new[] { "vrm" };
        }

        public override JObject ToJObject()
        {
            var extArray = new JArray();
            foreach (var ext in extensions)
            {
                extArray.Add(ext);
            }
            return new JObject
            {
                ["type"] = controlName,
                ["extensions"] = extArray,
            };
        }
    }

    /// <summary>
    /// 参照型プロパティ（LiveObject参照、または参照配列）に付与すると、
    /// RemoteApp側で新ペーン遷移ではなくインライン展開表示になる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class InlineReferenceAttribute : ControlAttribute
    {
        public InlineReferenceAttribute() : base("InlineReference") { }

        public override JObject ToJObject()
        {
            return new JObject
            {
                ["type"] = controlName,
            };
        }
    }

    [Obsolete("Use LiveDefaultAttribute instead.")]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class DefaultValueAttribute : Attribute
    {
        public object value { get; }

        public DefaultValueAttribute(object value)
        {
            this.value = value;
        }

        public DefaultValueAttribute(bool value)
        {
            this.value = value;
        }

        public DefaultValueAttribute(int value)
        {
            this.value = value;
        }

        public DefaultValueAttribute(float value)
        {
            this.value = value;
        }

        public DefaultValueAttribute(double value)
        {
            this.value = value;
        }

        public DefaultValueAttribute(string value)
        {
            this.value = value;
        }
    }

    /// <summary>
    /// 指定プロパティが特定の値の場合にのみ表示する条件属性。
    /// RemoteApp側で動的に表示/非表示を判定する。
    /// 複数付与でき (ShowIf/HideIf 混在可)、その場合は全条件を AND で評価する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true)]
    public class ShowIfAttribute : Attribute
    {
        public string propertyName { get; }
        public object value { get; }

        public ShowIfAttribute(string propertyName, bool value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public ShowIfAttribute(string propertyName, int value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public ShowIfAttribute(string propertyName, float value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public ShowIfAttribute(string propertyName, string value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }
    }

    /// <summary>
    /// 指定プロパティが特定の値の場合に非表示にする条件属性。
    /// RemoteApp側で動的に表示/非表示を判定する。
    /// 複数付与でき (ShowIf/HideIf 混在可)、その場合は全条件を AND で評価する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true)]
    public class HideIfAttribute : Attribute
    {
        public string propertyName { get; }
        public object value { get; }

        public HideIfAttribute(string propertyName, bool value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public HideIfAttribute(string propertyName, int value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public HideIfAttribute(string propertyName, float value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }

        public HideIfAttribute(string propertyName, string value)
        {
            this.propertyName = propertyName;
            this.value = value;
        }
    }

    /// <summary>
    /// SerializeReference属性のフィールドで派生クラスをドロップダウンで選択可能にするAttribute。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SelectAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// メンバーを「実験的機能」としてマークする属性。
    /// <see cref="SectionAttribute"/> と併用するとセクションの <c>accessLevel</c> を
    /// <see cref="AccessLevel.Experimental"/> に上書きする。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    public class ExperimentalAttribute : Attribute
    {
    }

    /// <summary>
    /// メンバーを「開発専用」としてマークする属性。
    /// <see cref="SectionAttribute"/> と併用するとセクションの <c>accessLevel</c> を
    /// <see cref="AccessLevel.Development"/> に上書きする。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    public class DevelopmentAttribute : Attribute
    {
    }

    /// <summary>
    /// メンバーをセクションの開始としてマークする属性。
    /// NavigatePageでのセクション表示に使用される。
    /// 次のSectionAttributeが出現するまで、後続メンバーは同じセクションに属する。
    /// 関数にも付与できるので、ボタンだけで構成されるセクションも宣言できる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    public class SectionAttribute : Attribute
    {
        /// <summary>
        /// Material Iconsのアイコン名
        /// </summary>
        public string icon { get; }

        /// <summary>
        /// セクションのタイトル（翻訳キーまたは表示文字列）
        /// </summary>
        public string title { get; }

        /// <summary>
        /// セクションのサブタイトル（翻訳キーまたは表示文字列）
        /// </summary>
        public string subtitle { get; }

        /// <summary>
        /// セクションのアクセスレベル。RemoteApp 側でバッジ表示や表示制御に使用される。
        /// </summary>
        public AccessLevel accessLevel { get; set; } = AccessLevel.Public;

        public SectionAttribute(string icon, string title, string subtitle = null)
        {
            this.icon = icon;
            this.title = title;
            this.subtitle = subtitle;
        }
    }

    /// <summary>
    /// Flow direction of a <see cref="LayoutAttribute"/> group.
    /// <see cref="Auto"/> resolves from the group's nesting depth: a section's members flow
    /// vertically by default, so depth 1 becomes <see cref="Horizontal"/>, depth 2
    /// <see cref="Vertical"/>, and so on alternately.
    /// </summary>
    public enum LayoutDirection
    {
        Auto = 0,
        Vertical = 1,
        Horizontal = 2,
    }

    /// <summary>
    /// Groups consecutive members into a layout container so the remote client can arrange them
    /// vertically or horizontally instead of stacking everything in one column.
    ///
    /// Groups are addressed by a <c>/</c>-separated <see cref="path"/>, so nesting needs no
    /// begin/end markers: a two-column row is just <c>[Layout("row/left")]</c> on one set of members
    /// and <c>[Layout("row/right")]</c> on the other. Members that share a path render inside the
    /// same container, in declaration order; a member without the attribute stays at the section root.
    ///
    /// A group's container is created the first time a member references it, so grouped and
    /// ungrouped members keep their relative declaration order.
    /// </summary>
    /// <example>
    /// [Section("videocam", "Camera")]
    /// [LiveProperty, ImagePreview]
    /// [Layout("row/right")]
    /// public static string cameraPreview => "/api/camera/image";
    ///
    /// [LiveProperty]
    /// [Layout("row/left")]
    /// public static string webcamDevice { get; set; }
    /// </example>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    public class LayoutAttribute : Attribute
    {
        /// <summary>
        /// Group path. <c>/</c> separates nesting levels (e.g. <c>"row/left"</c>).
        /// Empty or malformed paths are ignored (the member falls back to the section root).
        /// </summary>
        public string path { get; }

        /// <summary>
        /// Flow direction of the group this member belongs to (the last path segment).
        /// Defaults to <see cref="LayoutDirection.Auto"/>, which alternates by depth.
        /// The first member that declares a direction for a path wins.
        /// </summary>
        public LayoutDirection direction { get; set; } = LayoutDirection.Auto;

        /// <summary>
        /// Lay the group out as a grid of this many columns. 0 (default) keeps the plain flow layout.
        /// </summary>
        public int columns { get; set; }

        /// <summary>
        /// Relative weight when this group shares a horizontal parent (CSS <c>flex-grow</c>).
        /// Default 1; 0 makes the group keep its content width instead of stretching.
        /// </summary>
        public int grow { get; set; } = 1;

        public LayoutAttribute(string path)
        {
            this.path = path;
        }
    }

}