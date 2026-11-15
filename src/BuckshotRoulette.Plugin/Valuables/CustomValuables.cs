using System;
using System.Collections.Generic;
using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Valuables;

/// <summary>Ценности мода: клон простой ванильной ценности (Keycard) с подменой модели, цены и поведения.</summary>
internal static class CustomValuables
{
    private const string Template = "Valuables/01 Tiny/Valuable Arctic Keycard";

    private sealed class Def
    {
        public string Name;
        public string Model;
        public float MinValue, MaxValue;
        public ConsumableKind? Kind;
        public bool Phone;
    }

    private static readonly Def[] Defs =
    {
        new Def { Name = "Valuable Beer Can", Model = "beer_can", MinValue = 500, MaxValue = 1000, Kind = ConsumableKind.Beer },
        new Def { Name = "Valuable Burner Phone", Model = "burner_phone", MinValue = 500, MaxValue = 1000, Phone = true },
        new Def { Name = "Valuable Cigarette Pack", Model = "cigarette_pack", MinValue = 200, MaxValue = 500, Kind = ConsumableKind.Cigarettes },
        new Def { Name = "Valuable Expired Medicine", Model = "expired_medicine", MinValue = 200, MaxValue = 500, Kind = ConsumableKind.Medicine },
    };

    public static readonly List<PrefabRef> Registered = new List<PrefabRef>();

    private static Transform _holder;

    /// <summary>Вызывать из Awake плагина: REPOLib сам доложит ценности в пулы до загрузки уровня.</summary>
    public static void RegisterAll()
    {
        HealEffect.Load();
        var template = Resources.Load<GameObject>(Template);
        if (template == null)
        {
            Plugin.Log.LogError($"Шаблон ценности не найден: {Template}");
            return;
        }

        _holder = new GameObject("BuckshotRoulette Prefabs").transform;
        _holder.gameObject.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_holder.gameObject);

        foreach (var def in Defs)
        {
            try
            {
                var prefabRef = REPOLib.Modules.Valuables.RegisterValuable(Build(template, def));
                if (prefabRef != null) Registered.Add(prefabRef);
                Plugin.Log.LogInfo($"Ценность зарегистрирована: {def.Name} ({def.MinValue}..{def.MaxValue})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Не удалось собрать {def.Name}: {e}");
            }
        }
    }

    private static GameObject Build(GameObject template, Def def)
    {
        var go = UnityEngine.Object.Instantiate(template, _holder);
        go.name = def.Name;

        var valuable = go.GetComponent<ValuableObject>();
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = def.Name + " Value";
        value.valueMin = def.MinValue;
        value.valueMax = def.MaxValue;
        valuable.valuePreset = value;
        valuable.volumeType = ValuableVolume.Type.Tiny;
        if (def.Kind == ConsumableKind.Beer) BeerAudio.Configure(valuable);

        // У ванильного шаблона меш лежит под масштабированными узлами (меш keycard авторский, не в метрах):
        // обнуляем локальные трансформы, тогда размеры модели = размеры в мире.
        var objectNode = go.transform.Find("Object");
        var meshObject = objectNode.Find("Mesh");
        PrefabUtil.ResetLocal(objectNode);
        PrefabUtil.ResetLocal(meshObject);

        var filter = meshObject.GetComponent<MeshFilter>();
        var renderer = meshObject.GetComponent<MeshRenderer>();
        var model = ModelLoader.Load(def.Model);
        filter.sharedMesh = model.Mesh;
        renderer.sharedMaterials = model.Materials;

        foreach (var box in go.GetComponentsInChildren<BoxCollider>(true))
        {
            PrefabUtil.ResetLocal(box.transform);
            box.center = model.Mesh.bounds.center;
            box.size = model.Mesh.bounds.size;
            var colliderFilter = box.GetComponent<MeshFilter>();
            if (colliderFilter != null) colliderFilter.sharedMesh = model.Mesh;
        }

        if (def.Kind.HasValue) go.AddComponent<ConsumableValuable>().Kind = def.Kind.Value;
        if (def.Phone) go.AddComponent<BurnerPhone>();
        return go;
    }
}
