using System.Collections.Generic;
using BuckshotRoulette.Core;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Shells;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// Дробовик дилера в магазине: клон ванильного Item Gun Shotgun за полцены.
/// Патроны — полосы батареи: полная батарея (энергия пяти выстрелов ванильного дробовика) = размер общего пула (2..8).
/// Каждое нажатие курка берёт патрон из общего пула раунда: боевой — 100 урона (весь залп дроби), холостой — хлопок без дроби.
/// </summary>
internal static class BuckshotShotgun
{
    private const string TemplatePath = "Items/Item Gun Shotgun";
    private const string ItemName = "Item Gun Buckshot";
    private const int ShotDamage = 100;
    private const float SawCut = 0.2f;              // м модели: столько ствола отпиливает ножовка (до конца цевья)

    public static Item Item { get; private set; }

    public static void Register(Transform holder)
    {
        var template = Resources.Load<GameObject>(TemplatePath);
        if (template == null)
        {
            Plugin.Log.LogError($"Шаблон дробовика не найден: {TemplatePath}");
            return;
        }

        var prefab = Object.Instantiate(template, holder);
        prefab.name = ItemName;
        var attributes = prefab.GetComponent<ItemAttributes>();
        var item = Object.Instantiate(attributes.item);
        item.name = ItemName;
        item.itemName = Texts.T("Дробовик дилера", "Dealer's Shotgun");
        item.itemNameLocalized = null;
        var value = Object.Instantiate(item.value);
        value.name = ItemName + " Value";
        value.valueMin /= 2f;
        value.valueMax /= 2f;
        item.value = value;
        attributes.item = item;

        var gun = prefab.GetComponent<ItemGun>();
        SwapModel(prefab, gun.gunMuzzle);
        prefab.AddComponent<BuckshotShells>();
        // выстрел (боевой или холостой) — ровно одна полоса батареи, полос столько, сколько патронов в полном дробовике
        Plugin.Log.LogInfo($"Дробовик: ванильная батарея {prefab.GetComponent<ItemBattery>().batteryBars} полос, " +
                           $"расход {(gun.batteryDrainFullBar ? gun.batteryDrainFullBars + " полос" : gun.batteryDrain + " энергии")} на выстрел");
        gun.batteryDrainFullBar = true;
        gun.batteryDrainFullBars = 1;

        // свой префаб дроби: урон выстрела делится на все дробины
        var bullet = Object.Instantiate(gun.bulletPrefab, holder);
        bullet.name = "Buckshot Bullet";
        var hurt = bullet.GetComponentInChildren<HurtCollider>(true);
        int pelletDamage = Mathf.CeilToInt(ShotDamage / (float)Mathf.Max(1, gun.numberOfBullets));
        Plugin.Log.LogInfo($"Дробовик: ванильный — дробин {gun.numberOfBullets}, урон дробины {hurt.enemyDamage}, " +
                           $"цена {value.valueMin}..{value.valueMax}; наш урон дробины {pelletDamage}");
        hurt.enemyDamage = pelletDamage;
        gun.bulletPrefab = bullet;

        if (REPOLib.Modules.Items.RegisterItem(attributes) == null)
        {
            Plugin.Log.LogError("Дробовик не зарегистрирован");
            return;
        }
        Item = item;
        Plugin.Log.LogInfo($"Предмет зарегистрирован: {ItemName}");
    }

