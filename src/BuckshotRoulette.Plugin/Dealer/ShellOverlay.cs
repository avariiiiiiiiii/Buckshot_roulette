using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuckshotRoulette.Core;
using BuckshotRoulette.Logic;
using BuckshotRoulette.Shells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// Экранная анимация зарядки дробовика дилера. Показывается всем игрокам, когда дилер появляется
/// и когда он перезаряжается: патроны по одному вставляются в ряд (красные — боевые, синие — холостые).
/// Порядок патронов в магазине игрокам не раскрывается, поэтому сначала идут все боевые, затем холостые.
/// </summary>
internal sealed class ShellOverlay : MonoBehaviour
{
    private const float InsertInterval = 0.30f;
    private const float HoldSeconds = 1.8f;
    private const float FadeSeconds = 0.45f;

    private static ShellOverlay _instance;
    private static Sprite _liveSprite;
    private static Sprite _blankSprite;

    private CanvasGroup _group;
    private RectTransform _row;
    private TextMeshProUGUI _label;
    private Coroutine _running;

    public static void Show(ShellMagazine magazine)
    {
        if (magazine == null || magazine.IsEmpty) return;
        if (_instance == null) _instance = Create();
        _instance.Begin(magazine);
    }

    private static ShellOverlay Create()
    {
        var root = new GameObject("BuckshotRoulette Shell Overlay");
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var overlay = root.AddComponent<ShellOverlay>();
        overlay._group = root.AddComponent<CanvasGroup>();
        overlay._group.alpha = 0f;
        overlay._group.blocksRaycasts = false;
        overlay._group.interactable = false;

        // контейнер без подложки: патроны и надпись прямо поверх игры
        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 150f);
        panelRect.sizeDelta = new Vector2(900f, 230f);

        var row = new GameObject("Shells", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        overlay._row = (RectTransform)row.transform;
        overlay._row.anchorMin = overlay._row.anchorMax = new Vector2(0.5f, 0.5f);
        overlay._row.anchoredPosition = new Vector2(0f, 18f);
        overlay._row.sizeDelta = new Vector2(10f, 10f);

        var label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(panel.transform, false);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 12f);
        labelRect.sizeDelta = new Vector2(860f, 44f);
        overlay._label = label.AddComponent<TextMeshProUGUI>();
        overlay._label.alignment = TextAlignmentOptions.Center;
        overlay._label.fontSize = 30f;
        overlay._label.color = Color.white;
        overlay._label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) overlay._label.font = TMP_Settings.defaultFontAsset;

        _liveSprite = MakeShellSprite(new Color32(0xC8, 0x3A, 0x22, 0xFF));
        _blankSprite = MakeShellSprite(new Color32(0x2C, 0x6F, 0xD6, 0xFF));
        return overlay;
    }

    private void Begin(ShellMagazine magazine)
    {
        if (_running != null) StopCoroutine(_running);
        foreach (Transform child in _row) Destroy(child.gameObject);
        _running = StartCoroutine(Play(magazine.Live, magazine.Blank));
    }

    private IEnumerator Play(int live, int blank)
    {
        _label.text = "";
        _group.alpha = 0f;
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
        {
            _group.alpha = t / 0.25f;
            yield return null;
        }
        _group.alpha = 1f;

        var order = Enumerable.Repeat(true, live).Concat(Enumerable.Repeat(false, blank)).ToList();
        const float spacing = 82f;
        float start = -(order.Count - 1) * spacing / 2f;
        for (int i = 0; i < order.Count; i++)
        {
            var image = NewShell(order[i], new Vector2(start + i * spacing, 0f));
            GunFx.Click(ListenerPosition());
            yield return DropIn(image.rectTransform);
            yield return new WaitForSecondsRealtime(InsertInterval);
        }

        _label.text = Texts.T($"Боевых: {live}   Холостых: {blank}", $"Live: {live}   Blank: {blank}");
        yield return new WaitForSecondsRealtime(HoldSeconds);
        for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
        {
            _group.alpha = 1f - t / FadeSeconds;
            yield return null;
        }
        _group.alpha = 0f;
        _running = null;
    }

    private Image NewShell(bool isLive, Vector2 position)
    {
        var go = new GameObject(isLive ? "Live" : "Blank", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_row, false);
        var image = go.GetComponent<Image>();
        image.sprite = isLive ? _liveSprite : _blankSprite;
        image.raycastTarget = false;
        image.rectTransform.sizeDelta = new Vector2(52f, 140f);
        image.rectTransform.anchoredPosition = position + new Vector2(0f, 90f);
        image.color = new Color(1f, 1f, 1f, 0f);
        image.rectTransform.localScale = Vector3.one * 1.35f;
        return image;
    }

    /// <summary>Патрон падает сверху в своё гнездо, чуть «подпрыгивая» по масштабу.</summary>
    private static IEnumerator DropIn(RectTransform rect)
    {
        var image = rect.GetComponent<Image>();
        Vector2 end = rect.anchoredPosition - new Vector2(0f, 90f);
        Vector2 from = rect.anchoredPosition;
        const float duration = 0.22f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / duration, 3f);
            rect.anchoredPosition = Vector2.Lerp(from, end, k);
            rect.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, k);
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / (duration * 0.6f)));
            yield return null;
        }
        rect.anchoredPosition = end;
        rect.localScale = Vector3.one;
        image.color = Color.white;
    }

    private static Vector3 ListenerPosition()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    /// <summary>Гильза: скруглённая верхушка, латунное донце, тёмная проточка между ними.</summary>
    private static Sprite MakeShellSprite(Color32 body)
    {
        const int w = 52, h = 140, brassHeight = 36;
        var brass = new Color32(0xC8, 0xA2, 0x4A, 0xFF);
        var brassDark = new Color32(0x8A, 0x6E, 0x2C, 0xFF);
        var groove = new Color32(0x16, 0x10, 0x10, 0xFF);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[w * h];
        float radius = w / 2f - 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x - (w - 1) / 2f;
                float half = radius;
                if (y > h - radius) half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - (y - (h - radius)) * (y - (h - radius))));
                bool inside = Mathf.Abs(dx) <= half;
                Color32 c = new Color32(0, 0, 0, 0);
                if (inside)
                {
                    if (y < brassHeight - 4) c = Mathf.Abs(dx) > radius - 4f ? brassDark : brass;
                    else if (y < brassHeight) c = groove;
                    else c = Mathf.Abs(dx) > radius - 3f ? Color32.Lerp(body, new Color32(0, 0, 0, 255), 0.45f) : body;
                }
                pixels[y * w + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
