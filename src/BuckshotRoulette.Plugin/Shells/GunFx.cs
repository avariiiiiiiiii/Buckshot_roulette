using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Shells;

/// <summary>
/// Звук и эффекты выстрела из дробовика — у дилера и у дробовика игроков; берутся у ванильного дробовика.
/// Боевой: грохот, вспышка, тряска. Холостой: щелчок курка, глухой хлопок, лёгкая тряска и дымок из дула — без вспышки и дроби.
/// </summary>
internal static class GunFx
{
    private const string ShotgunPath = "Items/Item Gun Shotgun";
    private static ItemGun _gun;
    private static Sound _pop;
    private static GameObject _puffPrefab;

    public static void Load()
    {
        var prefab = Resources.Load<GameObject>(ShotgunPath);
        _gun = prefab != null ? prefab.GetComponent<ItemGun>() : null;
        if (_gun == null) Plugin.Log.LogError($"Не найден ItemGun в {ShotgunPath}: у дробовиков не будет звука выстрела");
    }

    /// <summary>Боевой выстрел дилера: звук, тряска, вспышка у дула (дробовик игроков стреляет ванильным ShootRPC).</summary>
    public static void Fire(Transform muzzle)
    {
        if (_gun == null || muzzle == null) return;
        SemiFunc.CameraShakeImpactDistance(muzzle.position, 5f, 0.1f, 3f, 16f);
        SemiFunc.CameraShakeDistance(muzzle.position, 0.1f, 0.1f, 3f, 16f);
        _gun.soundShoot.Play(muzzle.position);
        _gun.soundShootGlobal.Play(muzzle.position);
        if (_gun.muzzleFlashPrefab == null) return;
        var flash = Object.Instantiate(_gun.muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle);
        flash.GetComponent<ItemGunMuzzleFlash>()?.ActivateAllEffects();
    }

    /// <summary>Холостой выстрел: щелчок, глухой хлопок, дымок — сразу видно и слышно, что патрон пустой.</summary>
    public static void Blank(Transform muzzle)
    {
        if (muzzle == null) return;
        SemiFunc.CameraShakeImpactDistance(muzzle.position, 1.5f, 0.1f, 2f, 8f);
        if (_gun != null) _gun.soundNoAmmoClick.Play(muzzle.position);
        _pop ??= new Sound { Sounds = new[] { PopClip() }, Volume = 0.6f, VolumeRandom = 0.05f, PitchRandom = 0.08f };
        _pop.Play(muzzle.position);
        var puff = Object.Instantiate(PuffPrefab(), muzzle.position, muzzle.rotation);
        puff.SetActive(true);
        Object.Destroy(puff, 2.5f);
    }

    /// <summary>Щелчок: патрон холостой (при попадании по дилеру) или вставляется патрон в оверлее.</summary>
    public static void Click(Vector3 position)
    {
        if (_gun != null) _gun.soundNoAmmoClick.Play(position);
    }

    /// <summary>Глухой хлопок: короткий низкий шумовой удар без грохота боевого выстрела.</summary>
    private static AudioClip PopClip()
    {
        const int rate = 44100;
        int count = (int)(rate * 0.35f);
        var data = new float[count];
        var random = new System.Random(11);
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float envelope = Mathf.Exp(-t * 18f);
            low += ((float)random.NextDouble() * 2f - 1f - low) * 0.08f;           // сильно сглаженный шум — «бух»
            data[i] = 0.9f * envelope * (low * 3f + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 90f));
        }
        var clip = AudioClip.Create("BuckshotRoulette Blank Pop", count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Серый дымок из дула: короткий выброс частиц вперёд.</summary>
    private static GameObject PuffPrefab()
    {
        if (_puffPrefab != null) return _puffPrefab;
        _puffPrefab = new GameObject("BuckshotRoulette Blank Puff");
        _puffPrefab.SetActive(false);
        Object.DontDestroyOnLoad(_puffPrefab);
        var particles = _puffPrefab.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.55f, 0.55f, 0.5f), new Color(0.75f, 0.75f, 0.72f, 0.7f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.05f;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.02f;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var renderer = _puffPrefab.GetComponent<ParticleSystemRenderer>();
        var material = FxTextures.AlphaMaterial("BuckshotRoulette Blank Puff", FxTextures.Smoke());
        if (material != null) renderer.sharedMaterial = material;
        else renderer.enabled = false;
        return _puffPrefab;
    }
}
