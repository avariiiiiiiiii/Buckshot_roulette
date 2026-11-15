using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>Compensation is confined to player/held-item clocks and Rigidbody calls.
/// Enemies and their coroutines run on Unity's unchanged global slow clock.</summary>
[HarmonyPatch]
internal static class AdrenalinePatches
{
    private static readonly Dictionary<MethodBase, MethodInfo> PhysicsCalls = BuildPhysicsCalls();
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
    private static readonly HashSet<MethodBase> Selected = new HashSet<MethodBase>();

    private static Dictionary<MethodBase, MethodInfo> BuildPhysicsCalls()
    {
        var result = new Dictionary<MethodBase, MethodInfo>();
        foreach (var method in typeof(Rigidbody).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            string name = method.Name;
            if (name == "get_linearVelocity" || name == "get_velocity") name = "GetLinearVelocity";
            else if (name == "set_linearVelocity" || name == "set_velocity") name = "SetLinearVelocity";
            else if (name == "get_angularVelocity") name = "GetAngularVelocity";
            else if (name == "set_angularVelocity") name = "SetAngularVelocity";
            var parameters = new[] { typeof(Rigidbody) }.Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
            var replacement = typeof(AdrenalinePhysics).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, parameters, null);
            if (replacement != null) result[method] = replacement;
        }
        return result;
    }

    private static bool OwnClock(Type type)
    {
        string name = type.Name;
        return name.StartsWith("Player", StringComparison.Ordinal) || name.StartsWith("Camera", StringComparison.Ordinal)
            || name.StartsWith("PhysGrab", StringComparison.Ordinal) || name.StartsWith("Item", StringComparison.Ordinal)
            || (type.Namespace == "BuckshotRoulette.Items" && !name.StartsWith("Adrenaline", StringComparison.Ordinal));
    }

    private static FieldInfo CoroutineOwner(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(field => field.Name == "<>4__this" && typeof(Component).IsAssignableFrom(field.FieldType) && OwnClock(field.FieldType));

    private static bool ClockMethod(MethodBase method) => !method.IsStatic
        && ((typeof(Component).IsAssignableFrom(method.DeclaringType) && OwnClock(method.DeclaringType)) || CoroutineOwner(method.DeclaringType) != null);

    private static bool TimeCall(MethodBase call) => call.DeclaringType == typeof(Time)
        && (call.Name == "get_deltaTime" || call.Name == "get_smoothDeltaTime" || call.Name == "get_fixedDeltaTime" || call.Name == "get_time" || call.Name == "get_timeAsDouble");

    // Read IL operands before asking Harmony to patch: avoid patching thousands of
    // unrelated methods or relying on byte searches that mistake operands for opcodes.
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        for (int i = 0; i < il.Length;)
        {
            short value = il[i++];
            if (value == 0xfe) value = (short)(0xfe00 | il[i++]);
            var opcode = OpCodesByValue[value];
            int size;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + BitConverter.ToInt32(il, i) * 4; break;
                default: size = 4; break;
            }
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                MethodBase target = null;
                try { target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i), method.DeclaringType.GetGenericArguments(), method.GetGenericArguments()); }
                catch (ArgumentException) { }
                if (target != null) yield return target;
            }
            i += size;
        }
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Selected.Clear();
        foreach (var assembly in new[] { typeof(PlayerController).Assembly, typeof(AdrenalinePatches).Assembly, typeof(Photon.Pun.PhotonTransformView).Assembly }.Distinct())
        foreach (var type in assembly.GetTypes())
        {
            if (type.FullName.Contains("Adrenaline") || type.ContainsGenericParameters) continue;
            bool network = type == typeof(Photon.Pun.PhotonTransformView);
            if (assembly != typeof(PlayerController).Assembly && assembly != typeof(AdrenalinePatches).Assembly && !network) continue;
            foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                bool clock = ClockMethod(method);
                if (!Calls(method).Any(call => PhysicsCalls.ContainsKey(call) || ((clock || network) && (TimeCall(call) || call.DeclaringType == typeof(WaitForSeconds))))) continue;
                Selected.Add(method);
                yield return method;
            }
        }
        Plugin.Log.LogInfo($"Адреналин: подготовлена компенсация времени/физики для {Selected.Count} методов");
    }

    private static IEnumerable<CodeInstruction> Owner(MethodBase method)
    {
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        if (!typeof(Component).IsAssignableFrom(method.DeclaringType))
            yield return new CodeInstruction(OpCodes.Ldfld, CoroutineOwner(method.DeclaringType));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        bool clock = ClockMethod(__originalMethod);
        bool network = __originalMethod.DeclaringType == typeof(Photon.Pun.PhotonTransformView);
        for (int i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            if (!(instruction.operand is MethodBase call)) { yield return instruction; continue; }
            if (PhysicsCalls.TryGetValue(call, out var physics))
            {
                if (network)
                {
                    string replacement = call.Name == "get_linearVelocity" ? nameof(AdrenalinePhysics.NetworkLinearVelocity)
                        : call.Name == "set_linearVelocity" ? nameof(AdrenalinePhysics.SetNetworkLinearVelocity)
                        : call.Name == "get_angularVelocity" ? nameof(AdrenalinePhysics.NetworkAngularVelocity)
                        : call.Name == "set_angularVelocity" ? nameof(AdrenalinePhysics.SetNetworkAngularVelocity) : null;
                    if (replacement != null) physics = typeof(AdrenalinePhysics).GetMethod(replacement, BindingFlags.Static | BindingFlags.NonPublic);
                }
                instruction.opcode = OpCodes.Call; instruction.operand = physics;
                yield return instruction; continue;
            }
            if (network && TimeCall(call) && (call.Name == "get_time" || call.Name == "get_deltaTime"))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(AdrenalinePhysics).GetMethod(call.Name == "get_time" ? nameof(AdrenalinePhysics.NetworkTime) : nameof(AdrenalinePhysics.NetworkDelta), BindingFlags.Static | BindingFlags.NonPublic);
                yield return instruction; continue;
            }
            string helper = null;
            if (clock && TimeCall(call))
                helper = call.Name == "get_deltaTime" ? nameof(AdrenalineTime.Delta)
                    : call.Name == "get_smoothDeltaTime" ? nameof(AdrenalineTime.SmoothDelta)
                    : call.Name == "get_fixedDeltaTime" ? nameof(AdrenalineTime.FixedDelta)
                    : call.Name == "get_time" ? nameof(AdrenalineTime.ClockFloat) : nameof(AdrenalineTime.ClockTime);
            else if (clock && instruction.opcode == OpCodes.Newobj && call.DeclaringType == typeof(WaitForSeconds)
                && i + 1 < code.Count && code[i + 1].opcode == OpCodes.Stfld && code[i + 1].operand is FieldInfo current && current.FieldType == typeof(object))
                helper = nameof(AdrenalineTime.Wait);
            if (helper == null) { yield return instruction; continue; }
            var owner = Owner(__originalMethod).ToList();
            // Copy metadata through Harmony's constructor; its Mono Label type is
            // not directly available to a netstandard consumer of this Harmony build.
            owner[0] = new CodeInstruction(instruction) { opcode = owner[0].opcode, operand = owner[0].operand };
            foreach (var load in owner) yield return load;
            yield return new CodeInstruction(OpCodes.Call, typeof(AdrenalineTime).GetMethod(helper, BindingFlags.Static | BindingFlags.NonPublic));
        }
    }
}
