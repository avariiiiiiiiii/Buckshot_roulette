using System.Collections.Generic;
using BuckshotRoulette.Logic;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// The world uses Unity's normal slow physics. Beneficiaries use virtual normal
/// velocities: physical velocity ×10, continuous force ×100, impulse ×10.
/// Keeping the real fixed-step frequency unchanged avoids a 5 Hz simulation.
/// </summary>
internal static class AdrenalinePhysics
{
    private sealed class Body
    {
        public float Factor = 1f;
        public float Drag, AngularDrag, MaxAngular;
        public bool ParametersApplied;
    }
    private static readonly Dictionary<Rigidbody, Body> Bodies = new Dictionary<Rigidbody, Body>();
    private static readonly List<Rigidbody> Removed = new List<Rigidbody>();

    internal static float Factor(Rigidbody rb)
    {
        if (rb == null) return 1f;
        float desired = AdrenalineTime.Active && AdrenalineTime.NormalBody(rb) ? AdrenalineTime.Compensation : 1f;
        if (!Bodies.TryGetValue(rb, out var body))
        {
            if (desired == 1f) return 1f;
            body = new Body(); Bodies.Add(rb, body);
        }
        if (!Mathf.Approximately(body.Factor, desired))
        {
            RestoreParameters(rb, body);
            if (!rb.isKinematic)
            {
                float ratio = desired / body.Factor;
                rb.linearVelocity *= ratio;
                rb.angularVelocity *= ratio;
                // Do not leave boosted queued forces behind when the effect ends.
                var force = rb.GetAccumulatedForce();
                var torque = rb.GetAccumulatedTorque();
                rb.AddForce(force * (ratio * ratio - 1f), ForceMode.Force);
                rb.AddTorque(torque * (ratio * ratio - 1f), ForceMode.Force);
            }
            body.Factor = desired;
        }
        return desired;
    }

    internal static void RestoreStepParameters()
    {
        foreach (var pair in Bodies) if (pair.Key != null) RestoreParameters(pair.Key, pair.Value);
    }

    private static void RestoreParameters(Rigidbody rb, Body body)
    {
        if (!body.ParametersApplied) return;
        if (Mathf.Approximately(rb.linearDamping, body.Drag * body.Factor)) rb.linearDamping = body.Drag;
        if (Mathf.Approximately(rb.angularDamping, body.AngularDrag * body.Factor)) rb.angularDamping = body.AngularDrag;
        if (Mathf.Approximately(rb.maxAngularVelocity, body.MaxAngular * body.Factor)) rb.maxAngularVelocity = body.MaxAngular;
        body.ParametersApplied = false;
    }

    internal static void BeforeSimulation()
    {
        Removed.Clear();
        // Ownership may change between grabbing and this physics step.
        foreach (var pair in Bodies) if (pair.Key == null) Removed.Add(pair.Key);
        foreach (var rb in Removed) Bodies.Remove(rb);
        foreach (var pair in Bodies)
        {
            var rb = pair.Key; var body = pair.Value;
            float factor = Factor(rb);
            if (factor == 1f || rb.isKinematic || rb.IsSleeping()) continue;
            body.Drag = rb.linearDamping; body.AngularDrag = rb.angularDamping; body.MaxAngular = rb.maxAngularVelocity;
            rb.linearDamping = body.Drag * factor;
            rb.angularDamping = body.AngularDrag * factor;
            rb.maxAngularVelocity = body.MaxAngular * factor;
            body.ParametersApplied = true;
            if (rb.useGravity) rb.AddForce(Physics.gravity * (factor * factor - 1f), ForceMode.Acceleration);
        }
    }

    internal static void RestoreAll()
    {
        foreach (var pair in Bodies)
        {
            var rb = pair.Key; var body = pair.Value;
            if (rb == null) continue;
            RestoreParameters(rb, body);
            if (!rb.isKinematic && body.Factor != 1f)
            {
                rb.linearVelocity /= body.Factor; rb.angularVelocity /= body.Factor;
                float inverseSquared = 1f / (body.Factor * body.Factor);
                rb.AddForce(rb.GetAccumulatedForce() * (inverseSquared - 1f), ForceMode.Force);
                rb.AddTorque(rb.GetAccumulatedTorque() * (inverseSquared - 1f), ForceMode.Force);
            }
        }
        Bodies.Clear();
    }

