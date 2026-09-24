using Vintagestory.API.Common;

namespace PlayerCommandFixtureMod;

/// <summary>An optional one-word argument parser that always reports
/// <see cref="EnumParseResult.Deferred"/> and resolves the word from a later, genuinely
/// asynchronous continuation, the same shape a real async-parsed argument (a player-name lookup,
/// for one) takes. Exercises the exact pitfall <c>Atlas.Internal.Hosting.ConsoleCommands</c>
/// documents: a caller that only reads the first (<c>Deferred</c>) callback and ignores the
/// later, real one would see a command using this argument hang forever.</summary>
internal sealed class DeferredEchoArgParser : ArgumentParserBase
{
    private string? _word;

    public DeferredEchoArgParser(string argName)
        : base(argName, isMandatoryArg: false)
    {
    }

    public override object GetValue() => IsMissing ? null! : _word!;

    public override void SetValue(object data) => _word = (string)data;

    public override void PreProcess(TextCommandCallingArgs args)
    {
        base.PreProcess(args);
        _word = null;
    }

    public override EnumParseResult TryProcess(TextCommandCallingArgs args, Action<AsyncParseResults>? onReady = null)
    {
        string? word = args.RawArgs.PopWord();
        if (word == null)
        {
            lastErrorMessage = "Argument is missing";
            return EnumParseResult.Bad;
        }

        // Deliberately not a same-frame callback: proves the caller actually waits for a later
        // continuation instead of happening to work because onReady fired before TryProcess
        // returned.
        _ = Task.Delay(TimeSpan.FromMilliseconds(1)).ContinueWith(
            _ => onReady!(new AsyncParseResults { Status = EnumParseResultStatus.Ready, Data = word }),
            TaskScheduler.Default);

        return EnumParseResult.Deferred;
    }
}
