using System.Linq;
using BuckshotRoulette.Core;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Items;
using BuckshotRoulette.Logic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using REPOLib.Modules;
using UnityEngine;

namespace BuckshotRoulette.Shells;

/// <summary>
/// Общий пул патронов раунда. Каждый выстрел нашего дробовика (даже промах) и дилера снимает патрон.
/// Остальное оружие снимает патрон при попадании по дилеру, не меняя личную батарею дробовика.
/// При активном дилере пустой пул сразу заменяется с анимацией у всех.
/// Без дилера пустой пул ждёт зарядки дробовика.
/// Размер пула = число патронов в полной батарее дробовика: при смене пула процент заряда сохраняется, число патронов меняется.
/// Ведёт хост; состав и размер рассылаются событием.
/// </summary>
internal static class ShellPool
{
    private static NetworkedEvent _loadEvent;
    private static ShellMagazine _magazine;     // только у хоста
    private static LevelGenerator _level;
    private static readonly System.Random Rng = new System.Random();

    /// <summary>Размер текущего пула (у всех клиентов); до первого пула — ванильные 5 выстрелов дробовика.</summary>
    public static int Size { get; private set; } = 5;

    public static void Register() => _loadEvent = new NetworkedEvent("BuckshotRoulette.ShellPool", OnLoadEvent);

    /// <summary>Хост: пул на этот уровень есть (создаётся при первом обращении, с анимацией).</summary>
    public static void EnsureLoaded()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        SyncLevel();
        if (_magazine == null) Load(showAnimation: true);
    }

    /// <summary>
    /// Хост: следующий патрон из пула. true — боевой. При активном дилере пустой пул сразу заменяется новым.
    /// </summary>
    public static bool Take() => Take(reloadAfter: true);
    internal static bool TakeForShotgun() => Take(reloadAfter: false);
    private static bool Take(bool reloadAfter)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsLevel()) return true;
        EnsureLoaded();
        if (_magazine.IsEmpty)
        {
            if (!DealerIsActive()) return true;
            Load(showAnimation: true);
        }
        bool live = _magazine.Fire() == Shell.Live;
        Plugin.Log.LogInfo($"Пул: {(live ? "БОЕВОЙ" : "холостой")}, осталось {_magazine.Live} боевых, {_magazine.Blank} холостых");
        if (reloadAfter) ReloadIfEmpty();
        return live;
    }

    public static void ReloadIfEmpty()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || _magazine == null || !_magazine.IsEmpty) return;
        if (DealerIsActive()) Load(showAnimation: true);
    }

    /// <summary>Хост: показать всем, что сейчас в пуле (дилер появился, дробовик зарядили).</summary>
    public static void ShowCurrent()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        SyncLevel();
        if (_magazine == null || _magazine.IsEmpty)
        {
            Load(showAnimation: true);
            return;
        }
        Raise(_magazine, showAnimation: true);
    }

    /// <summary>Зарядка дробовика: без дилера пустой пул создаётся сейчас; существующий пул не переигрывается.</summary>
    public static void OnShotgunCharged()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        SyncLevel();
        if (_magazine == null || _magazine.IsEmpty) Load(showAnimation: true);
    }

    public static void SyncToPlayer(int actorNumber)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        SyncLevel();
        if (_magazine != null) Raise(_magazine, showAnimation: !_magazine.IsEmpty, targetActorNumber: actorNumber);
    }

    private static void SyncLevel()
    {
        if (_level == LevelGenerator.Instance) return;
        _level = LevelGenerator.Instance;
        _magazine = null;
    }

    private static void Load(bool showAnimation)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        _magazine = ShellMagazine.Random(Rng, Settings.MagazineMin.Value, Settings.MagazineMax.Value);
        Size = _magazine.Remaining;
        Plugin.Log.LogInfo($"Пул: новый — {_magazine.Serialize()} ({_magazine.Live} боевых, {_magazine.Blank} холостых)");
        if (showAnimation) Raise(_magazine, showAnimation: true);
    }

    private static bool DealerIsActive() => UnityEngine.Object.FindObjectsByType<EnemyDealer>(FindObjectsSortMode.None)
        .Any(dealer => dealer != null && dealer.GetComponent<Enemy>() != null
            && dealer.GetComponent<Enemy>().EnemyParent != null && dealer.GetComponent<Enemy>().EnemyParent.Spawned);

    private static void Raise(ShellMagazine shown, bool showAnimation, int targetActorNumber = 0) =>
        _loadEvent.RaiseEvent($"{Size}|{shown.Live}|{shown.Blank}|{showAnimation}|{targetActorNumber}", NetworkingEvents.RaiseAll, SendOptions.SendReliable);

    private static void OnLoadEvent(EventData data)
    {
        var parts = ((string)data.CustomData).Split('|');
        if (parts.Length != 5 || !int.TryParse(parts[0], out int size)
            || !int.TryParse(parts[1], out int live) || !int.TryParse(parts[2], out int blank)
            || !bool.TryParse(parts[3], out bool showAnimation)
            || !int.TryParse(parts[4], out int targetActorNumber) || targetActorNumber < 0
            || size < 2 || live < 0 || blank < 0 || live + blank > size
            || (showAnimation && live + blank == 0))
        {
            Plugin.Log.LogWarning("Получено некорректное состояние пула патронов");
            return;
        }
        Size = size;
        bool showForThisClient = targetActorNumber == 0
            || (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == targetActorNumber);
        if (showAnimation && showForThisClient)
            ShellOverlay.Show(new ShellMagazine(Enumerable.Repeat(Shell.Live, live).Concat(Enumerable.Repeat(Shell.Blank, blank))));
        BuckshotShells.ResizeAll(Size);
    }
}

internal sealed class ShellPoolCallbacks : MonoBehaviourPunCallbacks
{
    public override void OnPlayerEnteredRoom(Player newPlayer) => ShellPool.SyncToPlayer(newPlayer.ActorNumber);
}
