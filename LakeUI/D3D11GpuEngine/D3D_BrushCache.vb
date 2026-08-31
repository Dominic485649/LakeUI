Imports Vortice.Direct2D1

''' <summary>
''' D3D_BrushCache 管理窗口级 D2D brush 资源。
''' 它持有 GPU 对象，绑定 device generation；generation 变化或 device lost 时必须整体释放。
''' 它不允许控件跨帧持有 ID2D1Brush，RenderGpu 只能通过 D3D_PaintContext/Compositor 请求画刷。
''' </summary>
Public NotInheritable Class D3D_BrushCache
    Implements IDisposable

    Private ReadOnly _solidBrushes As New Dictionary(Of D3D_BrushKey, D3D_BrushCacheEntry)()
    ' 动画颜色可能每帧产生新 key；使用链表维护 LRU，避免每次 miss 都扫描全部画刷。
    Private ReadOnly _solidBrushLru As New LinkedList(Of D3D_BrushKey)()
    Private _clock As Long
    Private _disposed As Boolean

    Public Property MaxSolidBrushes As Integer = 256

    Public Function GetSolidBrush(context As ID2D1DeviceContext, color As System.Drawing.Color, generation As Integer) As ID2D1SolidColorBrush
        Return GetSolidBrushCore(context, color, generation, mapHdr:=True)
    End Function

    Friend Function GetRawSolidBrush(context As ID2D1DeviceContext, color As System.Drawing.Color, generation As Integer) As ID2D1SolidColorBrush
        Return GetSolidBrushCore(context, color, generation, mapHdr:=False)
    End Function

    Private Function GetSolidBrushCore(context As ID2D1DeviceContext,
                                       color As System.Drawing.Color,
                                       generation As Integer,
                                       mapHdr As Boolean) As ID2D1SolidColorBrush
        If _disposed Then Throw New ObjectDisposedException(NameOf(D3D_BrushCache))
        If context Is Nothing Then Throw New ArgumentNullException(NameOf(context))

        Dim hdrRevision = If(mapHdr, D3D_HdrOutput.VectorColorRevision, 0)
        Dim key As New D3D_BrushKey(context, generation, hdrRevision, mapHdr, color.ToArgb())
        Dim entry As D3D_BrushCacheEntry = Nothing
        If _solidBrushes.TryGetValue(key, entry) Then
            entry.LastUsed = NextClock()
            Touch(entry)
            Return entry.Brush
        End If

        Dim brushColor = If(mapHdr, D3D_HdrOutput.MapColor4(color), D3D_HdrOutput.ToRawColor4(color))
        Dim brush = context.CreateSolidColorBrush(brushColor)
        Dim newEntry = New D3D_BrushCacheEntry(brush, generation, NextClock())
        newEntry.LruNode = _solidBrushLru.AddLast(key)
        _solidBrushes(key) = newEntry
        Trim(protectedKey:=key)
        Return brush
    End Function

    Public Sub Invalidate()
        For Each entry In _solidBrushes.Values
            Try : entry.Brush.Dispose() : Catch : End Try
        Next
        _solidBrushes.Clear()
        _solidBrushLru.Clear()
    End Sub

    Private Sub Trim(protectedKey As D3D_BrushKey)
        Dim limit = Math.Max(0, GlobalOptions.BrushCacheLimit)
        MaxSolidBrushes = limit
        While _solidBrushes.Count > limit
            Dim victimNode = _solidBrushLru.First
            If victimNode Is Nothing Then Exit While
            Dim victimKey = victimNode.Value
            If victimKey.Equals(protectedKey) Then
                victimNode = victimNode.Next
                If victimNode Is Nothing Then Exit While
                victimKey = victimNode.Value
            End If
            Dim victimEntry As D3D_BrushCacheEntry = Nothing
            _solidBrushes.TryGetValue(victimKey, victimEntry)
            If victimEntry Is Nothing Then Exit While
            _solidBrushes.Remove(victimKey)
            _solidBrushLru.Remove(victimNode)
            victimEntry.LruNode = Nothing
            Try : victimEntry.Brush.Dispose() : Catch : End Try
        End While
    End Sub

    Private Sub Touch(entry As D3D_BrushCacheEntry)
        If entry Is Nothing OrElse entry.LruNode Is Nothing Then Return
        Dim node = entry.LruNode
        _solidBrushLru.Remove(node)
        ' Reattach the same node; AddLast(key) would allocate on every animation hit.
        _solidBrushLru.AddLast(node)
    End Sub

    Private Structure D3D_BrushKey
        Implements IEquatable(Of D3D_BrushKey)

        Private ReadOnly _context As ID2D1DeviceContext
        Private ReadOnly _generation As Integer
        Private ReadOnly _hdrRevision As Integer
        Private ReadOnly _mapHdr As Boolean
        Private ReadOnly _argb As Integer

        Friend Sub New(context As ID2D1DeviceContext, generation As Integer, hdrRevision As Integer, mapHdr As Boolean, argb As Integer)
            _context = context
            _generation = generation
            _hdrRevision = hdrRevision
            _mapHdr = mapHdr
            _argb = argb
        End Sub

        Public Overloads Function Equals(other As D3D_BrushKey) As Boolean Implements IEquatable(Of D3D_BrushKey).Equals
            Return ReferenceEquals(_context, other._context) AndAlso
                   _generation = other._generation AndAlso _hdrRevision = other._hdrRevision AndAlso
                   _mapHdr = other._mapHdr AndAlso _argb = other._argb
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return TypeOf obj Is D3D_BrushKey AndAlso Equals(DirectCast(obj, D3D_BrushKey))
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return HashCode.Combine(Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_context), _generation, _hdrRevision, _mapHdr, _argb)
        End Function
    End Structure

    Private Function NextClock() As Long
        _clock += 1
        Return _clock
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Invalidate()
        GC.SuppressFinalize(Me)
    End Sub

    Private NotInheritable Class D3D_BrushCacheEntry
        Public Sub New(brush As ID2D1SolidColorBrush, generation As Integer, lastUsed As Long)
            Me.Brush = brush
            Me.Generation = generation
            Me.LastUsed = lastUsed
        End Sub

        Public ReadOnly Property Brush As ID2D1SolidColorBrush
        Public ReadOnly Property Generation As Integer
        Public Property LastUsed As Long
        Public Property LruNode As LinkedListNode(Of D3D_BrushKey)
    End Class
End Class
