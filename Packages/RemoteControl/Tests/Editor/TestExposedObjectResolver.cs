// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// テスト共通の IExposedObjectResolver 実装。ExposedObjectRegistry へ素通しする。
    /// 各テストが個別に定義していた MockResolver / TestResolver / MockExposedObjectResolver
    /// （すべて同一実装）を統一したもの。
    /// </summary>
    internal sealed class TestExposedObjectResolver : IExposedObjectResolver
    {
        public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
        public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
    }
}
