// Managed stand-ins exercise production targeting/state code. They do not simulate Unity physics, rendering or Photon.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : Component => gameObject.GetComponentInParent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public string name;
        public Transform transform;
        public GameObject(string name = "Object") { this.name = name; transform = new Transform { gameObject = this }; components.Add(transform); }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this }; components.Add(component);
            typeof(T).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
            return component;
        }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault();
        public T GetComponentInParent<T>() where T : Component
        {
            for (var current = transform; current != null; current = current.parent)
            { var component = current.gameObject.GetComponent<T>(); if (component != null) return component; }
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component => components.OfType<T>().Concat(transform.children.SelectMany(t => t.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }
    public class MonoBehaviour : Component { public void StartCoroutine(IEnumerator value) { } }
    public class Transform : Component
    {
        public readonly List<Transform> children = new();
        private Transform parentValue;
        public Transform parent { get => parentValue; set { parentValue?.children.Remove(this); parentValue = value; value?.children.Add(this); } }
        public Vector3 position, localPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion rotation = Quaternion.identity, localRotation = Quaternion.identity;
        public Vector3 forward => Vector3.forward;
        public Vector3 right => Vector3.right;
        public Vector3 up => Vector3.up;
        public void SetPositionAndRotation(Vector3 value, Quaternion rot) { position = value; rotation = rot; }
        public bool IsChildOf(Transform ancestor) { for (var t = this; t != null; t = t.parent) if (t == ancestor) return true; return false; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 right => new(1, 0, 0);
        public float sqrMagnitude => x*x + y*y + z*z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator -(Vector3 a) => zero-a;
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b, a.y*b, a.z*b);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x/b, a.y/b, a.z/b);
        public static float Distance(Vector3 a, Vector3 b) => (a-b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x + a.y*b.y + a.z*b.z;
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 normal) => vector - normal * Dot(vector, normal);
    }
    public struct Quaternion
    {
        public static Quaternion identity => new();
        public static Quaternion Euler(float x, float y, float z) => new();
        public static Quaternion FromToRotation(Vector3 a, Vector3 b) => new();
        public static Quaternion operator *(Quaternion a, Quaternion b) => new();
    }
    public class Collider : Component { public bool enabled = true; public bool isTrigger; }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 point; public Transform transform => collider.transform; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static RaycastHit[] Hits = Array.Empty<RaycastHit>();
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float distance, int mask, QueryTriggerInteraction trigger) => Hits.Where(hit => hit.distance <= distance).ToArray();
    }
    public static class Time { public static float deltaTime = .02f; }
    public static class Mathf
    {
        public static float Min(float a, float b) => MathF.Min(a,b);
        public static float Max(float a, float b) => MathF.Max(a,b);
        public static float Sqrt(float a) => MathF.Sqrt(a);
        public static float Clamp(float a,float b,float c) => Math.Clamp(a,b,c);
    }
    public class WaitForSeconds { public WaitForSeconds(float value) { } }
    public class Camera : Component { public static Camera main; }
    public enum HumanBodyBones { LeftHand, RightHand, LeftLowerArm, RightLowerArm, LeftUpperArm, RightUpperArm }
    public class Animator : Component
    {
        public bool isActiveAndEnabled = true, isHuman;
        public readonly Dictionary<HumanBodyBones, Transform> Bones = new();
        public Transform GetBoneTransform(HumanBodyBones bone) => Bones.GetValueOrDefault(bone);
    }
    public class SkinnedMeshRenderer : Component { public Transform[] bones = Array.Empty<Transform>(); }
    public class Mesh { }
    public class Material { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class MeshRenderer : Component { public Material[] sharedMaterials; }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
}
namespace Photon.Pun
{
    public class PunRPC : Attribute { }
    public enum RpcTarget { MasterClient, AllBufferedViaServer, All }
    public class PhotonView : UnityEngine.Component
    {
        public int ViewID, OwnerActorNr;
        public static PhotonView Find(int id) => null;
        public void RPC(string method, RpcTarget target, params object[] values) { }
    }
    public class Sender { public int ActorNumber; }
    public struct PhotonMessageInfo { public Sender Sender; }
}
public enum EnemyState { Spawn, Despawn, Chase, LookUnder, Roaming }
public class Enemy : UnityEngine.Component
{
    public Photon.Pun.PhotonView PhotonView;
    public EnemyParent EnemyParent;
    public EnemyRigidbody Rigidbody;
    public EnemyHealth Health;
    public UnityEngine.Transform CenterTransform;
    public EnemyState CurrentState = EnemyState.Roaming;
}
public class EnemyParent : UnityEngine.Component { public Enemy Enemy; public bool Spawned = true; }
public class EnemyRigidbody : UnityEngine.Component { public Enemy enemy; }
public class SlowWalkerAttack : UnityEngine.Component { public Enemy enemy; public bool enabled = true; }
public class EnemyHealth : UnityEngine.Component { public bool dead; }
public class EnemyTriggerAttack : UnityEngine.Component { public bool Attack; }
public class PlayerVisionTarget { public UnityEngine.Transform VisionTransform; }
public class PlayerAvatar : UnityEngine.Component
{
    public static PlayerAvatar instance;
    public Photon.Pun.PhotonView photonView;
    public PlayerVisionTarget PlayerVisionTarget;
}
public class PhysGrabber { public PlayerAvatar playerAvatar; }
public class PhysGrabObject : UnityEngine.Component
{
    public readonly List<PhysGrabber> playerGrabbing = new();
    public bool heldByLocalPlayer;
    public void TurnXYZ(UnityEngine.Quaternion a, UnityEngine.Quaternion b, UnityEngine.Quaternion c) { }
    public void OverrideGrabVerticalPosition(float value) { }
    public void DestroyPhysGrabObject() { }
}
public class ItemAttributes : UnityEngine.Component { public string instanceName; public void DisableUI(bool _disable) { } }
public class ItemEquippable : UnityEngine.Component { }
public class PlayerController { public static PlayerController instance = new(); public float InputDisableTimer; }
public enum InputKey { Interact }
public class StatsManager
{
    public static StatsManager instance = new();
    public int Removed;
    public void ItemRemove(string name) => Removed++;
}
public static class SemiFunc
{
    public static bool Multiplayer, Master = true;
    public static readonly List<Enemy> Enemies = new();
    public static bool IsMasterClientOrSingleplayer() => Master;
    public static bool IsMultiplayer() => Multiplayer;
    public static bool InputDown(InputKey key) => true;
    public static bool MasterOnlyRPC(Photon.Pun.PhotonMessageInfo info) => Master;
    public static int EnemyGetIndex(Enemy enemy) => Enemies.IndexOf(enemy);
    public static Enemy EnemyGetFromIndex(int index) => index >= 0 && index < Enemies.Count ? Enemies[index] : null;
}
namespace BuckshotRoulette.Core { }
namespace BuckshotRoulette
{
    public static class Plugin { public static Logger Log = new(); }
    public class Logger { public void LogInfo(string message) { } }
}
namespace BuckshotRoulette.Items
{
    public static class HandcuffsFx { public static void Play(UnityEngine.Vector3 value) { } }
    public class Model { public UnityEngine.Mesh Mesh; public UnityEngine.Material[] Materials; }
    public static class HandcuffsItem { public static Model VisualModel; }
}
