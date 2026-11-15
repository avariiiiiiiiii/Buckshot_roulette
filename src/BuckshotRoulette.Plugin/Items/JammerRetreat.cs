using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using BuckshotRoulette.Logic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using NVector3 = System.Numerics.Vector3;

namespace BuckshotRoulette.Items;

/// <summary>Хост временно направляет слышащего моба в достижимую точку от источника.</summary>
[DefaultExecutionOrder(9000)]
internal sealed class JammerRetreat : MonoBehaviour
{
    private static readonly Dictionary<EnemyNavMeshAgent, JammerRetreat> Active = new Dictionary<EnemyNavMeshAgent, JammerRetreat>();
    private static readonly Dictionary<MonoBehaviour, JammerRetreat> BrainOwners = new Dictionary<MonoBehaviour, JammerRetreat>();
    private readonly List<MonoBehaviour> _brains = new List<MonoBehaviour>();
    private readonly List<Controller> _controllers = new List<Controller>();
    private Enemy _enemy;
    private EnemyNavMeshAgent _navigation;
    private Vector3 _target;
    private Vector3 _lastPosition;
    private float _deadline;
    private float _nextControl;
    private float _stuckTime;
    private float _speed;
    private float _acceleration;
    private bool _active;

    private sealed class Controller
    {
        public MonoBehaviour Behaviour;
        public FieldInfo State;
        public MethodInfo Transition;
        public object Leave;
        public object Resume;
    }

