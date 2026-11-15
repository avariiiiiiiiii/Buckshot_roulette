using UnityEngine;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// У Animal тело компактное и центр масс у начала Rigidbody, поэтому его упругая подвеска держит его прямо.
/// У дилера капсулы головы и рук висят высоко над началом Rigidbody, центр масс уезжает вверх и тело опрокидывается.
/// Поэтому центр масс и тензор инерции задаются вручную (инерция по форме Animal), форма коллайдеров остаётся для попаданий.
/// В оглушении тело — шар головы: центр масс в центре головы, инерция шара, остальные коллайдеры выключает EnemyDealer.
/// </summary>
internal sealed class DealerPhysics : MonoBehaviour
{
    private static readonly Vector3 InertiaPerMass = new Vector3(0.28f, 0.10f, 0.28f);

    /// <summary>Центр головы в пространстве Rigidbody (задаётся при сборке префаба).</summary>
    public Vector3 HeadCenter;
    public float HeadRadius;

    // в оглушении игра почти снимает сопротивление, а катящийся шар в физике не останавливается сам:
    // после первого кувырка шар гасит качение и остаётся лежать рядом, а не укатывается через комнату
    private const float TumbleSeconds = 0.5f;
    private const float RollDamping = 3f;           // 1/с

    private Rigidbody _rb;
    private bool _stunned;
    private float _stunTime;

    private void Awake() => _rb = GetComponent<Rigidbody>();

    private void OnEnable() => Apply();

    private void Start() => Apply();

    public void SetStunned(bool stunned)
    {
        _stunned = stunned;
        _stunTime = 0f;
        Apply();
    }

    private void FixedUpdate()
    {
        if (!_stunned || _rb.isKinematic) return;
        _stunTime += Time.fixedDeltaTime;
        if (_stunTime < TumbleSeconds) return;
        float keep = Mathf.Exp(-RollDamping * Time.fixedDeltaTime);
        _rb.angularVelocity *= keep;
        var velocity = _rb.linearVelocity;
        _rb.linearVelocity = new Vector3(velocity.x * keep, velocity.y, velocity.z * keep);
    }

    private void Apply()
    {
        if (_rb == null) return;
        _rb.inertiaTensorRotation = Quaternion.identity;
        if (_stunned)
        {
            _rb.centerOfMass = HeadCenter;
            _rb.inertiaTensor = Vector3.one * (0.4f * _rb.mass * HeadRadius * HeadRadius);
        }
        else
        {
            _rb.centerOfMass = Vector3.zero;
            _rb.inertiaTensor = InertiaPerMass * _rb.mass;
        }
    }
}
