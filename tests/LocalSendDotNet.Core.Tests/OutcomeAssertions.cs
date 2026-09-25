using Xunit.Sdk;

namespace LocalSendDotNet.Core.Tests;

internal static class OutcomeAssertions
{
    public static SendOutcome.Completed RequireCompleted(this SendOutcome outcome) => outcome switch
    {
        SendOutcome.Completed completed => completed,
        _ => throw new XunitException($"Expected completed send outcome, got {outcome.GetType().Name}."),
    };

    public static SendOutcome.Cancelled RequireCancelled(this SendOutcome outcome) => outcome switch
    {
        SendOutcome.Cancelled cancelled => cancelled,
        _ => throw new XunitException($"Expected cancelled send outcome, got {outcome.GetType().Name}."),
    };

    public static SendOutcome.PinRequired RequirePinRequired(this SendOutcome outcome) => outcome switch
    {
        SendOutcome.PinRequired pinRequired => pinRequired,
        _ => throw new XunitException($"Expected PIN-required send outcome, got {outcome.GetType().Name}."),
    };

    public static ReceiveOutcome.Completed RequireCompleted(this ReceiveOutcome outcome) => outcome switch
    {
        ReceiveOutcome.Completed completed => completed,
        _ => throw new XunitException($"Expected completed receive outcome, got {outcome.GetType().Name}."),
    };

    public static ReceiveOutcome.Failed RequireFailed(this ReceiveOutcome outcome) => outcome switch
    {
        ReceiveOutcome.Failed failed => failed,
        _ => throw new XunitException($"Expected failed receive outcome, got {outcome.GetType().Name}."),
    };
}
