using BepInEx.Configuration;

namespace BuckshotRoulette.Core;

internal static class Settings
{
    public static ConfigEntry<bool> DiagnosticsEnabled { get; private set; }
    public static ConfigEntry<bool> AutoStartSingleplayer { get; private set; }
    public static ConfigEntry<int> AutoStartLevel { get; private set; }
    public static ConfigEntry<bool> SpawnValuablesOnLevelStart { get; private set; }
    public static ConfigEntry<bool> SpawnDealerOnLevelStart { get; private set; }
    public static ConfigEntry<bool> DealerStunTest { get; private set; }
    public static ConfigEntry<bool> SpawnMassKitOnLevelStart { get; private set; }
    public static ConfigEntry<bool> SpawnItemsOnLevelStart { get; private set; }
    public static ConfigEntry<bool> ShellPoolSelfTest { get; private set; }
    public static ConfigEntry<float> DealerSpeed { get; private set; }
    public static ConfigEntry<float> MusicVolume { get; private set; }
    public static ConfigEntry<float> MusicRadius { get; private set; }
    public static ConfigEntry<float> MusicNearDistance { get; private set; }
    public static ConfigEntry<float> DealerChaseSpeed { get; private set; }
    public static ConfigEntry<float> DealerShootRange { get; private set; }
    public static ConfigEntry<float> DealerAimTime { get; private set; }
    public static ConfigEntry<float> DealerRetargetCooldown { get; private set; }
    public static ConfigEntry<float> DealerShotNoiseRadius { get; private set; }
    public static ConfigEntry<int> PlayerDamage { get; private set; }
    public static ConfigEntry<int> MagazineMin { get; private set; }
    public static ConfigEntry<int> MagazineMax { get; private set; }
    public static ConfigEntry<float> JammerRadius { get; private set; }

    private static bool _normalizingMagazineRange;

    public static void Bind(ConfigFile config)
    {
        JammerRadius = config.Bind("Jammer", "Radius", 45f,
            new ConfigDescription("Радиус глушилки в метрах; умножается на слух моба.", new AcceptableValueRange<float>(1f, 200f)));
        // Новый ключ намеренно заменяет прежний DumpOnLevelStart (по умолчанию включённый).
        // Иначе старые конфиги продолжали бы запускать диагностические логи в обычной игре.
        DiagnosticsEnabled = config.Bind("Debug", "DiagnosticsEnabled", false,
            "Включить диагностические дампы и журнал зрения дилера (для разработки).");
        AutoStartSingleplayer = config.Bind("Debug", "AutoStartSingleplayer", false,
            "Из главного меню сразу стартовать одиночный забег через тестовую ветку игры (для разработки).");
        AutoStartLevel = config.Bind("Debug", "AutoStartLevel", 0,
            "Куда стартовать автозапуском: 0 — уровень забега, 1 — магазин, 2 — обычный переход.");
        SpawnValuablesOnLevelStart = config.Bind("Debug", "SpawnValuablesOnLevelStart", false,
            "Выложить все ценности мода в фургоне, когда уровень сгенерирован (для разработки).");
        SpawnDealerOnLevelStart = config.Bind("Debug", "SpawnDealerOnLevelStart", false,
            "Заспавнить дилера, когда уровень сгенерирован (для разработки).");
        DealerStunTest = config.Bind("Debug", "DealerStunTest", false,
            "Оглушать дилера на 5 секунд каждые 20 секунд, чтобы проверить анимацию оглушения (для разработки).");
        SpawnMassKitOnLevelStart = config.Bind("Debug", "SpawnMassKitOnLevelStart", false,
            "Выложить перед игроком по одной ценности игры каждой массы и записать массы всех ценностей в лог — проверка, чем оглушается дилер (для разработки).");
        SpawnItemsOnLevelStart = config.Bind("Debug", "SpawnItemsOnLevelStart", false,
            "Выложить все пять предметов магазина мода в фургоне (для разработки).");
        ShellPoolSelfTest = config.Bind("Debug", "ShellPoolSelfTest", false,
            "Одиночка: стрелять кодом в дилера ванильным дробовиком, лазером и дробовиком дилера и писать в лог расход общего пула (для разработки).");
        DealerSpeed = config.Bind("Dealer", "Speed", 1.5f, "Скорость дилера, когда он бродит по карте, м/с (медиана ванильных врагов — 1.5).");
        MusicVolume = config.Bind("Dealer", "MusicVolume", 0.6f, new ConfigDescription("Громкость музыки General рядом с дилером.", new AcceptableValueRange<float>(0f, 1f)));
        MusicRadius = config.Bind("Dealer", "MusicRadius", 14f, "Радиус, в котором слышна музыка дилера, метры.");
        MusicNearDistance = config.Bind("Dealer", "MusicNearDistance", 3f, "До этой дистанции музыка играет на полной громкости, метры.");
        DealerChaseSpeed = config.Bind("Dealer", "ChaseSpeed", 3f, "Скорость погони за замеченным игроком, м/с (как у Robe).");
        DealerShootRange = config.Bind("Dealer", "ShootRange", 10f, "С какой дистанции дилер целится и стреляет, метры.");
        DealerAimTime = config.Bind("Dealer", "AimTime", 0.9f, "Сколько секунд дилер целится перед выстрелом.");
        DealerRetargetCooldown = config.Bind("Dealer", "RetargetCooldown", 25f, "После выстрела в игрока дилер не стреляет в него снова это количество секунд и ищет другого.");
        DealerShotNoiseRadius = config.Bind("Dealer", "ShotNoiseRadius", 45f, "Радиус, в котором все монстры слышат выстрел дилера, метры.");
        PlayerDamage = config.Bind("Dealer", "PlayerDamage", 100, "Урон боевым патроном по игроку (100 и больше — гарантированная смерть).");
        MagazineMin = config.Bind("Dealer", "MagazineMin", 2, new ConfigDescription("Минимум патронов в магазине.", new AcceptableValueRange<int>(2, 16)));
        MagazineMax = config.Bind("Dealer", "MagazineMax", 8, new ConfigDescription("Максимум патронов в магазине.", new AcceptableValueRange<int>(2, 16)));
        MagazineMin.SettingChanged += (_, __) => NormalizeMagazineRange(minimumChanged: true);
        MagazineMax.SettingChanged += (_, __) => NormalizeMagazineRange(minimumChanged: false);
        NormalizeMagazineRange(minimumChanged: true);
    }

    private static void NormalizeMagazineRange(bool minimumChanged)
    {
        if (_normalizingMagazineRange || MagazineMin.Value <= MagazineMax.Value) return;
        _normalizingMagazineRange = true;
        if (minimumChanged) MagazineMax.Value = MagazineMin.Value;
        else MagazineMin.Value = MagazineMax.Value;
        Plugin.Log.LogWarning($"Диапазон патронов исправлен на {MagazineMin.Value}..{MagazineMax.Value}.");
        _normalizingMagazineRange = false;
    }
}
