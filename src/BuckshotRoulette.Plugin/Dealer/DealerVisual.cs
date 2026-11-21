using System.Collections.Generic;
using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// Модель дилера из Blender и процедурная анимация летающей головы в ауре темноты.
/// Обычно: голова парит и покачивается, руки с дробовиком парят следом; спокойный дилер держит ствол опущенным,
/// злой — поднимает на цель; выстрел — отдача, лицо искажается (head_shoot), затем дилер передёргивает помпу.
/// Оглушение — без резких переключений: дробовик выскальзывает из рук и падает на пол рядом, аура гаснет,
/// руки по дуге летят к лицу и закрывают глаза (тело при этом падает и катается шаром — это физика игры).
/// После оглушения руки снимаются с лица, дробовик возвращается в руки, аура нарастает.
/// </summary>
internal sealed class DealerVisual : MonoBehaviour
{
    private const float HoverHeight = 0.05f;
    private const float HoverSpeed = 1.7f;          // рад/с
    private const float ArmsHoverLag = 0.6f;        // рад: руки отстают от головы по фазе
    private const float RecoilDegrees = 16f;
    private const float LoweredPitch = 22f;         // ствол опущен, пока дилер спокоен
    private const float AimYawLimit = 35f;          // злой дилер наводит ствол на цель в этих пределах
    private const float AimPitchLimit = 45f;
    private const float PoseSpeed = 3f;             // 1/с: подъём и опускание ствола
    private const float ShootFaceSeconds = 0.8f;
    private const float PumpDelay = 0.35f;          // с после выстрела
    private const float PumpDuration = 0.45f;       // назад и вперёд
    private const float PumpStroke = 0.12f;         // м

    private const float Gravity = 9.81f;
    private const float GunHalfThickness = 0.045f;  // дробовик лежит на боку
    private const float HandsToFaceDelay = 0.15f;   // руки сначала отпускают дробовик
    private const float HandsToFaceTime = 0.55f;
    private const float HandsArc = 0.12f;           // м: руки идут к лицу по дуге
    private const float FlinchDegrees = 18f;        // голова откидывается назад от удара
    private const float RecoverTime = 0.6f;
    private const float AuraBackDelay = 0.25f;

    private enum Phase { Normal, Stunned, Recovering }

    public Transform Head;
    public Transform HeadShoot;
    public Transform Arms;
    public Transform Gun;
    public Transform Forend;
    public Transform HandRear;
    public Transform HandFront;
    public Transform CoverLeft;
    public Transform CoverRight;
    public Transform Muzzle;
    public DealerAura Aura;

    // позы покоя: читаются в Awake из трансформов клона (Instantiate копирует трансформы, а не поля этого класса)
    private Vector3 _headPivot;
    private Vector3 _armsRest;
    private Vector3 _gunRestPos;
    private Quaternion _gunRestRot;
    private Vector3 _forendRest;
    private Vector3 _handRearRestPos;
    private Quaternion _handRearRestRot;
    private Vector3 _handFrontRestPos;
    private Quaternion _handFrontRestRot;

    private Vector3 _lastPosition;
    private float _speed;
    private float _recoil;
    private float _pose;
    private float _shootFace;
    private float _pumpTime = PumpDuration;

    private Phase _phase;
    private float _phaseTime;
    private float _floorY;
    private Vector3 _gunVelocity;
    private Vector3 _gunSpin;
    private bool _gunBounced;
    private bool _gunResting;
    private Vector3 _gunFromPos;
    private Quaternion _gunFromRot;
    private Vector3 _rearFromPos, _frontFromPos;
    private Quaternion _rearFromRot, _frontFromRot;
    private bool _handsOnFace;
    private bool _resetPending;
    private Vector3? _aimPoint;

