using System;

namespace Lilium.RemoteControl
{
    public interface IExposedObjectResolver
    {
        public ExposedObject FindById(string id);
        public ExposedObject FindByTarget(object target);
    }

    /// <summary>
    /// デフォルトのリゾルバー（ExposedObjectRegistry.FindById と FindByTarget を直接呼び出す）
    /// </summary>
    public class DefaultExposedObjectResolver : IExposedObjectResolver
    {
        public static readonly DefaultExposedObjectResolver Instance = new DefaultExposedObjectResolver();

        public ExposedObject FindById(string id)
        {
            return ExposedObjectRegistry.FindById(id);
        }

        public ExposedObject FindByTarget(object target)
        {
            return ExposedObjectRegistry.FindByTarget(target);
        }
    }
}
