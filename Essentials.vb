Option Strict On
Option Infer On
Imports NAudio.Wave
Imports VbPixelGameEngine

Public Enum GameState As Byte
    Title = 0
    Playing = 1
    Paused = 2
    GameOver = 3
End Enum

Public Structure BlockDetails
    Public ReadOnly type As Char
    Public ReadOnly color As Pixel, shape As Integer(,), wallKick As Vi2d()

    Public Sub New(type As Char, color As Pixel, shape As Integer(,), wallKick As Vi2d())
        Me.type = type
        Me.color = color
        Me.shape = shape
        Me.wallKick = wallKick
    End Sub
End Structure

Public Module Tetromino
    Public ReadOnly Property BlockMap As New Dictionary(Of Char, BlockDetails) From {
        {"I"c, New BlockDetails("I"c, Presets.Cyan, shape:={{1, 1, 1, 1}}, wallKick:={
            New Vi2d(-1, 0), New Vi2d(-1, -1), New Vi2d(0, 2), New Vi2d(-1, 2)})},
        {"J"c, New BlockDetails("J"c, Presets.Blue, shape:={{1, 0, 0}, {1, 1, 1}}, wallKick:={
            New Vi2d(0, 0), New Vi2d(-1, 0), New Vi2d(-1, 1), New Vi2d(0, -2), New Vi2d(-1, -2)})},
        {"L"c, New BlockDetails("L"c, Presets.Orange, shape:={{0, 0, 1}, {1, 1, 1}}, wallKick:={
            New Vi2d(0, 0), New Vi2d(1, 0), New Vi2d(1, -1), New Vi2d(0, 2), New Vi2d(1, 2)})},
        {"T"c, New BlockDetails("T"c, Presets.Purple, shape:={{1, 1, 1}, {0, 1, 0}}, wallKick:={
            New Vi2d(0, 0), New Vi2d(-1, 0), New Vi2d(1, 0), New Vi2d(-1, 1), New Vi2d(1, -1)})},
        {"S"c, New BlockDetails("S"c, Presets.Green, shape:={{0, 1, 1}, {1, 1, 0}}, wallKick:={
            New Vi2d(0, 0), New Vi2d(1, 0), New Vi2d(-1, 0), New Vi2d(1, 1), New Vi2d(-1, -1)})},
        {"Z"c, New BlockDetails("Z"c, Presets.Red, shape:={{1, 1, 0}, {0, 1, 1}}, wallKick:={
            New Vi2d(0, 0), New Vi2d(1, 0), New Vi2d(-1, 0), New Vi2d(1, 1), New Vi2d(-1, -1)})},
        {"O"c, New BlockDetails("O"c, Presets.Yellow, {{1, 1}, {1, 1}}, Array.Empty(Of Vi2d)())}
    }

    Public Function RotateMatrix(matrix As Integer(,)) As Integer(,)
        Dim rowMaxIdx = UBound(matrix, 1), colMaxIdx = UBound(matrix, 2)
        Dim rotated(colMaxIdx, rowMaxIdx) As Integer
        For row As Integer = 0 To rowMaxIdx
            For col As Integer = 0 To colMaxIdx
                rotated(colMaxIdx - col, row) = matrix(row, col)
            Next col
        Next row
        Return rotated
    End Function
End Module

Public Class SoundPlayer
    Implements IDisposable

    Private ReadOnly reader As AudioFileReader
    Private ReadOnly waveOut As WaveOutEvent
    Private isLooping As Boolean = False
    Private disposedValue As Boolean

    Public Sub New(filename As String)
        reader = New AudioFileReader(filename)
        waveOut = New WaveOutEvent
        waveOut.Init(reader)

        AddHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
    End Sub

    Public Sub Play()
        If waveOut IsNot Nothing Then
            isLooping = False
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub PlayLooping()
        If waveOut IsNot Nothing Then
            isLooping = True
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub [Stop]()
        If waveOut IsNot Nothing Then
            isLooping = False
            waveOut.Stop()
        End If
    End Sub

    Public Sub OnPlaybackStopped(sender As Object, e As StoppedEventArgs)
        If isLooping AndAlso waveOut IsNot Nothing Then
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                ' TODO: dispose managed state (managed objects)
                If waveOut IsNot Nothing Then
                    RemoveHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
                End If
                waveOut?.Dispose()
                reader?.Dispose()
            End If

            ' TODO: free unmanaged resources (unmanaged objects) and override finalizer
            ' TODO: set large fields to null
            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ' Do not change this code. Put cleanup code in 'Dispose(disposing As Boolean)' method
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
End Class
