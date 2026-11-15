using UnityEngine;

namespace BuckshotRoulette.Core;

/// <summary>
/// Постоянный объект мода: тошнота. Живёт отдельно от предметов,
/// потому что предмет после использования остаётся в мире и может исчезнуть вместе с уровнем.
/// </summary>
internal sealed class EffectsHost : MonoBehaviour
{
    public static EffectsHost Instance { get; private set; }

    private const float NauseaFadeIn = 1.0f;
    private const float NauseaFadeOut = 2.0f;

    private float _nauseaDuration;
    private float _nauseaElapsed;

    public static void Create(GameObject owner) => Instance = owner.AddComponent<EffectsHost>();

    /// <summary>Тошнота как в Minecraft: качающееся поле зрения и искажение линзы, зеленоватая виньетка.</summary>
    public void StartNausea(float seconds)
    {
        _nauseaDuration = seconds;
        _nauseaElapsed = 0f;
    }

    private void Update()
    {
        if (_nauseaElapsed >= _nauseaDuration) return;
        _nauseaElapsed += Time.deltaTime;
        ApplyNausea();
    }

    private void ApplyNausea()
    {
        var post = PostProcessing.Instance;
        var zoom = CameraZoom.Instance;
        if (post == null || zoom == null) return;

        float left = _nauseaDuration - _nauseaElapsed;
        float envelope = Mathf.Clamp01(_nauseaElapsed / NauseaFadeIn) * Mathf.Clamp01(left / NauseaFadeOut);
        float t = _nauseaElapsed;

        post.LensDistortionOverride(45f * envelope * Mathf.Sin(t * Mathf.PI * 2f * 0.35f), 6f, 6f, 0.15f, gameObject);
        zoom.OverrideZoomSet(zoom.playerZoomDefault + 12f * envelope * Mathf.Sin(t * Mathf.PI * 2f * 0.23f), 0.15f, 3f, 3f, gameObject, 30);
        post.VignetteOverride(new Color(0.25f, 0.6f, 0.1f), 0.4f * envelope, 0.6f, 3f, 3f, 0.15f, gameObject, 30);
        post.SaturationOverride(-25f * envelope, 3f, 3f, 0.15f, gameObject, 30);
    }
}
