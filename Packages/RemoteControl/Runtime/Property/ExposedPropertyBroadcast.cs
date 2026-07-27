// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Publishes ExposedObject property changes to connected remote apps.
    ///
    /// Publishing records the changed object's id in <see cref="ExposedChangeLog"/>; it does not send
    /// the value anywhere. Remote apps poll the change log and refetch only the objects they actually
    /// hold, so a client is never handed data for a page it is not looking at, and a change costs the
    /// same whether zero or five clients are connected.
    ///
    /// Call these whenever a property changes through a path the remote app cannot observe by writing
    /// it itself — Studio-side edits, operations, deserialization, computed values.
    /// </summary>
    public static class ExposedPropertyBroadcast
    {
        /// <summary>
        /// Publishes a change to the given property of <paramref name="target"/>.
        /// <paramref name="propertyPath"/> is accepted for call-site clarity but is not transmitted:
        /// the change log works per object, and the client refetches what it needs.
        /// </summary>
        public static void BroadcastProperty(object target, string propertyPath)
        {
            if (!ExposedObjectRegistry.TryFindByTarget(target, out var exposedObj)) return;
            BroadcastProperty(exposedObj, propertyPath);
        }

        /// <summary>
        /// Publishes a change to a property of an unregistered <see cref="UnityEngine.Object"/> target,
        /// keyed by instanceID. RemoteApp holds inline elements under a selector by their instanceID,
        /// so recording the same instanceID reaches the element that needs refreshing.
        /// </summary>
        public static void BroadcastProperty(UnityEngine.Object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath)) return;
            ExposedChangeLog.Record(ExposedObjectUtility.GetInstanceID(target).ToString());
        }

        /// <summary>
        /// Publishes a change to the given property of <paramref name="exposedObj"/>.
        /// </summary>
        public static void BroadcastProperty(ExposedObjectHandle exposedObj, string propertyPath)
        {
            // An unregistered handle has no id to key the change on; the client could not address it.
            if (!exposedObj.hasId) return;
            ExposedChangeLog.Record(exposedObj.id);
        }

        /// <summary>
        /// Publishes a change to a property of a static <see cref="ExposedClass"/>. A static class has
        /// no target instance to look the handle up by, so it is keyed by the registry id assigned at
        /// registration time (the exposed type name).
        /// </summary>
        public static void BroadcastStaticProperty(System.Type staticType, string propertyPath)
        {
            if (staticType == null || string.IsNullOrEmpty(propertyPath)) return;

            var exposedClass = ExposedClass.Find(staticType);
            if (exposedClass == null) return;
            // Skip types that were never registered — the client has no object under that id.
            if (ExposedObjectRegistry.FindById(exposedClass.typeName) == null) return;

            ExposedChangeLog.Record(exposedClass.typeName);
        }

        /// <summary>
        /// Publishes that the ExposedClass / ExposedEnum tables have been rebuilt. Remote apps refetch
        /// /exposed/types and /exposed/enums together so the two stay consistent.
        /// </summary>
        public static void BroadcastTypesUpdate()
        {
            ExposedChangeLog.Record(ExposedChangeLog.kTypesId);
        }
    }
}
