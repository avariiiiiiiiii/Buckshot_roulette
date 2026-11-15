using System;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

[DefaultExecutionOrder(10000)]
internal sealed class CuffedEnemy : MonoBehaviour
{
    private Enemy _enemy;
    private Animator _animator;
    private Transform _rigRoot;
    private Transform _leftHand;
    private Transform _rightHand;
    private ArmChain _leftArm;
    private ArmChain _rightArm;
    private GameObject _cuffVisual;
    public bool IsCuffed { get; private set; }

    private readonly struct ArmChain
    {
        public readonly Transform UpperArm;
        public readonly Transform Forearm;
        public readonly Transform Hand;

        public ArmChain(Transform upperArm, Transform forearm, Transform hand)
        {
            UpperArm = upperArm;
            Forearm = forearm;
            Hand = hand;
        }

        public bool IsValid => UpperArm != null && Forearm != null && Hand != null;
    }

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
        // EnemyRigidbody.Awake can run after Enemy.Awake. Resolve the model again when cuffs are applied.
    }

    private static Transform RigRoot(Enemy enemy) => enemy == null ? null
        : enemy.EnemyParent != null ? enemy.EnemyParent.transform
        : enemy.GetComponentInParent<EnemyParent>()?.transform ?? enemy.transform;

    private void ResolveRig()
    {
        _rigRoot = RigRoot(_enemy);
        if (_rigRoot == null) return;
        _animator = _rigRoot.GetComponentInChildren<Animator>(true);
        FindHandPair();
        _leftArm = FindArmChain(_leftHand);
        _rightArm = FindArmChain(_rightHand);
        if (_animator != null && _animator.isHuman)
        {
            _leftArm = new ArmChain(_animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), _leftHand);
            _rightArm = new ArmChain(_animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerArm), _rightHand);
        }
    }

    public static CuffedEnemy On(Enemy enemy)
    {
        if (enemy == null) return null;
        var state = enemy.GetComponent<CuffedEnemy>();
        return state != null ? state : enemy.gameObject.AddComponent<CuffedEnemy>();
    }

    public static bool IsTargetCuffed(Enemy enemy) => enemy != null && enemy.GetComponent<CuffedEnemy>()?.IsCuffed == true;

    public static bool HasHands(Enemy enemy)
    {
        var root = RigRoot(enemy);
        if (root == null) return false;
        // Include skinned rig bones as well as visible object names; enemy prefabs differ in how they structure meshes.
        var animator = root.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman && animator.GetBoneTransform(HumanBodyBones.LeftHand) != null
            && animator.GetBoneTransform(HumanBodyBones.RightHand) != null) return true;
        return root.GetComponentsInChildren<Transform>(true).Any(t => IsHandName(t.name))
            || root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null && renderer.bones != null)
                .SelectMany(renderer => renderer.bones)
                .Any(bone => bone != null && IsHandName(bone.name));
    }

    private static bool IsHandName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string normalized = name.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        return normalized.Contains("hand") || normalized.Contains("wrist") || normalized.Contains("forearm")
            || normalized.Contains("upperarm") || normalized.Contains("armleft") || normalized.Contains("armright");
    }

    public bool ApplyOnMaster()
    {
        if (IsCuffed || !SemiFunc.IsMasterClientOrSingleplayer()) return false;
        var view = _enemy != null ? _enemy.PhotonView : GetComponent<PhotonView>();
        if (SemiFunc.IsMultiplayer() && (view == null || view.ViewID == 0)) return false;
        ResolveRig();
        IsCuffed = true;
        StopActiveAttack();
        EnsureCuffVisual();
        if (SemiFunc.IsMultiplayer())
        {
            if (view != null)
            {
                view.RPC(nameof(ApplyCuffRPC), RpcTarget.AllBufferedViaServer);
                Vector3 position = _enemy != null && _enemy.CenterTransform != null
                    ? _enemy.CenterTransform.position
                    : transform.position;
                view.RPC(nameof(CuffSoundRPC), RpcTarget.All, position);
            }
        }
        else HandcuffsFx.Play(_enemy != null && _enemy.CenterTransform != null ? _enemy.CenterTransform.position : transform.position);
        return true;
    }

    [PunRPC]
    private void ApplyCuffRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        ResolveRig();
        IsCuffed = true;
        StopActiveAttack();
        EnsureCuffVisual();
    }

    [PunRPC]
    private void CuffSoundRPC(Vector3 position, PhotonMessageInfo info = default)
    {
        if (SemiFunc.MasterOnlyRPC(info)) HandcuffsFx.Play(position);
    }

    private void Update()
    {
        if (!IsCuffed) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || _enemy == null) return;

        // The common enemy state machine uses Chase/LookUnder for contact attacks. Keep the mob roaming,
        // and suppress the attack trigger separately so it cannot damage players or grabbed objects.
        if (_enemy.CurrentState == EnemyState.Chase || _enemy.CurrentState == EnemyState.LookUnder)
            _enemy.CurrentState = EnemyState.Roaming;
        var trigger = _enemy.GetComponent<EnemyTriggerAttack>();
        if (trigger != null) trigger.Attack = false;
    }

    private void LateUpdate()
    {
        if (!IsCuffed) return;
        PoseArms();
        UpdateCuffVisual();
    }

    private void StopActiveAttack()
    {
        // Trudge's separate attack object continues its vacuum/impact even after its AI leaves Attack.
        // Disabling it runs the native OnDisable cleanup on every peer.
        if (_rigRoot == null) return;
        foreach (var attack in _rigRoot.GetComponentsInChildren<SlowWalkerAttack>(true))
            if (attack.enemy == _enemy) attack.enabled = false;
    }

    private void OnDestroy()
    {
        if (_cuffVisual != null) Destroy(_cuffVisual);
    }

    private void EnsureCuffVisual()
    {
        if (_cuffVisual != null || _leftHand == null || _rightHand == null) return;
        var model = HandcuffsItem.VisualModel;
        if (model == null) return;
        var renderer = new GameObject("Handcuffs Visual");
        renderer.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
        renderer.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
        _cuffVisual = renderer;
    }

    private void UpdateCuffVisual()
    {
        if (_cuffVisual == null || _leftHand == null || _rightHand == null) return;
        Vector3 span = _rightHand.position - _leftHand.position;
        _cuffVisual.transform.SetPositionAndRotation((_leftHand.position + _rightHand.position) * 0.5f,
            Quaternion.FromToRotation(Vector3.right, span));
        float scale = Mathf.Clamp(span.magnitude / 0.20f, 0.5f, 2.5f);
        _cuffVisual.transform.localScale = Vector3.one * scale;
    }

    private void FindHandPair()
    {
        _leftHand = null;
        _rightHand = null;
        if (_animator != null && _animator.isHuman)
        {
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (_leftHand != null && _rightHand != null) return;
        }
        var candidates = _rigRoot.GetComponentsInChildren<Transform>(true)
            .Where(t => t != transform && IsWristName(t.name))
            .ToArray();
        if (candidates.Length < 2) return;

        // Prefer explicit left/right bones, then the dealer's rear/front hands, otherwise the two most separated wrists.
        _leftHand = candidates.FirstOrDefault(t => HasSide(t.name, "left"))
            ?? candidates.FirstOrDefault(t => HasSide(t.name, "rear"));
        _rightHand = candidates.FirstOrDefault(t => HasSide(t.name, "right"))
            ?? candidates.FirstOrDefault(t => HasSide(t.name, "front"));
        if (_leftHand != null && _rightHand != null && _leftHand != _rightHand) return;

        float farthest = 0f;
        for (int i = 0; i < candidates.Length; i++)
        for (int j = i + 1; j < candidates.Length; j++)
        {
            float distance = (candidates[i].position - candidates[j].position).sqrMagnitude;
            if (distance <= farthest) continue;
            farthest = distance;
            _leftHand = candidates[i];
            _rightHand = candidates[j];
        }
    }

    private ArmChain FindArmChain(Transform hand)
    {
        if (hand == null) return default;
        Transform forearm = hand.parent;
        while (forearm != null && forearm != _rigRoot && !IsForearmName(forearm.name)) forearm = forearm.parent;
        if (forearm == null || forearm == _rigRoot) return default;

        Transform upperArm = forearm.parent;
        while (upperArm != null && upperArm != _rigRoot && !IsUpperArmName(upperArm.name)) upperArm = upperArm.parent;
        return upperArm == null || upperArm == _rigRoot ? default : new ArmChain(upperArm, forearm, hand);
    }

    private void PoseArms()
    {
        if (_enemy == null) return;
        var body = _enemy.Rigidbody != null ? _enemy.Rigidbody.transform : _enemy.transform;
        Vector3 center = _enemy.CenterTransform != null
            ? _enemy.CenterTransform.position
            : _enemy.transform.position + Vector3.up;
        Vector3 behind = -body.forward * 0.28f - body.up * 0.12f;
        PoseArm(_leftArm, center + behind - body.right * 0.1f, -1f);
        PoseArm(_rightArm, center + behind + body.right * 0.1f, 1f);
    }

    private void PoseArm(ArmChain arm, Vector3 target, float side)
    {
        if (!arm.IsValid) return;
        Transform shoulder = arm.UpperArm;
        Transform elbow = arm.Forearm;
        Transform hand = arm.Hand;
        Vector3 shoulderPosition = shoulder.position;
        Vector3 elbowPosition = elbow.position;
        Vector3 handPosition = hand.position;
        float upperLength = Vector3.Distance(shoulderPosition, elbowPosition);
        float lowerLength = Vector3.Distance(elbowPosition, handPosition);
        Vector3 toTarget = target - shoulderPosition;
        float distance = Mathf.Min(toTarget.magnitude, upperLength + lowerLength - 0.01f);
        if (distance < 0.01f || upperLength < 0.01f || lowerLength < 0.01f) return;

        Vector3 direction = toTarget.normalized;
        Vector3 bendDirection = Vector3.ProjectOnPlane(_enemy.transform.right * side, direction).normalized;
        if (bendDirection.sqrMagnitude < 0.01f)
            bendDirection = Vector3.ProjectOnPlane(_enemy.transform.forward, direction).normalized;
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 desiredElbow = shoulderPosition + direction * along + bendDirection * bend;

        shoulder.rotation = Quaternion.FromToRotation(elbowPosition - shoulderPosition, desiredElbow - shoulderPosition) * shoulder.rotation;
        elbowPosition = elbow.position;
        handPosition = hand.position;
        elbow.rotation = Quaternion.FromToRotation(handPosition - elbowPosition, target - elbowPosition) * elbow.rotation;
    }

    private static bool IsWristName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string normalized = Normalize(name);
        return (normalized.Contains("hand") || normalized.Contains("wrist"))
            && !normalized.Contains("cover") && !normalized.Contains("finger") && !normalized.Contains("thumb");
    }

    private static bool IsForearmName(string name)
    {
        string normalized = Normalize(name);
        return normalized.Contains("forearm") || normalized.Contains("lowerarm") || normalized.Contains("elbow");
    }

    private static bool IsUpperArmName(string name)
    {
        string normalized = Normalize(name);
        return normalized.Contains("upperarm") || normalized.Contains("shoulder")
            || (normalized.Contains("arm") && !IsForearmName(normalized));
    }

    private static bool HasSide(string name, string side)
    {
        string normalized = Normalize(name);
        if (normalized.Contains(side)) return true;
        if (side == "left") return normalized.EndsWith("handl") || normalized.EndsWith("wristl");
        if (side == "right") return normalized.EndsWith("handr") || normalized.EndsWith("wristr");
        return false;
    }

    private static string Normalize(string name) => string.IsNullOrEmpty(name)
        ? string.Empty
        : name.Replace("_", string.Empty).Replace("-", string.Empty).Replace(".", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
}
