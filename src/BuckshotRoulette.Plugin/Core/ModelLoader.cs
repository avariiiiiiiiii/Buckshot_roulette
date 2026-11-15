using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BuckshotRoulette.Core;

/// <summary>
/// Загружает модели из Blender: JSON с вершинами, нормалями и подмешами по материалам.
/// Координаты уже переведены в систему Unity при экспорте. Модель — один меш или набор частей
/// с точками вращения (pivot) и метками (дульный срез, глаза). Необязательно: UV и PNG-текстура
/// подмеша (лежит рядом с JSON, фильтрация point — пиксельный вид).
/// </summary>
internal static class ModelLoader
{
    private sealed class ModelData
    {
        public float[] positions;
        public float[] normals;
        public float[] uvs;
        public List<SubmeshData> submeshes;
        public List<PartData> parts;
        public Dictionary<string, float[]> markers;
    }

    private sealed class PartData
    {
        public string name;
        public float[] pivot;
        public float[] positions;
        public float[] normals;
        public float[] uvs;
        public List<SubmeshData> submeshes;
    }

    private sealed class SubmeshData
    {
        public string material;
        public float[] color;
        public float metallic;
        public float smoothness;
        public float[] emission;
        public string texture;
        public int[] triangles;
    }

    public sealed class Model
    {
        public Mesh Mesh;
        public Material[] Materials;
    }

    public sealed class Part
    {
        public string Name;
        public Vector3 Pivot;
        public Model Model;
    }

    public sealed class MultiPartModel
    {
        public List<Part> Parts = new List<Part>();
        public Dictionary<string, Vector3> Markers = new Dictionary<string, Vector3>();
    }

    private const string BaseMaterialSource = "Valuables/01 Tiny/Valuable Arctic Keycard";
    private static Material _baseMaterial;
    private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

    private static string ModelsDir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Models");

    /// <summary>Обычный Standard-материал игры: его копии получают цвета подмешей.</summary>
    public static Material BaseMaterial
    {
        get
        {
            if (_baseMaterial == null)
                _baseMaterial = Resources.Load<GameObject>(BaseMaterialSource).GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            return _baseMaterial;
        }
    }

    private static ModelData Read(string name) =>
        JsonConvert.DeserializeObject<ModelData>(File.ReadAllText(Path.Combine(ModelsDir, name + ".json")));

    public static Model Load(string name)
    {
        var data = Read(name);
        return Build(name, data.positions, data.normals, data.uvs, data.submeshes);
    }

    public static Model Scaled(Model source, float scale)
    {
        var mesh = Object.Instantiate(source.Mesh);
        mesh.name = source.Mesh.name + " Item Scale";
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] *= scale;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return new Model { Mesh = mesh, Materials = source.Materials };
    }

    public static MultiPartModel LoadParts(string name)
    {
        var data = Read(name);
        var result = new MultiPartModel();
        foreach (var p in data.parts)
        {
            result.Parts.Add(new Part
            {
                Name = p.name,
                Pivot = new Vector3(p.pivot[0], p.pivot[1], p.pivot[2]),
                Model = Build(name + " " + p.name, p.positions, p.normals, p.uvs, p.submeshes),
            });
        }
        if (data.markers != null)
        {
            foreach (var m in data.markers) result.Markers[m.Key] = new Vector3(m.Value[0], m.Value[1], m.Value[2]);
        }
        return result;
    }

    private static Model Build(string name, float[] positions, float[] normalValues, float[] uvValues, List<SubmeshData> submeshes)
    {
        var vertices = new Vector3[positions.Length / 3];
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            normals[i] = new Vector3(normalValues[i * 3], normalValues[i * 3 + 1], normalValues[i * 3 + 2]);
        }

        var mesh = new Mesh { name = name, subMeshCount = submeshes.Count };
        if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        if (uvValues != null)
        {
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uvValues[i * 2], uvValues[i * 2 + 1]);
            mesh.uv = uv;
        }

        var materials = new Material[submeshes.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            mesh.SetTriangles(submeshes[i].triangles, i);
            materials[i] = MakeMaterial(name + " " + submeshes[i].material, submeshes[i]);
        }
        mesh.RecalculateBounds();
        return new Model { Mesh = mesh, Materials = materials };
    }

    private static Material MakeMaterial(string name, SubmeshData sub)
    {
        // копия: sharedMaterial у шаблона общий с ванильным предметом
        var m = new Material(BaseMaterial) { name = name };
        m.mainTexture = sub.texture != null ? LoadTexture(sub.texture) : null;
        m.SetTexture("_BumpMap", null);
        m.SetTexture("_EmissionMap", null);
        m.DisableKeyword("_NORMALMAP");
        m.SetColor("_Color", new Color(sub.color[0], sub.color[1], sub.color[2], 1f));
        m.SetFloat("_Metallic", sub.metallic);
        m.SetFloat("_Glossiness", sub.smoothness);
        if (sub.emission != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(sub.emission[0], sub.emission[1], sub.emission[2], 1f));
        }
        else
        {
            m.SetColor("_EmissionColor", Color.black);
        }
        return m;
    }

    /// <summary>PNG из папки моделей, без мипмапов и с point-фильтром: пиксели текстуры видны как есть.</summary>
    private static Texture2D LoadTexture(string file)
    {
        if (Textures.TryGetValue(file, out var cached)) return cached;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = file };
        tex.LoadImage(File.ReadAllBytes(Path.Combine(ModelsDir, file)));
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        Textures[file] = tex;
        return tex;
    }
}
