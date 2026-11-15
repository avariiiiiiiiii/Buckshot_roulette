using System.Collections.Generic;
using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Items;

internal static class JammerItem
{
    internal const float ModelScale = 1.8f;
    public static Item Item { get; private set; }
    internal static Material ChargedMaterial { get; private set; }
    internal static Material EmptyMaterial { get; private set; }

    public static void Register(Transform holder)
    {
        const string name = "Item Ultrasonic Jammer";
        var template = Resources.Load<GameObject>("Items/Item Health Pack Small");
        if (template == null) throw new System.InvalidOperationException("Не найден шаблон глушилки");
        var prefab = Object.Instantiate(template, holder);
        prefab.name = name;
        Object.DestroyImmediate(prefab.GetComponent<ItemHealthPack>());
        foreach (var childName in new[] { "Heal Effects", "Reject Effect", "Light" })
        {
            var child = prefab.transform.Find(childName);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }
        PrefabUtil.DisableDeadEvents(prefab);
        PrefabUtil.MakeDurableTool(prefab);

        var model = ModelLoader.Scaled(ModelLoader.Load("jammer"), ModelScale);
        var mesh = prefab.transform.Find("Mesh");
        PrefabUtil.ResetLocal(mesh);
        var body = Object.Instantiate(model.Mesh);
        body.name = "Jammer Body";
        body.subMeshCount = 1;
        mesh.GetComponent<MeshFilter>().sharedMesh = body;
        mesh.GetComponent<MeshRenderer>().sharedMaterials = new[] { model.Materials[0] };
        ChargedMaterial = model.Materials[1];
        EmptyMaterial = new Material(ChargedMaterial) { name = "Jammer Empty Charge" };
        EmptyMaterial.SetColor("_Color", new Color(0.06f, 0.045f, 0.02f));
        EmptyMaterial.SetColor("_EmissionColor", Color.black);
        EmptyMaterial.DisableKeyword("_EMISSION");
        for (int i = 0; i < 3; i++)
        {
            var indicator = new GameObject("Charge_" + (i + 1));
            indicator.transform.SetParent(mesh, false);
            indicator.AddComponent<MeshFilter>().sharedMesh = IndicatorMesh(model.Mesh, i);
            indicator.AddComponent<MeshRenderer>().sharedMaterial = ChargedMaterial;
        }
        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            PrefabUtil.ResetLocal(box.transform);
            box.center = model.Mesh.bounds.center;
            box.size = model.Mesh.bounds.size;
            var preview = box.GetComponent<MeshFilter>();
            if (preview != null && preview.transform != mesh) preview.sharedMesh = model.Mesh;
        }

        var attributes = prefab.GetComponent<ItemAttributes>();
        var item = Object.Instantiate(attributes.item);
        item.name = name;
        item.itemName = Texts.T("Глушилка", "Ultrasonic Repeller");
        item.itemNameLocalized = null;
        item.itemType = SemiFunc.itemType.tool;
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = name + " Value";
        value.valueMin = 8750f; // Штатная магазинная наценка ×4: 35–40 тысяч.
        value.valueMax = 10000f;
        item.value = value;
        attributes.item = item;
        attributes.icon = null;
        attributes.hasIcon = false;
        var battery = prefab.GetComponent<ItemBattery>() ?? prefab.AddComponent<ItemBattery>();
        battery.batteryBars = 3;
        battery.autoDrain = false;
        battery.isUnchargable = true;
        battery.hasBatteryVisuals = false;
        battery.onlyShowWhenItemToggleIsOn = false;
        prefab.AddComponent<Jammer>();
        if (REPOLib.Modules.Items.RegisterItem(attributes) == null)
            throw new System.InvalidOperationException("Глушилка не зарегистрирована");
        Item = item;
        Plugin.Log.LogInfo($"Предмет зарегистрирован: {name}");
    }

    private static Mesh IndicatorMesh(Mesh source, int indicator)
    {
        var sourceVertices = source.vertices;
        var sourceNormals = source.normals;
        var sourceUv = source.uv;
        var sourceTriangles = source.GetTriangles(1);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        var indices = new Dictionary<int, int>();
        for (int t = 0; t < sourceTriangles.Length; t += 3)
        {
            float x = (sourceVertices[sourceTriangles[t]].x + sourceVertices[sourceTriangles[t + 1]].x
                + sourceVertices[sourceTriangles[t + 2]].x) / 3f;
            int group = x < -0.017f ? 0 : x > 0.017f ? 2 : 1;
            if (group != indicator) continue;
            for (int j = 0; j < 3; j++)
            {
                int oldIndex = sourceTriangles[t + j];
                if (!indices.TryGetValue(oldIndex, out int index))
                {
                    index = vertices.Count;
                    indices.Add(oldIndex, index);
                    vertices.Add(sourceVertices[oldIndex]);
                    normals.Add(sourceNormals[oldIndex]);
                    uv.Add(sourceUv[oldIndex]);
                }
                triangles.Add(index);
            }
        }
        var result = new Mesh { name = "Jammer Charge " + (indicator + 1) };
        result.SetVertices(vertices);
        result.SetNormals(normals);
        result.SetUVs(0, uv);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;
    }
}
