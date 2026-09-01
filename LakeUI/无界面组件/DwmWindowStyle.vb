Imports System.Runtime.InteropServices

''' <summary>
''' 使用 DWM (Desktop Window Manager) 设置目标窗口的主题外观，包括亮色/暗色模式以及窗口圆角样式。
''' 需要 Windows 11 Build 22000 及以上版本才能生效。
''' </summary>
Public Class DwmWindowStyle

#Region "Win32"

    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
    Private Const DWMWA_WINDOW_CORNER_PREFERENCE As Integer = 33
    Private Const DWMWA_BORDER_COLOR As Integer = 34
    Private Const DWMWA_COLOR_NONE As Integer = &HFFFFFFFE

    <DllImport("dwmapi.dll", EntryPoint:="DwmSetWindowAttribute")>
    Private Shared Function DwmSetWindowAttributeInt(hwnd As IntPtr, dwAttribute As Integer,
                                                      ByRef pvAttribute As Integer, cbAttribute As Integer) As Integer
    End Function

    <DllImport("dwmapi.dll", EntryPoint:="DwmSetWindowAttribute")>
    Private Shared Function DwmSetWindowAttributeBool(hwnd As IntPtr, dwAttribute As Integer,
                                                       <MarshalAs(UnmanagedType.Bool)> ByRef pvAttribute As Boolean, cbAttribute As Integer) As Integer
    End Function

#End Region

    ''' <summary>
    ''' 窗口圆角模式
    ''' </summary>
    Public Enum CornerMode
        ''' <summary>
        ''' 跟随系统默认行为
        ''' </summary>
        [Default] = 0
        ''' <summary>
        ''' 直角（不圆角）
        ''' </summary>
        Square = 1
        ''' <summary>
        ''' 圆角
        ''' </summary>
        Round = 2
        ''' <summary>
        ''' 小圆角
        ''' </summary>
        RoundSmall = 3
    End Enum

    ''' <summary>
    ''' LakeUI 自带无边框弹窗的全局圆角首选项。默认保持既有弹窗的 Windows 11 圆角外观；
    ''' 宿主应用可在运行时修改，之后创建的 ExMsgBox / ExInputBox / ExFloating* / ExOverlayMsgBox 会统一采用该值。
    ''' </summary>
    Public Shared Property PopupCornerMode As CornerMode = CornerMode.Round
    ''' <summary>当前系统是否支持 DWM 窗口圆角首选项（Windows 11 Build 22000+）。</summary>
    Public Shared ReadOnly Property IsCornerModeSupported As Boolean
        Get
            Return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        End Get
    End Property

    ''' <summary>
    ''' 设置目标窗口的亮色/暗色模式。设置为 True 时窗口标题栏和边框使用暗色主题，False 时使用亮色主题。
    ''' </summary>
    ''' <param name="windowHandle">窗口句柄，通常传入 Me.Handle</param>
    ''' <param name="isDarkMode">True 为暗色模式，False 为亮色模式</param>
    ''' <returns>返回 HRESULT 值，0 表示成功。</returns>
    Public Shared Function SetDarkMode(windowHandle As IntPtr, isDarkMode As Boolean) As Integer
        Dim value As Boolean = isDarkMode
        Return DwmSetWindowAttributeBool(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, value, Marshal.SizeOf(Of Boolean)())
    End Function

    ''' <summary>
    ''' 设置目标窗口的圆角样式。
    ''' </summary>
    ''' <param name="windowHandle">窗口句柄，通常传入 Me.Handle</param>
    ''' <param name="mode">圆角模式</param>
    ''' <returns>返回 HRESULT 值，0 表示成功。</returns>
    Public Shared Function SetCornerMode(windowHandle As IntPtr, mode As CornerMode) As Integer
        Dim value As Integer = CInt(mode)
        Return DwmSetWindowAttributeInt(windowHandle, DWMWA_WINDOW_CORNER_PREFERENCE, value, 4)
    End Function

    ''' <summary>禁止 DWM 为无边框窗口额外绘制系统边框，避免深色窗口出现浅色/白色外沿。</summary>
    Public Shared Function SuppressSystemBorder(windowHandle As IntPtr) As Integer
        Dim value As Integer = DWMWA_COLOR_NONE
        Return DwmSetWindowAttributeInt(windowHandle, DWMWA_BORDER_COLOR, value, 4)
    End Function

    ''' <summary>将 LakeUI 的全局弹窗圆角策略应用到目标无边框弹窗，并同时抑制系统边框。</summary>
    Public Shared Function ApplyPopupWindowStyle(windowHandle As IntPtr) As Integer
        Dim cornerResult = SetCornerMode(windowHandle, PopupCornerMode)
        Dim borderResult = SuppressSystemBorder(windowHandle)
        Return If(cornerResult <> 0, cornerResult, borderResult)
    End Function

    ''' <summary>返回与 Windows 11 软件圆角匹配的逻辑半径：Round=8px、RoundSmall=4px、其余=0。</summary>
    Public Shared Function GetCornerRadiusLogical(mode As CornerMode) As Single
        Select Case mode
            Case CornerMode.Round
                Return 8.0F
            Case CornerMode.RoundSmall
                Return 4.0F
            Case Else
                Return 0.0F
        End Select
    End Function

    ''' <summary>当前全局弹窗策略是否使用圆角几何。</summary>
    Public Shared ReadOnly Property PopupUsesRoundedCorners As Boolean
        Get
            Return GetCornerRadiusLogical(PopupCornerMode) > 0.0F
        End Get
    End Property

    ''' <summary>当前全局弹窗策略对应的逻辑圆角半径。</summary>
    Public Shared ReadOnly Property PopupCornerRadiusLogical As Single
        Get
            Return GetCornerRadiusLogical(PopupCornerMode)
        End Get
    End Property

End Class
