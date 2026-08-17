Public Class Form_AgentRoom
    Private Sub Form_AgentRoom_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        AgentRoom1.AddUserMessage("这是一段示例文本")

        Dim turnId = Guid.NewGuid().ToString("N")
        AgentRoom1.AddTurnHeader(turnId, "已工作 8.4 秒 · 2 次工具调用", expanded:=True)
        AgentRoom1.AddAssistantActivity(turnId, "这是一段推理中途的消息")
        AgentRoom1.AddToolCall(
            turnId,
            "读取面板 · 114 毫秒",
            "调用参数" & vbCrLf & "{}" & vbCrLf & vbCrLf &
            "返回结果",
            expanded:=False)
        AgentRoom1.AddAssistantActivity(turnId, "当前画面是 1080p60，接着确认准备文件的音视频流。")
        AgentRoom1.AddToolCall(
            turnId,
            "读取文件 · 514 毫秒",
            "调用参数" & vbCrLf & "{""include_details"":true}" & vbCrLf & vbCrLf &
            "返回结果",
            expanded:=True)

        AgentRoom1.AddAssistantMessage("这是一条 AI 消息")
    End Sub
End Class
