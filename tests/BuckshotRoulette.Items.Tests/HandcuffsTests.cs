using System;
using System.Reflection;
using BuckshotRoulette.Items;
using Photon.Pun;
using UnityEngine;
using Xunit;

public class HandcuffsTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public HandcuffsTests()
    {
        SemiFunc.Master = true; SemiFunc.Multiplayer = false; SemiFunc.Enemies.Clear();
        Physics.Hits = Array.Empty<RaycastHit>(); StatsManager.instance.Removed = 0;
        Camera.main = new GameObject("Camera").AddComponent<Camera>();
        PlayerAvatar.instance = new GameObject("Player").AddComponent<PlayerAvatar>();
        PlayerAvatar.instance.PlayerVisionTarget = new PlayerVisionTarget { VisionTransform = Camera.main.transform };
    }
    private static GameObject Child(GameObject parent, string name)
    { var go = new GameObject(name); go.transform.parent = parent.transform; return go; }
    private static Enemy Enemy(bool siblingBody = false)
    {
        var root = new GameObject("Enemy").AddComponent<EnemyParent>();
        var controller = Child(root.gameObject, "Controller").AddComponent<Enemy>();
        root.Enemy = controller; controller.EnemyParent = root;
        controller.PhotonView = controller.gameObject.AddComponent<PhotonView>(); // ViewID intentionally zero: native singleplayer.
        var body = siblingBody ? Child(root.gameObject, "Rigidbody") : controller.gameObject;
        controller.Rigidbody = body.AddComponent<EnemyRigidbody>(); controller.Rigidbody.enemy = controller;
        Child(body, "hand_left").transform.position = new Vector3(-.4f,1,0);
        Child(body, "hand_right").transform.position = new Vector3(.4f,1,0);
        SemiFunc.Enemies.Add(controller);
        return controller;
    }
    private static Handcuffs Cuffs()
    {
        var go = new GameObject("Cuffs");
        var grab = go.AddComponent<PhysGrabObject>(); grab.heldByLocalPlayer = true;
        grab.playerGrabbing.Add(new PhysGrabber { playerAvatar = PlayerAvatar.instance });
        go.AddComponent<PhotonView>(); go.AddComponent<ItemAttributes>();
        return go.AddComponent<Handcuffs>();
    }
    private static RaycastHit Hit(Collider collider, float distance) => new() { collider = collider, distance = distance, point = new Vector3(0,0,distance) };

    [Fact]
    public void SingleplayerEnemyWithoutNetworkIdCanBeCuffed()
    {
        var enemy = Enemy(); var cuffs = Cuffs();
        Physics.Hits = new[] { Hit(enemy.gameObject.AddComponent<Collider>(), 1f) };
        typeof(Handcuffs).GetMethod("Update", Private).Invoke(cuffs, null);
        Assert.True(CuffedEnemy.IsTargetCuffed(enemy)); Assert.Equal(1, StatsManager.instance.Removed);
    }
    [Fact]
    public void HandsAreFoundUnderSiblingPhysicsBody()
    { Assert.True(CuffedEnemy.HasHands(Enemy(siblingBody:true))); }
    [Fact]
    public void HeldCuffsDoNotObstructOwnAim()
    {
        var enemy = Enemy(); var cuffs = Cuffs();
        Physics.Hits = new[] { Hit(cuffs.gameObject.AddComponent<Collider>(), .2f), Hit(enemy.gameObject.AddComponent<Collider>(), 1f) };
        typeof(Handcuffs).GetMethod("Update", Private).Invoke(cuffs, null);
        Assert.True(CuffedEnemy.IsTargetCuffed(enemy));
    }
    [Fact]
    public void SiblingBodyColliderResolvesToEnemy()
    {
        var enemy = Enemy(siblingBody:true); var cuffs = Cuffs();
        Physics.Hits = new[] { Hit(enemy.Rigidbody.gameObject.AddComponent<Collider>(), 1f) };
        typeof(Handcuffs).GetMethod("Update", Private).Invoke(cuffs, null);
        Assert.True(CuffedEnemy.IsTargetCuffed(enemy));
    }
    [Fact]
    public void DistantControllerDoesNotRejectReachableBody()
    {
        var enemy = Enemy(); enemy.transform.position = new Vector3(0,-3,0); var cuffs = Cuffs();
        Physics.Hits = new[] { Hit(enemy.Rigidbody.gameObject.AddComponent<Collider>(), 1f) };
        typeof(Handcuffs).GetMethod("Update", Private).Invoke(cuffs, null);
        Assert.True(CuffedEnemy.IsTargetCuffed(enemy));
    }
    [Fact]
    public void WallBlocksTargetAndDoesNotSpendCuffs()
    {
        var enemy = Enemy(); var cuffs = Cuffs();
        Physics.Hits = new[] { Hit(new GameObject("Wall").AddComponent<Collider>(), .5f), Hit(enemy.gameObject.AddComponent<Collider>(), 1f) };
        typeof(Handcuffs).GetMethod("Update", Private).Invoke(cuffs, null);
        Assert.False(CuffedEnemy.IsTargetCuffed(enemy)); Assert.Equal(0, StatsManager.instance.Removed);
    }
}
