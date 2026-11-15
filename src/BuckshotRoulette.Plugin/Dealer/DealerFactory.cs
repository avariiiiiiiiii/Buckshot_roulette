using System.Collections.Generic;
using System.Text;
using BuckshotRoulette.Core;
using HarmonyLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using UnityEngine;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// Собирает врага-дилера как клон ванильного Animal: общие компоненты врага остаются,
/// специфичное для Animal (ИИ, модель, частицы, события) убирается, ставятся модель, музыка и свой ИИ.
/// </summary>
internal static class DealerFactory
{
    private const string TemplatePath = "Enemies/Enemy - Animal";
    private const string ModelName = "dealer";
    private const float RigidbodyHeight = 0.74f;   // высота центра Rigidbody над землёй у Animal (см. дамп шаблона)

    /// <summary>Капсула головы (пространство модели): в оглушении тело катается этим шаром.</summary>
    private static readonly (Vector3 center, float radius) HeadCollider = (new Vector3(0f, 1.60f, 0.03f), 0.37f);

    public static EnemySetup Setup { get; private set; }

    public static void RegisterAll()
    {
        var template = Resources.Load<GameObject>(TemplatePath);
        if (template == null)
        {
            Plugin.Log.LogError($"Шаблон врага не найден: {TemplatePath}");
            return;
        }

        var holder = new GameObject("BuckshotRoulette Enemy Prefabs");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);

        var root = Object.Instantiate(template, holder.transform);
        root.name = "Enemy - Dealer";
        if (Settings.DiagnosticsEnabled.Value) Plugin.Log.LogInfo(DescribeTemplate(root));

        Strip(root);
        Assemble(root);
        // вызовы методов EnemyAnimal, который выкинут
        Plugin.Log.LogInfo($"Дилер: отключено вызовов удалённых целей: {PrefabUtil.DisableDeadEvents(root)}");

        Setup = ScriptableObject.CreateInstance<EnemySetup>();
        Setup.name = "Enemy - Dealer";
        Setup.status = EnemySetup.Status.Finalized;
        Setup.levelsCompletedCondition = false;
        Setup.spawnObjects = new List<PrefabRef>();

