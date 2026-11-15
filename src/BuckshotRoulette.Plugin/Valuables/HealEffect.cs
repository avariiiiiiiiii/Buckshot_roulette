using UnityEngine;

namespace BuckshotRoulette.Valuables;

/// <summary>Эффект лечения как у ванильной аптечки: зелёные крестики вокруг игрока и звук.</summary>
internal static class HealEffect
{
    private const string PackPath = "Items/Item Health Pack Small";
    private const float PlayerHeight = 0.7f;
    private const float Lifetime = 4f;

    private static GameObject _particles;
    private static Sound _sound;

    /// <summary>Берёт эффект и звук прямо из префаба аптечки, чтобы не рисовать свои.</summary>
    public static void Load()
    {
        var pack = Resources.Load<GameObject>(PackPath);
        var effects = pack != null ? pack.transform.Find("Heal Effects") : null;
        var healthPack = pack != null ? pack.GetComponent<ItemHealthPack>() : null;
        if (effects == null || healthPack == null)
        {
            Plugin.Log.LogError($"Эффект лечения не найден в {PackPath}");
            return;
        }
        _particles = effects.gameObject;
        _sound = healthPack.soundUse;
    }

    public static void Play(Transform around)
    {
        if (_particles == null || around == null) return;

        var fx = Object.Instantiate(_particles, around);
        fx.transform.localPosition = Vector3.up * PlayerHeight;
        fx.transform.localRotation = Quaternion.identity;
        fx.SetActive(true);
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
        Object.Destroy(fx, Lifetime);

        _sound?.Play(around.position + Vector3.up * PlayerHeight);
    }
}
