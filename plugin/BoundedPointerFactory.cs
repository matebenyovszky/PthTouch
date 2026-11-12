using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace PthTouch;

// Override the stock pointer's protected event-position hook while inheriting
// its pressure, tilt, button, eraser and flush behavior. Runtime linking avoids
// shipping or compiling against a second copy of OTD.Desktop. Signature changes
// fail cleanly to the mode's pre-output delta clamp.
public static class BoundedPointerFactory
{
    private static readonly ConditionalWeakTable<Type, ConstructorInfo> constructors = new();
    public static IRelativePointer? Create(Type parent, Action<IntPtr, float, float> writer)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || parent.IsSealed) return null;
        var signature = new[] { typeof(IntPtr), typeof(float), typeof(float) };
        var hook = parent.GetMethod("SetPendingPosition", BindingFlags.Instance | BindingFlags.NonPublic, null, signature, null);
        var baseCtor = parent.GetConstructor(Type.EmptyTypes);
        if (hook is null || !hook.IsVirtual || hook.IsFinal || hook.ReturnType != typeof(void) || baseCtor is null) return null;
        var ctor = constructors.GetValue(parent, _ =>
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PthTouch.BoundedPointer"), AssemblyBuilderAccess.RunAndCollect);
            var type = assembly.DefineDynamicModule("Pointer").DefineType("PthTouch.BoundedMacPointer", TypeAttributes.Public, parent);
            var callbackType = typeof(Action<IntPtr, float, float>);
            var field = type.DefineField("writer", callbackType, FieldAttributes.Private | FieldAttributes.InitOnly);
            var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [callbackType]);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, baseCtor);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            var method = type.DefineMethod(hook.Name, MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void), signature);
            il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, callbackType.GetMethod("Invoke")!); il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(method, hook);
            return type.CreateType()!.GetConstructor([callbackType])!;
        });
        return (IRelativePointer)ctor.Invoke([writer]);
    }
}
