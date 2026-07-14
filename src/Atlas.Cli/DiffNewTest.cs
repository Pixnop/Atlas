namespace Atlas.Cli;

/// <summary>One new test found by `atlas diff`: absent from the baseline and not failing in the
/// candidate (a new test that fails is reported as a <see cref="DiffFailure"/> instead).</summary>
/// <param name="TestName">The new test.</param>
/// <param name="CandidateKind">Its outcome in the candidate (Passed or Skipped).</param>
internal sealed record DiffNewTest(string TestName, TestOutcomeKind CandidateKind);