    /// <summary>
    /// Вместо ванильной модели — обрез дилера (части gun и forend модели dealer). Дуло нашего ствола ставится в дульный срез
    /// ванильного ружья и смотрит туда же, длина подгоняется под ванильную: вспышка, дробь и коллайдеры остаются на месте.
    /// </summary>
    private static void SwapModel(GameObject prefab, Transform muzzle)
    {
        var model = ModelLoader.LoadParts("dealer");
        var gunPart = model.Parts.Find(p => p.Name == "gun");
        var forendPart = model.Parts.Find(p => p.Name == "forend");
        var muzzleModel = model.Markers["muzzle"];

        // ванильная длина вдоль ствола: углы видимых мешей, спроецированные на ось ствола; сами меши прячем.
        // В префабе лежат и служебные кубы объёмов предметов (Small, Large, Vehicle…) с выключенными рендерерами — их не трогаем
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        float min = float.MaxValue, max = float.MinValue;
        var dump = new System.Text.StringBuilder();
        int hidden = 0;
        foreach (var r in renderers)
        {
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (!r.enabled || !r.gameObject.activeSelf || mesh == null) continue;
            r.enabled = false;
            hidden++;
            var b = mesh.bounds;
            float rMin = float.MaxValue, rMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float along = Vector3.Dot(r.transform.TransformPoint(corner), muzzle.forward);
                rMin = Mathf.Min(rMin, along);
                rMax = Mathf.Max(rMax, along);
            }
            dump.Append($"\n  {r.name} ({mesh.name}), вдоль ствола {rMin:F2}..{rMax:F2}");
            min = Mathf.Min(min, rMin);
            max = Mathf.Max(max, rMax);
        }
        if (Settings.DiagnosticsEnabled.Value) Plugin.Log.LogInfo($"Дробовик: ванильные меши:{dump}");
        float vanillaLength = max > min ? max - min : 0f;
        float ourLength = gunPart.Model.Mesh.bounds.size.z;
        float scale = vanillaLength > 0.1f ? Mathf.Clamp(vanillaLength / ourLength, 0.6f, 1.4f) : 1f;

        var root = new GameObject("Buckshot Model").transform;
        root.SetParent(muzzle.parent, false);
        root.localRotation = muzzle.localRotation;
        root.localScale = Vector3.one * scale;
        // дуло модели (относительно пивота обреза) совпадает с дульным срезом
        root.localPosition = muzzle.localPosition - muzzle.localRotation * ((muzzleModel - gunPart.Pivot) * scale);

        var gunGo = AddPart(root, gunPart, Vector3.zero);
        AddPart(gunGo.transform, forendPart, forendPart.Pivot - gunPart.Pivot);

        // пропиленный ножовкой вариант: всё, что дальше линии отпила, прижато к ней; дульный срез уезжает назад
        float muzzleZ = (muzzleModel - gunPart.Pivot).z;
        float cutZ = muzzleZ - SawCut;
        var sawed = Object.Instantiate(gunPart.Model.Mesh);
        sawed.name = gunPart.Model.Mesh.name + " Sawed";
        var vertices = sawed.vertices;
        for (int i = 0; i < vertices.Length; i++)
            if (vertices[i].z > cutZ) vertices[i].z = cutZ;
        sawed.vertices = vertices;
        sawed.RecalculateBounds();
        var buckshot = root.gameObject.AddComponent<BuckshotModel>();
        buckshot.Gun = gunGo.GetComponent<MeshFilter>();
        buckshot.Normal = gunPart.Model.Mesh;
        buckshot.Sawed = sawed;
        buckshot.Tip = CutTip(gunPart.Model.Mesh, cutZ);
        buckshot.CutLocal = new Vector3((muzzleModel - gunPart.Pivot).x, (muzzleModel - gunPart.Pivot).y, cutZ);
        buckshot.Muzzle = muzzle;
        buckshot.MuzzleNormal = muzzle.localPosition;
        buckshot.MuzzleSawed = muzzle.localPosition - muzzle.localRotation * Vector3.forward * (SawCut * scale);
        Plugin.Log.LogInfo($"Дробовик: модель обреза, ванильная длина {vanillaLength:F2} м, наша {ourLength:F2} м, масштаб {scale:F2}, скрыто ванильных мешей: {hidden}");
    }

    /// <summary>Отпиливаемый кусок: треугольники ствола и подствольного магазина целиком за линией отпила.</summary>
    private static Mesh CutTip(Mesh source, float cutZ)
    {
        var vertices = source.vertices;
        var normals = source.normals;
        var uv = source.uv;
        var triangles = source.triangles;
        var map = new Dictionary<int, int>();
        var tipVertices = new List<Vector3>();
        var tipNormals = new List<Vector3>();
        var tipUv = new List<Vector2>();
        var tipTriangles = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            if (vertices[triangles[i]].z <= cutZ || vertices[triangles[i + 1]].z <= cutZ || vertices[triangles[i + 2]].z <= cutZ) continue;
            for (int k = 0; k < 3; k++)
            {
                int index = triangles[i + k];
                if (!map.TryGetValue(index, out int mapped))
                {
                    mapped = tipVertices.Count;
                    map[index] = mapped;
                    tipVertices.Add(vertices[index]);
                    tipNormals.Add(normals[index]);
                    tipUv.Add(uv[index]);
                }
                tipTriangles.Add(mapped);
            }
        }
        var tip = new Mesh { name = source.name + " Tip" };
        tip.SetVertices(tipVertices);
        tip.SetNormals(tipNormals);
        tip.SetUVs(0, tipUv);
        tip.SetTriangles(tipTriangles, 0);
        tip.RecalculateBounds();
        return tip;
    }

    private static GameObject AddPart(Transform parent, ModelLoader.Part part, Vector3 localPosition)
    {
        var go = new GameObject(part.Name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.AddComponent<MeshFilter>().sharedMesh = part.Model.Mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = part.Model.Materials;
        return go;
    }
}

