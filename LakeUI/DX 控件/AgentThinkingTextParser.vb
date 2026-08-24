''' <summary>
''' 解析 Agent 响应中的 &lt;think&gt; 思考段。
''' 解析器保留未完成的标签前缀，因此标签被 SSE 分片拆开时不会泄漏到正文。
''' </summary>
Public NotInheritable Class AgentThinkingTextParser
    Private Const OpenTag As String = "<think>"
    Private Const CloseTag As String = "</think>"

    Private _pending As String = ""
    Private _thinking As Boolean

    Public ReadOnly Property IsThinking As Boolean
        Get
            Return _thinking
        End Get
    End Property

    ''' <summary>追加一个流式片段，并返回本次新增的正文和思考文本。</summary>
    Public Function Append(text As String) As AgentThinkingTextChunk
        If String.IsNullOrEmpty(text) Then Return AgentThinkingTextChunk.Empty

        Dim source = _pending & text
        _pending = ""
        Dim visible As New System.Text.StringBuilder()
        Dim thinking As New System.Text.StringBuilder()
        ParseSource(source, visible, thinking)
        Return New AgentThinkingTextChunk(visible.ToString(), thinking.ToString())
    End Function

    ''' <summary>
    ''' 完成流式解析。未闭合的标签前缀按普通文本保留，避免截断模型的实际回答。
    ''' </summary>
    Public Function Complete() As AgentThinkingTextChunk
        If String.IsNullOrEmpty(_pending) Then
            _thinking = False
            Return AgentThinkingTextChunk.Empty
        End If

        Dim visible As New System.Text.StringBuilder()
        Dim thinking As New System.Text.StringBuilder()
        If _thinking Then
            thinking.Append(_pending)
        Else
            visible.Append(_pending)
        End If
        _pending = ""
        _thinking = False
        Return New AgentThinkingTextChunk(visible.ToString(), thinking.ToString())
    End Function

    Public Sub Reset()
        _pending = ""
        _thinking = False
    End Sub

    Private Sub ParseSource(source As String,
                            visible As System.Text.StringBuilder,
                            thinking As System.Text.StringBuilder)
        Dim position As Integer = 0
        While position < source.Length
            Dim tag = If(_thinking, CloseTag, OpenTag)
            Dim tagIndex = source.IndexOf(tag, position, StringComparison.OrdinalIgnoreCase)
            If tagIndex < 0 Then
                Dim holdLength = GetTrailingTagPrefixLength(source, position, tag)
                Dim emitLength = source.Length - position - holdLength
                If emitLength > 0 Then
                    If _thinking Then
                        thinking.Append(source, position, emitLength)
                    Else
                        visible.Append(source, position, emitLength)
                    End If
                End If
                If holdLength > 0 Then _pending = source.Substring(source.Length - holdLength)
                Return
            End If

            If tagIndex > position Then
                If _thinking Then
                    thinking.Append(source, position, tagIndex - position)
                Else
                    visible.Append(source, position, tagIndex - position)
                End If
            End If
            position = tagIndex + tag.Length
            _thinking = Not _thinking
        End While
    End Sub

    Private Shared Function GetTrailingTagPrefixLength(source As String,
                                                       start As Integer,
                                                       tag As String) As Integer
        Dim available = source.Length - start
        Dim maxLength = Math.Min(tag.Length - 1, available)
        For length = maxLength To 1 Step -1
            If source.EndsWith(tag.Substring(0, length), StringComparison.OrdinalIgnoreCase) Then
                Return length
            End If
        Next
        Return 0
    End Function
End Class

Public NotInheritable Class AgentThinkingTextChunk
    Friend Shared ReadOnly Empty As New AgentThinkingTextChunk("", "")

    Public ReadOnly Property VisibleText As String
    Public ReadOnly Property ThinkingText As String

    Friend Sub New(visibleText As String, thinkingText As String)
        Me.VisibleText = If(visibleText, "")
        Me.ThinkingText = If(thinkingText, "")
    End Sub
End Class
