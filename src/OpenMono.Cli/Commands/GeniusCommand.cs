using OpenMono.Session;

namespace OpenMono.Commands;

public sealed class GeniusCommand : ICommand
{
    public string Name => "genius";
    public string Description => "Toggle genius mode — deep autopsy analysis, thick 10x thinking, kill the critic (bold decisive full-context output).";
    public CommandType Type => CommandType.Local;

    public Task ExecuteAsync(string[] args, CommandContext context, CancellationToken ct)
    {
        context.Session.Meta.GeniusEnabled = !context.Session.Meta.GeniusEnabled;

        if (context.Session.Meta.GeniusEnabled)
        {
            context.Session.AddMessage(new Message
            {
                Role = MessageRole.User,
                Content = GeniusModeInstructions.Activation("activated by user via /genius"),
            });
            context.Renderer.WriteInfo("Genius mode ON — deep autopsy, thick 10x thinking, kill the critic.");
            context.Renderer.WriteInfo("Use /genius again to disable. Expect bold, comprehensive analysis.");
        }
        else
        {
            context.Session.AddMessage(new Message
            {
                Role = MessageRole.User,
                Content = GeniusModeInstructions.Deactivation,
            });
            context.Renderer.WriteInfo("Genius mode OFF — normal operation resumed.");
        }

        return Task.CompletedTask;
    }
}