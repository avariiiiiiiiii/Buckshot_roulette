using System.Collections;
using BuckshotRoulette.Core;
using BuckshotRoulette.Logic;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Valuables;

/// <summary>
/// Ценность-телефон: как только его берут в руку, он звонит и роботным голосом один раз называет имя ближайшего монстра.
/// Ближайшего выбирает хост (у него настоящие позиции врагов), озвучка идёт у всех игроков.
/// Врага передаём индексом в EnemyDirector.enemiesSpawned, как сама игра: у врагов в одиночной игре нет
/// зарегистрированного PhotonView, и поиск по ViewID всегда давал «монстров рядом нет».
/// </summary>
internal sealed class BurnerPhone : MonoBehaviour
{
    private const float RingSeconds = 2.4f;

    private static Sound _ring;

    private PhotonView _view;
    private PhysGrabObject _grab;
    private bool _rung;

    private void Awake()
    {
        _view = GetComponent<PhotonView>();
        _grab = GetComponent<PhysGrabObject>();
    }

    private void Update()
    {
        if (_rung || _grab == null || !_grab.grabbed) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        _rung = true;
        int target = NearestEnemyIndex();
        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(RingRPC), RpcTarget.All, target);
        else RingRPC(target);
    }

    private int NearestEnemyIndex()
    {
        var director = EnemyDirector.instance;
        if (director == null) return -1;

        EnemyParent best = null;
        float bestDistance = float.MaxValue;
        foreach (var enemy in director.enemiesSpawned)
        {
            if (enemy == null || !enemy.Spawned || enemy.Enemy == null || enemy.Enemy.CenterTransform == null) continue;
            if (enemy.Enemy.Health != null && enemy.Enemy.Health.dead) continue;
            float distance = Vector3.Distance(transform.position, enemy.Enemy.CenterTransform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = enemy;
            }
        }
        Plugin.Log.LogInfo(best == null
            ? $"Телефон: живых заспавненных монстров нет (всего в списке {director.enemiesSpawned.Count})"
            : $"Телефон: ближайший монстр — {best.enemyName}, {bestDistance:F1} м");
        return best == null ? -1 : SemiFunc.EnemyGetIndex(best.Enemy);
    }

    [PunRPC]
    private void RingRPC(int enemyIndex)
    {
        _rung = true;
        StartCoroutine(RingAndSpeak(enemyIndex));
    }

    private IEnumerator RingAndSpeak(int enemyIndex)
    {
        RingSound().Play(transform);
        // Leave a short quiet gap after the ring; the first consonant carries
        // most of the information in one-word monster names.
        yield return new WaitForSecondsRealtime(RingSeconds + 0.2f);
        if (this == null) yield break;
        RobotVoice.Speak(transform, PhraseFor(enemyIndex));
    }

    private static string PhraseFor(int enemyIndex)
    {
        var enemy = SemiFunc.EnemyGetFromIndex(enemyIndex)?.EnemyParent;
        if (enemy == null) return Texts.T("Монстров рядом нет", "No monsters nearby");
        string name = enemy.GetComponentInChildren<BuckshotRoulette.Dealer.EnemyDealer>(true) != null
            ? Texts.T("Дилер", "Dealer")
            : enemy.enemyNameLocalized != null ? enemy.enemyNameLocalized.GetLocalizedString() : enemy.enemyName;
        // A short lead-in lets the listener distinguish the start of the name
        // from the end of the ringtone. Keep names intact for modded enemies.
        return PhoneSpeech.ForName(name);
    }

    private static Sound RingSound() => _ring ??= new Sound
    {
        Sounds = new[] { RingTone.Create() },
        Volume = 0.8f,
        VolumeRandom = 0f,
        PitchRandom = 0f,
    };
}
