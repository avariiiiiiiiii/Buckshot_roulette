using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuckshotRoulette.Diagnostics;

/// <summary>Разбор «ценность есть, меша не видно»: сравнение материала и рендерера клона с ванильным шаблоном.</summary>
internal static class ValuableInspect
{
    public static IEnumerator Run(GameObject spawned, string vanillaTemplatePath)
    {
        // даём игре отрисовать пару кадров, иначе isVisible всегда false
        yield return new WaitForSeconds(3f);

        var sb = new StringBuilder("\n===== BuckshotRoulette: осмотр ценности =====\n");

        var template = Resources.Load<GameObject>(vanillaTemplatePath);
        if (template != null)
        {
            sb.AppendLine($"-- ШАБЛОН {template.name}");
            foreach (var r in template.GetComponentsInChildren<Renderer>(true)) Describe(r, sb);
        }

        if (spawned != null)
        {
            sb.AppendLine($"-- КЛОН {spawned.name} activeInHierarchy={spawned.activeInHierarchy} pos={spawned.transform.position} scale={spawned.transform.lossyScale}");
            foreach (var r in spawned.GetComponentsInChildren<Renderer>(true)) Describe(r, sb);
        }
        else
        {
            sb.AppendLine("-- КЛОН уничтожен или не создан");
        }

        Plugin.Log.LogInfo(sb.ToString());
    }

    private static void Describe(Renderer r, StringBuilder sb)
    {
        var filter = r.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        sb.AppendLine($"  [{r.GetType().Name}] {Path(r.transform)} enabled={r.enabled} visible={r.isVisible} layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                      $"shadows={r.shadowCastingMode} boundsSize={r.bounds.size} " +
                      $"mesh={(mesh == null ? "null" : $"{mesh.name} v={mesh.vertexCount} tris={mesh.triangles.Length / 3} uv={mesh.uv.Length > 0} col={mesh.colors.Length > 0} readable={mesh.isReadable}")}");

        foreach (var m in r.sharedMaterials)
        {
            if (m == null) { sb.AppendLine("    материал: null"); continue; }
            var shader = m.shader;
            sb.AppendLine($"    материал '{m.name}' shader='{shader.name}' queue={m.renderQueue} keywords=[{string.Join(",", m.shaderKeywords)}]");
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string pn = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: sb.AppendLine($"      {pn} (color) = {m.GetColor(pn)}"); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: sb.AppendLine($"      {pn} (float) = {m.GetFloat(pn)}"); break;
                    case ShaderPropertyType.Texture:
                        var tex = m.GetTexture(pn);
                        sb.AppendLine($"      {pn} (tex) = {(tex == null ? "null" : tex.name + " " + tex.width + "x" + tex.height)}");
                        break;
                }
            }
        }
    }

    private static string Path(Transform t)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var c = t; c != null; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts.Skip(Mathf.Max(0, parts.Count - 3)));
    }
}