/// <summary>
/// Модель обреза на предмете: целый и пропиленный ножовкой меш, дульный срез переезжает вместе с концом ствола.
/// Поля публичные: префаб собирается кодом, а в игре живёт его клон (Instantiate копирует только сериализуемое).
/// </summary>
internal sealed class BuckshotModel : MonoBehaviour
{
    private const float TipLifeSeconds = 8f;

    public MeshFilter Gun;
    public Mesh Normal;
    public Mesh Sawed;
    public Mesh Tip;
    public Vector3 CutLocal;
    public Transform Muzzle;
    public Vector3 MuzzleNormal;
    public Vector3 MuzzleSawed;

    public void SetSawed(bool sawed)
    {
        if (sawed && Gun.sharedMesh != Sawed) DropTip();
        Gun.sharedMesh = sawed ? Sawed : Normal;
        Muzzle.localPosition = sawed ? MuzzleSawed : MuzzleNormal;
    }

    /// <summary>Отпиленный кусок ствола падает на пол (у каждого клиента своя копия, на игру не влияет) и исчезает.</summary>
    private void DropTip()
    {
        var tip = new GameObject("Sawed Barrel Tip");
        tip.transform.SetPositionAndRotation(Gun.transform.position, Gun.transform.rotation);
        tip.transform.localScale = Gun.transform.lossyScale;
        tip.AddComponent<MeshFilter>().sharedMesh = Tip;
        tip.AddComponent<MeshRenderer>().sharedMaterials = Gun.GetComponent<MeshRenderer>().sharedMaterials;
        var box = tip.AddComponent<BoxCollider>();
        box.center = Tip.bounds.center;
        box.size = Tip.bounds.size;
        var body = tip.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.linearVelocity = -Gun.transform.up * 0.6f + Random.insideUnitSphere * 0.3f;
        body.angularVelocity = Random.insideUnitSphere * 5f;
        Destroy(tip, TipLifeSeconds);
    }
}

/// <summary>
/// Батарея дробовика — его личный запас. Каждый выстрел снимает один патрон пула;
/// сохранённый результат применяется ко всему залпу: холостой вообще не создаёт дробь.
/// Новый пул при активном дилере появляется сразу после исчерпания старого; без дилера пустой пул
/// ждёт зарядки дробовика. Повторная зарядка непустой пул не переигрывает.
/// </summary>
internal sealed class BuckshotShells : MonoBehaviour
{
    private const float ChargeQuietSeconds = 1f;     // столько без прироста заряда — зарядка закончилась

    private static readonly List<BuckshotShells> Alive = new List<BuckshotShells>();

    private ItemGun _gun;
    private ItemBattery _battery;
    private float _lastLife = -1f;
    private float _chargeQuiet;
    private bool _charging;
    private double _nextShotTime;
    internal bool LastPoolShellLive { get; set; }

    private void Awake()
    {
        _gun = GetComponent<ItemGun>();
        _battery = GetComponent<ItemBattery>();
    }

    private void OnEnable() => Alive.Add(this);

    private void OnDisable() => Alive.Remove(this);

    private void Start()
    {
        Resize(ShellPool.Size);
    }

