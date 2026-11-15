using System.Collections;
using System.Collections.Generic;
using BuckshotRoulette.Core;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// Ножовка из магазина (2–5 тыс., одноразовая): держишь её, смотришь на оружие в пределах 3 м (оно подсвечено жёлтым)
/// и жмёшь E — оружие пропилено (SawedOff: следующий выстрел/удар ×2, не стакается). Цель ищет тот, кто нажал (у него камера),
/// решает хост: тратит ножовку (ItemRemove), рассылает пропил всем и после пропила убирает ножовку.
/// </summary>
internal static class HacksawItem
{
    private const string TemplatePath = "Items/Item Health Pack Small";
    private const string ItemName = "Item Hacksaw";

    public static Item Item { get; private set; }

    public static void Register(Transform holder)
    {
        var template = Resources.Load<GameObject>(TemplatePath);
        if (template == null)
        {
            Plugin.Log.LogError($"Шаблон ножовки не найден: {TemplatePath}");
            return;
        }

        var prefab = Object.Instantiate(template, holder);
        prefab.name = ItemName;
        Object.DestroyImmediate(prefab.GetComponent<ItemHealthPack>());
        foreach (var child in new[] { "Heal Effects", "Reject Effect", "Light" })
        {
            var t = prefab.transform.Find(child);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        Plugin.Log.LogInfo($"Ножовка: отключено вызовов удалённых целей: {PrefabUtil.DisableDeadEvents(prefab)}");
        PrefabUtil.MakeDurableTool(prefab);

        var model = ModelLoader.Load("hacksaw");
        var mesh = prefab.transform.Find("Mesh");
        PrefabUtil.ResetLocal(mesh);
        mesh.GetComponent<MeshFilter>().sharedMesh = model.Mesh;
        mesh.GetComponent<MeshRenderer>().sharedMaterials = model.Materials;
        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            PrefabUtil.ResetLocal(box.transform);
            box.center = model.Mesh.bounds.center;
            box.size = model.Mesh.bounds.size;
            var preview = box.GetComponent<MeshFilter>();
            if (preview != null) preview.sharedMesh = model.Mesh;
        }

        var attributes = prefab.GetComponent<ItemAttributes>();
        var item = Object.Instantiate(attributes.item);
        item.name = ItemName;
        item.itemName = Texts.T("Ножовка", "Hacksaw");
        item.itemNameLocalized = null;
        item.itemType = SemiFunc.itemType.tool;
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = ItemName + " Value";
        value.valueMin = 500f;      // в магазине ×4 и вверх до тысяч: 2–5 тыс.
        value.valueMax = 1250f;
        item.value = value;
        attributes.item = item;

        prefab.AddComponent<Hacksaw>();
        if (REPOLib.Modules.Items.RegisterItem(attributes) == null)
        {
            Plugin.Log.LogError("Ножовка не зарегистрирована");
            return;
        }
        Item = item;
        Plugin.Log.LogInfo($"Предмет зарегистрирован: {ItemName}");
    }
}

/// <summary>
/// Ножовка в руках. Держится перед лицом, полотном вперёд (как ружьё). У держащего оружие в прицеле подсвечивается жёлтым.
/// Нажал E — хост проверяет цель и рассылает пропил: у всех полотно ходит взад-вперёд, звук пилы, искры на цели;
/// в конце пропила на цели появляется усиление (у обреза отваливается кусок ствола), ножовка исчезает — она одноразовая.
/// </summary>
internal sealed class Hacksaw : MonoBehaviour
{
    private const float ReachMeters = 3f;
    private const float StrokeAmplitude = 0.06f;    // м: ход полотна при пропиле
    private const float StrokesPerSecond = 2.6f;
    // держание перед лицом: длинная ось ножовки (X модели) — вперёд от камеры, рама сверху, чуть наклонена вниз
    private static readonly Quaternion HoldPitch = Quaternion.Euler(8f, 0f, 0f);
    private static readonly Quaternion HoldYaw = Quaternion.Euler(0f, 90f, 0f);
    private static readonly HashSet<int> SawingTargets = new HashSet<int>();

    private PhysGrabObject _grab;
    private PhotonView _view;
    private ItemAttributes _attributes;
    private Transform _mesh;
    private Vector3 _meshRest;
    private bool _used;
    private float _sawTime = -1f;
    private int _reservedTargetId;

    private ItemAttributes _highlighted;
    private List<GameObject> _outline = new List<GameObject>();

    private void Awake()
    {
        _grab = GetComponent<PhysGrabObject>();
        _view = GetComponent<PhotonView>();
        _attributes = GetComponent<ItemAttributes>();
        _mesh = transform.Find("Mesh");
        _meshRest = _mesh.localPosition;
    }