    private static float ForceScale(Rigidbody rb, ForceMode mode) => AdrenalineWindow.ForceMultiplier(Factor(rb),
        mode == ForceMode.Impulse || mode == ForceMode.VelocityChange);
    internal static Vector3 GetLinearVelocity(Rigidbody rb) { float factor = Factor(rb); return rb.linearVelocity / factor; }
    internal static void SetLinearVelocity(Rigidbody rb, Vector3 value) => rb.linearVelocity = value * Factor(rb);
    internal static Vector3 GetAngularVelocity(Rigidbody rb) { float factor = Factor(rb); return rb.angularVelocity / factor; }
    internal static void SetAngularVelocity(Rigidbody rb, Vector3 value) => rb.angularVelocity = value * Factor(rb);
    internal static Vector3 GetPointVelocity(Rigidbody rb, Vector3 point) { float factor = Factor(rb); return rb.GetPointVelocity(point) / factor; }
    internal static Vector3 GetRelativePointVelocity(Rigidbody rb, Vector3 point) { float factor = Factor(rb); return rb.GetRelativePointVelocity(point) / factor; }
    // Photon extrapolates in server/real seconds, not Unity's scaled seconds.
    internal static Vector3 NetworkLinearVelocity(Rigidbody rb) { Factor(rb); return rb.linearVelocity * Time.timeScale; }
    internal static Vector3 NetworkAngularVelocity(Rigidbody rb) { Factor(rb); return rb.angularVelocity * Time.timeScale; }
    internal static void SetNetworkLinearVelocity(Rigidbody rb, Vector3 value) { Factor(rb); rb.linearVelocity = value / Mathf.Max(Time.timeScale, .0001f); }
    internal static void SetNetworkAngularVelocity(Rigidbody rb, Vector3 value) { Factor(rb); rb.angularVelocity = value / Mathf.Max(Time.timeScale, .0001f); }
    internal static float NetworkTime() => Time.unscaledTime;
    internal static float NetworkDelta() => Time.unscaledDeltaTime;
    internal static void AddForce(Rigidbody rb, Vector3 value) => AddForce(rb, value, ForceMode.Force);
    internal static void AddForce(Rigidbody rb, Vector3 value, ForceMode mode) => rb.AddForce(value * ForceScale(rb, mode), mode);
    internal static void AddRelativeForce(Rigidbody rb, Vector3 value) => AddRelativeForce(rb, value, ForceMode.Force);
    internal static void AddRelativeForce(Rigidbody rb, Vector3 value, ForceMode mode) => rb.AddRelativeForce(value * ForceScale(rb, mode), mode);
    internal static void AddTorque(Rigidbody rb, Vector3 value) => AddTorque(rb, value, ForceMode.Force);
    internal static void AddTorque(Rigidbody rb, Vector3 value, ForceMode mode) => rb.AddTorque(value * ForceScale(rb, mode), mode);
    internal static void AddRelativeTorque(Rigidbody rb, Vector3 value) => AddRelativeTorque(rb, value, ForceMode.Force);
    internal static void AddRelativeTorque(Rigidbody rb, Vector3 value, ForceMode mode) => rb.AddRelativeTorque(value * ForceScale(rb, mode), mode);
    internal static void AddForceAtPosition(Rigidbody rb, Vector3 value, Vector3 point) => AddForceAtPosition(rb, value, point, ForceMode.Force);
    internal static void AddForceAtPosition(Rigidbody rb, Vector3 value, Vector3 point, ForceMode mode) => rb.AddForceAtPosition(value * ForceScale(rb, mode), point, mode);
    internal static void AddExplosionForce(Rigidbody rb, float force, Vector3 point, float radius) => AddExplosionForce(rb, force, point, radius, 0f, ForceMode.Force);
    internal static void AddExplosionForce(Rigidbody rb, float force, Vector3 point, float radius, float lift) => AddExplosionForce(rb, force, point, radius, lift, ForceMode.Force);
    internal static void AddExplosionForce(Rigidbody rb, float force, Vector3 point, float radius, float lift, ForceMode mode)
        => rb.AddExplosionForce(force * ForceScale(rb, mode), point, radius, lift, mode);
}

[DefaultExecutionOrder(32000)]
internal sealed class AdrenalinePhysicsStep : MonoBehaviour
{
    private void FixedUpdate() { if (AdrenalineTime.Active) AdrenalinePhysics.BeforeSimulation(); }
    private void LateUpdate() { if (AdrenalineTime.Instance != null) AdrenalineTime.Instance.ApplyAnimations(); }
}
