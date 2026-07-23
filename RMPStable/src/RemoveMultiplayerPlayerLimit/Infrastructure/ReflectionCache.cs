using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

public class ReflectionCache
{
	private readonly Dictionary<string, FieldInfo?> _fields = new Dictionary<string, FieldInfo>();

	private readonly Dictionary<string, MethodInfo?> _methods = new Dictionary<string, MethodInfo>();

	private readonly Dictionary<string, PropertyInfo?> _properties = new Dictionary<string, PropertyInfo>();

	private readonly Dictionary<string, Type?> _types = new Dictionary<string, Type>();

	private readonly Assembly _gameAssembly;

	public ReflectionCache()
	{
		_gameAssembly = typeof(ModInitializerAttribute).Assembly;
	}

	public Type? GetType(string fullName)
	{
		if (!_types.TryGetValue(fullName, out Type value))
		{
			value = _gameAssembly.GetType(fullName);
			_types[fullName] = value;
		}
		return value;
	}

	public FieldInfo? GetField(string typeName, string fieldName)
	{
		string key = typeName + "." + fieldName;
		if (!_fields.TryGetValue(key, out FieldInfo value))
		{
			value = GetType(typeName)?.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_fields[key] = value;
		}
		return value;
	}

	public FieldInfo? GetField(Type type, string fieldName)
	{
		string key = type.FullName + "." + fieldName;
		if (!_fields.TryGetValue(key, out FieldInfo value))
		{
			value = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_fields[key] = value;
		}
		return value;
	}

	public MethodInfo? GetMethod(string typeName, string methodName, Type[]? paramTypes = null)
	{
		string key = ((paramTypes == null) ? (typeName + "." + methodName) : $"{typeName}.{methodName}({string.Join(",", paramTypes.Select((Type t) => t.Name))})");
		if (!_methods.TryGetValue(key, out MethodInfo value))
		{
			Type type = GetType(typeName);
			value = ((paramTypes == null) ? type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, paramTypes, null));
			_methods[key] = value;
		}
		return value;
	}

	public MethodInfo? GetMethod(Type type, string methodName, Type[]? paramTypes = null)
	{
		return GetMethod(type.FullName, methodName, paramTypes);
	}

	public PropertyInfo? GetProperty(string typeName, string propertyName)
	{
		string key = typeName + "." + propertyName;
		if (!_properties.TryGetValue(key, out PropertyInfo value))
		{
			value = GetType(typeName)?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_properties[key] = value;
		}
		return value;
	}

	public PropertyInfo? GetProperty(Type type, string propertyName)
	{
		return GetProperty(type.FullName, propertyName);
	}

	public bool TrySetField(object? target, string typeName, string fieldName, object? value)
	{
		FieldInfo field = GetField(typeName, fieldName);
		if (field == null)
		{
			GD.PrintErr("[RMP] Field not found: " + typeName + "." + fieldName);
			return false;
		}
		field.SetValue(target, value);
		return true;
	}

	public T? TryGetField<T>(object? target, string typeName, string fieldName, T? fallback = default(T?))
	{
		FieldInfo field = GetField(typeName, fieldName);
		if (field == null)
		{
			return fallback;
		}
		object value = field.GetValue(target);
		if (value is T)
		{
			return (T)value;
		}
		return fallback;
	}

	public bool TrySetField(object? target, Type type, string fieldName, object? value)
	{
		return TrySetField(target, type.FullName, fieldName, value);
	}

	public T? TryGetField<T>(object? target, Type type, string fieldName, T? fallback = default(T?))
	{
		return TryGetField(target, type.FullName, fieldName, fallback);
	}

	public object? TryInvokeMethod(object? target, string typeName, string methodName, object?[]? args = null)
	{
		MethodInfo method = GetMethod(typeName, methodName);
		if (method == null)
		{
			GD.PrintErr("[RMP] Method not found: " + typeName + "." + methodName);
			return null;
		}
		return method.Invoke(target, args);
	}
}