    private void Update()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && _grab.playerGrabbing.Count > 0)
        {
            _grab.TurnXYZ(HoldPitch, HoldYaw, Quaternion.identity);
            _grab.OverrideGrabVerticalPosition(-0.2f);
        }
        AnimateSawing();

        bool held = !_used && _grab.heldByLocalPlayer;
        var target = held ? FindTarget() : null;
        Highlight(target);
        if (!held || target == null) return;
        if (PlayerController.instance.InputDisableTimer > 0f || !SemiFunc.InputDown(InputKey.Interact)) return;

        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(SawRequestRPC), RpcTarget.MasterClient, target.GetComponent<PhotonView>().ViewID);
        else Saw(target);
    }

    private void OnDisable() => Highlight(null);

    private void OnDestroy()
    {
        if (_reservedTargetId != 0) SawingTargets.Remove(_reservedTargetId);
    }

    /// <summary>Первое, во что упирается луч из камеры, если это оружие (кроме самой ножовки).</summary>
    private ItemAttributes FindTarget()
    {
        var camera = Camera.main;
        if (camera == null) return null;
        var hits = Physics.RaycastAll(camera.transform.position, camera.transform.forward, ReachMeters, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.GetComponentInParent<PlayerAvatar>() != null) continue;
            var item = hit.transform.GetComponentInParent<ItemAttributes>();
            if (item != null && item.gameObject == gameObject) continue;
            return item != null && DealsDamage(item) ? item : null;
        }
        return null;
    }

    /// <summary>Пилить имеет смысл только то, что наносит урон: огнестрел, ближний бой, всё с HurtCollider.</summary>
    private static bool DealsDamage(ItemAttributes item) =>
        item.GetComponent<ItemGun>() != null || item.GetComponentInChildren<ItemMelee>(true) != null
        || item.GetComponentInChildren<HurtCollider>(true) != null;

    private void Highlight(ItemAttributes target)
    {
        if (target == _highlighted) return;
        foreach (var hull in _outline)
            if (hull != null) Destroy(hull);
        _outline.Clear();
        _highlighted = target;
        if (target != null) _outline = HacksawFx.Outline(target.gameObject);
    }

    [PunRPC]
    private void SawRequestRPC(int targetViewId, PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info) || info.Sender == null || _used || _grab == null) return;
        PlayerAvatar holder = null;
        foreach (var grabber in _grab.playerGrabbing)
        {
            var player = grabber != null ? grabber.playerAvatar : null;
            if (player == null || player.photonView == null || player.photonView.OwnerActorNr != info.Sender.ActorNumber) continue;
            holder = player;
            break;
        }
        if (holder == null) return;

        var view = PhotonView.Find(targetViewId);
        var target = view != null ? view.GetComponent<ItemAttributes>() : null;
        if (target == null || !DealsDamage(target)
            || Vector3.Distance(holder.transform.position, target.transform.position) > ReachMeters + 1f) return;
        Saw(target);
    }

    /// <summary>Хост: пропил разрешён, если усиление ещё не наложено (не стакается); ножовка тратится.</summary>
    private void Saw(ItemAttributes target)
    {
        if (_used || target == null || !DealsDamage(target)) return;
        var targetView = target.GetComponent<PhotonView>();
        if (targetView == null) return;
        int targetId = targetView.ViewID != 0 ? targetView.ViewID : target.GetInstanceID();
        if (SawingTargets.Contains(targetId)) return;
        var sawed = target.GetComponent<SawedOff>();
        if (sawed != null && sawed.Active)
        {
            Plugin.Log.LogInfo($"Ножовка: {target.name} уже пропилен, ножовка не тратится");
            return;
        }
        SawingTargets.Add(targetId);
        _reservedTargetId = targetId;
        _used = true;
        StatsManager.instance.ItemRemove(_attributes.instanceName);
        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(SawRPC), RpcTarget.All, targetView.ViewID);
        else StartCoroutine(Sawing(target.gameObject, targetId));
        Plugin.Log.LogInfo($"Ножовка: пилит {target.name}");
    }

    [PunRPC]
    private void SawRPC(int targetViewId, PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        var view = PhotonView.Find(targetViewId);
        if (view != null) StartCoroutine(Sawing(view.gameObject, targetViewId));
    }

    /// <summary>У всех: пропил идёт SawSeconds, потом усиление на цели; хост убирает ножовку.</summary>
    private IEnumerator Sawing(GameObject target, int reservationId)
    {
        _used = true;
        Highlight(null);
        _attributes.DisableUI(_disable: true);
        var equippable = GetComponent<ItemEquippable>();
        if (equippable != null) Destroy(equippable);
        _sawTime = 0f;
        var cut = target.GetComponentInChildren<BuckshotModel>(true);
        var gun = target.GetComponent<ItemGun>();
        Transform cutAnchor = cut != null ? cut.Gun.transform : gun != null ? gun.gunMuzzle : target.transform;
        Vector3 cutLocal = cut != null ? cut.CutLocal : Vector3.zero;
        HacksawFx.SawSound(cutAnchor.TransformPoint(cutLocal));
        HacksawFx.Sparks(cutAnchor, cutLocal);

        yield return new WaitForSeconds(HacksawFx.SawSeconds);
        if (target != null)
        {
            SawedOff.On(target).Arm();
            Plugin.Log.LogInfo($"Ножовка: пропилен {target.name}");
        }
        _sawTime = -1f;
        SawingTargets.Remove(reservationId);
        if (_reservedTargetId == reservationId) _reservedTargetId = 0;
        if (SemiFunc.IsMasterClientOrSingleplayer()) _grab.DestroyPhysGrabObject();
    }

    private void AnimateSawing()
    {
        if (_sawTime < 0f) return;
        _sawTime += Time.deltaTime;
        // полотно — вдоль X модели
        _mesh.localPosition = _meshRest + Vector3.right * (Mathf.Sin(_sawTime * StrokesPerSecond * Mathf.PI * 2f) * StrokeAmplitude);
    }
}
