namespace Atlas.GuineaPig.Scenarios;

/// <summary>Deliberately non-serializable data-row payload: xUnit cannot serialize an arbitrary
/// class, which forces <c>TheoryRowScenarios</c>' MemberData theory onto the single
/// runtime-enumerating <c>AtlasTheoryTestCase</c> fallback instead of one pre-enumerated test
/// case per row.</summary>
public sealed class TheoryRowMarker
{
    public TheoryRowMarker(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => Name;
}
