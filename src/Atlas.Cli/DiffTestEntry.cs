namespace Atlas.Cli;

/// <summary>One merged test identity paired across both runs: the opt-in per-test listing behind
/// `--json-tests` (see docs/specs/2026-07-14-diff-command.md). Produced by
/// <see cref="TrxDiff.MergeTests"/>, which merges duplicate names on each side the same
/// worst-outcome-first way <see cref="TrxDiff.Compute"/> does before pairing the two sides.</summary>
/// <param name="TestName">The merged test identity.</param>
/// <param name="Baseline">The baseline's merged result, or null when the test is absent from the
/// baseline.</param>
/// <param name="Candidate">The candidate's merged result, or null when the test is absent from
/// the candidate.</param>
internal sealed record DiffTestEntry(string TestName, TrxTestResult? Baseline, TrxTestResult? Candidate);
