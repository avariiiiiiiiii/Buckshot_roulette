using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public sealed class PhoneSpeechTests
{
    [Theory]
    [InlineData("Дилер", "Ди́лер")]
    [InlineData("Тяжеловес", "Тяжелове́с")]
    [InlineData("Охотник", "Охо́тник")]
    [InlineData("Теневой ребёнок", "Тенево́й ребёнок")]
    public void Russian_names_have_explicit_word_stress(string name, string expected)
        => Assert.Equal(expected, PhoneSpeech.Prepare(name));

    [Fact]
    public void Pronunciation_does_not_replace_custom_names_or_existing_stress()
    {
        Assert.Equal("Мо́нстр Дилерон", PhoneSpeech.Prepare("Мо́нстр Дилерон"));
        Assert.Equal("Тяжелове́с", PhoneSpeech.Prepare(PhoneSpeech.Prepare("Тяжеловес")));
    }

    [Fact]
    public void Markup_is_not_spoken_and_english_fallback_keeps_english_lead_in()
    {
        Assert.Equal("Nearby. Trudge.", PhoneSpeech.ForName("<color=red>Trudge</color>"));
        Assert.Equal("Ря́дом. Ди́лер.", PhoneSpeech.ForName("<b>Дилер</b>"));
    }
}
