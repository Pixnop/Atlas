using System.Xml.Linq;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class XmlOutputTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("atlas-xml-output-");

    public void Dispose()
    {
        _directory.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Escape_Should_ReturnTheSameString_When_EveryCharacterIsAllowed()
    {
        // Tab, line feed and carriage return are the three control characters XML 1.0 allows; a
        // surrogate pair (U+1F5FA) is one allowed character, not two lone halves.
        string text = "plain\ttext\r\nover two lines, café ÿ 🗺 end";

        Assert.Same(text, XmlOutput.Escape(text));
    }

    [Theory]
    [InlineData("payload \u0012 bytes", "payload \\u0012 bytes")]
    [InlineData("\0nul", "\\u0000nul")]
    [InlineData("a\u000bb\u000cc", "a\\u000Bb\\u000Cc")]
    [InlineData("esc \u001f end", "esc \\u001F end")]
    [InlineData("not a character \uFFFE and \uFFFF", "not a character \\uFFFE and \\uFFFF")]
    public void Escape_Should_ReplaceForbiddenCharactersWithAVisibleEscape_When_TheTextHoldsThem(
        string text, string expected)
    {
        Assert.Equal(expected, XmlOutput.Escape(text));
    }

    [Fact]
    public void Escape_Should_ReplaceLoneSurrogatesOnly_When_PairsAndHalvesAreMixed()
    {
        // A Fact, not a Theory: xUnit serializes theory data as text, which would swap a lone
        // surrogate for U+FFFD before the test saw it.
        (string Text, string Expected)[] cases =
        [
            ("lone high \ud800 end", "lone high \\uD800 end"),
            ("lone low \udc00 end", "lone low \\uDC00 end"),
            ("ends high \ud83d", "ends high \\uD83D"),
            ("reversed \udc00\ud800", "reversed \\uDC00\\uD800"),
            ("pair \ud83d\uddfa then lone \ud83d", "pair \ud83d\uddfa then lone \\uD83D"),
        ];

        foreach ((string text, string expected) in cases)
        {
            Assert.Equal(expected, XmlOutput.Escape(text));
        }
    }

    [Fact]
    public void Escape_Should_KeepCharactersXmlDiscouragesButAllows_When_TheyAreInTheText()
    {
        // U+007F to U+009F are legal in XML 1.0 (only discouraged), so they are information the
        // test produced and stay as they are.
        string text = "del \u007f next-line \u0085 c1 \u009f";

        Assert.Same(text, XmlOutput.Escape(text));
    }

    [Fact]
    public void Sanitize_Should_MakeTheWholeDocumentSerializable_When_TextAndAttributesHoldForbiddenCharacters()
    {
        XNamespace ns = "urn:test";
        var document = new XDocument(
            new XElement(
                ns + "Root",
                new XAttribute("name", "run \u0001"),
                new XElement(ns + "Message", "payload \u0012 \ud800 \uFFFE"),
                new XElement(ns + "Data", new XCData("cdata \u0003")),
                new XElement(ns + "Fine", "unchanged")));

        XmlOutput.Sanitize(document);

        XDocument parsed = RoundTrip(document);
        Assert.Equal("run \\u0001", parsed.Root!.Attribute("name")!.Value);
        Assert.Equal("payload \\u0012 \\uD800 \\uFFFE", parsed.Root.Element(ns + "Message")!.Value);
        Assert.Equal("cdata \\u0003", parsed.Root.Element(ns + "Data")!.Value);
        Assert.Equal("unchanged", parsed.Root.Element(ns + "Fine")!.Value);
    }

    [Fact]
    public void TrySave_Should_WriteTheDocumentAndLeaveNoTemporaryFile_When_TheDirectoryExists()
    {
        string path = Path.Combine(_directory.FullName, "report.xml");

        bool saved = XmlOutput.TrySave(new XDocument(new XElement("Root", "ok")), path, out string? error);

        Assert.True(saved, error);
        Assert.Null(error);
        Assert.Equal("ok", XDocument.Load(path).Root!.Value);
        Assert.Equal([path], Directory.GetFileSystemEntries(_directory.FullName));
    }

    [Theory]
    [InlineData("one\r\ntwo")]
    [InlineData("one\rtwo")]
    [InlineData("one\ntwo")]
    [InlineData("a\r\n\r\nb\rc\nd")]
    public void TrySave_Should_KeepEveryLineBreakAsWritten_When_TheTextHoldsThem(string text)
    {
        // The writer's default turns a carriage return into the platform's line end, so a message
        // that held one read back with a line feed. Entitizing keeps the character, the way
        // VSTest's own TRX writer does, and the file still parses to the same string.
        string path = Path.Combine(_directory.FullName, "report.xml");
        var document = new XDocument(new XElement("Root", new XAttribute("name", text), new XElement("Message", text)));

        bool saved = XmlOutput.TrySave(document, path, out string? error);

        Assert.True(saved, error);
        XDocument parsed = XDocument.Load(path);
        Assert.Equal(text, parsed.Root!.Element("Message")!.Value);
        Assert.Equal(text, parsed.Root.Attribute("name")!.Value);
    }

    [Fact]
    public void TrySave_Should_StillEscapeForbiddenCharacters_When_TheTextAlsoHoldsACarriageReturn()
    {
        // The two protections do not get in each other's way: Sanitize turns the control character
        // into a visible escape first, and the carriage return then survives the write.
        string path = Path.Combine(_directory.FullName, "report.xml");
        var document = XmlOutput.Sanitize(new XDocument(new XElement("Root", "payload \u0012 end\r\nnext")));

        bool saved = XmlOutput.TrySave(document, path, out string? error);

        Assert.True(saved, error);
        Assert.Equal("payload \\u0012 end\r\nnext", XDocument.Load(path).Root!.Value);
    }

    [Fact]
    public void TrySave_Should_KeepTheDeclarationAndIndentation_When_WritingADocument()
    {
        string path = Path.Combine(_directory.FullName, "report.xml");
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("Root", new XElement("Child", "x")));

        bool saved = XmlOutput.TrySave(document, path, out string? error);

        Assert.True(saved, error);
        string text = File.ReadAllText(path);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text, StringComparison.Ordinal);
        Assert.Contains("\n  <Child>x</Child>", text.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TrySave_Should_ReplaceAnExistingReport_When_ThePathIsTaken()
    {
        string path = Path.Combine(_directory.FullName, "report.xml");
        File.WriteAllText(path, "<Old/>");

        bool saved = XmlOutput.TrySave(new XDocument(new XElement("New")), path, out string? error);

        Assert.True(saved, error);
        Assert.Equal("New", XDocument.Load(path).Root!.Name.LocalName);
    }

    [Fact]
    public void TrySave_Should_KeepTheExistingReportAndLeaveNoFile_When_TheDocumentCannotBeSerialized()
    {
        // A document that skipped Sanitize still must not truncate the report it would replace,
        // nor leave a half-written sibling: the old content is the only thing that stays.
        string path = Path.Combine(_directory.FullName, "report.xml");
        File.WriteAllText(path, "<Old/>");

        bool saved = XmlOutput.TrySave(
            new XDocument(new XElement("Root", "payload \u0012")), path, out string? error);

        Assert.False(saved);
        Assert.Contains("0x12", error);
        Assert.Equal("<Old/>", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(_directory.FullName));
    }

    [Fact]
    public void TrySave_Should_ReportTheFailureAndLeaveNoTemporaryFile_When_TheTargetIsADirectory()
    {
        string path = Path.Combine(_directory.FullName, "report.xml");
        Directory.CreateDirectory(path);

        bool saved = XmlOutput.TrySave(new XDocument(new XElement("Root")), path, out string? error);

        Assert.False(saved);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal([path], Directory.GetFileSystemEntries(_directory.FullName));
    }

    [Fact]
    public void TrySave_Should_CreateMissingDirectories_When_TheParentDoesNotExist()
    {
        string path = Path.Combine(_directory.FullName, "nested", "deeper", "report.xml");

        bool saved = XmlOutput.TrySave(new XDocument(new XElement("Root")), path, out string? error);

        Assert.True(saved, error);
        Assert.True(File.Exists(path));
    }

    private static XDocument RoundTrip(XDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        stream.Position = 0;
        return XDocument.Load(stream);
    }
}