    /// <summary>Строит иерархию из частей модели под родителем. Возвращает визуал.</summary>
    public static DealerVisual Create(Transform parent, ModelLoader.MultiPartModel model)
    {
        var root = new GameObject("Dealer Visual");
        root.transform.SetParent(parent, false);
        var visual = root.AddComponent<DealerVisual>();

        var parts = new Dictionary<string, Transform>();
        foreach (var part in model.Parts)
        {
            var go = new GameObject(part.Name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = part.Pivot;
            go.AddComponent<MeshFilter>().sharedMesh = part.Model.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = part.Model.Materials;
            parts[part.Name] = go.transform;
        }

        visual.Head = parts["head"];
        visual.HeadShoot = parts["head_shoot"];
        visual.HeadShoot.gameObject.SetActive(false);
        // руки на лице держатся за голову и откидываются вместе с ней
        visual.CoverLeft = parts["hand_cover_l"];
        visual.CoverRight = parts["hand_cover_r"];
        visual.CoverLeft.SetParent(visual.Head, true);
        visual.CoverRight.SetParent(visual.Head, true);
        visual.CoverLeft.gameObject.SetActive(false);
        visual.CoverRight.gameObject.SetActive(false);

        // руки с дробовиком: контейнер (парение, прицел, отдача) -> дробовик -> цевьё (помпа) -> передняя рука
        visual.Arms = new GameObject("Arms").transform;
        visual.Arms.SetParent(root.transform, false);
        visual.Arms.localPosition = model.Markers["arms"];
        visual.Gun = parts["gun"];
        visual.Gun.SetParent(visual.Arms, true);
        visual.Forend = parts["forend"];
        visual.Forend.SetParent(visual.Gun, true);
        visual.HandFront = parts["hand_front"];
        visual.HandFront.SetParent(visual.Forend, true);
        visual.HandRear = parts["hand_rear"];
        visual.HandRear.SetParent(visual.Arms, true);

        // дульный срез — на дробовике
        visual.Muzzle = new GameObject("Muzzle").transform;
        visual.Muzzle.SetParent(root.transform, false);
        visual.Muzzle.localPosition = model.Markers["muzzle"];
        visual.Muzzle.SetParent(visual.Gun, true);

        // облако между головой и руками, чуть позади них
        var center = (model.Markers["head"] + model.Markers["arms"]) * 0.5f + new Vector3(0f, 0f, -0.15f);
        visual.Aura = DealerAura.Create(root.transform, center);
        return visual;
    }

    private void Awake()
    {
        _headPivot = Head.localPosition;
        _armsRest = Arms.localPosition;
        _gunRestPos = Gun.localPosition;
        _gunRestRot = Gun.localRotation;
        _forendRest = Forend.localPosition;
        _handRearRestPos = HandRear.localPosition;
        _handRearRestRot = HandRear.localRotation;
        _handFrontRestPos = HandFront.localPosition;
        _handFrontRestRot = HandFront.localRotation;
    }

    /// <summary>Дилер злой, когда видит игрока и стреляет: ствол поднят на цель.</summary>
    public bool Angry { get; private set; }

    public void SetAngry(bool angry) => Angry = angry;

    /// <summary>Куда целится злой дилер (мировая точка) или null.</summary>
    public void SetAimTarget(Vector3? point) => _aimPoint = point;

    /// <summary>Выстрел: ствол подбрасывает, лицо искажается, после паузы дилер передёргивает помпу.</summary>
    public void Recoil()
    {
        if (_phase != Phase.Normal) return;
        _recoil = RecoilDegrees;
        _shootFace = ShootFaceSeconds;
        _pumpTime = -PumpDelay;
    }

    public void SetStunned(bool stunned)
    {
        if (stunned && _phase != Phase.Stunned) BeginStun();
        else if (!stunned && _phase == Phase.Stunned) BeginRecover();
    }

    private void OnEnable()
    {
        _lastPosition = transform.position;
        _speed = 0f;
    }

    /// <summary>
    /// Враг исчез посреди оглушения. Менять родителей во время (де)активации иерархии Unity запрещает,
    /// поэтому дробовик, лежавший в мире, прячется сейчас, а руки и дробовик возвращаются на место в первом кадре после включения.
    /// </summary>
    private void OnDisable()
    {
        if (_phase == Phase.Normal) return;
        _resetPending = true;
        if (Gun.parent == null) Gun.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Gun != null && Gun.parent == null) Destroy(Gun.gameObject);
    }

