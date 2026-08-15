Imports System.Numerics
Imports System.Threading

''' <summary>
''' RSA repeated-squaring 顺序工作量证明计算器。
''' 挑战参数和结果使用固定长度无符号大端 base64url 编码。
''' </summary>
Public NotInheritable Class RsaRepeatedSquaring

    Private Sub New()
    End Sub

    Public Shared Function Solve(modulus As String,
                                 baseValue As String,
                                 iterations As Integer,
                                 Optional byteLength As Integer = 384,
                                 Optional cancellationToken As CancellationToken = Nothing) As String
        If String.IsNullOrWhiteSpace(modulus) Then Throw New ArgumentException("modulus 不能为空。", NameOf(modulus))
        If String.IsNullOrWhiteSpace(baseValue) Then Throw New ArgumentException("base 不能为空。", NameOf(baseValue))
        If iterations <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(iterations), "iterations 必须大于 0。")
        If byteLength <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(byteLength), "byteLength 必须大于 0。")

        Dim modulusBytes As Byte() = DecodeBase64Url(modulus)
        Dim baseBytes As Byte() = DecodeBase64Url(baseValue)
        If modulusBytes.Length <> byteLength OrElse baseBytes.Length <> byteLength Then
            Throw New FormatException($"modulus 和 base 必须编码为 {byteLength} 字节无符号大端整数。")
        End If

        Dim modulusInteger As BigInteger = New BigInteger(modulusBytes, isUnsigned:=True, isBigEndian:=True)
        Dim value As BigInteger = New BigInteger(baseBytes, isUnsigned:=True, isBigEndian:=True)
        If modulusInteger <= BigInteger.One Then Throw New FormatException("modulus 必须大于 1。")
        If value <= BigInteger.Zero OrElse value >= modulusInteger Then Throw New FormatException("base 必须满足 0 < base < modulus。")

        For index As Integer = 1 To iterations
            cancellationToken.ThrowIfCancellationRequested()
            value = (value * value) Mod modulusInteger
        Next

        Return EncodeFixedBase64Url(value, byteLength)
    End Function

    Private Shared Function DecodeBase64Url(value As String) As Byte()
        Dim normalized As String = value.Replace("-"c, "+"c).Replace("_"c, "/"c)
        Select Case normalized.Length Mod 4
            Case 0
            Case 2 : normalized &= "=="
            Case 3 : normalized &= "="
            Case Else : Throw New FormatException("base64url 字符串长度无效。")
        End Select

        Try
            Return Convert.FromBase64String(normalized)
        Catch ex As FormatException
            Throw New FormatException("base64url 字符串无效。", ex)
        End Try
    End Function

    Private Shared Function EncodeFixedBase64Url(value As BigInteger, byteLength As Integer) As String
        Dim raw As Byte() = value.ToByteArray(isUnsigned:=True, isBigEndian:=True)
        If raw.Length > byteLength Then Throw New FormatException("计算结果超出固定整数长度。")

        Dim padded As Byte() = New Byte(byteLength - 1) {}
        Buffer.BlockCopy(raw, 0, padded, byteLength - raw.Length, raw.Length)
        Return Convert.ToBase64String(padded).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

End Class
