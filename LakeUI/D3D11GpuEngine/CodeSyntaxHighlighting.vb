Imports System.Drawing
Imports System.Text.RegularExpressions

''' <summary>代码块单行语法着色标记。</summary>
Public Structure CodeSyntaxToken
    Public StartCol As Integer
    Public Length As Integer
    Public ForeColor As Color
    Public Sub New(startCol As Integer, length As Integer, foreColor As Color)
        Me.StartCol = startCol
        Me.Length = length
        Me.ForeColor = foreColor
    End Sub
End Structure

''' <summary>代码块逐行语法着色结果。EndState 支持跨行注释。</summary>
Public Structure CodeSyntaxHighlightResult
    Public Tokens As List(Of CodeSyntaxToken)
    Public EndState As Integer
    Public Sub New(tokens As List(Of CodeSyntaxToken), endState As Integer)
        Me.Tokens = tokens
        Me.EndState = endState
    End Sub
End Structure

''' <summary>自定义代码块高亮器接口，遵循 ModernTextBox 的逐行状态模型。</summary>
Public Interface ICodeSyntaxHighlighter
    Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult
End Interface

''' <summary>语法缩进分析结果。IndentLevel 是当前行，NextIndentLevel 用于下一行。</summary>
Public Structure CodeIndentationResult
    Public IndentLevel As Integer
    Public NextIndentLevel As Integer
    Public Text As String
    Public Sub New(indentLevel As Integer, nextIndentLevel As Integer, text As String)
        Me.IndentLevel = indentLevel
        Me.NextIndentLevel = nextIndentLevel
        Me.Text = text
    End Sub
End Structure

