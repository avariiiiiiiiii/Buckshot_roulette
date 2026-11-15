using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Items;

internal static class HandcuffsItem
{
    private const string TemplatePath = "Items/Item Health Pack Small";
    private const string ItemName = "Item Handcuffs";

    public static Item Item { get; private set; }
    public static ModelLoader.Model VisualModel { get; private set; }

    public static void Register(Transform holder)
    {
        var template = Resources.Load<GameObject>(TemplatePath);
        if (template == null)
        {
            Plugin.Log.LogError($"Шаблон наручников не найден: {TemplatePath}");
            return;
        }

        var prefab = UnityEngine.Object.Instantiate(template, holder);
        prefab.name = ItemName;
        UnityEngine.Object.DestroyImmediate(prefab.GetComponent<ItemHealthPack>());
        foreach (var childName in new[] { "Heal Effects", "Reject Effect", "Light" })
        {
            var child = prefab.transform.Find(childName);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        PrefabUtil.DisableDeadEvents(prefab);
        PrefabUtil.MakeDurableTool(prefab);

        VisualModel = ModelLoader.Load("handcuffs");
        var itemModel = ModelLoader.Scaled(VisualModel, 1.6f);
        var meshTransform = prefab.transform.Find("Mesh");
        PrefabUtil.ResetLocal(meshTransform);
        meshTransform.GetComponent<MeshFilter>().sharedMesh = itemModel.Mesh;
        meshTransform.GetComponent<MeshRenderer>().sharedMaterials = VisualModel.Materials;
        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            PrefabUtil.ResetLocal(box.transform);
            box.center = itemModel.Mesh.bounds.center;
            box.size = itemModel.Mesh.bounds.size;
            var preview = box.GetComponent<MeshFilter>();
            if (preview != null) preview.sharedMesh = itemModel.Mesh;
        }

        var attributes = prefab.GetComponent<ItemAttributes>();
        var item = UnityEngine.Object.Instantiate(attributes.item);
        item.name = ItemName;
        item.itemName = Texts.T("Наручники", "Handcuffs");
        item.itemNameLocalized = null;
        item.itemType = SemiFunc.itemType.tool;
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = ItemName + " Value";
        value.valueMin = 3750f;
        value.valueMax = 3750f;
        item.value = value;
        attributes.item = item;
        attributes.icon = null;
        attributes.hasIcon = false;

        prefab.AddComponent<Handcuffs>();
        if (REPOLib.Modules.Items.RegisterItem(attributes) == null)
        {
            Plugin.Log.LogError("Наручники не зарегистрированы");
            return;
        }
        Item = item;
        Plugin.Log.LogInfo($"Предмет зарегистрирован: {ItemName}");
    }
}
