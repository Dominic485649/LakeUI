Imports System.Numerics
Imports Vortice.Direct2D1
Imports Vortice.DXGI

''' <summary>
''' V5 控件的持久 GPU 内容表面。表面可被同一 D2D 设备上的其他控件直接采样，
''' 也可由 HWND 呈现器下采样并提交。这里没有 GDI、HDC 或 CPU 后备路线。
''' </summary>
Friend NotInheritable Class D3D_ControlSurface
    Implements IDisposable, D3D_IRenderCacheOwner

    Private ReadOnly _owner As Control
    Private ReadOnly _deviceManager As D3D_DeviceManager
    Private _compositor As D3D_WindowCompositor
    Private _context As ID2D1DeviceContext
    Private _bitmap As ID2D1Bitmap1
    Private _logicalSize As Size
    Private _pixelSize As Size
    Private _sampleScale As Integer = 1
    Private _generation As Integer = -1
    Private _revision As Long
    Private _textureUseStarted As Boolean
    Private _backdropUseStarted As Boolean
    Private _allocatedBytes As Long
    Private _drawing As Boolean
    Private _resourceUseDepth As Integer
    Private _lastUsed As Long
    Private _disposed As Boolean

    Friend Sub New(owner As Control, deviceManager As D3D_DeviceManager)
        _owner = owner
        _deviceManager = deviceManager
        D3D_GpuCache.Register(Me)
    End Sub

    Friend ReadOnly Property Bitmap As ID2D1Bitmap1
        Get
            Return _bitmap
        End Get
    End Property

    Friend ReadOnly Property LogicalSize As Size
        Get
            Return _logicalSize
        End Get
    End Property

    Friend ReadOnly Property SampleScale As Integer
        Get
            Return _sampleScale
        End Get
    End Property

    Friend ReadOnly Property DeviceGeneration As Integer
        Get
            Return _generation
        End Get
    End Property

    ''' <summary>单调递增的内容修订号；设备重建不会复用旧表面，但修订号仍用于稳定帧判定。</summary>
    Friend ReadOnly Property Revision As Long
        Get
            Return _revision
        End Get
    End Property

    Friend ReadOnly Property AllocatedBytes As Long
        Get
            Return _allocatedBytes
        End Get
    End Property

    Private ReadOnly Property CacheBytes As Long Implements D3D_IRenderCacheOwner.CacheBytes
        Get
            Return Math.Max(0L, _allocatedBytes)
        End Get
    End Property

    Private ReadOnly Property OldestUseTick As Long Implements D3D_IRenderCacheOwner.OldestUseTick
        Get
            If _drawing OrElse _resourceUseDepth > 0 OrElse _allocatedBytes <= 0 Then Return Long.MaxValue
            Return If(_lastUsed <= 0, Long.MaxValue - 1, _lastUsed)
        End Get
    End Property

    Private Function TrimOldest() As Boolean Implements D3D_IRenderCacheOwner.TrimOldest
        If _drawing OrElse _resourceUseDepth > 0 OrElse _allocatedBytes <= 0 Then Return False
        ReleaseSurfaceResources()
        Return True
    End Function

    Private Sub ReleaseAllBudgeted() Implements D3D_IRenderCacheOwner.ReleaseAll
        ReleaseSurfaceResources()
    End Sub

    Friend Function Render(renderable As D3D_IGpuRenderable,
                           Optional requestedDirty As Rectangle = Nothing,
                           Optional 绘制后处理 As Action(Of D3D_PaintContext) = Nothing) As Boolean
        If _disposed OrElse renderable Is Nothing OrElse Not 可以渲染所有者() Then Return False

        Dim 逻辑尺寸 = New Size(Math.Max(1, _owner.ClientSize.Width), Math.Max(1, _owner.ClientSize.Height))
        Dim 请求采样倍率 = 解析采样倍率(renderable)
        确保资源(逻辑尺寸, 请求采样倍率)
        If _context Is Nothing OrElse _bitmap Is Nothing OrElse _compositor Is Nothing Then Return False

        _textureUseStarted = False
        _backdropUseStarted = False
        _context.Target = _bitmap
        _context.Transform = Matrix3x2.CreateScale(_sampleScale)
        _context.AntialiasMode = AntialiasMode.PerPrimitive
        _context.BeginDraw()
        _drawing = True
        Try
            ' 当前目标按完整帧清空并重建。请求脏区继续保留在诊断与调用结构中，
            ' 只有在视觉验证确认清除和合成契约可靠后，才启用局部渲染。
            Dim 有效脏区 = New Rectangle(Point.Empty, 逻辑尺寸)
            _context.Clear(New Vortice.Mathematics.Color4(0, 0, 0, 0))
            _context.PushAxisAlignedClip(New Vortice.RawRectF(0, 0, 逻辑尺寸.Width, 逻辑尺寸.Height), AntialiasMode.Aliased)
            Try
                _compositor.TextRenderer.ConfigureDeviceContext(_context, _compositor.TextQuality, targetHasAlpha:=True)
                Dim DPI信息 = D3D_DpiContext.FromControl(_owner)
                Using 绘制上下文 As New D3D_PaintContext(
                    _compositor,
                    _context,
                    Matrix3x2.CreateScale(_sampleScale),
                    New RectangleF(0, 0, 逻辑尺寸.Width, 逻辑尺寸.Height),
                    DPI信息.Scale,
                    _compositor.TextQuality,
                    targetHasAlpha:=True,
                    frameGeneration:=D3D_ControlSurfaceRegistry.NextFrameGeneration(),
                    deviceGeneration:=_generation,
                    dirtyRectangle:=有效脏区,
                    beginTextureUse:=AddressOf 开始使用本帧纹理,
                    beginBackdropUse:=AddressOf 开始使用本帧背景,
                    isDirectPresentation:=True)
                    ' 强制约束：先准备当前控件所需的外层背景，再绘制当前控件自身；
                    ' RenderGpu 不得在此阶段同步驱动任何子控件或兄弟控件。
                    D3D_ControlSurfaceRegistry.DrawAutomaticGpuBackdrop(_owner, 绘制上下文)
                    renderable.RenderGpu(绘制上下文)
                    ' 设计器选中线框必须位于控件内容最上层，并与 GPU 表面同帧提交，
                    ' 否则交换链子窗口会覆盖 WinForms 的 GDI 设计时装饰。
                    绘制后处理?.Invoke(绘制上下文)
                End Using
            Finally
                _context.PopAxisAlignedClip()
            End Try
            _context.Transform = Matrix3x2.Identity
            _context.EndDraw()
            _drawing = False
            _revision += 1
            _lastUsed = D3D_GpuCache.NextTick()
            Return True
        Catch
            If _drawing Then
                Try : _context.EndDraw() : Catch : End Try
                _drawing = False
            End If
            Throw
        Finally
            _context.Target = Nothing
            结束本帧资源使用()
        End Try
    End Function

    Private Sub 确保资源(逻辑尺寸 As Size, 采样倍率 As Integer)
        _deviceManager.EnsureCreated()
        Dim 设备代次 = _deviceManager.DeviceGeneration
        Dim 像素尺寸 As New Size(Math.Max(1, 逻辑尺寸.Width * 采样倍率), Math.Max(1, 逻辑尺寸.Height * 采样倍率))
        If 设备代次 <> _generation Then 释放设备资源()

        If _context Is Nothing Then
            _context = _deviceManager.CreateDeviceContext()
            _generation = 设备代次
        End If
        If _compositor Is Nothing OrElse _compositor.IsDisposed Then
            _compositor = D3D_RenderCore.GetWindowCompositor(_owner)
        End If
        If _bitmap IsNot Nothing AndAlso 逻辑尺寸 = _logicalSize AndAlso 像素尺寸 = _pixelSize AndAlso 采样倍率 = _sampleScale Then Return

        _context.Target = Nothing
        If _allocatedBytes > 0 Then
            D3D_RenderDiagnostics.V5SurfaceBytesChanged(-_allocatedBytes)
            _allocatedBytes = 0
        End If
        安全释放(_bitmap)
        _bitmap = Nothing
        _logicalSize = 逻辑尺寸
        _pixelSize = 像素尺寸
        _sampleScale = 采样倍率

        Dim 位图属性 As New BitmapProperties1(
            New Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96.0F,
            96.0F,
            BitmapOptions.Target)
        _bitmap = _context.CreateBitmap(New Vortice.Mathematics.SizeI(像素尺寸.Width, 像素尺寸.Height), IntPtr.Zero, 0UI, 位图属性)
        _allocatedBytes = CLng(像素尺寸.Width) * CLng(像素尺寸.Height) * 4L
        D3D_RenderDiagnostics.V5SurfaceBytesChanged(_allocatedBytes)
        D3D_RenderDiagnostics.V5SurfaceRecreate()
    End Sub

    Private Function 解析采样倍率(可渲染对象 As D3D_IGpuRenderable) As Integer
        Dim 本地倍率 As Integer = 1
        Dim 倍率来源 = TryCast(可渲染对象, D3D_ISuperSamplingSource)
        If 倍率来源 IsNot Nothing Then
            Try : 本地倍率 = CInt(倍率来源.SuperSamplingScale) : Catch : 本地倍率 = 1 : End Try
        End If
        Return Math.Clamp(GlobalOptions.GetEffectiveSsaaScale(本地倍率), 1, 4)
    End Function

    Private Function 可以渲染所有者() As Boolean
        If _owner Is Nothing OrElse _owner.IsDisposed OrElse Not _owner.IsHandleCreated Then Return False
        If _owner.ClientSize.Width <= 0 OrElse _owner.ClientSize.Height <= 0 Then Return False
        Return True
    End Function

    Private Sub 开始使用本帧纹理()
        If _textureUseStarted OrElse _compositor Is Nothing Then Return
        _compositor.TextureCache.BeginFrameUse()
        _textureUseStarted = True
    End Sub

    Private Sub 开始使用本帧背景()
        If _backdropUseStarted OrElse _compositor Is Nothing Then Return
        _compositor.BackdropRenderer.BeginFrameUse()
        _backdropUseStarted = True
    End Sub

    Friend Sub BeginResourceUse()
        If _disposed Then Return
        _resourceUseDepth += 1
        _lastUsed = D3D_GpuCache.NextTick()
    End Sub

    Friend Sub EndResourceUse()
        If _resourceUseDepth > 0 Then _resourceUseDepth -= 1
    End Sub

    Private Sub 结束本帧资源使用()
        If _backdropUseStarted Then
            Try : _compositor.BackdropRenderer.EndFrameUse() : Catch : End Try
            _backdropUseStarted = False
        End If
        If _textureUseStarted Then
            Try : _compositor.TextureCache.EndFrameUse() : Catch : End Try
            _textureUseStarted = False
        End If
    End Sub

    Friend Sub HandleDeviceLost()
        释放设备资源()
    End Sub

    ''' <summary>
    ''' 释放持久位图和上下文，同时保留轻量注册项。隐藏控件借此立即归还显存，
    ''' 下次恢复可见并渲染时再按需重建资源。
    ''' </summary>
    Friend Sub ReleaseSurfaceResources(Optional markRegistryDirty As Boolean = True)
        If _disposed Then Return
        释放设备资源()
        If markRegistryDirty Then D3D_ControlSurfaceRegistry.SurfaceResourcesReleased(_owner)
    End Sub

    Private Sub 释放设备资源()
        结束本帧资源使用()
        If _context IsNot Nothing Then _context.Target = Nothing
        If _allocatedBytes > 0 Then
            D3D_RenderDiagnostics.V5SurfaceBytesChanged(-_allocatedBytes)
            _allocatedBytes = 0
        End If
        安全释放(_bitmap)
        安全释放(_context)
        _bitmap = Nothing
        _context = Nothing
        _generation = -1
        _logicalSize = Size.Empty
        _pixelSize = Size.Empty
    End Sub

    Private Shared Sub 安全释放(资源 As IDisposable)
        If 资源 Is Nothing Then Return
        Try : 资源.Dispose() : Catch : End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        释放设备资源()
    End Sub
End Class
