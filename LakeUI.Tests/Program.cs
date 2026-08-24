using System.Drawing;
using System.Collections;
using System.Reflection;
using LakeUI;

static class Program
{
    private static void Main()
    {
        VerifyFenceParsing();
        VerifyBuiltInHighlighters();
        VerifySyntaxIndentation();
        VerifyRenderedIndentationOffset();
        VerifyMermaidCopyText();
        VerifyCustomHighlighterRegistration();
        Console.WriteLine("Markdown code and Mermaid parser tests passed.");
    }

    private static void VerifyFenceParsing()
    {
        var parser = new MarkdownViewerCore.MarkdownParser();
        var markdown = "```csharp\npublic class Sample { }\n```\n\n```mermaid\nsequenceDiagram\nparticipant Client\nparticipant API\nClient->>API: Request\nAPI-->>Client: Response\n```";
        var document = parser.Parse(markdown);
        var codeBlock = document.Blocks[0];
        Assert(codeBlock.Kind == MarkdownViewerCore.BlockKind.CodeBlock, "Expected a fenced code block.");
        Assert(codeBlock.Language == "csharp", "Expected csharp fence language.");
        var mermaidBlock = document.Blocks.Find(block => block.Language == "mermaid");
        Assert(mermaidBlock is not null && mermaidBlock.IsMermaidSequenceDiagram, "Expected Mermaid sequence diagram recognition.");

        var emptySequence = parser.Parse("```mermaid\nsequenceDiagram\nparticipant Client as Browser\n```").Blocks[0];
        Assert(emptySequence.IsMermaidSequenceDiagram, "A participant-only Mermaid sequence diagram must still use the sequence renderer.");
    }

    private static void VerifyBuiltInHighlighters()
    {
        var cases = new (string Language, string Line)[]
        {
            ("csharp", "public class Sample { return 42; }"),
            ("vbnet", "Public Class Sample : End Class"),
            ("cpp", "class Sample { public: int value; };"),
            ("c", "static int value = 42;"),
            ("python", "def sample(value): return value"),
            ("java", "public sealed class Sample<T> { private int value = 0x2A; return true; }"),
            ("xml", "<item id=\"42\">&amp;</item>"),
            ("html", "<!doctype html><main class=\"content\">Hello</main>"),
            ("vb6", "Private Sub Sample(): End Sub"),
            ("json", "{ \"value\": true, \"count\": 42 }"),
            ("asm", "mov eax, 42 ; load value")
        };

        foreach (var test in cases)
        {
            var highlighter = CodeSyntaxHighlighterRegistry.GetHighlighter(test.Language);
            Assert(highlighter is not null, $"Missing built-in highlighter for {test.Language}.");
            var result = highlighter!.HighlightLine(0, test.Line, 0);
            Assert(result.Tokens is { Count: > 0 }, $"Expected color tokens for {test.Language}.");
        }

        Assert(CodeSyntaxHighlighterRegistry.GetHighlighter("javascript") is null, "Unsupported languages must not receive implicit built-in highlighting.");

        var java = CodeSyntaxHighlighterRegistry.GetHighlighter("java")!;
        var comment = java.HighlightLine(0, "/* Java block", 0);
        Assert(comment.EndState == 1 && comment.Tokens.Count == 1, "Java block comments must carry state across lines.");
        var commentEnd = java.HighlightLine(1, " comment */ int value = 42;", comment.EndState);
        Assert(commentEnd.EndState == 0 && commentEnd.Tokens.Count >= 2, "Java block comments must resume normal scanning after closing.");
        var textBlock = java.HighlightLine(0, "String json = \"\"\"", 0);
        Assert(textBlock.EndState == 2, "Java text blocks must carry a dedicated multiline state.");
        var textBlockEnd = java.HighlightLine(1, "{\"value\": 1}\"\"\";", textBlock.EndState);
        Assert(textBlockEnd.EndState == 0 && textBlockEnd.Tokens.Count == 1, "Java text block content must remain a single string token.");

        VerifyCurrentLanguageKeywords();
        VerifyMarkupHighlighting();
    }

    private static void VerifyCurrentLanguageKeywords()
    {
        AssertHighlightedWords("csharp", "file extension Sample { required string Name { get; init; } field = value; }", "file", "extension", "required", "init", "field");
        AssertHighlightedWords("cpp", "template<class T> concept Value = requires(T value) { value; }; co_await task;", "template", "concept", "requires", "co_await");
        AssertHighlightedWords("c", "constexpr typeof_unqual(int) value = nullptr;", "constexpr", "typeof_unqual", "nullptr");
        AssertHighlightedWords("python", "type Alias = int | None", "type", "int", "None");
        AssertHighlightedWords("python", "assert value is not None", "assert", "is", "not", "None");
        AssertHighlightedWords("vbnet", "Public Async Iterator Function Values() As IEnumerable(Of Integer)", "Async", "Iterator", "Function", "Integer");
    }

