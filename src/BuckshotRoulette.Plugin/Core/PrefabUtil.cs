using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace BuckshotRoulette.Core;

/// <summary>Сборка префабов мода из клонов ванильных.</summary>
internal static class PrefabUtil
{
    /// <summary>
    /// Сериализованные в префабе вызовы, чьи цели удалены (компоненты шаблона, которые мы выкинули), нельзя убрать в рантайме,
    /// но можно выключить. Остальные вызовы продолжают работать. Возвращает число выключенных.
    /// </summary>
    public static int DisableDeadEvents(GameObject root)
    {
        int disabled = 0;
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType)) continue;
                if (!(field.GetValue(component) is UnityEventBase evt)) continue;
                for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                {
                    if (evt.GetPersistentTarget(i) != null) continue;
                    evt.SetPersistentListenerState(i, UnityEventCallState.Off);
                    disabled++;
                }
            }
        }
        return disabled;
    }

    public static void ResetLocal(Transform t)
    {
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    /// <summary>Remove the health-pack shell's impact destruction; explicit item use still destroys normally.</summary>
    public static void MakeDurableTool(GameObject prefab)
    {
        var physical = prefab.GetComponent<NotValuableObject>();
        if (physical != null)
        {
            physical.hasHealth = false;
            physical.healthDestroy = false;
            physical.durabilityPreset = null;
        }
        var impact = prefab.GetComponent<PhysGrabObjectImpactDetector>();
        if (impact != null) impact.destroyDisable = true;
    }
}
