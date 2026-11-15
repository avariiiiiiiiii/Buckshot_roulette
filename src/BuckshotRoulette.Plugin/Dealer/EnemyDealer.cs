using System.Collections.Generic;
using BuckshotRoulette.Core;
using BuckshotRoulette.Items;
using BuckshotRoulette.Shells;
using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// ИИ дилера. Живёт на объекте Controller клона ванильного врага, заменяя EnemyAnimal.
/// Логику ведёт хост, состояние синхронизируется RPC (как у ванильных врагов).
/// Патроны — из общего пула раунда (ShellPool): из него берёт и сам дилер при выстреле, и каждое оружие игроков при попадании по нему.
/// </summary>
internal sealed class EnemyDealer : MonoBehaviour
{
    public enum State
    {
        Spawn,
        Idle,
        Roam,
        Notice,
        Chase,
        Aim,
        Recover,
        Leave,
        Stun,
    }

    public State CurrentState;

    private Enemy _enemy;
    private PhotonView _view;
    private DealerVisual _visual;

    private float _stateTimer;
    private bool _stateImpulse;
    private Vector3 _destination;

    private PlayerAvatar _target;
    private float _lostSightTimer;
    private readonly Dictionary<int, float> _ignoreUntil = new Dictionary<int, float>();

    private float _visionLogTimer;
    private readonly RaycastHit[] _sightHits = new RaycastHit[16];

    private bool _stunned;
    private DealerPhysics _physics;
    private CapsuleCollider[] _colliders;
    private bool _collidersPending;
    private float _collidersPendingTime;
    private float _stunTestTimer;

    private const float AimBelowEyes = 0.3f;        // м: ствол наводится в грудь, а не в глаза
    private const float TumbleSpin = 4f;            // рад/с: закрутка шара в начале оглушения
    private const float TumblePush = 0.6f;          // м/с
    private const float UprightAngle = 25f;
    private const float CollidersTimeout = 3f;

