using FluentAssertions;
using Spectre.Console;
using OpenMono.Rendering;

namespace OpenMono.Tests.Rendering;

public sealed class TerminalRendererTests
{
    private static (TerminalRenderer Renderer, StringWriter Output) CreateRenderer()
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(writer),
        });
        return (new TerminalRenderer(console), writer);
    }

    [Fact]
    public void WriteWelcome_DoesNotThrow_AndRendersProductName()
    {
        var (renderer, output) = CreateRenderer();

        // Regression: the welcome panel previously crashed with
        // "Color number must be between 0 and 255" because a 24-bit RGB value
        // (0xA3FF66) was passed to Color.FromInt32, whose valid range is 0-255.
        var act = () => renderer.WriteWelcome("mock-model", "http://localhost:7474");

        act.Should().NotThrow("the welcome panel must render without invalid color errors");
        output.ToString().Should().Contain("OpenMono.ai");
    }
}