    private void Update()
    {
        if (_resetPending)
        {
            _resetPending = false;
            Gun.gameObject.SetActive(true);
            FinishRecover();
        }

        // скорость по перемещению объекта: на клиентах NavMeshAgent выключен, позицию двигает синхронизация
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;
        _lastPosition = transform.position;
        _speed = Mathf.Lerp(_speed, Mathf.Min(delta.magnitude / dt, 9f), 10f * dt);
        float k = Mathf.Clamp01(_speed / 2.5f);

        _recoil = Mathf.Lerp(_recoil, 0f, 9f * dt);
        _pose = Mathf.MoveTowards(_pose, Angry && _phase == Phase.Normal ? 1f : 0f, PoseSpeed * dt);
        _shootFace -= dt;
        _phaseTime += dt;
        float hover = Time.time * HoverSpeed;

        // голова: парит, в оглушении замирает с откинутым назад рывком
        float flinch = _phase == Phase.Stunned ? Mathf.Sin(Mathf.Clamp01(_phaseTime / 0.4f) * Mathf.PI) * FlinchDegrees : 0f;
        float hoverWeight = _phase == Phase.Normal ? 1f : _phase == Phase.Recovering ? Mathf.Clamp01(_phaseTime / RecoverTime) : 0f;
        Head.localPosition = _headPivot + new Vector3(0f, Mathf.Sin(hover) * HoverHeight * hoverWeight, 0f);
        Head.localRotation = Quaternion.Euler(4f * k * hoverWeight - flinch, Mathf.Sin(hover * 0.5f) * 1.5f * hoverWeight, Mathf.Sin(hover * 0.7f) * 1f * hoverWeight);
        bool shooting = _shootFace > 0f && _phase == Phase.Normal;
        Head.gameObject.SetActive(!shooting);
        HeadShoot.gameObject.SetActive(shooting);
        HeadShoot.localPosition = Head.localPosition;
        HeadShoot.localRotation = Head.localRotation;

        // контейнер рук парит следом за головой с отставанием; опущенный ствол слегка покачивается, поднятый наведён на цель
        Arms.localPosition = _armsRest + new Vector3(0f, Mathf.Sin(hover - ArmsHoverLag) * HoverHeight, 0f);
        var loweredRot = Quaternion.Euler(LoweredPitch + Mathf.Sin(hover * 1.3f) * 1.5f, Mathf.Sin(hover * 0.6f) * 2f, Mathf.Sin(hover * 0.9f) * 1.5f);
        Arms.localRotation = Quaternion.Slerp(loweredRot, AimRotation(), _pose) * Quaternion.Euler(-_recoil, 0f, 0f);

        if (_pumpTime < PumpDuration)
        {
            _pumpTime += dt;
            float t = Mathf.Clamp01(_pumpTime / PumpDuration);
            float stroke = _pumpTime > 0f ? Mathf.Sin(t * Mathf.PI) * PumpStroke : 0f;
            Forend.localPosition = _forendRest + Vector3.back * stroke;
        }

        if (_phase == Phase.Stunned) AnimateStun(dt);
        else if (_phase == Phase.Recovering) AnimateRecover();
    }

    /// <summary>Поворот контейнера рук, при котором ствол (ось +Z через пивот рук) смотрит на цель; без цели — прямо.</summary>
    private Quaternion AimRotation()
    {
        if (_aimPoint == null) return Quaternion.identity;
        var dir = transform.InverseTransformDirection(_aimPoint.Value - Arms.position);
        if (dir.sqrMagnitude < 0.01f) return Quaternion.identity;
        float yaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -AimYawLimit, AimYawLimit);
        float pitch = Mathf.Clamp(-Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -AimPitchLimit, AimPitchLimit);
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    // ---------------------------------------------------------------- оглушение

