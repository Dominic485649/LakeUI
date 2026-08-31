''' <summary>
''' 进程级渲染缓存预算协调器。GPU 与 CPU 缓存分别注册 owner，
''' 由 owner 自己负责释放最旧条目，协调器只做总量统计与全局 LRU 调度。
''' </summary>
Friend Interface D3D_IRenderCacheOwner
    ReadOnly Property CacheBytes As Long
    ReadOnly Property OldestUseTick As Long
    Function TrimOldest() As Boolean
    Sub ReleaseAll()
End Interface

Friend NotInheritable Class D3D_RenderCacheBudgetCoordinator
    Private ReadOnly _lock As New Object()
    Private ReadOnly _trimLock As New Object()
    Private ReadOnly _owners As New List(Of WeakReference(Of D3D_IRenderCacheOwner))()
    Private _trimActive As Boolean

    Friend Sub Register(owner As D3D_IRenderCacheOwner)
        If owner Is Nothing Then Return
        SyncLock _lock
            CompactNoLock()
            For Each wr In _owners
                Dim existing As D3D_IRenderCacheOwner = Nothing
                If wr.TryGetTarget(existing) AndAlso ReferenceEquals(existing, owner) Then Return
            Next
            _owners.Add(New WeakReference(Of D3D_IRenderCacheOwner)(owner))
        End SyncLock
    End Sub

    Friend Sub TrimToBudget(budget As Long,
                            protectedOwner As D3D_IRenderCacheOwner,
                            evictionCallback As Action)
        D3D_RenderDiagnostics.BudgetScan()
        budget = Math.Max(0L, budget)

        SyncLock _trimLock
            ' Evicting a surface can finish its frame-use scope, which may ask
            ' the coordinator to trim again on the same UI thread. Do not recurse
            ' into the coordinator while the outer eviction pass is still active.
            If _trimActive Then Return
            _trimActive = True
            Try
            Dim failedOwners As New HashSet(Of D3D_IRenderCacheOwner)(ReferenceEqualityComparer.Instance)
            Dim guard As Integer = 0
            Do
                Dim total As Long = 0
                Dim oldest As D3D_IRenderCacheOwner = Nothing
                Dim oldestTick As Long = Long.MaxValue

                For Each owner In SnapshotOwners()
                    Dim bytes As Long
                    Try
                        bytes = Math.Max(0L, owner.CacheBytes)
                    Catch
                        failedOwners.Add(owner)
                        Continue For
                    End Try
                    total = SaturatingAdd(total, bytes)
                    If bytes <= 0 OrElse ReferenceEquals(owner, protectedOwner) OrElse failedOwners.Contains(owner) Then Continue For

                    Dim tick As Long
                    Try
                        tick = owner.OldestUseTick
                    Catch
                        failedOwners.Add(owner)
                        Continue For
                    End Try
                    If tick < oldestTick Then
                        oldestTick = tick
                        oldest = owner
                    End If
                Next

                If total <= budget OrElse oldest Is Nothing Then Exit Do

                Dim trimmed As Boolean
                Try
                    trimmed = oldest.TrimOldest()
                Catch
                    trimmed = False
                End Try
                If Not trimmed Then
                    ' 正在绘制或后台处理中的 owner 暂时不可回收；本轮跳过它，
                    ' 继续处理其他全局 LRU 候选项。
                    failedOwners.Add(oldest)
                    Continue Do
                End If

                evictionCallback?.Invoke()
                guard += 1
            Loop While guard < 4096
            Finally
                _trimActive = False
            End Try
        End SyncLock
    End Sub

    Friend Sub ReleaseAll()
        SyncLock _trimLock
            Dim owners As List(Of D3D_IRenderCacheOwner) = SnapshotOwners()
            For Each owner In owners
                Try : owner.ReleaseAll() : Catch : End Try
            Next
        End SyncLock
    End Sub

    Private Function SnapshotOwners() As List(Of D3D_IRenderCacheOwner)
        Dim result As New List(Of D3D_IRenderCacheOwner)()
        SyncLock _lock
            CompactNoLock()
            For Each wr In _owners
                Dim owner As D3D_IRenderCacheOwner = Nothing
                If wr.TryGetTarget(owner) AndAlso owner IsNot Nothing Then result.Add(owner)
            Next
        End SyncLock
        Return result
    End Function

    Friend Function TotalCacheBytes() As Long
        Dim total As Long
        For Each owner In SnapshotOwners()
            Try
                total = SaturatingAdd(total, Math.Max(0L, owner.CacheBytes))
            Catch
            End Try
        Next
        Return total
    End Function

    Private Shared Function SaturatingAdd(current As Long, value As Long) As Long
        If value <= 0 Then Return current
        If current >= Long.MaxValue - value Then Return Long.MaxValue
        Return current + value
    End Function

    Private Sub CompactNoLock()
        For i As Integer = _owners.Count - 1 To 0 Step -1
            Dim owner As D3D_IRenderCacheOwner = Nothing
            If Not _owners(i).TryGetTarget(owner) OrElse owner Is Nothing Then _owners.RemoveAt(i)
        Next
    End Sub
End Class

Friend Module D3D_GpuCache
    Private ReadOnly _coordinator As New D3D_RenderCacheBudgetCoordinator()
    Private _tick As Long

    Friend Function NextTick() As Long
        Return Threading.Interlocked.Increment(_tick)
    End Function

    Friend Sub Register(owner As D3D_IRenderCacheOwner)
        _coordinator.Register(owner)
    End Sub

    Friend Sub TrimToBudget(Optional protectedOwner As D3D_IRenderCacheOwner = Nothing)
        _coordinator.TrimToBudget(GlobalOptions.GpuCacheBudgetBytes,
                                  protectedOwner,
                                  AddressOf D3D_RenderDiagnostics.CacheEviction)
    End Sub

    Friend Function TotalCacheBytes() As Long
        Return _coordinator.TotalCacheBytes()
    End Function

    Friend Sub ReleaseAll()
        _coordinator.ReleaseAll()
    End Sub
End Module

Friend Module D3D_CpuCache
    Private ReadOnly _coordinator As New D3D_RenderCacheBudgetCoordinator()
    Private _tick As Long

    Friend Function NextTick() As Long
        Return Threading.Interlocked.Increment(_tick)
    End Function

    Friend Sub Register(owner As D3D_IRenderCacheOwner)
        _coordinator.Register(owner)
    End Sub

    Friend Sub TrimToBudget(Optional protectedOwner As D3D_IRenderCacheOwner = Nothing)
        _coordinator.TrimToBudget(GlobalOptions.CpuCacheBudgetBytes,
                                  protectedOwner,
                                  AddressOf D3D_RenderDiagnostics.CacheEviction)
    End Sub

    Friend Function TotalCacheBytes() As Long
        Return _coordinator.TotalCacheBytes()
    End Function

    Friend Sub ReleaseAll()
        _coordinator.ReleaseAll()
    End Sub
End Module