        var content = ScriptableObject.CreateInstance<EnemyContent>();
        AccessTools.Field(typeof(EnemyContent), "_setup").SetValue(content, Setup);
        AccessTools.Field(typeof(EnemyContent), "_spawnObjects").SetValue(content, new List<GameObject> { root });
        Enemies.RegisterEnemy(content);
        Plugin.Log.LogInfo("Враг зарегистрирован: Enemy - Dealer");
    }

    /// <summary>Убирает всё, что принадлежит Animal. DestroyImmediate: клон лежит в неактивном контейнере и ни разу не оживал.</summary>
    private static void Strip(GameObject root)
    {
        Object.DestroyImmediate(root.GetComponentInChildren<EnemyAnimal>(true));
        foreach (var anim in root.GetComponentsInChildren<EnemyAnimalAnim>(true)) Object.DestroyImmediate(anim.gameObject);
        foreach (var name in new[] { "Plane", "Plane (1)", "Particles" })
        {
            var t = root.transform.Find(name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
    }

    private static EnemyDealer Assemble(GameObject root)
    {
        var controller = root.transform.Find("Enable/Controller");
        var mesh = root.transform.Find("Enable/Rigidbody/Mesh");

        // Rigidbody у Animal парит на RigidbodyHeight над контроллером, а его Mesh смещён, повёрнут и уменьшен под зверя:
        // ставим узел так, чтобы земля модели (y=0) совпала с полом, без поворота и масштаба.
        // Контроллер стоит ниже пола на baseOffset агента (у Animal −0.1; в логе земля модели была на 0.10 м под полом).
        float floorBelowController = -root.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true).baseOffset;
        var groundOffset = new Vector3(0f, floorBelowController - RigidbodyHeight, 0f);   // из пространства модели в пространство Rigidbody
        mesh.localPosition = groundOffset;
        mesh.localRotation = Quaternion.identity;
        mesh.localScale = Vector3.one;

        var model = ModelLoader.LoadParts(ModelName);
        var visual = DealerVisual.Create(mesh, model);
        visual.gameObject.AddComponent<DealerMusic>();

        var enemy = controller.GetComponent<Enemy>();
        var head = new GameObject("Kill Look At").transform;
        head.SetParent(visual.transform, false);
        head.localPosition = model.Markers.TryGetValue("eyes", out var eyes) ? eyes : new Vector3(0f, 1.8f, 0.3f);
        enemy.KillLookAtTransform = head;
        controller.GetComponent<EnemyVision>().VisionTransform.localPosition = head.localPosition + groundOffset;
        if (model.Markers.TryGetValue("center", out var center)) enemy.CenterTransform.localPosition = center + groundOffset;

        // летающая голова: без гравитации тело держит высоту само (игра включает ему невесомость), до пола ничего не достаёт
        var rigidbody = root.transform.Find("Enable/Rigidbody");
        var enemyRigidbody = rigidbody.GetComponent<EnemyRigidbody>();
        enemyRigidbody.gravity = false;
        FitColliders(rigidbody, groundOffset);
        var physics = rigidbody.gameObject.AddComponent<DealerPhysics>();
        physics.HeadCenter = HeadCollider.center + groundOffset;
        physics.HeadRadius = HeadCollider.radius;

        var health = controller.GetComponent<EnemyHealth>();
        health.health = 100;
        health.meshParent = mesh;

        root.GetComponent<EnemyParent>().enemyName = "Dealer";
        root.GetComponent<EnemyParent>().enemyNameLocalized = null;

        return controller.gameObject.AddComponent<EnemyDealer>();
    }

    /// <summary>Три капсулы Animal -> голова, руки с дробовиком вдоль ствола и перемычка между ними. Координаты в пространстве Rigidbody.</summary>
    private static void FitColliders(Transform rigidbody, Vector3 groundOffset)
    {
        var capsules = rigidbody.GetComponentsInChildren<CapsuleCollider>(true);
        // (центр в пространстве модели, радиус, высота, ось: 1 — вертикаль, 2 — вперёд)
        var shape = new[]
        {
            (HeadCollider.center, HeadCollider.radius, HeadCollider.radius * 2f, 1),
            (new Vector3(0f, 1.10f, 0.45f), 0.16f, 1.10f, 2),
            (new Vector3(0f, 1.32f, 0.12f), 0.20f, 0.40f, 1),
        };
        for (int i = 0; i < capsules.Length && i < shape.Length; i++)
        {
            var t = capsules[i].transform;
            t.localScale = Vector3.one;
            t.localRotation = Quaternion.identity;
            t.localPosition = shape[i].Item1 + groundOffset;
            capsules[i].center = Vector3.zero;
            capsules[i].direction = shape[i].Item4;
            capsules[i].radius = shape[i].Item2;
            capsules[i].height = shape[i].Item3;
        }
    }

    /// <summary>Разовый дамп размеров шаблона для подгонки коллайдеров под рост дилера.</summary>
    private static string DescribeTemplate(GameObject root)
    {
        var sb = new StringBuilder("\n===== BuckshotRoulette: шаблон Animal =====\n");
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponent<CapsuleCollider>() is CapsuleCollider cap)
                sb.AppendLine($"  Capsule {t.name} local={t.localPosition} scale={t.localScale} center={cap.center} r={cap.radius} h={cap.height} dir={cap.direction} trigger={cap.isTrigger}");
            if (t.GetComponent<BoxCollider>() is BoxCollider box)
                sb.AppendLine($"  Box {t.name} local={t.localPosition} center={box.center} size={box.size} trigger={box.isTrigger}");
        }
        foreach (var n in new[] { "Enable", "Enable/Controller", "Enable/Controller/Follow", "Enable/Rigidbody", "Enable/Rigidbody/Mesh", "Enable/Rigidbody/Vision Transform", "Enable/Rigidbody/Center" })
        {
            var t = root.transform.Find(n);
            if (t != null) sb.AppendLine($"  {n}: local={t.localPosition} euler={t.localEulerAngles} scale={t.localScale}");
        }
        var agent = root.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
        if (agent != null) sb.AppendLine($"  NavMeshAgent: r={agent.radius} h={agent.height} baseOffset={agent.baseOffset} speed={agent.speed} accel={agent.acceleration}");
        return sb.ToString();
    }
}