    private void BeginStun()
    {
        _phase = Phase.Stunned;
        _phaseTime = 0f;
        _pumpTime = PumpDuration;
        Forend.localPosition = _forendRest;
        Aura.SetVisible(false);

        // дробовик выскальзывает из рук: дальше он живёт в мире и падает на пол рядом с дилером
        _floorY = transform.position.y;     // земля модели совпадает с полом
        Gun.SetParent(null, true);
        var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        _gunVelocity = forward * Random.Range(0.4f, 0.9f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.6f;
        _gunSpin = Random.onUnitSphere * Random.Range(180f, 320f);
        _gunBounced = false;
        _gunResting = false;

        // руки отпускают дробовик и переходят в пространство визуала, чтобы лететь к лицу
        HandRear.SetParent(transform, true);
        HandFront.SetParent(transform, true);
        _rearFromPos = HandRear.localPosition; _rearFromRot = HandRear.localRotation;
        _frontFromPos = HandFront.localPosition; _frontFromRot = HandFront.localRotation;
        _handsOnFace = false;
    }

    private void AnimateStun(float dt)
    {
        DropGun(dt);
        if (_handsOnFace) return;

        float t = Mathf.Clamp01((_phaseTime - HandsToFaceDelay) / HandsToFaceTime);
        float s = t * t * (3f - 2f * t);
        float arc = Mathf.Sin(s * Mathf.PI) * HandsArc;
        // задняя рука — правая дилера, передняя — левая
        MoveHand(HandRear, _rearFromPos, _rearFromRot, CoverRight, s, arc);
        MoveHand(HandFront, _frontFromPos, _frontFromRot, CoverLeft, s, arc);
        if (t < 1f) return;

        // руки легли на лицо: держащие кисти сменяются кистями, закрывающими глаза
        _handsOnFace = true;
        HandRear.gameObject.SetActive(false);
        HandFront.gameObject.SetActive(false);
        CoverLeft.gameObject.SetActive(true);
        CoverRight.gameObject.SetActive(true);
    }

    /// <summary>Рука летит в пространстве визуала к месту ладони на лице (та держится за голову, поэтому цель — через мировую позу).</summary>
    private void MoveHand(Transform hand, Vector3 fromPos, Quaternion fromRot, Transform cover, float s, float arc)
    {
        hand.localPosition = Vector3.Lerp(fromPos, transform.InverseTransformPoint(cover.position), s) + new Vector3(0f, arc, arc * 0.5f);
        hand.localRotation = Quaternion.Slerp(fromRot, Quaternion.Inverse(transform.rotation) * cover.rotation, s);
    }

    /// <summary>Падение дробовика: баллистика с вращением, один отскок, потом ложится на бок.</summary>
    private void DropGun(float dt)
    {
        if (_gunResting) return;
        _gunVelocity += Vector3.down * Gravity * dt;
        Gun.position += _gunVelocity * dt;
        Gun.rotation = Quaternion.Euler(_gunSpin * dt) * Gun.rotation;
        if (Gun.position.y > _floorY + GunHalfThickness) return;

        Gun.position = new Vector3(Gun.position.x, _floorY + GunHalfThickness, Gun.position.z);
        if (!_gunBounced && _gunVelocity.y < -1f)
        {
            _gunBounced = true;
            _gunVelocity = new Vector3(_gunVelocity.x * 0.5f, -_gunVelocity.y * 0.3f, _gunVelocity.z * 0.5f);
            _gunSpin *= 0.4f;
            return;
        }
        // лёг на бок: ствол горизонтально, курком в сторону
        var flat = Vector3.ProjectOnPlane(Gun.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = transform.forward;
        Gun.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
        _gunResting = true;
    }

    // ---------------------------------------------------------------- восстановление

    private void BeginRecover()
    {
        _phase = Phase.Recovering;
        _phaseTime = 0f;

        // руки снимаются с лица: снова держащие кисти, с поз ладоней на лице (или оттуда, куда успели долететь)
        if (_handsOnFace)
        {
            HandRear.position = CoverRight.position; HandRear.rotation = CoverRight.rotation;
            HandFront.position = CoverLeft.position; HandFront.rotation = CoverLeft.rotation;
        }
        CoverLeft.gameObject.SetActive(false);
        CoverRight.gameObject.SetActive(false);
        HandRear.gameObject.SetActive(true);
        HandFront.gameObject.SetActive(true);
        _rearFromPos = HandRear.localPosition; _rearFromRot = HandRear.localRotation;
        _frontFromPos = HandFront.localPosition; _frontFromRot = HandFront.localRotation;
        _handsOnFace = false;

        // дробовик возвращается в руки с пола
        Gun.SetParent(Arms, true);
        _gunFromPos = Gun.localPosition;
        _gunFromRot = Gun.localRotation;
    }

    private void AnimateRecover()
    {
        float t = Mathf.Clamp01(_phaseTime / RecoverTime);
        float s = t * t * (3f - 2f * t);
        float arc = Mathf.Sin(s * Mathf.PI);

        Gun.localPosition = Vector3.Lerp(_gunFromPos, _gunRestPos, s) + Vector3.up * arc * 0.2f;
        Gun.localRotation = Quaternion.Slerp(_gunFromRot, _gunRestRot, s);

        // цели рук — их места на дробовике, который сам ещё летит: пересчитываются каждый кадр
        var rearTargetPos = transform.InverseTransformPoint(Arms.TransformPoint(_handRearRestPos));
        var rearTargetRot = Quaternion.Inverse(transform.rotation) * Arms.rotation * _handRearRestRot;
        var frontTargetPos = transform.InverseTransformPoint(Forend.TransformPoint(_handFrontRestPos));
        var frontTargetRot = Quaternion.Inverse(transform.rotation) * Forend.rotation * _handFrontRestRot;
        HandRear.localPosition = Vector3.Lerp(_rearFromPos, rearTargetPos, s);
        HandRear.localRotation = Quaternion.Slerp(_rearFromRot, rearTargetRot, s);
        HandFront.localPosition = Vector3.Lerp(_frontFromPos, frontTargetPos, s);
        HandFront.localRotation = Quaternion.Slerp(_frontFromRot, frontTargetRot, s);

        if (_phaseTime >= AuraBackDelay) Aura.SetVisible(true);
        if (t >= 1f) FinishRecover();
    }

    private void FinishRecover()
    {
        Gun.SetParent(Arms, false);
        Gun.localPosition = _gunRestPos; Gun.localRotation = _gunRestRot;
        Forend.localPosition = _forendRest;
        HandRear.SetParent(Arms, false);
        HandRear.localPosition = _handRearRestPos; HandRear.localRotation = _handRearRestRot;
        HandFront.SetParent(Forend, false);
        HandFront.localPosition = _handFrontRestPos; HandFront.localRotation = _handFrontRestRot;
        HandRear.gameObject.SetActive(true);
        HandFront.gameObject.SetActive(true);
        CoverLeft.gameObject.SetActive(false);
        CoverRight.gameObject.SetActive(false);
        Aura.SetVisible(true);
        _handsOnFace = false;
        _phase = Phase.Normal;
    }
}
