using System;

namespace Lilium.RemoteControl
{
    public interface IExposedObjectResolver
    {
        public ExposedObjectHandle? FindById(string id);
        public ExposedObjectHandle? FindByTarget(object target);
    }

    /// <summary>
    /// デフォルトのリゾルバー（ExposedObjectRegistry.FindById と FindByTarget を直接呼び出す）
    /// </summary>
    public class DefaultExposedObjectResolver : IExposedObjectResolver
    {
        public static readonly DefaultExposedObjectResolver Instance = new DefaultExposedObjectResolver();

        public ExposedObjectHandle? FindById(string id)
        {
            return ExposedObjectRegistry.FindById(id);
        }

        public ExposedObjectHandle? FindByTarget(object target)
        {
            return ExposedObjectRegistry.FindByTarget(target);
        }
    }
}
