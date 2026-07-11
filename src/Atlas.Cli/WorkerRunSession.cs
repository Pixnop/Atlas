namespace Atlas.Cli;

/// <summary>Pure state machine of a worker run: turns runner callbacks into ordered protocol
/// events (inserting class-start/class-end lines on class transitions, since the run is strictly
/// sequential) and computes the totals and exit code. The shell (<see cref="WorkerRunner"/>)
/// feeds it and writes whatever it returns, so every protocol decision is unit-testable without
/// a runner. Thread-safe: xunit callbacks arrive from worker threads.</summary>
internal sealed class WorkerRunSession(string assemblyPath, IReadOnlyList<string>? classes, int pid, string atlasVersion)
{
    private readonly object _lock = new();

    private string? _currentClass;
    private int _classPassed;
    private int _classFailed;
    private int _classSkipped;
    private int _passed;
    private int _failed;
    private int _skipped;
    private int _errors;
    private bool _completed;

    /// <summary>Gets the process exit code for the run: 0 only when every scenario passed and at
    /// least one ran (an empty run is a failure, same rule as <see cref="RunReport"/>).</summary>
    public int ExitCode => _failed > 0 || _errors > 0 || Total == 0 ? 1 : 0;

    private int Total => _passed + _failed + _skipped;

    /// <summary>Builds the run-start event opening the stream.</summary>
    /// <returns>The run-start event.</returns>
    public WorkerEvent Start() => new RunStartEvent
    {
        Assembly = assemblyPath,
        Classes = classes,
        Pid = pid,
        AtlasVersion = atlasVersion,
    };

    /// <summary>Records a passed scenario.</summary>
    /// <param name="className">Fully qualified name of the scenario class.</param>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="seconds">Execution time in seconds (xunit's unit).</param>
    /// <returns>The events to emit, class transitions included.</returns>
    public IReadOnlyList<WorkerEvent> RecordPass(string className, string displayName, decimal seconds)
    {
        lock (_lock)
        {
            List<WorkerEvent> events = TransitionTo(className);
            _passed++;
            _classPassed++;
            events.Add(new TestPassEvent { Class = className, Test = displayName, DurationMs = ToMilliseconds(seconds) });
            return events;
        }
    }

    /// <summary>Records a failed scenario.</summary>
    /// <param name="className">Fully qualified name of the scenario class.</param>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="seconds">Execution time in seconds (xunit's unit).</param>
    /// <param name="exceptionType">The failing exception's type name.</param>
    /// <param name="exceptionMessage">The failing exception's message.</param>
    /// <param name="stackTrace">The failing exception's stack trace, if any.</param>
    /// <returns>The events to emit, class transitions included.</returns>
    public IReadOnlyList<WorkerEvent> RecordFail(
        string className, string displayName, decimal seconds, string exceptionType, string exceptionMessage, string? stackTrace)
    {
        lock (_lock)
        {
            List<WorkerEvent> events = TransitionTo(className);
            _failed++;
            _classFailed++;
            events.Add(new TestFailEvent
            {
                Class = className,
                Test = displayName,
                DurationMs = ToMilliseconds(seconds),
                Message = $"{exceptionType}: {exceptionMessage}",
                Stack = string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace,
            });
            return events;
        }
    }

    /// <summary>Records a skipped scenario.</summary>
    /// <param name="className">Fully qualified name of the scenario class.</param>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="reason">The skip reason.</param>
    /// <returns>The events to emit, class transitions included.</returns>
    public IReadOnlyList<WorkerEvent> RecordSkip(string className, string displayName, string reason)
    {
        lock (_lock)
        {
            List<WorkerEvent> events = TransitionTo(className);
            _skipped++;
            _classSkipped++;
            events.Add(new TestSkipEvent { Class = className, Test = displayName, Reason = reason });
            return events;
        }
    }

