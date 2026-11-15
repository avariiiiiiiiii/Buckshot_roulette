using System.Collections;
using BuckshotRoulette.Core;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

internal sealed class Handcuffs : MonoBehaviour
{
    private const float ReachMeters = 2f;
    private static readonly Quaternion HoldPitch = Quaternion.Euler(8f, 0f, 0f);
    private static readonly Quaternion HoldYaw = Quaternion.identity;

    private PhysGrabObject _grab;
    private PhotonView _view;
    private ItemAttributes _attributes;
    private bool _used;
    private float _requestCooldown;

    private void Awake()
    {
        _grab = GetComponent<PhysGrabObject>();
        _view = GetComponent<PhotonView>();
        _attributes = GetComponent<ItemAttributes>();
    }

    private void Update()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer() && _grab.playerGrabbing.Count > 0)
        {
            _grab.TurnXYZ(HoldPitch, HoldYaw, Quaternion.identity);
            _grab.OverrideGrabVerticalPosition(-0.2f);
        }

        if (_requestCooldown > 0f) _requestCooldown -= Time.deltaTime;
        if (_used || _requestCooldown > 0f || !_grab.heldByLocalPlayer
            || PlayerController.instance == null || PlayerController.instance.InputDisableTimer > 0f
            || !SemiFunc.InputDown(InputKey.Interact)) return;

        var camera = Camera.main;
        if (camera == null) return;
        var target = FindTarget(camera.transform.position, camera.transform.forward, PlayerAvatar.instance, out var hitPoint);
        if (target == null) return;

        _requestCooldown = 0.5f;
        if (SemiFunc.IsMultiplayer())
        {
            int index = SemiFunc.EnemyGetIndex(target);
            if (index < 0 || _view == null) return;
            _view.RPC(nameof(CuffRequestRPC), RpcTarget.MasterClient, index, hitPoint);
        }
        else
            TryCuff(target, PlayerAvatar.instance, hitPoint);
    }

    private Enemy FindTarget(Vector3 origin, Vector3 direction, PlayerAvatar holder, out Vector3 hitPoint)
    {
        hitPoint = default;
        var hits = Physics.RaycastAll(origin, direction, ReachMeters, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.transform.IsChildOf(transform)
                || (holder != null && hit.transform.GetComponentInParent<PlayerAvatar>() == holder)) continue;
            // Enemy's Controller and Rigidbody are siblings in native prefabs.
            var body = hit.transform.GetComponentInParent<EnemyRigidbody>();
            var enemy = body != null ? body.enemy : hit.transform.GetComponentInParent<Enemy>();
            if (enemy == null) enemy = hit.transform.GetComponentInParent<EnemyParent>()?.Enemy;
            if (enemy != null)
            {
                hitPoint = hit.point;
                return CuffedEnemy.HasHands(enemy) ? enemy : null;
            }
            // A closer solid object blocks targeting enemies behind it.
            if (hit.collider != null && !hit.collider.isTrigger) return null;
        }
        return null;
    }

    [PunRPC]
    private void CuffRequestRPC(int enemyIndex, Vector3 hitPoint, PhotonMessageInfo info = default)
    {
        // Requests originate from the holding client, so MasterOnlyRPC (sender == master) would reject them.
        if (!SemiFunc.IsMasterClientOrSingleplayer() || info.Sender == null || _used || _grab == null) return;
        PlayerAvatar holder = null;
        foreach (var grabber in _grab.playerGrabbing)
        {
            var player = grabber != null ? grabber.playerAvatar : null;
            if (player == null || player.photonView == null || player.photonView.OwnerActorNr != info.Sender.ActorNumber) continue;
            holder = player;
            break;
        }
        if (holder == null) return;

        TryCuff(SemiFunc.EnemyGetFromIndex(enemyIndex), holder, hitPoint);
    }

    private void TryCuff(Enemy target, PlayerAvatar holder, Vector3 hitPoint)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || _used || target == null || holder == null
            || target.EnemyParent == null || !target.EnemyParent.Spawned
            || (target.Health != null && target.Health.dead)
            || !CuffedEnemy.HasHands(target)
            || CuffedEnemy.IsTargetCuffed(target)) return;

        // Recheck the physical surface and intervening solids on the host, rather than the nav root at the enemy's feet.
        var eyes = holder.PlayerVisionTarget != null ? holder.PlayerVisionTarget.VisionTransform : null;
        if (eyes == null) return;
        Vector3 aim = hitPoint - eyes.position;
        float distance = aim.magnitude;
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > ReachMeters || distance < .001f
            || FindTarget(eyes.position, aim / distance, holder, out _) != target) return;

        var cuffed = CuffedEnemy.On(target);
        if (cuffed == null || !cuffed.ApplyOnMaster()) return;
        _used = true;
        StatsManager.instance.ItemRemove(_attributes.instanceName);
        _attributes.DisableUI(_disable: true);
        var equippable = GetComponent<ItemEquippable>();
        if (equippable != null) Destroy(equippable);
        Plugin.Log.LogInfo($"Наручники: обезоружен {target.name} до конца забега");
        StartCoroutine(DestroyAfterRpc());
    }

    private IEnumerator DestroyAfterRpc()
    {
        yield return new WaitForSeconds(0.2f);
        if (_grab != null && SemiFunc.IsMasterClientOrSingleplayer()) _grab.DestroyPhysGrabObject();
    }
}
