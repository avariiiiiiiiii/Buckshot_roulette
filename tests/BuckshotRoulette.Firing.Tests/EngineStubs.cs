// These stubs test production dispatch, not Unity physics, audio or Photon transport.
namespace Photon.Pun { public struct PhotonMessageInfo { } }
namespace UnityEngine
{
    public struct Vector3
    {
        public static Vector3 operator -(Vector3 value) => value;
        public static Vector3 operator *(Vector3 value, float scale) => value;
    }
    public class Transform { public Vector3 position; public Vector3 forward; }
    public enum ForceMode { Impulse }
    public class Rigidbody
    {
        public int Impulses;
        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode) => Impulses++;
    }
}
public static class SemiFunc { public static bool Master; public static bool IsMasterClientOrSingleplayer() => Master; }
public class EnemyDirector
{
    public static EnemyDirector instance;
    public int Alerts;
    public void SetInvestigate(UnityEngine.Vector3 position, float radius) => Alerts++;
}
public class ItemBattery { public int Spent; public void RemoveFullBar(int count) => Spent += count; }
public class PhysGrabObject { public UnityEngine.Rigidbody rb = new(); }
public class ItemGun
{
    public UnityEngine.Transform gunMuzzle = new();
    public UnityEngine.Transform transform = new();
    public float gunRecoilForce = 1f;
    public float investigateRadius = 30f;
    public ItemBattery Battery = new();
    public PhysGrabObject Grab = new();
    internal BuckshotRoulette.Items.SawedOff Sawed;
    public int NativeShots, ObservedShot, TriggerAnimations;
    public bool ThrowOnShot;
    public T GetComponent<T>() where T : class => typeof(T) == typeof(ItemBattery) ? Battery as T
        : typeof(T) == typeof(PhysGrabObject) ? Grab as T : Sawed as T;
    public void StartTriggerAnimation() => TriggerAnimations++;
    public void ShootRPC(Photon.Pun.PhotonMessageInfo info = default)
    {
        NativeShots++;
        ObservedShot = BuckshotRoulette.Shells.ShotTag.Pending;
        if (ThrowOnShot) throw new System.InvalidOperationException();
    }
}
namespace BuckshotRoulette.Items
{
    internal class SawedOff
    {
        public bool Active = true;
        public int Consumed;
        public void Consume(int damage) { Consumed++; Active = false; }
    }
}
namespace BuckshotRoulette.Shells
{
    internal static class ShotTag { public static int Pending; }
    internal static class GunFx
    {
        public static int Blanks;
        public static void Blank(UnityEngine.Transform muzzle) => Blanks++;
    }
}
