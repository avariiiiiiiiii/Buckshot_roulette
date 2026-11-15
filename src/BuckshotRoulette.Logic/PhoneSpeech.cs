using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BuckshotRoulette.Logic;

/// <summary>Keep readable monster names, adding stress the Russian game TTS cannot infer for unknown words.</summary>
public static class PhoneSpeech
{
    private static readonly Regex Markup = new Regex("<[^>]*>");
    private static readonly Regex Words = new Regex(@"[\p{L}\p{M}]+");
    private static readonly Dictionary<string, string> StressedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["дилер"] = "Ди́лер",
        ["охотник"] = "Охо́тник",
        ["тяжеловес"] = "Тяжелове́с",
        ["рыцарь"] = "Ры́царь",
        ["мантия"] = "Ма́нтия",
        ["малыш"] = "Малы́ш",
        ["теневой"] = "Тенево́й",
        ["ребенок"] = "Ребёнок",
        ["голова"] = "Голова́",
        ["мясо"] = "Мя́со",
        ["клоун"] = "Кло́ун",
        ["невидимка"] = "Невиди́мка",
        ["менталист"] = "Ментали́ст",
        ["паразит"] = "Парази́т",
        ["попрыгун"] = "Попрыгу́н",
        ["плевун"] = "Плеву́н",
    };

    public static string Prepare(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string clean = Markup.Replace(name, string.Empty).Trim();
        return Words.Replace(clean, match => StressedNames.TryGetValue(match.Value, out var stressed) ? stressed : match.Value);
    }

    public static string ForName(string name)
    {
        string spoken = Prepare(name);
        if (spoken.Length == 0) return string.Empty;
        bool cyrillic = false;
        foreach (char c in spoken)
            if (c >= 'Ѐ' && c <= 'ӿ') { cyrillic = true; break; }
        return (cyrillic ? "Ря́дом. " : "Nearby. ") + spoken + ".";
    }
}
