using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Registration information for ExposedUnityObject types
    /// </summary>
    public class TypeRegistration
    {
        public string displayName;
        public Type componentType;
        public Func<UnityEngine.Object, ExposedUnityObjectBase> factory;

        public TypeRegistration(string displayName, Type componentType, Func<UnityEngine.Object, ExposedUnityObjectBase> factory)
        {
            this.displayName = displayName;
            this.componentType = componentType;
            this.factory = factory;
        }
    }

    /// <summary>
    /// Factory class for creating ExposedUnityObjectBase instances from Unity components.
    /// Supports registration-based type management.
    /// </summary>
    public static class ExposedUnityObjectFactory
    {
        private static readonly Dictionary<Type, TypeRegistration> _typeRegistry = new Dictionary<Type, TypeRegistration>();
        private static readonly List<TypeRegistration> _registrationList = new List<TypeRegistration>();
        private static bool _autoRegistered;

        /// <summary>
        /// Registers a type for ExposedUnityObject creation
        /// </summary>
        /// <typeparam name="T">The Unity component type</typeparam>
        /// <param name="displayName">Display name for UI</param>
        /// <param name="factory">Factory function to create ExposedUnityObjectBase</param>
        public static void Register<T>(string displayName, Func<T, ExposedUnityObjectBase> factory) where T : UnityEngine.Object
        {
            var componentType = typeof(T);

            // Wrap the factory to handle casting
            Func<UnityEngine.Object, ExposedUnityObjectBase> wrappedFactory = (obj) =>
            {
                if (obj == null)
                {
                    return factory(null);
                }
                if (obj is T typedObj)
                {
                    return factory(typedObj);
                }
                throw new InvalidCastException($"[RemoteControl] Cannot cast {obj.GetType().Name} to {componentType.Name}.");
            };

            var registration = new TypeRegistration(displayName, componentType, wrappedFactory);

            if (_typeRegistry.ContainsKey(componentType))
            {
                Debug.LogWarning($"[RemoteControl] Type {componentType.Name} is already registered. Overwriting.");
                _typeRegistry[componentType] = registration;

                // Update in list as well
                for (int i = 0; i < _registrationList.Count; i++)
                {
                    if (_registrationList[i].componentType == componentType)
                    {
                        _registrationList[i] = registration;
                        break;
                    }
                }
            }
            else
            {
                _typeRegistry.Add(componentType, registration);
                _registrationList.Add(registration);
            }
        }

        /// <summary>
        /// Gets all registered type information
        /// </summary>
        /// <returns>List of type registrations</returns>
        public static IReadOnlyList<TypeRegistration> GetRegisteredTypes()
        {
            _EnsureAutoRegistered();
            return _registrationList;
        }

        /// <summary>
        /// Creates an ExposedUnityObjectBase from a Unity component.
        /// Throws NotSupportedException if the type is not registered.
        /// </summary>
        /// <typeparam name="T">The type of Unity component</typeparam>
        /// <param name="component">The Unity component to wrap</param>
        /// <returns>An ExposedUnityObjectBase instance</returns>
        /// <exception cref="ArgumentNullException">Thrown when component is null</exception>
        /// <exception cref="NotSupportedException">Thrown when the type is not registered</exception>
        public static ExposedUnityObjectBase Create<T>(T component) where T : UnityEngine.Object
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component), "[RemoteControl] Component cannot be null.");
            }

            if (TryCreate(component, out var result))
            {
                return result;
            }

            throw new NotSupportedException($"[RemoteControl] Type {typeof(T).Name} is not registered for ExposedUnityObject creation.");
        }

        /// <summary>
        /// Tries to create an ExposedUnityObjectBase from a Unity component.
        /// Returns false if the type is not registered.
        /// </summary>
        /// <typeparam name="T">The type of Unity component</typeparam>
        /// <param name="component">The Unity component to wrap</param>
        /// <param name="result">The created ExposedUnityObjectBase instance, or null if creation failed</param>
        /// <returns>True if creation succeeded, false otherwise</returns>
        public static bool TryCreate<T>(T component, out ExposedUnityObjectBase result) where T : UnityEngine.Object
        {
            result = null;

            if (component == null)
            {
                return false;
            }

            _EnsureAutoRegistered();

            var componentType = component.GetType();

            // Try exact type match first
            if (_typeRegistry.TryGetValue(componentType, out var registration))
            {
                try
                {
                    result = registration.factory(component);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RemoteControl] Failed to create ExposedUnityObject for type {componentType.Name}: {e.Message}");
                    return false;
                }
            }

            // Try to find compatible base type
            foreach (var kvp in _typeRegistry)
            {
                if (kvp.Key.IsAssignableFrom(componentType))
                {
                    try
                    {
                        result = kvp.Value.factory(component);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[RemoteControl] Failed to create ExposedUnityObject for type {componentType.Name}: {e.Message}");
                        return false;
                    }
                }
            }

            // Type not registered
            return false;
        }

        private static void _EnsureAutoRegistered()
        {
            if (_autoRegistered) return;
            _autoRegistered = true;
            _AutoRegisterDerivedTypes(typeof(ExposedUnityObjectProxy<,>));
            _AutoRegisterDerivedTypes(typeof(ExposedUnityObjectReference<,>));
        }

        private static bool _ContainsDisplayName(string displayName)
        {
            for (int i = 0; i < _registrationList.Count; i++)
            {
                if (_registrationList[i].displayName == displayName) return true;
            }
            return false;
        }

        private static void _AutoRegisterDerivedTypes(Type openGenericBase)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract) continue;

                    var baseType = type.BaseType;
                    while (baseType != null)
                    {
                        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == openGenericBase)
                            break;
                        baseType = baseType.BaseType;
                    }
                    if (baseType == null) continue;

                    var genericArgs = baseType.GetGenericArguments();
                    var componentType = genericArgs[1]; // T

                    var classAttr = type.GetCustomAttribute<ExposedClassAttribute>();
                    var displayName = classAttr?.typeName ?? type.Name;

                    // displayName 既登録ならスキップ（明示 Register 済み or 二重走査対策）
                    if (_ContainsDisplayName(displayName)) continue;

                    var ctor = type.GetConstructor(new[] { componentType });
                    if (ctor == null) continue;

                    var registration = new TypeRegistration(displayName, componentType, (obj) =>
                    {
                        return (ExposedUnityObjectBase)ctor.Invoke(new object[] { obj });
                    });

                    // componentType 単位の default 自動ラップ先は先勝ち (例: GameObject → ExposedGameObject)。
                    // 同 componentType で displayName が異なる Proxy (例: ExposedGameObjectWithTransform) は
                    // displayName ベースの lookup (SceneFromJson など) のために _registrationList には必ず積む。
                    if (!_typeRegistry.ContainsKey(componentType))
                    {
                        _typeRegistry.Add(componentType, registration);
                    }
                    _registrationList.Add(registration);
                }
            }
        }
    }
}