    public bool IsAngry => CurrentState == State.Notice || CurrentState == State.Chase || CurrentState == State.Aim || CurrentState == State.Recover;

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
        _view = GetComponent<PhotonView>();
        // слушатели, добавленные к префабу через AddListener, не сериализуются и не попадают в клон при Instantiate,
        // поэтому подписка — на каждом экземпляре
        GetComponent<EnemyStateSpawn>().OnSpawn.AddListener(OnSpawn);
        GetComponent<EnemyHealth>().onDeath.AddListener(OnDeath);
        GetComponent<EnemyVision>().onVisionTriggered.AddListener(OnVision);
    }

    private void Update()
    {
        if (_visual == null && _enemy.Rigidbody != null)
        {
            _visual = _enemy.Rigidbody.GetComponentInChildren<DealerVisual>(true);
            _physics = _enemy.Rigidbody.GetComponent<DealerPhysics>();
            _colliders = _enemy.Rigidbody.GetComponentsInChildren<CapsuleCollider>(true);
        }
        if (_visual != null)
        {
            _visual.SetAngry(IsAngry);
            // цель знает хост; клиентам её рассылает сама игра через Enemy.TargetPlayerViewID
            var aimPlayer = SemiFunc.IsMasterClientOrSingleplayer() ? _target : _enemy.TargetPlayerAvatar;
            _visual.SetAimTarget(IsAngry && aimPlayer != null
                ? aimPlayer.PlayerVisionTarget.VisionTransform.position + Vector3.down * AimBelowEyes
                : (Vector3?)null);
        }
        // флаг оглушения игра рассылает всем клиентам, поэтому визуал и физика переключаются у каждого
        if (_visual != null && _enemy.IsStunned() != _stunned) ApplyStun(_enemy.IsStunned());
        if (_collidersPending) RestoreCollidersWhenUpright();

        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (Settings.DiagnosticsEnabled.Value) DebugVision();
        if (Settings.DealerStunTest.Value) DebugStun();
        if (_stunned && CurrentState != State.Stun) UpdateState(State.Stun);
        if (CuffedEnemy.IsTargetCuffed(_enemy) && !_stunned)
        {
            _target = null;
            if (CurrentState != State.Spawn && CurrentState != State.Stun && CurrentState != State.Leave)
                UpdateState(State.Roam);
            if (CurrentState == State.Roam) StateRoam();
            return;
        }
        switch (CurrentState)
        {
            case State.Spawn: StateSpawn(); break;
            case State.Idle: StateIdle(); break;
            case State.Roam: StateRoam(); break;
            case State.Notice: StateNotice(); break;
            case State.Chase: StateChase(); break;
            case State.Aim: StateAim(); break;
            case State.Recover: StateRecover(); break;
            case State.Leave: StateLeave(); break;
            case State.Stun: StateStun(); break;
        }
    }

    /// <summary>
    /// Оглушение: дробовик выпадает, руки закрывают лицо, тело катается шаром головы (лишние капсулы выключены).
    /// Само падение и кувырки — физика игры: на время оглушения она включает телу гравитацию и снимает сопротивление.
    /// </summary>
    private void ApplyStun(bool stunned)
    {
        _stunned = stunned;
        _visual.SetStunned(stunned);
        if (_physics != null) _physics.SetStunned(stunned);
        if (stunned)
        {
            _collidersPending = false;
            for (int i = 1; i < _colliders.Length; i++) _colliders[i].enabled = false;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                // от удара шар головы закручивается и откатывается
                var body = _enemy.Rigidbody.rb;
                var push = Random.insideUnitSphere;
                push.y = 0f;
                body.AddTorque(Random.onUnitSphere * TumbleSpin, ForceMode.VelocityChange);
                body.AddForce(push.normalized * TumblePush, ForceMode.VelocityChange);
            }
        }
        else
        {
            // капсулы рук включаются, когда тело встало: включённые в лежачем теле, они оказываются в полу и физика выбрасывает тело кувырком
            _collidersPending = true;
            _collidersPendingTime = 0f;
        }
        Plugin.Log.LogInfo(stunned ? "Дилер оглушён: роняет дробовик, закрывает лицо" : "Дилер пришёл в себя");
    }

    private void RestoreCollidersWhenUpright()
    {
        _collidersPendingTime += Time.deltaTime;
        var body = _enemy.Rigidbody.transform;
        bool upright = Vector3.Angle(body.up, Vector3.up) < UprightAngle && body.position.y - transform.position.y > 0.5f;
        if (!upright && _collidersPendingTime < CollidersTimeout) return;
        _collidersPending = false;
        for (int i = 1; i < _colliders.Length; i++) _colliders[i].enabled = true;
        Plugin.Log.LogInfo($"Дилер встал за {_collidersPendingTime:F1} с (наклон {Vector3.Angle(body.up, Vector3.up):F0}°): коллайдеры рук включены");
    }

    private void StateStun()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _target = null;
        }
        // пришёл в себя — снова бродит рядом и может заметить игрока (уход на 25–50 м прятал его от игроков)
        if (!_stunned) UpdateState(State.Idle);
    }

    /// <summary>Отладка: оглушение на 5 с каждые 20 с, чтобы проверить анимацию без броска предметом.</summary>
    private void DebugStun()
    {
        _stunTestTimer += Time.deltaTime;
        if (_stunTestTimer < 20f) return;
        _stunTestTimer = 0f;
        Plugin.Log.LogInfo("DebugStun: оглушаю дилера на 5 с");
        _enemy.StateStunned.Set(5f);
    }

    // ---------------------------------------------------------------- состояния

    private void StateSpawn()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _stateTimer = 1f;
        }
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f) UpdateState(State.Idle);
    }

    private void StateIdle()
    {
        if (_stateImpulse)
        {
            _enemy.NavMeshAgent.ResetPath();
            _enemy.NavMeshAgent.Warp(_enemy.Rigidbody.transform.position);
            _stateTimer = Random.Range(0.5f, 2f);
            _stateImpulse = false;
        }
        if (SemiFunc.EnemySpawnIdlePause()) return;

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f) UpdateState(State.Roam);
        if (SemiFunc.EnemyForceLeave(_enemy)) UpdateState(State.Leave);
    }

    private void StateRoam()
    {
        if (_stateImpulse)
        {
            _enemy.NavMeshAgent.ResetPath();
            _enemy.NavMeshAgent.Warp(_enemy.Rigidbody.transform.position);
            var point = SemiFunc.LevelPointGet(transform.position, 8f, 30f) ?? SemiFunc.LevelPointGet(transform.position, 0f, 999f);
            if (point != null && NavMesh.SamplePosition(point.transform.position + Random.insideUnitSphere * 3f, out var hit, 5f, -1)
                && Physics.Raycast(hit.position, Vector3.down, 5f, LayerMask.GetMask("Default")))
            {
                _destination = hit.position;
            }
            _stateTimer = 8f;
            _stateImpulse = false;
        }

        _enemy.NavMeshAgent.SetDestination(_destination);
        _enemy.NavMeshAgent.OverrideAgent(Settings.DealerSpeed.Value, 14f, 0.25f);
        if (_enemy.Rigidbody.notMovingTimer > 2f) _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f || Vector3.Distance(transform.position, _destination) < 1.2f) UpdateState(State.Idle);
        if (SemiFunc.EnemyForceLeave(_enemy)) UpdateState(State.Leave);
    }

    private void StateNotice()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _stateTimer = 0.5f;
        }
        if (!TargetValid()) { UpdateState(State.Leave); return; }
        _enemy.NavMeshAgent.ResetPath();
        _enemy.NavMeshAgent.Stop(0.1f);
        FaceTarget();
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _enemy.NavMeshAgent.Stop(0f);
            UpdateState(State.Chase);
        }
    }

    private void StateChase()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _lostSightTimer = 0f;
        }
        if (!TargetValid()) { UpdateState(State.Leave); return; }

        _enemy.NavMeshAgent.SetDestination(_target.transform.position);
        _enemy.NavMeshAgent.OverrideAgent(Settings.DealerChaseSpeed.Value, 16f, 0.25f);

        bool sees = HasLineOfSight(_target);
        float distance = Vector3.Distance(transform.position, _target.transform.position);
        if (sees && distance <= Settings.DealerShootRange.Value)
        {
            UpdateState(State.Aim);
            return;
        }

        _lostSightTimer = sees ? 0f : _lostSightTimer + Time.deltaTime;
        if (_lostSightTimer > 6f) UpdateState(State.Leave);
    }

    private void StateAim()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _stateTimer = Settings.DealerAimTime.Value;
        }
        if (!TargetValid()) { UpdateState(State.Leave); return; }

        _enemy.NavMeshAgent.ResetPath();
        _enemy.NavMeshAgent.Stop(0.1f);
        FaceTarget();

        float distance = Vector3.Distance(transform.position, _target.transform.position);
        if (!HasLineOfSight(_target) || distance > Settings.DealerShootRange.Value * 1.4f)
        {
            _enemy.NavMeshAgent.Stop(0f);
            UpdateState(State.Chase);
            return;
        }

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f) Fire(_target);
    }

    private void StateRecover()
    {
        if (_stateImpulse)
        {
            _stateImpulse = false;
            _stateTimer = 1.2f;
        }
        _enemy.NavMeshAgent.ResetPath();
        _enemy.NavMeshAgent.Stop(0.1f);
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _enemy.NavMeshAgent.Stop(0f);
            UpdateState(State.Leave);
        }
    }

    private void StateLeave()
    {
        if (_stateImpulse)
        {
            var point = SemiFunc.LevelPointGetPlayerDistance(transform.position, 25f, 50f) ?? SemiFunc.LevelPointGetFurthestFromPlayer(transform.position, 5f);
            if (point != null) _destination = point.transform.position;
            else _enemy.EnemyParent.SpawnedTimerSet(0f);
            _stateTimer = 10f;
            _stateImpulse = false;
            _target = null;
            SemiFunc.EnemyLeaveStart(_enemy);
            return;
        }

        _enemy.NavMeshAgent.SetDestination(_destination);
        _enemy.NavMeshAgent.OverrideAgent(Settings.DealerSpeed.Value + 1f, 14f, 0.25f);
        if (_enemy.Rigidbody.notMovingTimer > 2f) _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f || Vector3.Distance(transform.position, _destination) < 1.2f) UpdateState(State.Idle);
    }

    // ---------------------------------------------------------------- выстрел дилера

    private void Fire(PlayerAvatar target)
    {
        bool live = ShellPool.Take();
        int targetId = target.photonView.ViewID;
        _ignoreUntil[targetId] = Time.time + Settings.DealerRetargetCooldown.Value;

        if (GameManager.Multiplayer()) _view.RPC(nameof(ShotRPC), RpcTarget.All, live);
        else ShotRPC(live);

        if (live) HurtPlayer(target);
        MakeNoise();
        Plugin.Log.LogInfo($"Дилер выстрелил в {target.playerName}: {(live ? "БОЕВОЙ" : "холостой")}");
        UpdateState(State.Recover);
    }

    private void HurtPlayer(PlayerAvatar target)
    {
        var health = target.playerHealth;
        // «100 урона» должно убивать и при прокачанном здоровье
        int damage = Settings.PlayerDamage.Value >= 100 ? Mathf.Max(Settings.PlayerDamage.Value, health.maxHealth) : Settings.PlayerDamage.Value;
        health.HurtOther(damage, Vector3.zero, false, SemiFunc.EnemyGetIndex(_enemy));
    }

    /// <summary>
    /// Громкий выстрел слышат ВСЕ монстры, но идут не прямо к дилеру, а в случайную точку рядом.
    /// Хост решает, куда; ванильные враги реагируют на EnemyDirector.SetInvestigate.
    /// </summary>
    private void MakeNoise()
    {
        var offset = Random.insideUnitSphere;
        offset.y = 0f;
        var point = transform.position + offset.normalized * Random.Range(8f, 14f);
        EnemyDirector.instance.SetInvestigate(point, Settings.DealerShotNoiseRadius.Value);
    }

    // ---------------------------------------------------------------- общий пул патронов

    /// <summary>
    /// Хост: попадание оружием по дилеру, патрон уже решён (DealerDamagePatches). fresh — первое попадание этого выстрела:
    /// только оно пишется в лог и щёлкает холостым, остальные дробины/тики того же выстрела молчат.
    /// </summary>
    public void OnWeaponHit(bool live, bool fresh)
    {
        if (!fresh) return;
        Plugin.Log.LogInfo($"Игрок попал по дилеру: патрон {(live ? "БОЕВОЙ" : "холостой")}");
        if (live) return;
        Plugin.Log.LogInfo("Холостой: урон, оглушение и толчок по дилеру заблокированы");
        if (GameManager.Multiplayer()) _view.RPC(nameof(BlockedRPC), RpcTarget.All);
        else BlockedRPC();
    }

    // ---------------------------------------------------------------- события игры (подписываются в DealerFactory)

    public void OnSpawn()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            // игра выпускает врага в точке уровня в 18–35 м от игроков (SemiFunc.EnemySpawn), отладочный дилер переносится после неё
            if (SemiFunc.EnemySpawn(_enemy))
            {
                UpdateState(State.Spawn);
                if (Diagnostics.DebugSpawner.PendingDealerPoint is Vector3 debugPoint)
                {
                    Diagnostics.DebugSpawner.PendingDealerPoint = null;
                    _enemy.EnemyTeleported(debugPoint);
                    Plugin.Log.LogInfo($"DebugSpawn: дилер перенесён в тестовую точку карты {debugPoint}");
                }
            }
            // при каждом появлении дилера всем показывают, что в общем пуле (есть у кого-то дробовик или нет)
            ShellPool.ShowCurrent();
        }
        Plugin.Log.LogInfo($"Дилер появился: {transform.position}");
    }

    public void OnDeath()
    {
        GameDirector.instance.CameraShake.ShakeDistance(3f, 3f, 8f, transform.position, 0.5f);
        GameDirector.instance.CameraImpact.ShakeDistance(3f, 3f, 8f, transform.position, 0.1f);
        _enemy.EnemyParent.Despawn();
    }

    public void OnVision()
    {
        if (CuffedEnemy.IsTargetCuffed(_enemy)) return;
        if (CurrentState != State.Idle && CurrentState != State.Roam && CurrentState != State.Leave) return;
        var player = _enemy.Vision.onVisionTriggeredPlayer;
        if (player == null || player.isDisabled || player.deadSet) return;
        if (_ignoreUntil.TryGetValue(player.photonView.ViewID, out var until) && Time.time < until) return;
        _target = player;
        _enemy.TargetPlayerAvatar = player;
        _enemy.TargetPlayerViewID = player.photonView.ViewID;
        Plugin.Log.LogInfo($"Дилер заметил {player.playerName}");
        UpdateState(State.Notice);
    }

    /// <summary>Диагностика зрения: почему дилер видит или не видит локального игрока.</summary>
    private void DebugVision()
    {
        _visionLogTimer += Time.deltaTime;
        if (_visionLogTimer < 2f) return;
        _visionLogTimer = 0f;
        var vision = _enemy.Vision;
        var player = PlayerAvatar.instance;
        if (vision == null || player == null) return;
        var from = vision.VisionTransform.position;
        var to = player.PlayerVisionTarget.VisionTransform.position;
        float dot = Vector3.Dot(vision.VisionTransform.forward, (to - from).normalized);
        vision.VisionsTriggered.TryGetValue(player.photonView.ViewID, out int seen);
        var blocker = SightBlocker(player);
        string sight = blocker == null ? "да" : $"нет ({blocker.name}, tag={blocker.tag}, layer={LayerMask.LayerToName(blocker.gameObject.layer)})";
        Plugin.Log.LogInfo($"DealerVision: состояние={CurrentState}, enemyState={_enemy.CurrentState}, игроков в зрении={vision.VisionsTriggered.Count}, " +
                           $"счётчик={seen}/{vision.VisionsToTrigger}, дистанция={Vector3.Distance(from, to):F1}/{vision.VisionDistance:F1} м, " +
                           $"dot={dot:F2}, видит={sight}, отключено={vision.DisableTimer:F2}");
        if (_visual != null)
        {
            // высоты относительно контроллера (он стоит на навмеше, то есть на полу) и ступней игрока
            float floor = transform.position.y;
            Plugin.Log.LogInfo($"DealerHeight: над контроллером: тело={_enemy.Rigidbody.transform.position.y - floor:F2}, " +
                               $"корень модели={_visual.transform.position.y - floor:F2}, голова={(_visual.Head != null ? _visual.Head.position.y - floor : 0f):F2}, " +
                               $"дуло={(_visual.Muzzle != null ? _visual.Muzzle.position.y - floor : 0f):F2}; ступни игрока={player.transform.position.y - floor:F2}; " +
                               $"масштаб корня={_visual.transform.lossyScale.y:F2}");
        }
    }

    // ---------------------------------------------------------------- вспомогательное

    private bool TargetValid() => _target != null && !_target.isDisabled && !_target.deadSet;

    /// <summary>
    /// Линия огня от глаз до игрока. Не перекрывают её собственные коллайдеры дилера (голова вокруг глаз, руки с дробовиком)
    /// и тело самого игрока: луч идёт в точку его глаз и сначала упирается в него (зрение игры считает это «вижу»).
    /// </summary>
    private bool HasLineOfSight(PlayerAvatar player) => SightBlocker(player) == null;

    private Transform SightBlocker(PlayerAvatar player)
    {
        var from = _enemy.Vision.VisionTransform.position;
        var to = player.PlayerVisionTarget.VisionTransform.position;
        var direction = to - from;
        int count = Physics.RaycastNonAlloc(from, direction.normalized, _sightHits, direction.magnitude,
            SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore);
        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = _sightHits[i];
            if (hit.transform.GetComponentInParent<EnemyRigidbody>() == _enemy.Rigidbody) continue;
            if (hit.transform.CompareTag("Player") || hit.transform.GetComponentInParent<PlayerAvatar>() == player
                || hit.transform.GetComponentInParent<PlayerTumble>() != null) continue;
            if (hit.distance < nearestDistance) { nearestDistance = hit.distance; nearest = hit.transform; }
        }
        return nearest;
    }

    private void FaceTarget()
    {
        var flat = _target.transform.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 12f * Time.deltaTime);
    }

    private void UpdateState(State next)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || next == CurrentState) return;
        _stateTimer = 0f;
        _stateImpulse = true;
        CurrentState = next;
        if (GameManager.Multiplayer()) _view.RPC(nameof(UpdateStateRPC), RpcTarget.Others, (int)next);
    }

    [PunRPC]
    private void UpdateStateRPC(int next, PhotonMessageInfo info = default)
    {
        if (SemiFunc.MasterOnlyRPC(info)) CurrentState = (State)next;
    }

    /// <summary>Выстрел дилера: боевой — грохот и вспышка, холостой — щелчок, хлопок и дымок; отдача и лицо — в обоих случаях.</summary>
    [PunRPC]
    private void ShotRPC(bool live, PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        if (_visual != null) _visual.Recoil();
        var muzzle = _visual != null && _visual.Muzzle != null ? _visual.Muzzle : transform;
        if (live) GunFx.Fire(muzzle);
        else GunFx.Blank(muzzle);
    }

    [PunRPC]
    private void BlockedRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        GunFx.Click(transform.position + Vector3.up * 1.2f);
    }
}
