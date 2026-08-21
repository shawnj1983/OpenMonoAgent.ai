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

    [Fact]
    public void ReadInput_NonInteractiveStdin_ReturnsPipedLine()
    {
        // Under `dotnet test` stdin is redirected, so ReadInput takes the
        // non-interactive path and reads a raw line instead of using Spectre's
        // TextPrompt (which throws in non-interactive mode).
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader("what is 2+2?\n"));
            var renderer = new TerminalRenderer();

            renderer.ReadInput().Should().Be("what is 2+2?");
        }
        finally
        {
            Console.SetIn(original);
        }
    }

    [Fact]
    public void ReadInput_NonInteractiveStdin_ThrowsAtEndOfInput()
    {
        // EOF on piped stdin must surface as OperationCanceledException so the
        // agent loop exits cleanly instead of crashing.
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader(string.Empty));
            var renderer = new TerminalRenderer();

            var act = () => renderer.ReadInput();

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            Console.SetIn(original);
        }
    }
}
