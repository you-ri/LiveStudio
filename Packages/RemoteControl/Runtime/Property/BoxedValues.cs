// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Concurrent;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 値型メンバーの getter が返す boxing の割り当てを抑えるための正規箱ヘルパー。
    /// 取り得る値が有限な型 (bool / enum) は、事前確保・キャッシュした箱を使い回すことで、
    /// <see cref="LivePropertyUtility.GetValueRaw"/> のたびに新しい箱を確保するのを避ける。
    /// Source Generator が生成する高速 getter (<see cref="LiveMemberAccessorTable"/> 登録) から呼ばれる。
    /// float / Vector3 等の値が無限な struct は正規化できないため対象外 (従来どおり box する)。
    /// </summary>
    public static class BoxedValues
    {
        private static readonly object s_true = true;
        private static readonly object s_false = false;

        /// <summary>bool を正規箱で返す (true/false の 2 箱を使い回すので割り当てゼロ)。</summary>
        public static object Box(bool value) => value ? s_true : s_false;

        /// <summary>
        /// enum を正規箱で返す。取り得る値が有限なので、値ごとに 1 度だけ箱を作って使い回す。
        /// 生成 getter は具象 enum 型で呼ぶため IL2CPP(AOT) でも問題ない。
        /// </summary>
        public static object BoxEnum<T>(T value) where T : struct, Enum
            => EnumBoxCache<T>.Get(value);

        private static class EnumBoxCache<T> where T : struct, Enum
        {
            private static readonly ConcurrentDictionary<T, object> s_cache
                = new ConcurrentDictionary<T, object>();

            // ラムダは引数のみ参照 (キャプチャ無し) なので delegate は静的キャッシュされ、追加割り当ては無い。
            // 箱を作るのは値ごとに初回のみ。ワーカースレッド (RemoteControl ハンドラ) から呼ばれるため concurrent。
            public static object Get(T value) => s_cache.GetOrAdd(value, v => (object)v);
        }
    }
}
