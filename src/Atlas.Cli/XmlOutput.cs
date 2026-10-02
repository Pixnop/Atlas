using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Atlas.Cli;

/// <summary>The one place the CLI makes text safe for XML and writes an XML file. XML 1.0 cannot
/// carry most control characters, a lone surrogate, U+FFFE or U+FFFF, not even as a character
/// reference, and a test can produce any of them (a protobuf payload printed raw is one
/// <c>Assert.Fail</c> away), so every string a test supplies goes through <see cref="Escape"/>
/// before it reaches a document, and every document goes out through <see cref="TrySave"/>.</summary>
internal static class XmlOutput
{
    /// <summary>Replaces every character XML 1.0 forbids with a visible <c>\uXXXX</c> escape, so
    /// the information a test put in the string is not lost and not hidden. Tab, line feed,
    /// carriage return, surrogate pairs and everything else XML allows are left as they are.</summary>
    /// <remarks>The escape is for a reader of the report and cannot be undone: a message that
    /// already holds the six characters <c>\u0012</c> reads the same as one that held the control
    /// character. A lone surrogate a worker process reports never gets here as one, because the
    /// worker's JSON protocol turns it into U+FFFD first.</remarks>
    /// <param name="text">The text to make safe.</param>
    /// <returns>The same instance when nothing needed replacing, otherwise the escaped copy.</returns>
    public static string Escape(string text)
    {
        StringBuilder? escaped = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (char.IsHighSurrogate(current) && index + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[index + 1], current))
            {
                index++;
                escaped?.Append(current).Append(text[index]);
                continue;
            }

            // IsXmlChar answers true for a surrogate half on its own, which is the one thing XML 1.0
            // forbids about them: only a well-formed pair, handled above, is a character.
            if (!char.IsSurrogate(current) && XmlConvert.IsXmlChar(current))
            {
                escaped?.Append(current);
                continue;
            }

            escaped ??= new StringBuilder(text.Length + 8).Append(text, 0, index);
            escaped.Append(CultureInfo.InvariantCulture, $"\\u{(int)current:X4}");
        }

        return escaped?.ToString() ?? text;
    }

    /// <summary>Escapes, in place, every text node and attribute value of the document.</summary>
    /// <param name="document">The document to make serializable.</param>
    /// <returns>The same document.</returns>
    public static XDocument Sanitize(XDocument document)
    {
        foreach (XText text in document.DescendantNodes().OfType<XText>().ToList())
        {
            text.Value = Escape(text.Value);
        }

        foreach (XAttribute attribute in document.Descendants().Attributes().ToList())
        {
            attribute.Value = Escape(attribute.Value);
        }

        return document;
    }

    /// <summary>Writes the document to a temporary file next to <paramref name="path"/> and moves
    /// it into place, so a failure while writing never leaves a truncated report where a good one
    /// was expected, and never leaves the temporary file either.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="path">The report path; missing parent directories are created.</param>
    /// <param name="error">The reason, when the write failed.</param>
    /// <returns>True when the report is in place.</returns>
    public static bool TrySave(XDocument document, string path, out string? error)
    {
        string? temporary = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            temporary = $"{fullPath}.{Environment.ProcessId}.tmp";
            document.Save(temporary);
            File.Move(temporary, fullPath, overwrite: true);
            temporary = null;
            error = null;
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or XmlException)
        {
            error = failure.Message;
            return false;
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    private static void DeleteQuietly(string? path)
    {
        try
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The report is what matters; a stray temporary file is not worth a second failure.
        }
    }
}
