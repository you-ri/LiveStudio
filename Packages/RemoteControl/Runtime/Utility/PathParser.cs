using System;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// パス解析とパターンマッチング機能を提供するユーティリティクラス
    /// </summary>
    public static class PathParser
    {
        /// <summary>
        /// ワイルドカード（*）を使ったパターンマッチング
        /// * は0文字以上の任意の文字にマッチ
        /// ? は1文字の任意の文字にマッチ
        /// </summary>
        /// <param name="input">マッチング対象の文字列</param>
        /// <param name="pattern">パターン（*と?が使用可能）</param>
        /// <returns>マッチした場合true</returns>
        public static bool IsMatch(string input, string pattern)
        {
            if (input == null || pattern == null)
                return false;

            return IsMatchInternal(input, pattern, 0, 0);
        }

        private static bool IsMatchInternal(string input, string pattern, int inputIndex, int patternIndex)
        {
            // パターンの終端に到達
            if (patternIndex >= pattern.Length)
            {
                return inputIndex >= input.Length;
            }

            // 入力文字列の終端に到達
            if (inputIndex >= input.Length)
            {
                // 残りのパターンが全て*なら成功
                for (int i = patternIndex; i < pattern.Length; i++)
                {
                    if (pattern[i] != '*')
                        return false;
                }
                return true;
            }

            char currentPattern = pattern[patternIndex];
            char currentInput = input[inputIndex];

            switch (currentPattern)
            {
                case '*':
                    // *は0文字以上にマッチ
                    // 0文字の場合: 次のパターンに進む
                    if (IsMatchInternal(input, pattern, inputIndex, patternIndex + 1))
                        return true;

                    // 1文字以上の場合: 入力を1文字進める
                    return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex);

                case '?':
                    // ?は1文字にマッチ
                    return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex + 1);

                default:
                    // 通常文字は完全一致
                    if (currentInput == currentPattern)
                    {
                        return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex + 1);
                    }
                    return false;
            }
        }

        /// <summary>
        /// 大文字小文字を無視したマッチング
        /// </summary>
        public static bool IsMatchIgnoreCase(string input, string pattern)
        {
            if (input == null || pattern == null)
                return false;

            return IsMatch(input.ToLowerInvariant(), pattern.ToLowerInvariant());
        }

        /// <summary>
        /// URLパスから指定したインデックスのセグメントを取得
        /// </summary>
        /// <param name="path">URLパス（例: "/exposed/object/123/property/name"）</param>
        /// <param name="index">取得するセグメントのインデックス（0から開始）</param>
        /// <returns>指定したインデックスのセグメント（存在しない場合はnull）</returns>
        public static string GetPathSegment(string path, int index)
        {
            if (string.IsNullOrEmpty(path) || index < 0)
                return null;

            var parts = path.Trim('/').Split('/');
            return index < parts.Length ? Uri.UnescapeDataString(parts[index]) : null;
        }

        /// <summary>
        /// URLパスから指定したインデックス以降のすべてのセグメントを取得
        /// </summary>
        /// <param name="path">URLパス</param>
        /// <param name="fromIndex">開始インデックス（0から開始）</param>
        /// <param name="separator">結合に使用する区切り文字（デフォルト: "/"）</param>
        /// <returns>指定したインデックス以降のセグメントを結合した文字列</returns>
        public static string GetPathSegmentFrom(string path, int fromIndex, string separator = "/")
        {
            if (string.IsNullOrEmpty(path) || fromIndex < 0)
                return null;

            var parts = path.Trim('/').Split('/');
            if (fromIndex >= parts.Length)
                return null;

            var segments = new string[parts.Length - fromIndex];
            for (int i = fromIndex; i < parts.Length; i++)
            {
                segments[i - fromIndex] = Uri.UnescapeDataString(parts[i]);
            }

            return string.Join(separator, segments);
        }
    }
}