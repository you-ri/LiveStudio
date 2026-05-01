using System;
using System.Text;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// プロパティパスを抽象化（内部形式: DotBracket）
    /// Unity.Properties との互換性を確保するため、内部では DotBracket 形式を使用する。
    /// REST API 層でのみ Slash 形式との変換を行う。
    /// </summary>
    public readonly struct PropertyPath : IEquatable<PropertyPath>
    {
        // 内部は常にDotBracket形式で保持
        private readonly string _dotBracket;

        /// <summary>
        /// DotBracket形式でパスを作成
        /// </summary>
        /// <param name="dotBracketPath">DotBracket形式のパス（例: "components[0].value"）</param>
        public PropertyPath(string dotBracketPath)
        {
            _dotBracket = dotBracketPath ?? string.Empty;
        }

        /// <summary>
        /// DotBracket形式（内部形式）
        /// 例: "components[0].value"
        /// </summary>
        public string Value => _dotBracket ?? string.Empty;

        /// <summary>
        /// パスが空かどうか
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(_dotBracket);

        /// <summary>
        /// REST API用のSlash形式に変換
        /// 例: "components/0/value"
        /// </summary>
        public string ToSlash() => ConvertToSlash(_dotBracket);

        /// <summary>
        /// Unity.Properties.PropertyPath に変換
        /// </summary>
        public Unity.Properties.PropertyPath ToUnityPropertyPath()
        {
            return new Unity.Properties.PropertyPath(_dotBracket ?? string.Empty);
        }

        /// <summary>
        /// Slash形式から作成（REST API入力用）
        /// </summary>
        /// <param name="slashPath">Slash形式のパス（例: "components/0/value"）</param>
        public static PropertyPath FromSlash(string slashPath)
        {
            return new PropertyPath(ConvertFromSlash(slashPath));
        }

        /// <summary>
        /// Unity.Properties.PropertyPath から作成
        /// </summary>
        public static PropertyPath FromUnityPropertyPath(Unity.Properties.PropertyPath unityPath)
        {
            return new PropertyPath(unityPath.ToString());
        }

        /// <summary>
        /// 空のPropertyPath
        /// </summary>
        public static PropertyPath Empty => new PropertyPath(string.Empty);

        // Slash → DotBracket 変換
        // "components/0/value" → "components[0].value"
        private static string ConvertFromSlash(string slashPath)
        {
            if (string.IsNullOrEmpty(slashPath)) return string.Empty;

            var result = new StringBuilder();
            var segments = slashPath.Split('/');

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (string.IsNullOrEmpty(segment)) continue;

                if (int.TryParse(segment, out _))
                {
                    // 数値の場合はブラケット形式
                    result.Append('[').Append(segment).Append(']');
                }
                else
                {
                    // 名前の場合: 前にセグメントがあれば '.' を追加
                    if (result.Length > 0)
                    {
                        result.Append('.');
                    }
                    result.Append(segment);
                }
            }
            return result.ToString();
        }

        // DotBracket → Slash 変換
        // "components[0].value" → "components/0/value"
        private static string ConvertToSlash(string dotPath)
        {
            if (string.IsNullOrEmpty(dotPath)) return string.Empty;

            var result = new StringBuilder(dotPath.Length);

            for (int i = 0; i < dotPath.Length; i++)
            {
                char c = dotPath[i];

                if (c == '[')
                {
                    result.Append('/');
                }
                else if (c == ']')
                {
                    // スキップ
                }
                else if (c == '.')
                {
                    result.Append('/');
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        // 暗黙的変換（DotBracket形式のstring）
        public static implicit operator string(PropertyPath path) => path._dotBracket ?? string.Empty;
        public static implicit operator PropertyPath(string dotBracket) => new PropertyPath(dotBracket);

        public override string ToString() => _dotBracket ?? string.Empty;

        /// <summary>
        /// パスにセグメントを追加
        /// </summary>
        /// <param name="segment">追加するセグメント名</param>
        public PropertyPath Append(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return this;
            if (string.IsNullOrEmpty(_dotBracket)) return new PropertyPath(segment);
            return new PropertyPath($"{_dotBracket}.{segment}");
        }

        /// <summary>
        /// パスに配列インデックスを追加
        /// </summary>
        /// <param name="index">配列インデックス</param>
        public PropertyPath AppendIndex(int index)
        {
            if (string.IsNullOrEmpty(_dotBracket)) return new PropertyPath($"[{index}]");
            return new PropertyPath($"{_dotBracket}[{index}]");
        }

        /// <summary>
        /// ルートセグメント名を取得
        /// 例: "components[0].value" → "components"
        /// </summary>
        public string GetRootSegment()
        {
            if (string.IsNullOrEmpty(_dotBracket)) return string.Empty;

            int dotIndex = _dotBracket.IndexOf('.');
            int bracketIndex = _dotBracket.IndexOf('[');

            if (dotIndex < 0 && bracketIndex < 0) return _dotBracket;
            if (dotIndex < 0) return _dotBracket.Substring(0, bracketIndex);
            if (bracketIndex < 0) return _dotBracket.Substring(0, dotIndex);

            return _dotBracket.Substring(0, Math.Min(dotIndex, bracketIndex));
        }

        /// <summary>
        /// パスが指定されたプレフィックスで始まるかどうか
        /// </summary>
        public bool StartsWith(PropertyPath prefix)
        {
            if (string.IsNullOrEmpty(prefix._dotBracket)) return true;
            if (string.IsNullOrEmpty(_dotBracket)) return false;

            return _dotBracket.StartsWith(prefix._dotBracket, StringComparison.Ordinal);
        }

        /// <summary>
        /// 親パスを取得
        /// 例: "components[0].value" → "components[0]"
        ///     "components[0]" → "components"
        ///     "components" → ""
        /// </summary>
        public PropertyPath GetParent()
        {
            if (string.IsNullOrEmpty(_dotBracket)) return Empty;

            // 最後のセパレータを探す（'.' または '['）
            int lastDot = _dotBracket.LastIndexOf('.');
            int lastBracket = _dotBracket.LastIndexOf('[');

            int lastSeparator = Math.Max(lastDot, lastBracket);
            if (lastSeparator <= 0) return Empty;

            return new PropertyPath(_dotBracket.Substring(0, lastSeparator));
        }

        /// <summary>
        /// パスに親が存在するかどうか
        /// </summary>
        public bool HasParent()
        {
            if (string.IsNullOrEmpty(_dotBracket)) return false;
            return _dotBracket.IndexOf('.') > 0 || _dotBracket.IndexOf('[') > 0;
        }

        /// <summary>
        /// パスが指定されたプレフィックスで始まるかどうか（子パスとして）
        /// 例: "components[0].value".StartsWithChild("components[0]") → true
        ///     "components[0]value".StartsWithChild("components[0]") → false
        /// </summary>
        public bool StartsWithAsChild(PropertyPath parent)
        {
            if (string.IsNullOrEmpty(parent._dotBracket)) return true;
            if (string.IsNullOrEmpty(_dotBracket)) return false;

            if (!_dotBracket.StartsWith(parent._dotBracket, StringComparison.Ordinal))
                return false;

            // 完全一致の場合
            if (_dotBracket.Length == parent._dotBracket.Length)
                return true;

            // 次の文字が '.' または '[' であることを確認
            char nextChar = _dotBracket[parent._dotBracket.Length];
            return nextChar == '.' || nextChar == '[';
        }

        // IEquatable<PropertyPath> 実装
        public bool Equals(PropertyPath other)
        {
            return string.Equals(_dotBracket, other._dotBracket, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PropertyPath other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _dotBracket?.GetHashCode() ?? 0;
        }

        public static bool operator ==(PropertyPath left, PropertyPath right) => left.Equals(right);
        public static bool operator !=(PropertyPath left, PropertyPath right) => !left.Equals(right);
    }
}
