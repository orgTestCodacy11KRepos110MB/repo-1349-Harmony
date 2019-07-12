using HarmonyLib;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLibTests.IL
{
	public struct Vec3
	{
		public int v1;
		public int v2;
		public int v3;

		public Vec3(int v1, int v2, int v3)
		{
			this.v1 = v1;
			this.v2 = v2;
			this.v3 = v3;
		}

		public static Vec3 Zero => new Vec3(0, 0, 0);

		override public string ToString()
		{
			return v1 + "," + v2 + "," + v3;
		}
	}

	public static class TestMethods1
	{
		public static void Test1(out string s)
		{
			Console.WriteLine("Test1");
			s = "Test1";
		}
	}

	public class TestMethods2
	{
		public string Test2(int n, string s)
		{
			Console.WriteLine("Test2");
			return s;
		}
	}

	public class TestMethods3
	{
		public static List<int> Test3(Vec3 v, List<int> list)
		{
			Console.WriteLine("Test3");
			return new List<int>();
		}
	}

	[TestFixture]
	public class DynamicArgumentPatches
	{
		static readonly List<string> log = new List<string>();

		static bool General(string typeName, int token, object instance, object[] args)
		{
			var method = AccessTools.TypeByName(typeName).Module.ResolveMethod(token);
			log.Add(method.Name);
			log.Add(instance?.GetType().Name ?? "NULL");
			if (args != null)
				foreach (var arg in args)
					log.Add(arg?.ToString() ?? "NULL");
			return true;
		}

		static MethodInfo m_General = SymbolExtensions.GetMethodInfo(() => General("", 0, null, new object[0]));
		static MethodInfo m_Transpiler = SymbolExtensions.GetMethodInfo(() => Transpiler(null, null, null));
		static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions, ILGenerator gen)
		{
			var label = gen.DefineLabel();

			yield return new CodeInstruction(OpCodes.Ldstr, original.DeclaringType.FullName);
			yield return new CodeInstruction(OpCodes.Ldc_I4, original.MetadataToken);
			yield return new CodeInstruction(original.IsStatic ? OpCodes.Ldc_I4_0 : OpCodes.Ldarg_0);

			var parameter = original.GetParameters();
			yield return new CodeInstruction(OpCodes.Ldc_I4, parameter.Length);
			yield return new CodeInstruction(OpCodes.Newarr, typeof(object));

			var i = original.IsStatic ? 0 : 1;
			var j = 0;
			foreach (var pInfo in parameter)
			{
				var pType = pInfo.ParameterType;
				yield return new CodeInstruction(OpCodes.Dup);
				yield return new CodeInstruction(OpCodes.Ldc_I4, j++);
				yield return new CodeInstruction(OpCodes.Ldarg, i++);
				if (pInfo.IsOut || pInfo.IsRetval)
				{
					if (pType.IsValueType)
						yield return new CodeInstruction(OpCodes.Ldobj, pType);
					else
						yield return new CodeInstruction(OpCodes.Ldind_Ref);
				}
				if (pType.IsValueType)
					yield return new CodeInstruction(OpCodes.Box, pType);
				yield return new CodeInstruction(OpCodes.Stelem_Ref);
			}
			yield return new CodeInstruction(OpCodes.Call, m_General);
			yield return new CodeInstruction(OpCodes.Brtrue, label);
			foreach (var code in CreateDefaultCodes(gen, AccessTools.GetReturnedType(original)))
				yield return code;
			yield return new CodeInstruction(OpCodes.Ret);

			var list = instructions.ToList();
			list.First().labels.Add(label);
			foreach (var instruction in list)
				yield return instruction;
		}

		static IEnumerable<CodeInstruction> CreateDefaultCodes(ILGenerator generator, Type type)
		{
			if (type.IsByRef) type = type.GetElementType();

			if (AccessTools.IsClass(type))
			{
				yield return new CodeInstruction(OpCodes.Ldnull);
				yield break;
			}
			if (AccessTools.IsStruct(type))
			{
				var v = generator.DeclareLocal(type);
				yield return new CodeInstruction(OpCodes.Ldloca, v);
				yield return new CodeInstruction(OpCodes.Initobj, type);
				yield break;
			}
			if (AccessTools.IsValue(type))
			{
				if (type == typeof(float))
					yield return new CodeInstruction(OpCodes.Ldc_R4, (float)0);
				else if (type == typeof(double))
					yield return new CodeInstruction(OpCodes.Ldc_R8, (double)0);
				else if (type == typeof(long))
					yield return new CodeInstruction(OpCodes.Ldc_I8, (long)0);
				else
					yield return new CodeInstruction(OpCodes.Ldc_I4, 0);
				yield break;
			}
		}

		static MethodInfo[] methods = new MethodInfo[]
		{
			AccessTools.Method(typeof(TestMethods1), "Test1"),
			SymbolExtensions.GetMethodInfo(() => new TestMethods2().Test2(0, "")),
			SymbolExtensions.GetMethodInfo(() => TestMethods3.Test3(Vec3.Zero, null))
		};

		[Test]
		public void SendingArguments()
		{
			Harmony.DEBUG = true;
			var harmony = new Harmony("test");
			methods.Do(m =>
			{
				harmony.Patch(m, transpiler: new HarmonyMethod(m_Transpiler));
			});

			TestMethods1.Test1(out var s);
			new TestMethods2().Test2(123, "hello");
			TestMethods3.Test3(new Vec3(2, 4, 6), new[] { 100, 200, 300 }.ToList());

			var n = 0;
			Assert.AreEqual(11, log.Count);
			Assert.AreEqual(log[n++], "Test1");
			Assert.AreEqual(log[n++], "NULL");
			Assert.AreEqual(log[n++], "NULL");
			Assert.AreEqual(log[n++], "Test2");
			Assert.AreEqual(log[n++], "TestMethods2");
			Assert.AreEqual(log[n++], "123");
			Assert.AreEqual(log[n++], "hello");
			Assert.AreEqual(log[n++], "Test3");
			Assert.AreEqual(log[n++], "NULL");
			Assert.AreEqual(log[n++], "2,4,6");
			Assert.AreEqual(log[n++], "System.Collections.Generic.List`1[System.Int32]");
		}
	}
}