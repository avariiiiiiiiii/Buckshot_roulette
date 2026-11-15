using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Items;

internal static class AdrenalineItem
{
    internal static Item Item { get; private set; }
    internal static void Register(Transform holder)
    {
        const string name = "Item Buckshot Adrenaline";
        var template = Resources.Load<GameObject>("Items/Item Health Pack Small");
        if (template == null) throw new System.InvalidOperationException("Не найден шаблон адреналина");
        var prefab = Object.Instantiate(template, holder);
        prefab.name = name;
        Object.DestroyImmediate(prefab.GetComponent<ItemHealthPack>());
        foreach (string childName in new[] { "Heal Effects", "Reject Effect", "Light" })
        {
            var child = prefab.transform.Find(childName);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }
        PrefabUtil.DisableDeadEvents(prefab);
        PrefabUtil.MakeDurableTool(prefab);
        var model = ModelLoader.Scaled(ModelLoader.Load("adrenaline"), 1.7f);
        var mesh = prefab.transform.Find("Mesh");
        PrefabUtil.ResetLocal(mesh);
        mesh.GetComponent<MeshFilter>().sharedMesh = model.Mesh;
        mesh.GetComponent<MeshRenderer>().sharedMaterials = model.Materials;
        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            PrefabUtil.ResetLocal(box.transform);
            box.center = model.Mesh.bounds.center; box.size = model.Mesh.bounds.size;
            var preview = box.GetComponent<MeshFilter>();
            if (preview != null) preview.sharedMesh = model.Mesh;
        }
        var attributes = prefab.GetComponent<ItemAttributes>();
        var item = Object.Instantiate(attributes.item);
        item.name = name; item.itemName = Texts.T("Адреналин", "Adrenaline");
        item.itemNameLocalized = null; item.itemType = SemiFunc.itemType.tool;
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = name + " Value"; value.valueMin = 1250f; value.valueMax = 2500f;
        item.value = value; attributes.item = item;
        attributes.icon = null; attributes.hasIcon = false;
        prefab.AddComponent<Adrenaline>();
        if (REPOLib.Modules.Items.RegisterItem(attributes) == null)
            throw new System.InvalidOperationException("Адреналин не зарегистрирован");
        Item = item;
        Plugin.Log.LogInfo("Предмет зарегистрирован: " + name);
    }
}