''' <summary>代码块语法缩进分析器。只在启用语法高亮时调用。</summary>
Public NotInheritable Class CodeIndentationAnalyzer
    Private Sub New()
    End Sub

    Public Shared Function Analyze(language As String, lineText As String, previousIndentLevel As Integer) As CodeIndentationResult
        Dim text = If(lineText, "").TrimStart(" "c, ChrW(9))
        If text.Length = 0 Then Return New CodeIndentationResult(0, previousIndentLevel, text)
        Dim key = CodeSyntaxHighlighterRegistry.NormalizeLanguage(language)
        Dim level = Math.Max(0, previousIndentLevel)
        Dim nextLevel = level
        If key = "python" OrElse key = "py" OrElse key = "py3" Then
            If IsPythonDedent(text) Then level = Math.Max(0, level - 1)
            If text.EndsWith(":"c) AndAlso Not text.StartsWith("#", StringComparison.Ordinal) Then nextLevel = level + 1
        ElseIf key = "vb" OrElse key = "vbnet" OrElse key = "vb.net" OrElse key = "visualbasic.net" OrElse key = "vb6" OrElse key = "visualbasic6" Then
            If IsVisualBasicDedent(text) Then
                level = Math.Max(0, level - 1)
                nextLevel = level
            End If
            If IsVisualBasicContinuation(text) Then nextLevel = level + 1
            If IsVisualBasicMidBlock(text) Then nextLevel = level + 1
        ElseIf key = "asm" OrElse key = "assembly" OrElse key = "x86asm" OrElse key = "masm" OrElse key = "nasm" Then
            If IsAssemblyLabel(text) Then
                level = 0
                nextLevel = 1
            ElseIf IsAssemblyDirective(text) Then
                level = 0
                nextLevel = 0
            End If
        Else
            If StartsWithClosingBrace(text) Then level = Math.Max(0, level - 1)
            Dim clean = StripCStyleStringsAndComments(text)
            nextLevel = Math.Max(0, level + CountChar(clean, "{"c) - CountChar(clean, "}"c))
        End If
        Return New CodeIndentationResult(level, Math.Max(0, nextLevel), text)
    End Function

    Private Shared Function IsPythonDedent(text As String) As Boolean
        Return Regex.IsMatch(text, "^(elif|else|except|finally|case)\b", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function IsVisualBasicDedent(text As String) As Boolean
        Dim normalized = StripVisualBasicModifiers(text)
        Return Regex.IsMatch(normalized, "^(end\b|else\b|elseif\b|case\b|catch\b|finally\b|loop\b|next\b|wend\b)", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function IsVisualBasicContinuation(text As String) As Boolean
        Dim normalized = StripVisualBasicModifiers(text)
        Return Regex.IsMatch(normalized, "^(else|elseif|case|catch|finally)\b", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function IsVisualBasicMidBlock(text As String) As Boolean
        Dim normalized = StripVisualBasicModifiers(text)
        If Regex.IsMatch(normalized, "^(end\b|else\b|elseif\b|case\b|catch\b|finally\b|loop\b|next\b|wend\b)", RegexOptions.IgnoreCase) Then Return False
        If Regex.IsMatch(normalized, "^(class|module|namespace|sub|function|property|structure|enum|interface|for|foreach|while|do|select|try|with|using)\b", RegexOptions.IgnoreCase) Then Return True
        Return Regex.IsMatch(normalized, "^if\b.*\bthen\s*$", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function StripVisualBasicModifiers(text As String) As String
        Return Regex.Replace(If(text, ""), "^(?:(?:public|private|protected|friend|shared|partial|default|overloads|overridable|overrides|mustinherit|notoverridable|notinheritable|shadows|static|async|iterator)\s+)+", "", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function IsAssemblyLabel(text As String) As Boolean
        Return Regex.IsMatch(text, "^[A-Za-z_.$?][A-Za-z0-9_.$?]*:")
    End Function

    Private Shared Function IsAssemblyDirective(text As String) As Boolean
        Return Regex.IsMatch(text, "^(section|segment|global|extern|bits|org|align|db|dw|dd|dq)\b", RegexOptions.IgnoreCase)
    End Function

    Private Shared Function StartsWithClosingBrace(text As String) As Boolean
        Return text.StartsWith("}"c) OrElse text.StartsWith(")"c) OrElse text.StartsWith("]"c)
    End Function

    Private Shared Function CountChar(text As String, value As Char) As Integer
        Dim count = 0
        For Each ch In text
            If ch = value Then count += 1
        Next
        Return count
    End Function

    Private Shared Function StripCStyleStringsAndComments(text As String) As String
        Dim result As New System.Text.StringBuilder()
        Dim inString As Boolean = False
        Dim quote As Char = ChrW(0)
        Dim i As Integer = 0
        While i < text.Length
            If Not inString AndAlso i + 1 < text.Length AndAlso text(i) = "/"c AndAlso text(i + 1) = "/"c Then Exit While
            Dim ch = text(i)
            If ch = """"c OrElse ch = "'"c Then
                If inString Then
                    If ch = quote Then inString = False
                Else
                    quote = ch
                    inString = True
                End If
                result.Append(" "c)
            ElseIf inString Then
                result.Append(" "c)
            Else
                result.Append(ch)
            End If
            i += 1
        End While
        Return result.ToString()
    End Function
End Class

''' <summary>内置代码块高亮器注册表。Register 会覆盖现有语言映射。</summary>
Public NotInheritable Class CodeSyntaxHighlighterRegistry
    Private Shared ReadOnly _highlighters As New Dictionary(Of String, ICodeSyntaxHighlighter)(StringComparer.OrdinalIgnoreCase)

    Shared Sub New()
        Register(New CFamilyHighlighter("csharp"), "csharp", "cs", "c#")
        Register(New CFamilyHighlighter("cpp"), "cpp", "c++", "cxx", "cc", "hpp", "hxx")
        Register(New CFamilyHighlighter("c"), "c", "h")
        Register(New VisualBasicHighlighter(False), "vb", "vbnet", "vb.net", "visualbasic.net")
        Register(New VisualBasicHighlighter(True), "vb6", "visualbasic6")
        Register(New PythonHighlighter(), "python", "py", "py3")
        Register(New JsonHighlighter(), "json")
        Register(New AssemblyHighlighter(), "asm", "assembly", "x86asm", "masm", "nasm")
    End Sub

    Public Shared Sub Register(highlighter As ICodeSyntaxHighlighter, ParamArray languages As String())
        If highlighter Is Nothing OrElse languages Is Nothing Then Return
        SyncLock _highlighters
            For Each language In languages
                Dim key = NormalizeLanguage(language)
                If key.Length > 0 Then _highlighters(key) = highlighter
            Next
        End SyncLock
    End Sub

    Public Shared Function Unregister(language As String) As Boolean
        Dim key = NormalizeLanguage(language)
        If key.Length = 0 Then Return False
        SyncLock _highlighters
            Return _highlighters.Remove(key)
        End SyncLock
    End Function

    Public Shared Function GetHighlighter(language As String) As ICodeSyntaxHighlighter
        Dim result As ICodeSyntaxHighlighter = Nothing
        SyncLock _highlighters
            _highlighters.TryGetValue(NormalizeLanguage(language), result)
        End SyncLock
        Return result
    End Function

    Public Shared Function NormalizeLanguage(language As String) As String
        If String.IsNullOrWhiteSpace(language) Then Return ""
        Return language.Trim().ToLowerInvariant()
    End Function

    Private MustInherit Class BasicHighlighter
        Implements ICodeSyntaxHighlighter
        Protected Shared ReadOnly KeywordColor As Color = Color.FromArgb(86, 156, 214)
        Protected Shared ReadOnly ControlColor As Color = Color.FromArgb(216, 160, 223)
        Protected Shared ReadOnly TypeColor As Color = Color.FromArgb(78, 201, 176)
        Protected Shared ReadOnly StringColor As Color = Color.FromArgb(214, 157, 133)
        Protected Shared ReadOnly CommentColor As Color = Color.FromArgb(87, 166, 74)
        Protected Shared ReadOnly NumberColor As Color = Color.FromArgb(181, 206, 168)
        Protected Shared ReadOnly DirectiveColor As Color = Color.FromArgb(155, 155, 155)

        Public MustOverride Function HighlightLine(lineIndex As Integer,
                                                   lineText As String,
                                                   previousLineState As Integer) As CodeSyntaxHighlightResult _
                                                   Implements ICodeSyntaxHighlighter.HighlightLine

        Protected Shared Function Scan(lineText As String, previousLineState As Integer, keywords As HashSet(Of String), controls As HashSet(Of String), types As HashSet(Of String), lineComment As String, Optional blockStart As String = Nothing, Optional blockEnd As String = Nothing, Optional apostropheString As Boolean = True) As CodeSyntaxHighlightResult
            Dim tokens As New List(Of CodeSyntaxToken)
            Dim i As Integer = 0
            Dim inBlock = previousLineState = 1
            While i < lineText.Length
                If inBlock Then
                    Dim ending = lineText.IndexOf(blockEnd, i, StringComparison.Ordinal)
                    If ending < 0 Then
                        Add(tokens, i, lineText.Length - i, CommentColor)
                        Return New CodeSyntaxHighlightResult(tokens, 1)
                    End If
                    Add(tokens, i, ending + blockEnd.Length - i, CommentColor)
                    i = ending + blockEnd.Length
                    inBlock = False
                    Continue While
                End If
                If Not String.IsNullOrEmpty(blockStart) AndAlso lineText.IndexOf(blockStart, i, StringComparison.Ordinal) = i Then
                    Dim ending = lineText.IndexOf(blockEnd, i + blockStart.Length, StringComparison.Ordinal)
                    If ending < 0 Then
                        Add(tokens, i, lineText.Length - i, CommentColor)
                        Return New CodeSyntaxHighlightResult(tokens, 1)
                    End If
                    Add(tokens, i, ending + blockEnd.Length - i, CommentColor)
                    i = ending + blockEnd.Length
                    Continue While
                End If
                If Not String.IsNullOrEmpty(lineComment) AndAlso lineText.IndexOf(lineComment, i, StringComparison.Ordinal) = i Then
                    Add(tokens, i, lineText.Length - i, CommentColor)
                    Exit While
                End If
                Dim ch = lineText(i)
                If ch = """"c OrElse (apostropheString AndAlso ch = "'"c) Then
                    Dim start = i
                    Dim quote = ch
                    i += 1
                    While i < lineText.Length
                        If lineText(i) = "\"c AndAlso i + 1 < lineText.Length Then
                            i += 2
                        ElseIf lineText(i) = quote Then
                            i += 1
                            Exit While
                        Else
                            i += 1
                        End If
                    End While
                    Add(tokens, start, i - start, StringColor)
                    Continue While
                End If
                If Char.IsDigit(ch) AndAlso (i = 0 OrElse Not Char.IsLetterOrDigit(lineText(i - 1))) Then
                    Dim start = i
                    i += 1
                    While i < lineText.Length AndAlso (Char.IsLetterOrDigit(lineText(i)) OrElse "._xX+-".Contains(lineText(i)))
                        i += 1
                    End While
                    Add(tokens, start, i - start, NumberColor)
                    Continue While
                End If
                If Char.IsLetter(ch) OrElse ch = "_"c Then
                    Dim start = i
                    i += 1
                    While i < lineText.Length AndAlso (Char.IsLetterOrDigit(lineText(i)) OrElse lineText(i) = "_"c)
                        i += 1
                    End While
                    Dim word = lineText.Substring(start, i - start)
                    If controls.Contains(word) Then
                        Add(tokens, start, word.Length, ControlColor)
                    ElseIf types.Contains(word) Then
                        Add(tokens, start, word.Length, TypeColor)
                    ElseIf keywords.Contains(word) Then
                        Add(tokens, start, word.Length, KeywordColor)
                    End If
                    Continue While
                End If
                i += 1
            End While
            Return New CodeSyntaxHighlightResult(tokens, 0)
        End Function

        Protected Shared Sub Add(tokens As List(Of CodeSyntaxToken), startCol As Integer, length As Integer, color As Color)
            If length > 0 Then tokens.Add(New CodeSyntaxToken(startCol, length, color))
        End Sub
    End Class

    Private NotInheritable Class CFamilyHighlighter
        Inherits BasicHighlighter
        Private ReadOnly _language As String
        Private Shared ReadOnly Controls As New HashSet(Of String)(StringComparer.Ordinal) From {"if", "else", "switch", "case", "for", "while", "do", "break", "continue", "try", "catch", "throw", "return"}
        Private Shared ReadOnly Types As New HashSet(Of String)(StringComparer.Ordinal) From {"void", "bool", "char", "short", "int", "long", "float", "double", "decimal", "string", "object", "size_t"}
        Private Shared ReadOnly CSharpKeywords As New HashSet(Of String)(StringComparer.Ordinal) From {"class", "namespace", "using", "public", "private", "protected", "internal", "static", "readonly", "const", "new", "async", "await", "interface", "enum", "struct", "this", "base", "null", "true", "false"}
        Private Shared ReadOnly CppKeywords As New HashSet(Of String)(StringComparer.Ordinal) From {"class", "namespace", "using", "public", "private", "protected", "static", "const", "new", "delete", "template", "typename", "virtual", "nullptr", "true", "false"}
        Private Shared ReadOnly CKeywords As New HashSet(Of String)(StringComparer.Ordinal) From {"auto", "const", "enum", "extern", "register", "static", "struct", "typedef", "union", "unsigned", "volatile"}
        Public Sub New(language As String)
            _language = language
        End Sub
        Public Overrides Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult
            Dim trimmed = lineText.TrimStart()
            If trimmed.StartsWith("#", StringComparison.Ordinal) Then Return New CodeSyntaxHighlightResult(New List(Of CodeSyntaxToken) From {New CodeSyntaxToken(lineText.Length - trimmed.Length, trimmed.Length, DirectiveColor)}, 0)
            Dim keywords = If(_language = "csharp", CSharpKeywords, If(_language = "cpp", CppKeywords, CKeywords))
            Return Scan(lineText, previousLineState, keywords, Controls, Types, "//", "/*", "*/")
        End Function
    End Class

    Private NotInheritable Class VisualBasicHighlighter
        Inherits BasicHighlighter
        Private ReadOnly _vb6 As Boolean
        Private Shared ReadOnly NetKeywords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Imports", "Namespace", "Class", "Module", "Sub", "Function", "Property", "Dim", "As", "New", "Inherits", "Implements", "Interface", "Enum", "Structure", "Public", "Private", "Protected", "Friend", "Shared", "ReadOnly", "Nothing", "True", "False"}
        Private Shared ReadOnly Vb6Keywords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Option", "Explicit", "Private", "Public", "Dim", "Static", "Const", "Sub", "Function", "Property", "Set", "Let", "New", "Nothing", "True", "False", "ByVal", "ByRef"}
        Private Shared ReadOnly Controls As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"If", "Then", "Else", "ElseIf", "Select", "Case", "For", "Each", "While", "Wend", "Do", "Loop", "Try", "Catch", "Finally", "Throw", "Exit", "Continue", "Next", "End", "Return"}
        Private Shared ReadOnly Types As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"String", "Integer", "Long", "Boolean", "Double", "Decimal", "Object", "Date", "Byte", "Short", "Single", "Variant", "Currency"}
        Public Sub New(vb6 As Boolean)
            _vb6 = vb6
        End Sub
        Public Overrides Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult
            Dim trimmed = lineText.TrimStart()
            If Not _vb6 AndAlso trimmed.StartsWith("#", StringComparison.Ordinal) Then Return New CodeSyntaxHighlightResult(New List(Of CodeSyntaxToken) From {New CodeSyntaxToken(lineText.Length - trimmed.Length, trimmed.Length, DirectiveColor)}, 0)
            Return Scan(lineText, previousLineState, If(_vb6, Vb6Keywords, NetKeywords), Controls, Types, "'", apostropheString:=False)
        End Function
    End Class

    Private NotInheritable Class PythonHighlighter
        Inherits BasicHighlighter
        Private Shared ReadOnly Keywords As New HashSet(Of String)(StringComparer.Ordinal) From {"def", "class", "import", "from", "as", "return", "lambda", "pass", "raise", "with", "async", "await", "True", "False", "None", "global", "nonlocal"}
        Private Shared ReadOnly Controls As New HashSet(Of String)(StringComparer.Ordinal) From {"if", "elif", "else", "for", "while", "try", "except", "finally", "break", "continue", "match", "case", "yield"}
        Private Shared ReadOnly Types As New HashSet(Of String)(StringComparer.Ordinal) From {"str", "int", "float", "bool", "list", "dict", "set", "tuple", "bytes"}
        Public Overrides Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult
            Return Scan(lineText, previousLineState, Keywords, Controls, Types, "#")
        End Function
    End Class

    Private NotInheritable Class JsonHighlighter
        Implements ICodeSyntaxHighlighter
        Private Shared ReadOnly Pattern As New Regex("(?<key>""(?:\\.|[^""])*"")(?=\s*:)|(?<string>""(?:\\.|[^""])*"")|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(?<literal>\b(?:true|false|null)\b)", RegexOptions.Compiled)
        Public Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult Implements ICodeSyntaxHighlighter.HighlightLine
            Dim tokens As New List(Of CodeSyntaxToken)
            For Each match As Match In Pattern.Matches(lineText)
                Dim tokenColor As Color = If(match.Groups("key").Success, System.Drawing.Color.FromArgb(78, 201, 176), If(match.Groups("string").Success, System.Drawing.Color.FromArgb(214, 157, 133), If(match.Groups("number").Success, System.Drawing.Color.FromArgb(181, 206, 168), System.Drawing.Color.FromArgb(86, 156, 214))))
                tokens.Add(New CodeSyntaxToken(match.Index, match.Length, tokenColor))
            Next
            Return New CodeSyntaxHighlightResult(tokens, 0)
        End Function
    End Class

    Private NotInheritable Class AssemblyHighlighter
        Implements ICodeSyntaxHighlighter
        Private Shared ReadOnly Instructions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"mov", "lea", "push", "pop", "add", "sub", "imul", "idiv", "inc", "dec", "and", "or", "xor", "not", "cmp", "test", "jmp", "je", "jne", "jg", "jge", "jl", "jle", "call", "ret", "nop", "int", "syscall"}
        Private Shared ReadOnly Registers As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"rax", "rbx", "rcx", "rdx", "rsi", "rdi", "rbp", "rsp", "eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "esp", "ax", "bx", "cx", "dx", "al", "bl", "cl", "dl", "rip", "eip"}
        Private Shared ReadOnly Words As New Regex("\b[A-Za-z_.$?][A-Za-z0-9_.$?]*\b|-?(?:0x[0-9a-fA-F]+|[0-9A-Fa-f]+h|\d+)\b", RegexOptions.Compiled)
        Public Function HighlightLine(lineIndex As Integer, lineText As String, previousLineState As Integer) As CodeSyntaxHighlightResult Implements ICodeSyntaxHighlighter.HighlightLine
            Dim tokens As New List(Of CodeSyntaxToken)
            Dim commentStart = lineText.IndexOf(";"c)
            For Each match As Match In Words.Matches(lineText)
                If commentStart >= 0 AndAlso match.Index >= commentStart Then Exit For
                Dim tokenColor As Color = If(Instructions.Contains(match.Value), System.Drawing.Color.FromArgb(86, 156, 214), If(Registers.Contains(match.Value), System.Drawing.Color.FromArgb(78, 201, 176), If(Char.IsDigit(match.Value(0)) OrElse match.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase), System.Drawing.Color.FromArgb(181, 206, 168), System.Drawing.Color.Empty)))
                If tokenColor <> System.Drawing.Color.Empty Then tokens.Add(New CodeSyntaxToken(match.Index, match.Length, tokenColor))
            Next
            If commentStart >= 0 Then tokens.Add(New CodeSyntaxToken(commentStart, lineText.Length - commentStart, Color.FromArgb(87, 166, 74)))
            Return New CodeSyntaxHighlightResult(tokens, 0)
        End Function
    End Class
End Class
