using System;

namespace Lilium.RemoteControl.Utility
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

            return IsMatchInternal(input, pattern, 0, 0, ignoreCase: false);
        }

        private static bool IsMatchInternal(string input, string pattern, int inputIndex, int patternIndex, bool ignoreCase)
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
                    if (IsMatchInternal(input, pattern, inputIndex, patternIndex + 1, ignoreCase))
                        return true;

                    // 1文字以上の場合: 入力を1文字進める
                    return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex, ignoreCase);

                case '?':
                    // ?は1文字にマッチ
                    return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex + 1, ignoreCase);

                default:
                    // 通常文字は完全一致
                    if (_CharEquals(currentInput, currentPattern, ignoreCase))
                    {
                        return IsMatchInternal(input, pattern, inputIndex + 1, patternIndex + 1, ignoreCase);
                    }
                    return false;
            }
        }

        private static bool _CharEquals(char a, char b, bool ignoreCase)
        {
            if (a == b) return true;
            if (!ignoreCase) return false;
            return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
        }

        /// <summary>
        /// 大文字小文字を無視したマッチング
        /// </summary>
        public static bool IsMatchIgnoreCase(string input, string pattern)
        {
            if (input == null || pattern == null)
                return false;

            // 旧実装は input/pattern を ToLowerInvariant してから比較しており、ワイルドカード一致の
            // たびに一時文字列を 2 本確保していた。比較器側で 1 文字ずつ大小無視比較することで、
            // 判定結果は同一のままアロケーションを排除する (バッチ経路で 1 オペレーションあたり複数回呼ばれる)。
            return IsMatchInternal(input, pattern, 0, 0, ignoreCase: true);
        }

        /// <summary>
        /// URLパスから指定したインデックスのセグメントを取得
        /// </summary>
        /// <param name="path">URLパス（例: "/live/object/123/property/name"）</param>
        /// <param name="index">取得するセグメントのインデックス（0から開始）</param>
        /// <returns>指定したインデックスのセグメント（存在しない場合はnull）</returns>
        public static string GetPathSegment(string path, int index)
        {
            if (string.IsNullOrEmpty(path) || index < 0)
                return null;

            // Split はセグメントごとに部分文字列を、走査用に配列を確保する。対象セグメントの
            // 範囲だけを直接求め、部分文字列 1 本に絞る。URL エスケープ (%XX) を含む場合のみ
            // Uri.UnescapeDataString を通す (含まなければ復元は恒等変換なのでそのまま返す)。
            if (!_TryGetSegmentRange(path, index, out int start, out int length))
                return null;

            return _UnescapeSegment(path, start, length);
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

            // 高速経路: 既定区切り ("/") かつ対象範囲にエスケープが無ければ、元のパスの
            // 部分文字列がそのまま結合結果と一致する (Trim/Split/Join/Unescape をすべて回避)。
            // fromIndex 以降のセグメントは元の '/' 区切りをそのまま含み、末尾 '/' は除外済み。
            if (separator == "/"
                && _TryGetSegmentStart(path, fromIndex, out int start, out int end)
                && !_HasPercent(path, start, end - start))
            {
                return path.Substring(start, end - start);
            }

            // 従来経路 (カスタム区切り / エスケープ有り): セグメント単位に復元して結合する。
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

        // path 内の '/' 区切りセグメント (先頭/末尾の '/' は Trim 相当で無視、内部の連続 '/' は
        // 空セグメントとして保持) のうち index 番目の [start, start+length) を求める。範囲外なら false。
        // string.Split(Trim('/')) と同じセグメント境界を、部分文字列/配列を確保せず走査で再現する。
        private static bool _TryGetSegmentRange(string path, int index, out int start, out int length)
        {
            start = 0;
            length = 0;

            _TrimSlashRange(path, out int tStart, out int tEnd);

            // Trim 後が空 ("/", "//" 等) は 1 個の空セグメント [""] として扱う (string.Split 準拠)。
            if (tStart >= tEnd)
            {
                if (index == 0) { start = tStart; length = 0; return true; }
                return false;
            }

            int seg = 0;
            int segStart = tStart;
            for (int i = tStart; i <= tEnd; i++)
            {
                bool boundary = i == tEnd || path[i] == '/';
                if (!boundary) continue;

                if (seg == index)
                {
                    start = segStart;
                    length = i - segStart;
                    return true;
                }
                seg++;
                segStart = i + 1;
            }
            return false;
        }

        // fromIndex 番目のセグメント開始位置と Trim 後末尾 (end) を返す (GetPathSegmentFrom 高速経路用)。
        // [start, end) が fromIndex 以降を '/' 区切りで結合した文字列そのものになる。範囲外なら false。
        private static bool _TryGetSegmentStart(string path, int fromIndex, out int start, out int end)
        {
            start = 0;

            _TrimSlashRange(path, out int tStart, out int tEnd);
            end = tEnd;

            if (tStart >= tEnd)
            {
                if (fromIndex == 0) { start = tStart; return true; }
                return false;
            }

            int seg = 0;
            int segStart = tStart;
            for (int i = tStart; i <= tEnd; i++)
            {
                bool boundary = i == tEnd || path[i] == '/';
                if (!boundary) continue;

                if (seg == fromIndex)
                {
                    start = segStart;
                    return true;
                }
                seg++;
                segStart = i + 1;
            }
            return false;
        }

        // 先頭/末尾の '/' を除いた範囲 [start, end) を求める (path.Trim('/') 相当、部分文字列を確保しない)。
        private static void _TrimSlashRange(string path, out int start, out int end)
        {
            int s = 0;
            int e = path.Length;
            while (s < e && path[s] == '/') s++;
            while (e > s && path[e - 1] == '/') e--;
            start = s;
            end = e;
        }

        private static bool _HasPercent(string s, int start, int length)
        {
            for (int i = start, endEx = start + length; i < endEx; i++)
            {
                if (s[i] == '%') return true;
            }
            return false;
        }

        // [start, start+length) を部分文字列化する。エスケープ (%XX) を含む場合のみ復元する
        // (含まなければ Uri.UnescapeDataString は恒等なので部分文字列をそのまま返す)。
        private static string _UnescapeSegment(string path, int start, int length)
        {
            if (length == 0) return string.Empty;
            return _HasPercent(path, start, length)
                ? Uri.UnescapeDataString(path.Substring(start, length))
                : path.Substring(start, length);
        }
    }
}