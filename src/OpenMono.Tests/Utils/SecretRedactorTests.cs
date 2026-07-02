using FluentAssertions;
using OpenMono.Utils;

namespace OpenMono.Tests.Utils;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactUserText_Redacts_OpenAiStyleKeys()
    {
        var input = "my key is sk-proj-abcdefghijklmnopqrstuvwxyzABCDE_0123456789";
        var output = SecretRedactor.RedactUserText(input, out var changed);

        changed.Should().BeTrue();
        output.Should().NotContain("sk-proj-");
        output.Should().Contain("[REDACTED_SECRET]");
    }

    [Fact]
    public void RedactUserText_DoesNotRedact_ShortTokens()
    {
        var input = "sk-12345 is not a real key";
        var output = SecretRedactor.RedactUserText(input, out var changed);

        changed.Should().BeFalse();
        output.Should().Be(input);
    }

    [Fact]
    public void RedactUserText_Redacts_BearerTokens()
    {
        var input = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789";
        var output = SecretRedactor.RedactUserText(input, out var changed);

        changed.Should().BeTrue();
        output.Should().Contain("Bearer [REDACTED_SECRET]");
        output.Should().NotContain("abcdefghijklmnopqrstuvwxyz");
    }
}

