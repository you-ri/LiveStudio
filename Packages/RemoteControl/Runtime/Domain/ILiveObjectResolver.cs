using System;

namespace Lilium.RemoteControl
{
    public interface ILiveObjectResolver
    {
        public LiveObjectHandle? FindById(string id);
        public LiveObjectHandle? FindByTarget(object target);
    }

    /// <summary>
    /// デフォルトのリゾルバー（LiveObjectRegistry.FindById と FindByTarget を直接呼び出す）
    /// </summary>
    public class DefaultLiveObjectResolver : ILiveObjectResolver
    {
        public static readonly DefaultLiveObjectResolver Instance = new DefaultLiveObjectResolver();

        public LiveObjectHandle? FindById(string id)
        {
            return LiveObjectRegistry.FindById(id);
        }

        public LiveObjectHandle? FindByTarget(object target)
        {
            return LiveObjectRegistry.FindByTarget(target);
        }
    }
}
