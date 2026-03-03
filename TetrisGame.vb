Option Strict On
Option Infer On
Imports VbPixelGameEngine
Imports System.MathF

Public NotInheritable Class TetrisGame
    Inherits PixelGameEngine

    Private Const CELL_SIZE As Integer = 30, BLANK_WIDTH As Integer = 8
    Private Const GRID_WIDTH As Integer = 10, GRID_HEIGHT As Integer = 20
    Private Const FPS As Integer = 60
    Private Const FALL_SPEED_BASE As Single = 0.5F
    Private Const LOCK_DELAY As Single = 0.5F

    Friend Const VIEWPORT_W As Integer = CELL_SIZE * (GRID_WIDTH + BLANK_WIDTH)
    Friend Const VIEWPORT_H As Integer = CELL_SIZE * GRID_HEIGHT

    Private ReadOnly m_grid(GRID_HEIGHT - 1, GRID_WIDTH - 1) As Pixel?
    Private m_currentPiece As BlockDetails
    Private m_currentPos As Vi2d
    Private m_currentRotation As Integer = 0
    Private m_nextPiece As BlockDetails
    Private m_holdPiece As BlockDetails? = Nothing
    Private m_canHold As Boolean = True
    Private m_score As Integer = 0, m_level As Integer = 1
    Private m_lines As Integer = 0
    Private m_fallSpeed As Single = FALL_SPEED_BASE
    Private m_lastFallTime As Single = 0.0F
    Private m_gameState As GameState = GameState.Title
    Private m_accumulatedTime As Single = 0.0F
    Private m_pocket As IEnumerator(Of Char)
    Private m_lockTimer As Single = 0.0F
    Private m_isPieceLocking As Boolean = False
    Private m_lastActionWasRotation As Boolean = False
    Private m_lastRotationTime As Single = 0.0F

    Private ReadOnly bgmMainTheme As New SoundPlayer("main_theme.mp3")
    Private ReadOnly sndGameOver As New SoundPlayer("game_over.wav")

    Public Sub New()
        AppName = "VBPGE Tetris Game"
    End Sub

    Friend Shared Sub Main()
        With New TetrisGame
            ' Start the game window
            If .Construct(VIEWPORT_W, VIEWPORT_H, fullScreen:=True) Then .Start()

            ' On close: dispose resources to avoid memory leaks
            .bgmMainTheme.Dispose()
            .sndGameOver.Dispose()
        End With
    End Sub

    Protected Overrides Function OnUserCreate() As Boolean
        m_nextPiece = GenerateTetramino()
        m_currentPiece = GenerateTetramino()
        m_currentPos = New Vi2d(GRID_WIDTH \ 2 - m_currentPiece.shape.GetLength(1) \ 2, 0)
        Return True
    End Function

    Protected Overrides Function OnUserUpdate(elapsedTime As Single) As Boolean
        m_accumulatedTime += elapsedTime

        ' Title screen
        If m_gameState = GameState.Title AndAlso GetKey(Key.ENTER).Pressed Then
            m_gameState = GameState.Playing
            bgmMainTheme.PlayLooping()
        ElseIf m_gameState = GameState.Playing AndAlso GetKey(Key.P).Pressed Then
            m_gameState = GameState.Paused
        ElseIf m_gameState = GameState.Paused AndAlso GetKey(Key.P).Pressed Then
            m_gameState = GameState.Playing
        End If

        ' Process input
        If GetKey(Key.LEFT).Pressed Then
            Move(-1)
            m_lastActionWasRotation = False
        End If
        If GetKey(Key.RIGHT).Pressed Then
            Move(1)
            m_lastActionWasRotation = False
        End If
        If GetKey(Key.DOWN).Pressed Then
            MoveDown()
            m_lastActionWasRotation = False
        End If
        If GetKey(Key.UP).Pressed OrElse GetKey(Key.X).Pressed Then
            Rotate(clockwise:=True)
            m_lastActionWasRotation = True
            m_lastRotationTime = m_accumulatedTime
        End If
        If GetKey(Key.Z).Pressed Then
            Rotate(clockwise:=False)
            m_lastActionWasRotation = True
            m_lastRotationTime = m_accumulatedTime
        End If
        If GetKey(Key.SPACE).Pressed Then HardDrop()
        If GetKey(Key.C).Pressed Then Hold()
        If m_gameState = GameState.GameOver AndAlso GetKey(Key.R).Pressed Then ResetGame()

        ' Automatic falling
        If m_accumulatedTime - m_lastFallTime > m_fallSpeed Then
            MoveDown()
            m_lastFallTime = m_accumulatedTime
        End If

        ' Drawing
        Clear(Presets.Black)
        DrawGrid()
        If {GameState.Playing, GameState.Paused}.Contains(m_gameState) Then DrawCurrentPiece()
        DrawHoldPiece()
        DrawNextPiece()
        DrawScoreAndLevel()
        If m_gameState <> GameState.Playing Then
            DrawOverlay()
            m_pocket = Nothing
        End If

        Return Not GetKey(Key.ESCAPE).Pressed
    End Function

    Private Function GenerateTetramino() As BlockDetails
        If m_pocket Is Nothing OrElse Not m_pocket.MoveNext() Then
            Dim blockTypes = {"I"c, "J"c, "L"c, "T"c, "S"c, "Z"c, "O"c}
            Call (New Random).Shuffle(blockTypes)
            If m_pocket IsNot Nothing Then m_pocket.Reset()
            m_pocket = blockTypes.ToList().GetEnumerator()
            m_pocket.MoveNext()
        End If

        Return BlockMap.GetValueOrDefault(m_pocket.Current)
    End Function

    Private ReadOnly Property CurrentShape As Integer(,)
        Get
            Dim shape As Integer(,) = m_currentPiece.shape
            For i As Integer = 1 To m_currentRotation Mod 4
                shape = RotateMatrix(shape)
            Next i
            Return shape
        End Get
    End Property

    Private Function CheckCollision(pos As Vi2d, shape As Integer(,)) As Boolean
        For y As Integer = 0 To UBound(shape, 1)
            For x As Integer = 0 To UBound(shape, 2)
                If shape(y, x) <> 0 Then
                    Dim gridX As Integer = pos.x + x, gridY As Integer = pos.y + y
                    If gridX < 0 OrElse gridX >= GRID_WIDTH OrElse gridY >= GRID_HEIGHT Then
                        Return False
                    End If
                    If gridY >= 0 AndAlso m_grid(gridY, gridX).HasValue Then Return False
                End If
            Next x
        Next y
        Return True
    End Function

    Private Sub Move(dx As Integer)
        If m_gameState <> GameState.Playing Then Exit Sub
        Dim updatedPos As New Vi2d(m_currentPos.x + dx, m_currentPos.y)
        If CheckCollision(updatedPos, CurrentShape) Then m_currentPos = updatedPos
    End Sub

    Private Sub MoveDown()
        If m_gameState <> GameState.Playing Then Exit Sub
        If GetKey(Key.DOWN).Pressed AndAlso Not m_isPieceLocking Then m_score += 1

        Dim updatedPos As New Vi2d(m_currentPos.x, m_currentPos.y + 1)
        If CheckCollision(updatedPos, CurrentShape) Then
            m_currentPos = updatedPos
            m_isPieceLocking = False
            m_lockTimer = 0.0F
        Else
            ' Start or continue lock delay
            If Not m_isPieceLocking Then
                m_isPieceLocking = True
                m_lockTimer = 0.0F
            Else
                m_lockTimer += m_accumulatedTime - m_lastFallTime
                If m_lockTimer >= LOCK_DELAY Then LockPiece()
            End If
        End If
    End Sub

    Private Sub Rotate(clockwise As Boolean)
        If m_gameState <> GameState.Playing Then Exit Sub
        Dim oldRotation = m_currentRotation
        m_currentRotation = (m_currentRotation + If(clockwise, 1, 3)) Mod 4
        Dim newShape = CurrentShape

        If Not CheckCollision(m_currentPos, newShape) Then
            For Each offset In m_currentPiece.wallKick
                Dim newPos = New Vi2d(m_currentPos.x + offset.x, m_currentPos.y + offset.y)
                If CheckCollision(newPos, newShape) Then
                    m_currentPos = newPos
                    Exit Sub
                End If
            Next offset
            m_currentRotation = oldRotation
        End If
    End Sub

    Private Sub HardDrop()
        If m_gameState <> GameState.Playing Then Exit Sub
        While CheckCollision(m_currentPos + New Vi2d(0, 1), CurrentShape)
            m_currentPos.y += 1
            m_score += 2
        End While
        LockPiece()
    End Sub

    Private Sub Hold()
        If m_gameState <> GameState.Playing OrElse Not m_canHold Then Exit Sub
        If Not m_holdPiece.HasValue Then
            m_holdPiece = m_currentPiece
            m_currentPiece = m_nextPiece
            m_nextPiece = GenerateTetramino()
        Else
            Dim temp = m_currentPiece
            m_currentPiece = m_holdPiece.Value
            m_holdPiece = temp
        End If
        m_currentPos = New Vi2d(GRID_WIDTH \ 2 - m_currentPiece.shape.GetLength(1) \ 2, 0)
        m_currentRotation = 0
        m_canHold = False
    End Sub

    Private Sub LockPiece()
        Dim stdTSpin As Boolean = False, miniTSpin As Boolean = False

        ' Check for T-Spin if last action was rotation and piece is T
        If m_currentPiece.type = "T"c AndAlso m_lastActionWasRotation Then
            ' Only consider recent rotations (within 0.5 seconds)
            If m_accumulatedTime - m_lastRotationTime < 0.5F Then
                stdTSpin = CheckStandardTSpin()
                miniTSpin = Not stdTSpin AndAlso CheckMiniTSpin()
            End If
        End If

        Dim shape As Integer(,) = CurrentShape
        For y As Integer = 0 To UBound(shape, 1)
            For x As Integer = 0 To UBound(shape, 2)
                If shape(y, x) <> 0 Then
                    Dim gridY As Integer = m_currentPos.y + y
                    Dim gridX As Integer = m_currentPos.x + x
                    If gridY >= 0 Then m_grid(gridY, gridX) = m_currentPiece.color
                End If
            Next x
        Next y

        Dim cleared = ClearLines()
        m_isPieceLocking = False
        m_lockTimer = 0.0F

        ' Handle T-Spin scoring before/after clearing lines
        If stdTSpin Then
            m_score += If(cleared > 0, 150 * cleared, 150)
        ElseIf miniTSpin Then
            m_score += If(cleared > 0, 100 * cleared, 100)
        End If

        m_currentPiece = m_nextPiece
        m_nextPiece = GenerateTetramino()
        m_currentPos = New Vi2d(GRID_WIDTH \ 2 - m_currentPiece.shape.GetLength(1) \ 2, 0)
        m_currentRotation = 0
        m_canHold = True

        ' Check game over
        If Not CheckCollision(m_currentPos, CurrentShape) Then
            sndGameOver.Play()
            m_gameState = GameState.GameOver
            bgmMainTheme.Stop()
        End If
    End Sub

    Private Function ClearLines() As Integer
        Dim linesCleared As Integer = 0
        For y As Integer = GRID_HEIGHT - 1 To 0 Step -1
            Dim lineComplete = True
            For x As Integer = 0 To GRID_WIDTH - 1
                If Not m_grid(y, x).HasValue Then
                    lineComplete = False
                    Exit For
                End If
            Next x

            If lineComplete Then
                linesCleared += 1
                m_lines += 1
                For newY As Integer = y To 1 Step -1
                    For x As Integer = 0 To GRID_WIDTH - 1
                        m_grid(newY, x) = m_grid(newY - 1, x)
                    Next x
                Next newY
                y += 1 ' Recheck the same index.
            End If
        Next y

        If linesCleared > 0 Then
            Dim scores As Integer() = {0, 100, 300, 500, 800}
            m_score += scores(linesCleared) * m_level
            m_level = m_lines \ 10 + 1
            m_fallSpeed = Max(0.1F, FALL_SPEED_BASE - (m_level - 1) * 0.05F)
        End If

        Return linesCleared
    End Function

    Private Function CheckStandardTSpin() As Boolean
        ' T-Spin detection based on corner occupancy
        Dim centerX = m_currentPos.x + 1
        Dim centerY = m_currentPos.y + 1
        Dim occupiedCorners = 0

        ' Check all four diagonal corners
        If IsOccupied(centerX - 1, centerY - 1) Then occupiedCorners += 1 ' Top-left
        If IsOccupied(centerX + 1, centerY - 1) Then occupiedCorners += 1 ' Top-right
        If IsOccupied(centerX - 1, centerY + 1) Then occupiedCorners += 1 ' Bottom-left
        If IsOccupied(centerX + 1, centerY + 1) Then occupiedCorners += 1 ' Bottom-right

        ' T-Spin requires at least 3 occupied corners
        Return occupiedCorners >= 3
    End Function

    Private Function CheckMiniTSpin() As Boolean
        ' Mini T-Spin detection (only checks front corners)
        Dim centerX = m_currentPos.x + 1
        Dim centerY = m_currentPos.y + 1
        Dim occupiedFrontCorners = 0

        ' Determine orientation based on rotation
        Select Case m_currentRotation Mod 4
            Case 0 ' Facing up (checking bottom-left and bottom-right)
                If IsOccupied(centerX - 1, centerY + 1) Then occupiedFrontCorners += 1
                If IsOccupied(centerX + 1, centerY + 1) Then occupiedFrontCorners += 1
            Case 1 ' Facing right (checking top-left and bottom-left)
                If IsOccupied(centerX - 1, centerY - 1) Then occupiedFrontCorners += 1
                If IsOccupied(centerX - 1, centerY + 1) Then occupiedFrontCorners += 1
            Case 2 ' Facing down (checking top-left and top-right)
                If IsOccupied(centerX - 1, centerY - 1) Then occupiedFrontCorners += 1
                If IsOccupied(centerX + 1, centerY - 1) Then occupiedFrontCorners += 1
            Case 3 ' Facing left (checking top-right and bottom-right)
                If IsOccupied(centerX + 1, centerY - 1) Then occupiedFrontCorners += 1
                If IsOccupied(centerX + 1, centerY + 1) Then occupiedFrontCorners += 1
        End Select

        ' Mini T-Spin requires both front corners occupied
        Return occupiedFrontCorners = 2
    End Function

    Private Function IsOccupied(x As Integer, y As Integer) As Boolean
        ' Check if position is occupied (by wall or block)
        ' Note: Wall or above grid counts as occupied, whereas below grid doesn't.
        If x < 0 OrElse x >= GRID_WIDTH OrElse y < 0 Then
            Return True
        ElseIf y >= GRID_HEIGHT Then
            Return False
        Else
            Return m_grid(y, x).HasValue
        End If
    End Function

    Private Sub ResetGame()
        Array.Clear(m_grid, 0, m_grid.Length)
        m_currentPiece = GenerateTetramino()
        m_nextPiece = GenerateTetramino()
        m_holdPiece = Nothing
        m_currentPos = New Vi2d(GRID_WIDTH \ 2 - m_currentPiece.shape.GetLength(1) \ 2, 0)
        m_currentRotation = 0
        m_score = 0
        m_level = 1
        m_lines = 0
        m_fallSpeed = FALL_SPEED_BASE
        m_gameState = GameState.Playing
        bgmMainTheme.PlayLooping()
        m_canHold = True
    End Sub

    Private Sub DrawGrid()
        ' Draw grid background
        FillRect(0, 0, GRID_WIDTH * CELL_SIZE, GRID_HEIGHT * CELL_SIZE, Presets.Black)

        ' Draw grid lines in dark-grey, and fill the dark-red bar at the top
        For x As Integer = 0 To GRID_WIDTH * CELL_SIZE Step CELL_SIZE
            DrawLine(x, 0, x, GRID_HEIGHT * CELL_SIZE, Presets.DarkGrey)
        Next x
        For y As Integer = 0 To GRID_HEIGHT * CELL_SIZE Step CELL_SIZE
            DrawLine(0, y, GRID_WIDTH * CELL_SIZE, y, Presets.DarkGrey)
        Next y
        FillRect(0, CELL_SIZE, GRID_WIDTH * CELL_SIZE, 3, Presets.DarkRed)

        ' Draw filled cells
        For y As Integer = 0 To GRID_HEIGHT - 1
            For x As Integer = 0 To GRID_WIDTH - 1
                If m_grid(y, x).HasValue Then
                    FillRect(x * CELL_SIZE, y * CELL_SIZE, CELL_SIZE, CELL_SIZE,
                             m_grid(y, x).Value)
                    DrawRect(x * CELL_SIZE, y * CELL_SIZE, CELL_SIZE, CELL_SIZE,
                             Presets.Gray)
                End If
            Next x
        Next y
    End Sub

    Private Sub DrawCurrentPiece()
        Dim shape As Integer(,) = CurrentShape
        For y As Integer = 0 To UBound(shape, 1)
            For x As Integer = 0 To UBound(shape, 2)
                If shape(y, x) <> 0 Then
                    Dim gridY = m_currentPos.y + y
                    Dim gridX = m_currentPos.x + x
                    If gridY >= 0 Then
                        FillRect(gridX * CELL_SIZE, gridY * CELL_SIZE,
                                 CELL_SIZE, CELL_SIZE, m_currentPiece.color)
                        DrawRect(gridX * CELL_SIZE, gridY * CELL_SIZE,
                                 CELL_SIZE, CELL_SIZE, Presets.Gray)
                    End If
                End If
            Next x
        Next y
    End Sub

    Private Sub DrawHoldPiece()
        Const HOLD_X As Integer = GRID_WIDTH * CELL_SIZE + 10, HOLD_Y As Integer = 40
        FillRect(HOLD_X, HOLD_Y, CELL_SIZE * 4 + 20, CELL_SIZE * 4 + 20, Presets.DarkGrey)
        FillRect(HOLD_X + 5, HOLD_Y + 5, CELL_SIZE * 4 + 10, CELL_SIZE * 4 + 10, Presets.Black)
        DrawString(HOLD_X + 15, 150, "HOLD", Presets.DarkGrey, 2)

        If m_holdPiece.HasValue Then
            Dim piece = m_holdPiece.Value.shape
            Dim offsetX = (4 - piece.GetLength(1)) \ 2
            Dim offsetY = (4 - piece.GetLength(0)) \ 2
            For y As Integer = 0 To UBound(piece, 1)
                For x As Integer = 0 To UBound(piece, 2)
                    If piece(y, x) <> 0 Then
                        Dim color = If(m_canHold, m_holdPiece.Value.color, Presets.DarkGrey)
                        FillRect(HOLD_X + 5 + (x + offsetX) * CELL_SIZE,
                                 HOLD_Y + 5 + (y + offsetY) * CELL_SIZE,
                                 CELL_SIZE, CELL_SIZE, color)
                        DrawRect(HOLD_X + 5 + (x + offsetX) * CELL_SIZE,
                                 HOLD_Y + 5 + (y + offsetY) * CELL_SIZE,
                                 CELL_SIZE, CELL_SIZE, Presets.Gray)
                    End If
                Next x
            Next y
        End If
    End Sub

    Private Sub DrawNextPiece()
        Const NEXT_X As Integer = GRID_WIDTH * CELL_SIZE + 10, NEXT_Y As Integer = 190
        FillRect(NEXT_X, NEXT_Y, CELL_SIZE * 4 + 20, CELL_SIZE * 4 + 20, Presets.DarkGrey)
        FillRect(NEXT_X + 5, NEXT_Y + 5, CELL_SIZE * 4 + 10, CELL_SIZE * 4 + 10, Presets.Black)
        DrawString(NEXT_X + 15, 300, "NEXT", Presets.DarkGrey, 2)

        Dim piece As Integer(,) = m_nextPiece.shape
        Dim offsetX = (4 - piece.GetLength(1)) \ 2
        Dim offsetY = (4 - piece.GetLength(0)) \ 2
        For y As Integer = 0 To UBound(piece, 1)
            For x As Integer = 0 To UBound(piece, 2)
                If piece(y, x) <> 0 Then
                    FillRect(NEXT_X + 5 + (x + offsetX) * CELL_SIZE,
                             NEXT_Y + 5 + (y + offsetY) * CELL_SIZE,
                             CELL_SIZE, CELL_SIZE, m_nextPiece.color)
                    DrawRect(NEXT_X + 5 + (x + offsetX) * CELL_SIZE,
                             NEXT_Y + 5 + (y + offsetY) * CELL_SIZE,
                             CELL_SIZE, CELL_SIZE, Presets.Gray)
                End If
            Next x
        Next y
    End Sub

    Private Sub DrawScoreAndLevel()
        Const INFO_X As Integer = GRID_WIDTH * CELL_SIZE + 20
        DrawString(INFO_X, 380, "- CURRENT SCORE -", Presets.White)
        DrawString(INFO_X, 400, m_score.ToString().PadLeft(6, "0"c), Presets.White, 3)
        DrawString(INFO_X, 450, "LEVEL", Presets.Beige)
        DrawString(INFO_X, 470, m_level.ToString().PadLeft(2, "0"c), Presets.Beige, 3)
        DrawString(INFO_X + 70, 450, "- LINES -", Presets.White)
        DrawString(INFO_X + 70, 470, m_lines.ToString().PadLeft(3, "0"c), Presets.White, 3)
    End Sub

    Private Sub DrawOverlay()
        Const CENTER_X = GRID_WIDTH * CELL_SIZE \ 2, CENTER_Y = GRID_HEIGHT * CELL_SIZE \ 2
        FillRect(20, 225, CELL_SIZE * 8 + 30, CELL_SIZE * 4 + 30, Presets.Black)
        Select Case m_gameState
            Case GameState.Title
                DrawString(CENTER_X - 110, CENTER_Y - 50, "- TETRIS -", Presets.Green, 3)
                DrawString(CENTER_X - 115, CENTER_Y, "PUSH ENTER", Presets.White, 3)
                DrawString(CENTER_X - 100, CENTER_Y + 30, "TO BEGIN", Presets.White, 3)
            Case GameState.Paused
                DrawString(CENTER_X - 70, CENTER_Y - 50, "PAUSED", Presets.Yellow, 3)
                DrawString(CENTER_X - 100, CENTER_Y, "PRESS ""P""", Presets.White, 3)
                DrawString(CENTER_X - 100, CENTER_Y + 30, "TO RESUME", Presets.White, 3)
            Case GameState.GameOver
                DrawString(CENTER_X - 100, CENTER_Y - 50, "GAME OVER", Presets.Red, 3)
                DrawString(CENTER_X - 100, CENTER_Y, "PRESS ""R""", Presets.White, 3)
                DrawString(CENTER_X - 110, CENTER_Y + 30, "TO RESTART", Presets.White, 3)
        End Select
    End Sub
End Class