namespace Atlas.Cli;

/// <summary>One notable duration shift found by `atlas diff`: a test that passed in both runs
/// but took markedly more or less time in the candidate (see <see cref="DurationShiftRule"/> for
/// what counts as notable).</summary>
/// <param name="TestName">The shifted test.</param>
/// <param name="BaselineMs">Its baseline duration in milliseconds.</param>
/// <param name="CandidateMs">Its candidate duration in milliseconds.</param>
/// <param name="Slower">True when the candidate is the slower run.</param>
internal sealed record DurationShift(string TestName, long BaselineMs, long CandidateMs, bool Slower);
