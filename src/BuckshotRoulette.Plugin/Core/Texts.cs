namespace BuckshotRoulette.Core;

internal static class Texts
{
    /// <summary>Русский текст для русской локали игры, иначе английский.</summary>
    public static string T(string ru, string en) => SemiFunc.LocaleGetCode().StartsWith("ru") ? ru : en;
}
