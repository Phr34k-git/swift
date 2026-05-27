using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class RodClassifierTests
{
    [Theory]
    [InlineData("Pinion's Aria", RodKind.Pinion)]
    [InlineData("Bellona's Waraxe", RodKind.BellonaWaraxe)]
    [InlineData("Tranquility Rod", RodKind.Tranquility)]
    [InlineData("Dreambreaker", RodKind.Dreambreaker)]
    [InlineData("Requiem Rod", RodKind.Requiem)]
    [InlineData("Carbon Rod", RodKind.Default)]
    [InlineData("", RodKind.Default)]
    public void Classify_MatchesExpectedKind(string text, RodKind expected)
    {
        Assert.Equal(expected, RodClassifier.Classify(text));
    }

    [Fact]
    public void Classify_IsCaseInsensitive()
    {
        Assert.Equal(RodKind.Pinion, RodClassifier.Classify("PINION'S ARIA"));
    }

    [Fact]
    public void Classify_StripsRichTextTags()
    {
        Assert.Equal(RodKind.Requiem, RodClassifier.Classify("<b>Requiem</b> Rod"));
    }
}
