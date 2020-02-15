using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ZombieLand
{
	public static class SafeReflections
	{
		public static Type ToType(this string name)
		{
			var type = AccessTools.TypeByName(name);
			if (type == null) throw new Exception("Cannot find type named '" + name);
			return type;
		}

		public static ConstructorInfo Constructor(this Type type)
		{
			var constructor = AccessTools.Constructor(type);
			if (constructor == null) throw new Exception("Cannot find constructor for type " + type.FullName);
			return constructor;
		}

		public static ConstructorInfo Constructor(this Type type, Type[] argumentTypes)
		{
			var constructor = AccessTools.Constructor(type, argumentTypes);
			if (constructor == null) throw new Exception("Cannot find constructor" + argumentTypes.Description() + " for type " + type.FullName);
			return constructor;
		}

		public static MethodInfo MethodNamed(this Type type, string name)
		{
			var method = AccessTools.Method(type, name);
			if (method == null) throw new Exception("Cannot find method named '" + name + "' in type " + type.FullName);
			return method;
		}

		public static MethodInfo MethodNamed(this Type type, string name, Type[] argumentTypes)
		{
			var method = AccessTools.Method(type, name, argumentTypes);
			if (method == null) throw new Exception("Cannot find method " + name + argumentTypes.Description() + " in type " + type.FullName);
			return method;
		}

		public static FieldInfo Field(this Type type, string fieldName)
		{
			var field = AccessTools.Field(type, fieldName);
			if (field == null) throw new Exception("Cannot find field '" + fieldName + "' in type " + type.FullName);
			return field;
		}

		public static MethodInfo PropertyGetter(this Type type, string propertyName)
		{
			var method = AccessTools.Property(type, propertyName)?.GetGetMethod(true);
			if (method == null) throw new Exception("Cannot find property getter '" + propertyName + "' in type " + type.FullName);
			return method;
		}

		public static MethodInfo PropertySetter(this Type type, string propertyName)
		{
			var method = AccessTools.Property(type, propertyName)?.GetSetMethod(true);
			if (method == null) throw new Exception("Cannot find property getter '" + propertyName + "' in type " + type.FullName);
			return method;
		}

		public static List<MethodInfo> InnerMethodsStartingWith(this Type type, string prefix)
		{
			var method = type.GetNestedTypes(AccessTools.all)
				.SelectMany(innerType => innerType.GetMethods(AccessTools.all))
				.Where(m => m.Name.StartsWith(prefix))
				.ToList();
			if (method.Count == 0) throw new Exception("Cannot find method starting with '" + prefix + "' in any inner type of " + type.FullName);
			return method;
		}

		public static MethodInfo MethodMatching(this Type type, Func<MethodInfo[], MethodInfo> predicate)
		{
			var method = predicate(type.GetMethods(AccessTools.all));
			if (method == null) throw new Exception("Cannot find method matching " + predicate + " in type " + type.FullName);
			return method;
		}
	}
}