    private static void VerifyMarkupHighlighting()
    {
        foreach (var alias in new[] { "xml", "xsd", "xsl", "xslt", "html", "htm", "xhtml", "svg" })
            Assert(CodeSyntaxHighlighterRegistry.GetHighlighter(alias) is not null, $"Missing markup highlighter alias {alias}.");

        var xml = CodeSyntaxHighlighterRegistry.GetHighlighter("xml")!;
        var tag = xml.HighlightLine(0, "<book", 0);
        Assert(tag.EndState == 2, "An XML start tag must carry its state across lines.");
        var tagEnd = xml.HighlightLine(1, " id=\"42\">Text &amp;</book>", tag.EndState);
        Assert(tagEnd.EndState == 0 && tagEnd.Tokens.Count >= 7, "XML attributes, entities, and closing tags must be highlighted.");

        var comment = xml.HighlightLine(0, "<!-- comment", 0);
        Assert(comment.EndState == 1, "XML/HTML comments must carry state across lines.");
        Assert(xml.HighlightLine(1, "continued -->", comment.EndState).EndState == 0, "XML/HTML comment state must end at -->.");
        var cdata = xml.HighlightLine(0, "<![CDATA[<not-a-tag>", 0);
        Assert(cdata.EndState == 6, "XML CDATA sections must use their own multiline state.");
        Assert(xml.HighlightLine(1, "]]>", cdata.EndState).EndState == 0, "XML CDATA state must end at ]]>.");

        var opening = CodeIndentationAnalyzer.Analyze("html", "<main>", 0);
        var voidElement = CodeIndentationAnalyzer.Analyze("html", "<img src=\"cover.png\">", opening.NextIndentLevel);
        var closing = CodeIndentationAnalyzer.Analyze("html", "</main>", voidElement.NextIndentLevel);
        Assert(opening.NextIndentLevel == 1 && voidElement.NextIndentLevel == 1 && closing.NextIndentLevel == 0,
            "HTML indentation must handle container and void elements.");
    }

    private static void AssertHighlightedWords(string language, string line, params string[] words)
    {
        var result = CodeSyntaxHighlighterRegistry.GetHighlighter(language)!.HighlightLine(0, line, 0);
        foreach (var word in words)
            Assert(result.Tokens.Any(token => line.Substring(token.StartCol, token.Length) == word),
                $"Expected {language} keyword '{word}' to be highlighted.");
    }

    private static void VerifyCustomHighlighterRegistration()
    {
        var replacement = new SingleTokenHighlighter();
        CodeSyntaxHighlighterRegistry.Register(replacement, "csharp");
        Assert(ReferenceEquals(CodeSyntaxHighlighterRegistry.GetHighlighter("csharp"), replacement), "Custom registration must override a built-in language mapping.");
        var result = replacement.HighlightLine(0, "custom", 0);
        Assert(result.Tokens.Count == 1 && result.Tokens[0].ForeColor == Color.Magenta, "Custom highlighter result was not preserved.");
    }

    private static void VerifySyntaxIndentation()
    {
        var first = CodeIndentationAnalyzer.Analyze("csharp", "        if (ready) {", 0);
        Assert(first.Text == "if (ready) {" && first.IndentLevel == 0 && first.NextIndentLevel == 1,
            "C# indentation must be syntax-derived instead of source-whitespace-derived.");
        var closing = CodeIndentationAnalyzer.Analyze("csharp", "\t}", first.NextIndentLevel);
        Assert(closing.Text == "}" && closing.IndentLevel == 0 && closing.NextIndentLevel == 0,
            "Closing braces must reduce syntax indentation.");
        var structure = CodeIndentationAnalyzer.Analyze("vbnet", "Public Structure CodeIndentationResult", 0);
        Assert(structure.IndentLevel == 0 && structure.NextIndentLevel == 1,
            "VB.NET declarations with access modifiers must open a syntax indentation level.");
        var structureEnd = CodeIndentationAnalyzer.Analyze("vbnet", "End Structure", structure.NextIndentLevel);
        Assert(structureEnd.IndentLevel == 0 && structureEnd.NextIndentLevel == 0,
            "VB.NET End Structure must close the syntax indentation level.");
        var plain = CodeIndentationAnalyzer.Analyze("", "    plain", 3);
        Assert(plain.Text == "plain", "Indentation analyzer should still normalize text for an active custom highlighter.");
    }

    private static void VerifyRenderedIndentationOffset()
    {
        using var viewer = new MarkdownViewerCore
        {
            EmbeddedContentMode = true,
            Width = 640,
            CodeIndentSize = 4
        };
        viewer.SetMarkdownImmediate("```csharp\nif (ready) {\nreturn;\n}\n```");
        var field = typeof(MarkdownViewerCore).GetField("_visualLines", BindingFlags.Instance | BindingFlags.NonPublic);
        var lines = (IList?)field?.GetValue(viewer);
        Assert(lines is { Count: >= 3 }, "Expected laid out code lines.");
        var firstFragments = (IList?)lines![0]!.GetType().GetField("Fragments")!.GetValue(lines[0]);
        var nestedFragments = (IList?)lines[1]!.GetType().GetField("Fragments")!.GetValue(lines[1]);
        var xField = firstFragments![0]!.GetType().GetField("X")!;
        var firstX = (int)xField.GetValue(firstFragments[0])!;
        var nestedX = (int)xField.GetValue(nestedFragments![0])!;
        Assert(nestedX > firstX, "Syntax indentation must change the rendered fragment X position.");
    }

    private static void VerifyMermaidCopyText()
    {
        using var viewer = new MarkdownViewerCore { EmbeddedContentMode = true, Width = 640 };
        viewer.SetMarkdownImmediate("```mermaid\nsequenceDiagram\nparticipant Client\nparticipant API\nClient->>API: Request\n```");
        var selectAll = typeof(MarkdownViewerCore).GetMethod("SelectAllEmbeddedText", BindingFlags.Instance | BindingFlags.NonPublic);
        selectAll!.Invoke(viewer, null);
        var selected = viewer.GetSelectedText();
        Assert(selected.Contains("sequenceDiagram") && selected.Contains("Client->>API: Request"),
            "Mermaid source text must be available through the existing copy selection path.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SingleTokenHighlighter : ICodeSyntaxHighlighter
    {
        public CodeSyntaxHighlightResult HighlightLine(int lineIndex, string lineText, int previousLineState)
        {
            return new CodeSyntaxHighlightResult(new List<CodeSyntaxToken> { new(0, lineText.Length, Color.Magenta) }, 0);
        }
    }
}
