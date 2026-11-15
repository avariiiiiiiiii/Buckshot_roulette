using UnityEngine;

namespace BuckshotRoulette.Core;

/// <summary>Текстуры и материалы эффектов, генерируемые кодом (ассетов у мода нет).</summary>
internal static class FxTextures
{
    // шейдеры с альфа-смешиванием и цветом вершин; первый найденный в сборке игры
    private static readonly string[] AlphaShaders =
    {
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended",
        "Unlit/Transparent",
    };

    private static Texture2D _smoke;
    private static Shader _alphaShader;
    private static bool _shaderSearched;

    /// <summary>Мягкое пятно с рваным краем: белое, цвет и прозрачность задают частицы.</summary>
    public static Texture2D Smoke()
    {
        if (_smoke != null) return _smoke;
        const int size = 64;
        _smoke = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BuckshotRoulette Smoke" };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a) * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        _smoke.SetPixels(pixels);
        _smoke.Apply();
        return _smoke;
    }

    /// <summary>Материал с альфа-смешиванием (цвет — от частиц или линий); null, если в сборке нет подходящего шейдера.</summary>
    public static Material AlphaMaterial(string name, Texture texture)
    {
        if (!_shaderSearched)
        {
            _shaderSearched = true;
            foreach (var shaderName in AlphaShaders)
            {
                _alphaShader = Shader.Find(shaderName);
                if (_alphaShader == null) continue;
                Plugin.Log.LogInfo($"Эффекты: шейдер {shaderName}");
                break;
            }
            if (_alphaShader == null) Plugin.Log.LogError("Эффекты: в сборке игры нет шейдера с альфа-смешиванием, частицы выключены");
        }
        return _alphaShader == null ? null : new Material(_alphaShader) { name = name, mainTexture = texture, renderQueue = 3000 };
    }
}