    /// <summary>У всех клиентов: число полос = размер пула, процент заряда тот же.</summary>
    public static void ResizeAll(int bars)
    {
        foreach (var shells in Alive) shells.Resize(bars);
    }

    private void Resize(int bars)
    {
        if (_battery == null || bars <= 0 || _battery.batteryBars == bars) return;
        _battery.batteryBars = bars;
        _battery.batteryLifeInt = _battery.BatteryBarsGet();
        var visual = _battery.batteryVisualLogic;
        if (visual == null) return;
        visual.batteryBars = bars;
        visual.BatteryBarsSet();
        visual.BatteryBarsUpdate(_battery.batteryLifeInt, _forceUpdate: true);
    }

    private void Update()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsLevel()) return;
        float life = _battery.batteryLife;
        if (_lastLife < 0f) _lastLife = life;
        if (life > _lastLife + 0.01f)
        {
            _charging = true;
            _chargeQuiet = 0f;
        }
        else if (_charging && (_chargeQuiet += Time.deltaTime) > ChargeQuietSeconds)
        {
            _charging = false;
            Plugin.Log.LogInfo($"Дробовик заряжен: {_battery.batteryLifeInt} из {_battery.batteryBars} патронов");
            ShellPool.OnShotgunCharged();
        }
        _lastLife = life;
    }

    /// <summary>Хост: нажатие курка. true — выстрел состоялся.</summary>
    public bool TriggerPull()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return false;
        // щелчок пустого даёт сама игра (ItemGun.ShootLogic), самопроизвольный выстрел пустого молчит, как ванильный
        if (_battery.batteryLifeInt <= 0) return false;
        if (Time.timeAsDouble < _nextShotTime) return true;
        _nextShotTime = Time.timeAsDouble + Mathf.Max(.05f, _gun.shootCooldown);
        int shotId = ShotTag.Next();
        bool live = !SemiFunc.RunIsLevel() || DealerDamagePatches.ReserveShotgunShot(shotId);
        if (SemiFunc.IsMultiplayer()) _gun.photonView.RPC(nameof(FireShellRPC), RpcTarget.All, live, shotId);
        else FireShellRPC(live, shotId);
        _gun.StateSet(ItemGun.State.Reloading);
        return true;
    }

    [PunRPC]
    private void FireShellRPC(bool live, int shotId, PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        LastPoolShellLive = live;
        try { BuckshotFire.Execute(_gun, live, shotId, info); }
        finally
        {
            if (SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.RunIsLevel()) ShellPool.ReloadIfEmpty();
        }
        if (SemiFunc.IsMasterClientOrSingleplayer())
            Plugin.Log.LogInfo($"Дробовик: {(live ? "БОЕВОЙ" : "холостой без дроби и урона")}, " +
                               $"патронов {_battery.batteryLifeInt} из {_battery.batteryBars}");
    }

    internal bool RequestPull()
    {
        // Сохранить штатный dry-fire: ShootLogic использует false, чтобы показать щелчок пустого оружия.
        if (_battery == null || _battery.batteryLifeInt <= 0) return false;
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            return TriggerPull();
        }
        if (_gun == null || _gun.photonView == null) return false;
        _gun.photonView.RPC(nameof(RequestPullRPC), RpcTarget.MasterClient);
        return true;
    }

    [PunRPC]
    private void RequestPullRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || info.Sender == null) return;
        var grab = GetComponent<PhysGrabObject>();
        if (grab == null) return;
        bool heldBySender = false;
        foreach (var grabber in grab.playerGrabbing)
        {
            var player = grabber != null ? grabber.playerAvatar : null;
            if (player == null || player.photonView == null || player.photonView.OwnerActorNr != info.Sender.ActorNumber) continue;
            heldBySender = true;
            break;
        }
        if (!heldBySender) return;
        TriggerPull();
    }

}

[HarmonyPatch(typeof(ItemGun), nameof(ItemGun.Shoot))]
internal static class BuckshotShootPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemGun __instance, ref bool __result)
    {
        var shells = __instance.GetComponent<BuckshotShells>();
        if (shells == null) return true;
        __result = shells.RequestPull();
        return false;
    }
}

