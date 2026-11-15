using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// Аура темноты вокруг дилера: мягкие чёрные клочья вокруг и позади головы и рук, из которых видно лицо и руки.
/// Частицы живут в мире, поэтому за движущимся дилером тянется короткий шлейф. При оглушении новые клочья перестают
/// появляться и аура растворяется сама за время жизни частиц (около секунды), после оглушения нарастает снова.
/// </summary>
internal sealed class DealerAura : MonoBehaviour
{
    private ParticleSystem _particles;

    public static DealerAura Create(Transform parent, Vector3 center)
    {
        var go = new GameObject("Darkness Aura");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        var particles = go.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);   // короткая жизнь: шлейф за идущим дилером быстро тает
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0.75f), new Color(0.03f, 0.01f, 0.01f, 0.95f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        var emission = particles.emission;
        emission.rateOverTime = 80f;                // чаще, чтобы облако вокруг осталось таким же плотным

        // центр облака между головой и руками и чуть позади: лицо и руки торчат из темноты вперёд
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        shape.radiusThickness = 1f;

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.25f));

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.3f;

        var rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 3f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var material = FxTextures.AlphaMaterial("Dealer Darkness", FxTextures.Smoke());
        if (material != null) renderer.sharedMaterial = material;
        else renderer.enabled = false;

        return go.AddComponent<DealerAura>();
    }

    private void Awake() => _particles = GetComponent<ParticleSystem>();

    /// <summary>Аура включается и гаснет плавно: выключается только появление новых клочьев.</summary>
    public void SetVisible(bool visible)
    {
        var emission = _particles.emission;
        emission.enabled = visible;
    }

}
