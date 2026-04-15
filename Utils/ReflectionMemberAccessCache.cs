using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ReplayLogger
{
    internal static class ReflectionMemberAccessCache
    {
        private static readonly ConcurrentDictionary<FieldInfo, Func<object, object>> FieldGetters = new();
        private static readonly ConcurrentDictionary<PropertyInfo, Func<object, object>> PropertyGetters = new();
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object>> MethodInvokers = new();
        private static readonly ConcurrentDictionary<RuntimePropertyKey, Func<object, object>> RuntimePropertyGetters = new();
        private static readonly Func<object, object> NullGetter = _ => null;

        internal static object GetCachedValue(this FieldInfo field, object instance)
        {
            if (field == null)
            {
                return null;
            }

            Func<object, object> getter = FieldGetters.GetOrAdd(field, CreateFieldGetter);
            return getter(instance);
        }

        internal static object GetCachedValue(this PropertyInfo property, object instance)
        {
            if (property == null)
            {
                return null;
            }

            Func<object, object> getter = PropertyGetters.GetOrAdd(property, CreatePropertyGetter);
            return getter(instance);
        }

        internal static object InvokeCached(this MethodInfo method, object instance)
        {
            if (method == null)
            {
                return null;
            }

            Func<object, object> invoker = MethodInvokers.GetOrAdd(method, CreateMethodInvoker);
            return invoker(instance);
        }

        internal static bool TryGetCachedRuntimePropertyValue(object instance, string propertyName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            RuntimePropertyKey key = new(instance.GetType(), propertyName);
            Func<object, object> getter = RuntimePropertyGetters.GetOrAdd(key, CreateRuntimePropertyGetter);
            if (ReferenceEquals(getter, NullGetter))
            {
                return false;
            }

            value = getter(instance);
            return true;
        }

        internal static bool TryGetCachedRuntimeBoolProperty(object instance, string propertyName, out bool value)
        {
            value = false;
            if (!TryGetCachedRuntimePropertyValue(instance, propertyName, out object raw))
            {
                return false;
            }

            if (raw is bool b)
            {
                value = b;
                return true;
            }

            return false;
        }

        private static Func<object, object> CreateFieldGetter(FieldInfo field)
        {
            try
            {
                ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
                Expression fieldAccess = field.IsStatic
                    ? Expression.Field(null, field)
                    : Expression.Field(Expression.Convert(instanceParam, field.DeclaringType), field);
                UnaryExpression box = Expression.Convert(fieldAccess, typeof(object));
                return Expression.Lambda<Func<object, object>>(box, instanceParam).Compile();
            }
            catch
            {
                return instance =>
                {
                    try
                    {
                        return field.GetValue(field.IsStatic ? null : instance);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }
        }

        private static Func<object, object> CreatePropertyGetter(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod(nonPublic: true);
            if (getter == null || property.GetIndexParameters().Length != 0)
            {
                return NullGetter;
            }

            try
            {
                ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
                MethodCallExpression call = getter.IsStatic
                    ? Expression.Call(getter)
                    : Expression.Call(Expression.Convert(instanceParam, property.DeclaringType), getter);
                UnaryExpression box = Expression.Convert(call, typeof(object));
                return Expression.Lambda<Func<object, object>>(box, instanceParam).Compile();
            }
            catch
            {
                return instance =>
                {
                    try
                    {
                        return property.GetValue(getter.IsStatic ? null : instance);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }
        }

        private static Func<object, object> CreateMethodInvoker(MethodInfo method)
        {
            if (method.GetParameters().Length != 0)
            {
                return instance =>
                {
                    try
                    {
                        return method.Invoke(method.IsStatic ? null : instance, null);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }

            try
            {
                ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
                MethodCallExpression call = method.IsStatic
                    ? Expression.Call(method)
                    : Expression.Call(Expression.Convert(instanceParam, method.DeclaringType), method);

                if (method.ReturnType == typeof(void))
                {
                    BlockExpression block = Expression.Block(call, Expression.Constant(null, typeof(object)));
                    return Expression.Lambda<Func<object, object>>(block, instanceParam).Compile();
                }

                UnaryExpression box = Expression.Convert(call, typeof(object));
                return Expression.Lambda<Func<object, object>>(box, instanceParam).Compile();
            }
            catch
            {
                return instance =>
                {
                    try
                    {
                        return method.Invoke(method.IsStatic ? null : instance, null);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }
        }

        private static Func<object, object> CreateRuntimePropertyGetter(RuntimePropertyKey key)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = key.RuntimeType.GetProperty(key.PropertyName, flags);
            if (property == null || property.GetIndexParameters().Length != 0)
            {
                return NullGetter;
            }

            return PropertyGetters.GetOrAdd(property, CreatePropertyGetter);
        }

        private readonly struct RuntimePropertyKey : IEquatable<RuntimePropertyKey>
        {
            internal RuntimePropertyKey(Type runtimeType, string propertyName)
            {
                RuntimeType = runtimeType;
                PropertyName = propertyName;
            }

            internal Type RuntimeType { get; }
            internal string PropertyName { get; }

            public bool Equals(RuntimePropertyKey other)
            {
                return RuntimeType == other.RuntimeType &&
                       string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RuntimePropertyKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RuntimeType != null ? RuntimeType.GetHashCode() : 0;
                    hash = (hash * 397) ^ (PropertyName != null ? PropertyName.GetHashCode() : 0);
                    return hash;
                }
            }
        }
    }
}
