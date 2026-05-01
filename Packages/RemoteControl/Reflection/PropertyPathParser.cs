// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// パスセグメント構造体（GC alloc free）
    /// </summary>
    public ref struct PathSegment
    {
        public ReadOnlySpan<char> name;
        public int index;
        public bool isIndexed;
        public bool isError;

        public PathSegment(ReadOnlySpan<char> name)
        {
            this.name = name;
            this.index = 0;
            this.isIndexed = false;
            this.isError = false;
        }

        public PathSegment(int index)
        {
            this.name = ReadOnlySpan<char>.Empty;
            this.index = index;
            this.isIndexed = true;
            this.isError = false;
        }

        public static PathSegment Error => new PathSegment { isError = true };
    }

    /// <summary>
    /// DotBracket形式のプロパティパスを解析するEnumerator
    /// 例: "components[0].value" → "components", [0], "value"
    /// </summary>
    public ref struct PathSegmentEnumerator
    {
        private readonly ReadOnlySpan<char> _path;
        private int _position;
        private PathSegment _current;

        public PathSegmentEnumerator(ReadOnlySpan<char> path)
        {
            _path = path;
            _position = 0;
            _current = default;
        }

        public PathSegment Current => _current;

        public bool MoveNext()
        {
            if (_position >= _path.Length) return false;

            // 先頭の '.' をスキップ
            if (_path[_position] == '.')
            {
                _position++;
                if (_position >= _path.Length) return false;
            }

            // '[' で始まる場合は配列インデックス
            if (_path[_position] == '[')
            {
                int bracketEnd = _path.Slice(_position).IndexOf(']');
                if (bracketEnd < 0)
                {
                    Debug.LogWarning("[Reflection] Invalid path format: missing closing bracket");
                    _current = PathSegment.Error;
                    return true;
                }

                var indexPart = _path.Slice(_position + 1, bracketEnd - 1);
                if (_TryParseInt(indexPart, out int indexValue))
                {
                    _current = new PathSegment(indexValue);
                    _position += bracketEnd + 1;
                    return true;
                }
                else
                {
                    Debug.LogWarning("[Reflection] Invalid index in path");
                    _current = PathSegment.Error;
                    return true;
                }
            }

            // プロパティ名を解析（'.' または '[' まで）
            int start = _position;
            while (_position < _path.Length)
            {
                char c = _path[_position];
                if (c == '.' || c == '[')
                {
                    break;
                }
                _position++;
            }

            var namePart = _path.Slice(start, _position - start);
            if (namePart.IsEmpty)
            {
                return MoveNext(); // 空のセグメントはスキップ
            }

            _current = new PathSegment(namePart);
            return true;
        }

        public PathSegmentEnumerator GetEnumerator() => this;

        private static bool _TryParseInt(ReadOnlySpan<char> span, out int result)
        {
            result = 0;
            if (span.IsEmpty) return false;

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] < '0' || span[i] > '9') return false;
                result = result * 10 + (span[i] - '0');
            }
            return true;
        }
    }

    /// <summary>
    /// プロパティパスパーサー（GC alloc free）
    /// </summary>
    public static class PropertyPathParser
    {
        /// <summary>
        /// プロパティパスをパースしてセグメントを列挙する
        /// </summary>
        public static PathSegmentEnumerator Parse(ReadOnlySpan<char> propertyPath)
        {
            return new PathSegmentEnumerator(propertyPath);
        }

        /// <summary>
        /// プロパティパスからルートプロパティ名を取得（"components[0].value" → "components"）
        /// GC回避のためReadOnlySpanを返す
        /// </summary>
        public static ReadOnlySpan<char> GetRootName(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return ReadOnlySpan<char>.Empty;

            // '.' または '[' の最初の出現位置を探す
            int dotIndex = propertyPath.IndexOf('.');
            int bracketIndex = propertyPath.IndexOf('[');

            if (dotIndex < 0 && bracketIndex < 0)
            {
                return propertyPath.AsSpan();
            }
            if (dotIndex < 0)
            {
                return propertyPath.AsSpan(0, bracketIndex);
            }
            if (bracketIndex < 0)
            {
                return propertyPath.AsSpan(0, dotIndex);
            }

            return propertyPath.AsSpan(0, Math.Min(dotIndex, bracketIndex));
        }
    }
}
