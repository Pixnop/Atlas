namespace Atlas.Cli;

/// <summary>One vanished test found by `atlas diff`: present in the baseline, absent from the
/// candidate. Always a regression, whatever its baseline outcome was: a test CI can no longer
/// see cannot protect anything.</summary>
/// <param name="TestName">The vanished test.</param>
/// <param name="BaselineKind">Its outcome in the baseline.</param>
internal sealed record DiffVanishedTest(string TestName, TestOutcomeKind BaselineKind);
