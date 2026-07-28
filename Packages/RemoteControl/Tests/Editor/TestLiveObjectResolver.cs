// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// テスト共通の ILiveObjectResolver 実装。LiveObjectRegistry へ素通しする。
    /// 各テストが個別に定義していた MockResolver / TestResolver / MockLiveObjectResolver
    /// （すべて同一実装）を統一したもの。
    /// </summary>
    internal sealed class TestLiveObjectResolver : ILiveObjectResolver
    {
        public LiveObjectHandle? FindById(string id) => LiveObjectRegistry.FindById(id);
        public LiveObjectHandle? FindByTarget(object target) => LiveObjectRegistry.FindByTarget(target);
    }
}
