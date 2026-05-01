// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Source Generator が ModuleInitializer 経由で呼ぶ、型ごとの宣言順テーブル。
    /// ExposedClass の cctor を巻き込まないよう、別クラスに分離している。
    /// (ExposedClass.RegisterDeclarationOrder を直接呼ぶと、登録ループの途中で
    /// ExposedClass.cctor が起動して _RegisterAllTypesFromAttributes が走り、
    /// まだ登録されていない型に対して "宣言順未登録" 警告が誤発火する。)
    /// </summary>
    public static class ExposedClassDeclarationOrderTable
    {
        // key = [ExposedClass] が付いた Type、value = メンバー名 → 宣言順 index。
        private static readonly Dictionary<Type, Dictionary<string, int>> _table
            = new Dictionary<Type, Dictionary<string, int>>();

        // 「Source Gen に未登録」警告を一度だけ出すための型集合。
        private static readonly HashSet<Type> _warnedMissing = new HashSet<Type>();

        /// <summary>
        /// Source Generator から呼ばれる、宣言順テーブルの登録 API。
        /// memberNames は C# ソース上の宣言順で渡す。
        /// </summary>
        public static void Register(Type type, string[] memberNames)
        {
            if (type == null || memberNames == null) return;
            var map = new Dictionary<string, int>(memberNames.Length);
            for (int i = 0; i < memberNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(memberNames[i]))
                    map[memberNames[i]] = i;
            }
            _table[type] = map;
        }

        /// <summary>
        /// 型に対応する宣言順 index を取得。未登録なら -1。
        /// </summary>
        internal static int GetIndex(Type type, string memberName)
        {
            if (_table.TryGetValue(type, out var map)
                && map.TryGetValue(memberName, out var index))
            {
                return index;
            }
            return -1;
        }

        /// <summary>
        /// 型が登録済みかを返す。
        /// </summary>
        internal static bool IsRegistered(Type type) => _table.ContainsKey(type);

        /// <summary>
        /// 未登録警告を一度だけ発火するためのフラグ。true なら今回が初回。
        /// </summary>
        internal static bool TryMarkWarned(Type type) => _warnedMissing.Add(type);
    }
}