    public static int Pulse(Vector3 source, float radius)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || EnemyDirector.instance == null) return 0;
        int affected = 0;
        foreach (var parent in EnemyDirector.instance.enemiesSpawned)
        {
            var enemy = parent != null && parent.Spawned ? parent.Enemy : null;
            if (enemy == null || !enemy.HasStateInvestigate || enemy.StateInvestigate == null
                || enemy.StateInvestigate.rangeMultiplier <= 0f
                || Vector3.Distance(source, enemy.transform.position) / enemy.StateInvestigate.rangeMultiplier > radius
                || enemy.CurrentState == EnemyState.Spawn || enemy.CurrentState == EnemyState.Despawn
                || (enemy.HasHealth && enemy.Health.healthCurrent <= 0)) continue;
            try
            {
                if (!TryTarget(enemy, source, out var target)) continue;
                var retreat = enemy.GetComponent<JammerRetreat>() ?? enemy.gameObject.AddComponent<JammerRetreat>();
                retreat.Begin(target);
                affected++;
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Глушилка: не удалось направить {enemy.name}: {error.Message}");
            }
        }
        return affected;
    }

    private static bool TryTarget(Enemy enemy, Vector3 source, out Vector3 target)
    {
        target = default;
        var nav = enemy.NavMeshAgent != null ? enemy.NavMeshAgent.Agent : null;
        if (nav == null || !nav.enabled || !nav.isOnNavMesh || enemy.IsStunned()) return false;
        var position = enemy.HasRigidbody && enemy.Rigidbody != null ? enemy.Rigidbody.transform.position : enemy.transform.position;
        var result = Retreat.Target(new NVector3(position.x, position.y, position.z), new NVector3(source.x, source.y, source.z));
        if (!result.HasValue) return false;
        var desired = new Vector3(result.Value.X, result.Value.Y, result.Value.Z);
        if (!NavMesh.SamplePosition(position, out var start, 1f, nav.areaMask)) return false;
        // Stop at the first wall or ledge instead of forcing a route through geometry.
        if (NavMesh.Raycast(start.position, desired, out var edge, nav.areaMask))
            desired = Vector3.MoveTowards(edge.position, start.position, 0.05f);
        if (!NavMesh.SamplePosition(desired, out var end, 0.5f, nav.areaMask)) return false;
        var away = position - source;
        away.y = 0f;
        var step = end.position - start.position;
        step.y = 0f;
        if (Vector3.Dot(step, away.normalized) <= 0.1f) return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, nav.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        target = end.position;
        return true;
    }

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
        _navigation = _enemy.NavMeshAgent;
        // Vanilla enemies use different private enums. Invoke their own networked transition
        // to Leave (or Roam), so locomotion/animation cleanup stays with the original controller.
        foreach (var behaviour in GetComponents<MonoBehaviour>())
        {
            if (behaviour == null || behaviour == this) continue;
            var type = behaviour.GetType();
            if (IsGenericNavigation(type)) _brains.Add(behaviour);
            var state = StateField(type);
            if (state == null || !state.FieldType.IsEnum) continue;
            var transition = Transition(type, state.FieldType);
            if (transition == null) continue;
            string leave = Enum.IsDefined(state.FieldType, "Leave") ? "Leave"
                : Enum.IsDefined(state.FieldType, "Roam") ? "Roam"
                : Enum.IsDefined(state.FieldType, "Investigate") ? "Investigate"
                : Enum.IsDefined(state.FieldType, "Idle") ? "Idle" : null;
            if (leave == null) continue;
            _controllers.Add(new Controller { Behaviour = behaviour, State = state, Transition = transition,
                Leave = Enum.Parse(state.FieldType, leave), Resume = Enum.Parse(state.FieldType,
                    Enum.IsDefined(state.FieldType, "Roam") ? "Roam" : Enum.IsDefined(state.FieldType, "Idle") ? "Idle" : leave) });
            _brains.Add(behaviour);
        }
    }

    private void Begin(Vector3 target)
    {
        if (!_active)
        {
            _speed = _navigation.Agent.speed;
            _acceleration = _navigation.Agent.acceleration;
        }
        _target = target;
        _lastPosition = BodyPosition;
        _stuckTime = 0f;
        _nextControl = 0f;
        _deadline = Time.time + Mathf.Clamp(Vector3.Distance(_lastPosition, target)
            / Mathf.Max(0.75f, _speed) * 2f + 3f, 3f, 60f);
        _active = true;
        Active[_navigation] = this;
        foreach (var brain in _brains) BrainOwners[brain] = this;
        if (NavMesh.SamplePosition(BodyPosition, out var start, 1.5f, _navigation.Agent.areaMask))
            _navigation.Agent.Warp(start.position);
        Plugin.Log.LogInfo($"Глушилка: {_enemy.name}, уход {BodyPosition} -> {_target}, контроллеров {_brains.Count}");
        Drive();
    }

    private void LateUpdate()
    {
        if (!_active) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || _enemy.EnemyParent == null || !_enemy.EnemyParent.Spawned
            || _enemy.CurrentState == EnemyState.Despawn || Dead || Time.time >= _deadline)
        {
            Finish();
            return;
        }
        if (_enemy.IsStunned()) return;
        if (_enemy.HasRigidbody && _enemy.Rigidbody.physGrabObject.playerGrabbing.Count > 0) return;
        var distance = BodyPosition - _target;
        distance.y = 0f;
        if (distance.sqrMagnitude <= 0.35f * 0.35f) { Finish(); return; }
        if (Vector3.Distance(BodyPosition, _lastPosition) > 0.05f)
        {
            _lastPosition = BodyPosition;
            _stuckTime = 0f;
        }
        else _stuckTime += Time.deltaTime;
        if (_stuckTime > 3f)
        {
            Plugin.Log.LogWarning($"Глушилка: {_enemy.name} застрял при уходе к {_target}");
            Finish(); return;
        }
        Drive();
    }

    private void Drive()
    {
        if (_enemy.IsStunned()) return;
        _enemy.DisableChase(Mathf.Max(_enemy.DisableChaseTimer, 0.25f));
        if (_enemy.HasVision && _enemy.Vision != null)
            _enemy.Vision.DisableVision(Mathf.Max(_enemy.Vision.DisableTimer, 0.25f));
        if (Time.time >= _nextControl)
        {
            _nextControl = Time.time + 0.15f;
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                var controller = _controllers[i];
                if (controller.Behaviour == null) continue;
                try
                {
                    var current = controller.State.GetValue(controller.Behaviour);
                    string state = current.ToString();
                    if (state == "Spawn" || state == "Stun" || state == "Despawn" || state == "Dead") continue;
                    if (!current.Equals(controller.Leave)) controller.Transition.Invoke(controller.Behaviour, new[] { controller.Leave });
                }
                catch (Exception error)
                {
                    Plugin.Log.LogWarning($"Глушилка: контроллер {controller.Behaviour.GetType().Name}: {error.Message}");
                    _controllers.RemoveAt(i);
                }
            }
            if (_enemy.CurrentState != EnemyState.Stunned) _enemy.CurrentState = EnemyState.Roaming;
        }
        _navigation.Enable();
        var agent = _navigation.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        _navigation.OverrideAgent(Mathf.Max(0.75f, _navigation.DefaultSpeed, _speed),
            Mathf.Max(4f, _navigation.DefaultAcceleration), 0.2f);
        _navigation.StopTimer = 0f;
        agent.isStopped = false;
        // Repeated SetDestination plus each enemy's own ResetPath/Stop caused
        // competing paths. The retreat temporarily owns the navigation brain.
        if (!agent.hasPath || (agent.destination - _target).sqrMagnitude > .01f)
            _navigation.SetDestination(_target);
        if (!agent.updatePosition) _enemy.transform.position = agent.nextPosition;
        var direction = agent.steeringTarget - BodyPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude > .01f)
            _enemy.transform.rotation = Quaternion.RotateTowards(_enemy.transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
        if (_enemy.HasRigidbody && _enemy.Rigidbody != null)
        {
            var body = _enemy.Rigidbody;
            body.disableFollowPositionTimer = 0f;
            body.disableFollowRotationTimer = 0f;
            body.OverrideFollowPosition(.2f, Mathf.Max(1f, body.positionSpeedChase));
            body.OverrideFollowRotation(.2f, Mathf.Max(1f, body.rotationSpeedChase));
        }
    }

    private void Finish()
    {
        if (!_active) return;
        _active = false;
        Active.Remove(_navigation);
        foreach (var brain in _brains) BrainOwners.Remove(brain);
        var agent = _navigation != null ? _navigation.Agent : null;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
            _navigation.UpdateAgent(_speed, _acceleration);
        }
        foreach (var controller in _controllers)
            if (controller.Behaviour != null && _enemy != null && _enemy.isActiveAndEnabled && !Dead && !_enemy.IsStunned() && _enemy.CurrentState != EnemyState.Despawn)
                controller.Transition.Invoke(controller.Behaviour, new[] { controller.Resume });
    }

    private void OnDisable() => Finish();
    private void OnDestroy() => Finish();

    private Vector3 BodyPosition => _enemy.HasRigidbody && _enemy.Rigidbody != null ? _enemy.Rigidbody.transform.position : _enemy.transform.position;
    private bool Dead => _enemy.HasHealth && _enemy.Health != null && (_enemy.Health.dead || _enemy.Health.healthCurrent <= 0);
    private static bool IsGenericNavigation(Type type) => type == typeof(EnemyStateRoaming) || type == typeof(EnemyStateInvestigate)
        || type.Name == "EnemyStateChase" || type.Name == "EnemyStateIdle";
    internal static bool IsNavigationBrain(Type type)
    {
        if (IsGenericNavigation(type)) return true;
        var state = StateField(type);
        return state != null && state.FieldType.IsEnum && Transition(type, state.FieldType) != null;
    }
    private static FieldInfo StateField(Type type)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? type.GetField("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }
    private static MethodInfo Transition(Type type, Type state) => type.GetMethod("UpdateState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, new[] { state }, null);
    internal static bool RunBrain(MonoBehaviour brain) => !BrainOwners.TryGetValue(brain, out var retreat) || retreat == null
        || !retreat._active || !SemiFunc.IsMasterClientOrSingleplayer() || retreat.Dead || retreat._enemy.IsStunned()
        || retreat._enemy.CurrentState == EnemyState.Despawn || retreat._enemy.CurrentState == EnemyState.Spawn;

    internal static void Redirect(EnemyNavMeshAgent navigation, ref Vector3 target)
    {
        if (Active.TryGetValue(navigation, out var retreat) && retreat != null && retreat._active
            && !retreat._enemy.IsStunned()) target = retreat._target;
    }
}

[HarmonyPatch]
internal static class JammerOwnsNavigation
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in typeof(Enemy).Assembly.GetTypes().Concat(new[] { typeof(BuckshotRoulette.Dealer.EnemyDealer) }))
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(type) || !JammerRetreat.IsNavigationBrain(type)) continue;
            foreach (string name in new[] { "Update", "LateUpdate", "FixedUpdate" })
            {
                var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method != null && method.GetMethodBody() != null) yield return method;
            }
        }
    }
    private static bool Prefix(MonoBehaviour __instance) => JammerRetreat.RunBrain(__instance);
}

[HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.SetDestination))]
internal static class JammerDestinationPatch
{
    private static void Prefix(EnemyNavMeshAgent __instance, ref Vector3 __0) => JammerRetreat.Redirect(__instance, ref __0);
}