    /// <summary>Records the isolation summary of a class whose host was handed off. No class
    /// transition and no counting: the summary rides between the class's last test event and
    /// its class-end line (the hand-off fires while the NEXT class's first scenario boots, or
    /// when the worker shuts the final host down before closing the stream, both moments where
    /// the summarized class is still the open one).</summary>
    /// <param name="className">Fully qualified name of the summarized scenario class.</param>
    /// <param name="summary">The formatted isolation summary line.</param>
    /// <returns>The events to emit; empty when the stream is already closed (a summary that
    /// only surfaces at process exit has missed the protocol; stderr still carries it).</returns>
    public IReadOnlyList<WorkerEvent> RecordClassSummary(string className, string summary)
    {
        lock (_lock)
        {
            if (_completed)
            {
                return [];
            }

            return [new ClassSummaryEvent { Class = className, Summary = summary }];
        }
    }

    /// <summary>Records a runner-level error (a failure outside any single scenario). Errors
    /// force a non-zero exit code.</summary>
    /// <param name="exceptionType">The exception's type name.</param>
    /// <param name="exceptionMessage">The exception's message.</param>
    /// <returns>The events to emit.</returns>
    public IReadOnlyList<WorkerEvent> RecordError(string exceptionType, string exceptionMessage)
    {
        lock (_lock)
        {
            _errors++;
            return [new ErrorEvent { Message = $"{exceptionType}: {exceptionMessage}" }];
        }
    }

    /// <summary>Records a worker-level crash (an unhandled exception escaping the runner): the
    /// in-flight class, if any, gets a synthetic test-fail so the orchestrator never sees a
    /// silently shorter test list. Follow with <see cref="Complete"/>.</summary>
    /// <param name="message">A description of the crash.</param>
    /// <returns>The events to emit.</returns>
    public IReadOnlyList<WorkerEvent> RecordCrash(string message)
    {
        lock (_lock)
        {
            if (_completed || _currentClass is null)
            {
                _errors++;
                return [new ErrorEvent { Message = message }];
            }

            _failed++;
            _classFailed++;
            return
            [
                new TestFailEvent
                {
                    Class = _currentClass,
                    Test = $"{_currentClass} (worker crashed mid-class)",
                    DurationMs = 0,
                    Message = message,
                },
            ];
        }
    }

    /// <summary>Closes the stream: emits the pending class-end, if a class is open, and the final
    /// run-end. Idempotent, so the shell's fail-safe finally cannot double-close it.</summary>
    /// <param name="wallClockMs">Wall-clock duration of the whole run in milliseconds.</param>
    /// <param name="exitCode">Overrides the computed <see cref="ExitCode"/> (the environment
    /// error path exits 2); null uses the computed one.</param>
    /// <returns>The events to emit; empty when the stream is already closed.</returns>
    public IReadOnlyList<WorkerEvent> Complete(long wallClockMs, int? exitCode = null)
    {
        lock (_lock)
        {
            if (_completed)
            {
                return [];
            }

            _completed = true;
            List<WorkerEvent> events = [];
            CloseClassInto(events);
            events.Add(new RunEndEvent
            {
                Total = Total,
                Passed = _passed,
                Failed = _failed,
                Skipped = _skipped,
                Errors = _errors,
                WallClockMs = wallClockMs,
                ExitCode = exitCode ?? ExitCode,
            });
            return events;
        }
    }

    private static long ToMilliseconds(decimal seconds) => (long)Math.Round(seconds * 1000m);

    private List<WorkerEvent> TransitionTo(string className)
    {
        List<WorkerEvent> events = [];
        if (_currentClass == className)
        {
            return events;
        }

        CloseClassInto(events);
        _currentClass = className;
        events.Add(new ClassStartEvent { Class = className });
        return events;
    }

    private void CloseClassInto(List<WorkerEvent> events)
    {
        if (_currentClass is null)
        {
            return;
        }

        events.Add(new ClassEndEvent
        {
            Class = _currentClass,
            Passed = _classPassed,
            Failed = _classFailed,
            Skipped = _classSkipped,
        });
        _currentClass = null;
        _classPassed = 0;
        _classFailed = 0;
        _classSkipped = 0;
    }
}
