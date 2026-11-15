using System.Collections.Generic;
using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>Эффекты ножовки: жёлтая обводка цели, искры и стружка при пропиле, звук пилы по металлу.</summary>
internal static class HacksawFx
{
    private const float OutlineScale = 1.06f;
    private static readonly Color OutlineColor = new Color(1f, 0.85f, 0.1f, 0.45f);

    private static Material _outline;
    private static GameObject _sparksPrefab;
    private static Sound _saw;

    // ---------------------------------------------------------------- обводка

    /// <summary>
    /// Жёлтая обводка: полупрозрачная копия каждого видимого меша, увеличенная от его центра (вершины ванильных мешей
    /// могут быть недоступны для чтения, масштаб их не требует). Видна только тому, кто держит ножовку.
    /// </summary>
    public static List<GameObject> Outline(GameObject target)
    {
        var hulls = new List<GameObject>();
        _outline ??= FxTextures.AlphaMaterial("BuckshotRoulette Hacksaw Outline", null);
        if (_outline == null) return hulls;
        _outline.color = OutlineColor;
        foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (!renderer.enabled || filter == null || filter.sharedMesh == null) continue;
            var hull = new GameObject("Hacksaw Outline");
            hull.transform.SetParent(renderer.transform, false);
            var center = filter.sharedMesh.bounds.center;
            hull.transform.localScale = Vector3.one * OutlineScale;
            hull.transform.localPosition = center - center * OutlineScale;
            hull.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var hullRenderer = hull.AddComponent<MeshRenderer>();
            hullRenderer.sharedMaterial = _outline;
            hullRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hullRenderer.receiveShadows = false;
            hulls.Add(hull);
        }
        return hulls;
    }

    // ---------------------------------------------------------------- искры

    public static void Sparks(Transform anchor, Vector3 localPosition)
    {
        var sparks = Object.Instantiate(SparksPrefab(), anchor);
        sparks.transform.localPosition = localPosition;
        sparks.transform.rotation = Quaternion.identity;
        sparks.SetActive(true);
        Object.Destroy(sparks, SawSeconds + 1f);
    }

    /// <summary>Пропил длится несколько секунд: искры летят пачками всё это время (выпуск по времени, а не одним залпом).</summary>
    private static GameObject SparksPrefab()
    {
        if (_sparksPrefab != null) return _sparksPrefab;
        _sparksPrefab = new GameObject("BuckshotRoulette Hacksaw Sparks");
        _sparksPrefab.SetActive(false);
        Object.DontDestroyOnLoad(_sparksPrefab);
        var particles = _sparksPrefab.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = false;
        main.duration = SawSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f, 1f), new Color(1f, 0.95f, 0.5f, 1f));
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 70f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.04f;
        shape.rotation = new Vector3(-90f, 0f, 0f);       // полусфера вверх: искры фонтаном
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var renderer = _sparksPrefab.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1f;
        var material = FxTextures.AlphaMaterial("BuckshotRoulette Hacksaw Sparks", FxTextures.Smoke());
        if (material != null) renderer.sharedMaterial = material;
        else renderer.enabled = false;
        return _sparksPrefab;
    }

    // ---------------------------------------------------------------- звук

    public static void SawSound(Vector3 position)
    {
        _saw ??= new Sound { Sounds = new[] { SawClip() }, Volume = 0.8f, VolumeRandom = 0f, PitchRandom = 0.04f };
        _saw.Play(position);
    }

    /// <summary>
    /// Пила по металлу (свободной записи нет — синтез): ходы полотна туда-обратно; на каждом ходу зубья бьют по металлу
    /// с частотой, растущей со скоростью хода, удары возбуждают набор металлических резонансов (звон заготовки);
    /// ход «от себя» режет сильнее хода «к себе».
    /// </summary>
    private static AudioClip SawClip()
    {
        const int rate = 44100;
        const float seconds = SawSeconds;
        const float strokesPerSecond = 2.6f;
        int count = (int)(rate * seconds);
        var data = new float[count];
        var random = new System.Random(7);
        // резонансы заготовки: частота, затухание (1/с), громкость
        var modes = new[] { (2150f, 9f, 1f), (3380f, 12f, 0.7f), (5120f, 16f, 0.5f), (7930f, 22f, 0.35f), (11200f, 30f, 0.2f) };
        var y1 = new float[modes.Length];
        var y2 = new float[modes.Length];
        var coef = new (float a1, float a2, float g)[modes.Length];
        for (int m = 0; m < modes.Length; m++)
        {
            float r = Mathf.Exp(-modes[m].Item2 / rate);
            float w = 2f * Mathf.PI * modes[m].Item1 / rate;
            coef[m] = (2f * r * Mathf.Cos(w), -r * r, modes[m].Item3 * (1f - r));
        }
        float toothPhase = 0f, rasp = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float strokePhase = t * strokesPerSecond % 1f;
            float speed = Mathf.Abs(Mathf.Sin(strokePhase * Mathf.PI));        // скорость полотна в течение хода
            bool push = (int)(t * strokesPerSecond) % 2 == 0;
            float pressure = speed * (push ? 1f : 0.45f);
            toothPhase += (250f + 650f * speed) / rate;                         // удары зубьев чаще на скорости
            float tooth = 0f;
            if (toothPhase >= 1f)
            {
                toothPhase -= 1f;
                tooth = pressure * (0.6f + 0.4f * (float)random.NextDouble());
            }
            rasp += (((float)random.NextDouble() * 2f - 1f) * pressure - rasp) * 0.6f;   // скрежет — шум под давлением
            float excitation = tooth * 4f + rasp * 0.5f;
            float ring = 0f;
            for (int m = 0; m < modes.Length; m++)
            {
                float y = coef[m].a1 * y1[m] + coef[m].a2 * y2[m] + coef[m].g * excitation;
                y2[m] = y1[m];
                y1[m] = y;
                ring += y;
            }
            float fade = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((seconds - t) / 0.15f);
            data[i] = Mathf.Clamp(fade * (ring * 1.6f + rasp * 0.35f), -1f, 1f);
        }
        var clip = AudioClip.Create("BuckshotRoulette Hacksaw", count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public const float SawSeconds = 1.6f;
}
