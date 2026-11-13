' =============================================================================
' Spiegelwerk - Live-Test B54/B55 für den Blech-Ersatzweg (Schritt S7)
'
' B54: Nimmt SOLIDWORKS eine Spiegelung (Determinante -1) als Transformation
'      einer Körperkopie an (IBody2.Copy2 + IBody2.ApplyTransform)?
'      Geprüft in zwei Schreibweisen: direkt und als (-M, Skalierung -1).
' B55: Wird der gespiegelte Körper mit IPartDoc.CreateFeatureFromBody3 ein
'      eigener, gültiger Körper ohne externe Bezüge - im selben Teil (getrennt
'      vom Original und am Original anliegend) und in einem neuen Teil?
'      Ist die temporäre Kopie danach noch nutzbar?
'
' Ablauf:
'  1. Ein Testteil öffnen, am besten ein einfaches Blechteil mit einem Körper
'     (z. B. Platte 100 x 50 x 2 mit einer Biegung), gefaltet, Rollback-Leiste
'     am Ende. Das Makro fügt dem Teil Körper hinzu und legt ein neues Teil
'     an: beides NICHT speichern.
'  2. Extras > Makro > Neu..., Namen vergeben, speichern. Im VBA-Editor den
'     ganzen vorhandenen Text durch diesen ersetzen.
'  3. Cursor in "Sub main" setzen und F5 drücken. Am Ende öffnet sich der
'     Editor mit dem Protokoll. Den ganzen Text bitte zurückschicken.
'  4. Testteil und neues Teil ohne Speichern schließen.
'
' Das Makro benutzt keine Spiegel-Funktion von SOLIDWORKS (kein
' Spiegeln-Feature, kein MirrorPart), nur eine Körpertransformation.
' Alle Längen intern in Metern, im Protokoll in mm.
' =============================================================================
Option Explicit

Private Const DOC_PART As Long = 1            ' swDocPART
Private Const SOLID_BODY As Long = 0          ' swSolidBody
Private Const BODY_CHECK As Long = 1          ' swCreateFeatureBodyCheck
Private Const TEMPLATE_PART As Long = 8       ' swDefaultTemplatePart
Private Const TOL_POINT As Double = 0.000001  ' 1 Mikrometer für Punkte
Private Const TOL_REL As Double = 0.00001     ' relativ für Volumen und Schwerpunkt

Private swApp As SldWorks.SldWorks
Private mathUtil As SldWorks.MathUtility
Private logText As String
Private logPath As String

Sub main()
    Set swApp = Application.SldWorks
    Set mathUtil = swApp.GetMathUtility
    logText = ""

    Dim model As SldWorks.ModelDoc2
    Set model = swApp.ActiveDoc
    If model Is Nothing Then
        MsgBox "Bitte zuerst ein Testteil öffnen.", vbExclamation
        Exit Sub
    End If
    If model.GetType <> DOC_PART Then
        MsgBox "Das aktive Dokument ist kein Teil.", vbExclamation
        Exit Sub
    End If
    Dim part As SldWorks.PartDoc
    Set part = model

    Dim body As SldWorks.Body2
    Set body = FirstSolid(part)
    If body Is Nothing Then
        MsgBox "Das Teil hat keinen sichtbaren Volumenkörper.", vbExclamation
        Exit Sub
    End If

    On Error GoTo Failed

    Dim bodyName As String
    bodyName = body.Name
    Note "Spiegelwerk Live-Test B54/B55"
    Note "SOLIDWORKS-Version: " & swApp.RevisionNumber
    Note "Teil: " & model.GetTitle & ", Volumenkörper: " & SolidCount(part)
    If body.IsSheetMetal Then
        Note "Getesteter Körper: " & bodyName & " (Blech)"
    Else
        Note "Getesteter Körper: " & bodyName & " (kein Blech)"
    End If

    Dim mp0 As Variant, pts0 As Variant
    mp0 = body.GetMassProperties(1#)
    If IsEmpty(mp0) Then
        Note "Abbruch: Masseeigenschaften des Originals nicht lesbar."
        GoTo Done
    End If
    pts0 = VertexPoints(body)
    Note "Original: Volumen " & Vol(mp0(3)) & ", Schwerpunkt " & Pt(mp0(0), mp0(1), mp0(2)) & _
        ", Eckpunkte " & PointCount(pts0) & ", Fehler laut Check3: " & Faults(body)

    Dim xMin As Double, xMax As Double
    ExtentX body, xMin, xMax
    Dim cFar As Double, cTouch As Double
    cTouch = xMin
    cFar = xMin - 0.5 * (xMax - xMin) - 0.01
    Note "Spiegelebenen senkrecht zu X: getrennt bei x = " & Mm(cFar) & ", anliegend bei x = " & Mm(cTouch)

    ' ---- B54 ---------------------------------------------------------------
    Note ""
    Note "=== B54: Spiegelung als Transformation einer Körperkopie ==="
    Dim okDirect As Boolean, okNeg As Boolean
    okDirect = TestReflection(body, "direkt: Matrix mit Determinante -1, Skalierung 1", False, cFar, mp0, pts0)
    okNeg = TestReflection(body, "als (-M, Skalierung -1)", True, cFar, mp0, pts0)

    ' ---- B55 ---------------------------------------------------------------
    Note ""
    Note "=== B55: Gespiegelten Körper als eigenen Körper einfügen ==="
    Dim neg As Boolean
    neg = okNeg And Not okDirect
    If okDirect Or okNeg Then
        Note "Schreibweise: " & IIf(neg, "(-M, Skalierung -1)", "direkt")
    Else
        Note "Schreibweise: direkt (B54 nicht bestanden, B55 nur zur Information)"
    End If
    Dim s1 As String, s2 As String, s3 As String
    Dim r1 As Boolean, r2 As Boolean, r3 As Boolean
    r1 = TestInsert(part, bodyName, "selbes Teil, getrennt vom Original", cFar, neg, mp0, pts0, s1)
    r2 = TestInsert(part, bodyName, "selbes Teil, am Original anliegend", cTouch, neg, mp0, pts0, s2)
    r3 = TestNewPart(part, bodyName, cFar, neg, mp0, pts0, s3)

    Note ""
    Note "=== Ergebnis ==="
    Note "B54 direkt:                  " & Verdict(okDirect)
    Note "B54 (-M, Skalierung -1):     " & Verdict(okNeg)
    Note "B55 selbes Teil, getrennt:   " & Verdict(r1)
    Note "B55 selbes Teil, anliegend:  " & Verdict(r2)
    Note "B55 neues Teil:              " & Verdict(r3)
    Note "Temporäre Kopie nach dem Einfügen: " & s1 & " / " & s2 & " / " & s3
    GoTo Done

Failed:
    Note ""
    Note "ABBRUCH: Laufzeitfehler " & Err.Number & ": " & Err.Description
    Resume Done
Done:
    On Error Resume Next
    SaveLog
    MsgBox "Test fertig. Das Protokoll steht in" & vbCrLf & logPath & vbCrLf & _
        "Bitte den ganzen Text zurückschicken und beide Teile ohne Speichern schließen.", vbInformation
End Sub

' ---- Tests ------------------------------------------------------------------

Private Function TestReflection(ByVal src As SldWorks.Body2, ByVal label As String, ByVal neg As Boolean, _
        ByVal c As Double, ByVal mp0 As Variant, ByVal pts0 As Variant) As Boolean
    Note "- " & label
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, c, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim geomOk As Boolean
    geomOk = CheckMirrored(cp, c, mp0, pts0)
    TestReflection = applied And geomOk
    Note "  => " & Verdict(TestReflection)
End Function

Private Function TestInsert(ByVal part As SldWorks.PartDoc, ByVal bodyName As String, ByVal label As String, _
        ByVal c As Double, ByVal neg As Boolean, ByVal mp0 As Variant, ByVal pts0 As Variant, _
        copyInfo As String) As Boolean
    Note "- " & label
    copyInfo = "nicht geprüft"
    Dim src As SldWorks.Body2
    Set src = FindSolid(part, bodyName)
    If src Is Nothing Then
        Note "  Original-Körper " & bodyName & " nicht mehr gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, c, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim namesBefore As Collection
    Set namesBefore = SolidNames(part)
    Dim feat As SldWorks.Feature
    Set feat = InsertBody(part, cp)
    copyInfo = CopyState(cp)
    Note "  Temporäre Kopie danach: " & copyInfo
    If feat Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim countAfter As Long
    countAfter = SolidCount(part)
    Dim nb As SldWorks.Body2
    Set nb = NewSolid(part, namesBefore)
    Dim separate As Boolean
    separate = (countAfter = namesBefore.Count + 1) And Not nb Is Nothing
    If separate Then
        Note "  Volumenkörper vorher " & namesBefore.Count & ", nachher " & countAfter & _
            ": eigener Körper " & nb.Name & IIf(nb.IsSheetMetal, " (Blech)", " (kein Blech)")
    Else
        Note "  Volumenkörper vorher " & namesBefore.Count & ", nachher " & countAfter & ": KEIN eigener Körper"
    End If

    Dim geomOk As Boolean
    If Not nb Is Nothing Then geomOk = CheckMirrored(nb, c, mp0, pts0)
    Dim ext As Long
    ext = feat.ListExternalFileReferencesCount
    Note "  Externe Bezüge des neuen Features: " & ext
    Dim origOk As Boolean
    origOk = OriginalUnchanged(part, bodyName, mp0)

    TestInsert = separate And geomOk And (ext = 0) And origOk
    Note "  => " & Verdict(TestInsert)
End Function

Private Function TestNewPart(ByVal part As SldWorks.PartDoc, ByVal bodyName As String, ByVal c As Double, _
        ByVal neg As Boolean, ByVal mp0 As Variant, ByVal pts0 As Variant, copyInfo As String) As Boolean
    Note "- neues Teil (Kopie im Testteil gemacht, im neuen Teil eingefügt)"
    copyInfo = "nicht geprüft"
    Dim tpl As String
    tpl = swApp.GetUserPreferenceStringValue(TEMPLATE_PART)
    Note "  Vorlage: " & tpl
    If tpl = "" Then
        Note "  Keine Standardvorlage für Teile eingestellt: übersprungen"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim src As SldWorks.Body2
    Set src = FindSolid(part, bodyName)
    If src Is Nothing Then
        Note "  Original-Körper " & bodyName & " nicht mehr gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, c, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim doc As SldWorks.ModelDoc2
    On Error Resume Next
    Set doc = swApp.NewDocument(tpl, 0, 0#, 0#)
    If Err.Number <> 0 Then Note "  NewDocument: Laufzeitfehler " & ErrText()
    On Error GoTo 0
    If doc Is Nothing Then
        Note "  Neues Teil ließ sich nicht anlegen"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim np As SldWorks.PartDoc
    Set np = doc

    Dim feat As SldWorks.Feature
    Set feat = InsertBody(np, cp)
    copyInfo = CopyState(cp)
    Note "  Temporäre Kopie danach: " & copyInfo
    If feat Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim n As Long
    n = SolidCount(np)
    Note "  Volumenkörper im neuen Teil: " & n
    Dim nb As SldWorks.Body2
    Set nb = FirstSolid(np)
    Dim geomOk As Boolean
    If Not nb Is Nothing Then geomOk = CheckMirrored(nb, c, mp0, pts0)
    Dim ext As Long
    ext = feat.ListExternalFileReferencesCount
    Note "  Externe Bezüge des neuen Features: " & ext

    TestNewPart = (n = 1) And geomOk And (ext = 0)
    Note "  => " & Verdict(TestNewPart)
End Function

' ---- Spiegeln, Einfügen, Vergleichen ----------------------------------------

' Reflection in the plane x = c: (x, y, z) -> (2c - x, y, z).
' Array layout of IMathUtility.CreateTransform: 0-8 rotation, 9-11 translation,
' 12 scale, 13-15 unused. Diagonal matrices, so row/column order does not matter.
Private Function ReflectX(ByVal c As Double, ByVal neg As Boolean) As Variant
    Dim a(15) As Double
    If neg Then
        a(0) = 1#: a(4) = -1#: a(8) = -1#: a(12) = -1#
    Else
        a(0) = -1#: a(4) = 1#: a(8) = 1#: a(12) = 1#
    End If
    a(9) = 2# * c
    ReflectX = a
End Function

Private Function MirroredCopy(ByVal src As SldWorks.Body2, ByVal c As Double, ByVal neg As Boolean, _
        applied As Boolean) As SldWorks.Body2
    applied = False
    On Error Resume Next
    Err.Clear
    Dim cp As SldWorks.Body2
    Set cp = src.Copy2(False)
    If Err.Number <> 0 Or cp Is Nothing Then
        Note "  Copy2 fehlgeschlagen " & ErrText()
        Exit Function
    End If
    Dim xf As SldWorks.MathTransform
    Set xf = mathUtil.CreateTransform(ReflectX(c, neg))
    If Err.Number <> 0 Or xf Is Nothing Then
        Note "  CreateTransform fehlgeschlagen " & ErrText()
        Exit Function
    End If
    Note "  Transformation zurückgelesen: " & ArrText(xf.ArrayData)
    applied = cp.ApplyTransform(xf)
    If Err.Number <> 0 Then
        Note "  ApplyTransform: Laufzeitfehler " & ErrText()
        applied = False
        Exit Function
    End If
    Note "  ApplyTransform meldet " & IIf(applied, "True", "False")
    Set MirroredCopy = cp
End Function

Private Function InsertBody(ByVal p As SldWorks.PartDoc, ByVal cp As SldWorks.Body2) As SldWorks.Feature
    On Error Resume Next
    Err.Clear
    Dim o As Object
    Set o = p.CreateFeatureFromBody3(cp, False, BODY_CHECK)
    If Err.Number <> 0 Then
        Note "  CreateFeatureFromBody3: Laufzeitfehler " & ErrText()
        Exit Function
    End If
    If o Is Nothing Then
        Note "  CreateFeatureFromBody3 lieferte Nothing (fehlgeschlagen)"
        Exit Function
    End If
    Dim f As SldWorks.Feature
    Set f = o
    Dim isWarn As Boolean, code As Long
    code = f.GetErrorCode2(isWarn)
    Note "  Neues Feature: " & f.Name & " (Typ " & f.GetTypeName2 & "), Fehlercode " & code & _
        IIf(isWarn, " (Warnung)", "")
    Set InsertBody = f
End Function

' Compares a body with the original mirrored in x = c: volume, centroid, every vertex, Check3.
Private Function CheckMirrored(ByVal b As SldWorks.Body2, ByVal c As Double, ByVal mp0 As Variant, _
        ByVal pts0 As Variant) As Boolean
    Dim mp As Variant
    On Error Resume Next
    Err.Clear
    mp = b.GetMassProperties(1#)
    If Err.Number <> 0 Or IsEmpty(mp) Then
        Note "  Masseeigenschaften nicht lesbar " & ErrText()
        Exit Function
    End If
    On Error GoTo 0

    Dim size As Double, dVol As Double, dCg As Double
    size = Abs(mp0(3)) ^ (1# / 3#)
    dVol = Abs(mp(3) - mp0(3)) / Abs(mp0(3))
    dCg = Dist(mp(0), mp(1), mp(2), 2# * c - mp0(0), mp0(1), mp0(2))
    Note "  Volumen " & Vol(mp(3)) & " (relative Abweichung " & Format(dVol, "0.0E+00") & ")"
    Note "  Schwerpunkt " & Pt(mp(0), mp(1), mp(2)) & ", erwartet " & Pt(2# * c - mp0(0), mp0(1), mp0(2)) & _
        " (Abstand " & Mm(dCg) & ")"

    Dim volOk As Boolean, cgOk As Boolean, vtxOk As Boolean
    volOk = (mp(3) > 0) And (dVol <= TOL_REL)
    cgOk = dCg <= TOL_REL * size + TOL_POINT
    vtxOk = CheckVertices(b, c, pts0)
    Dim nf As Long
    nf = Faults(b)
    Note "  Fehler laut Check3: " & IIf(nf < 0, "nicht lesbar", CStr(nf))
    CheckMirrored = volOk And cgOk And vtxOk And (nf = 0)
End Function

Private Function CheckVertices(ByVal b As SldWorks.Body2, ByVal c As Double, ByVal pts0 As Variant) As Boolean
    Dim pts As Variant
    pts = VertexPoints(b)
    Dim n0 As Long, n1 As Long
    n0 = PointCount(pts0)
    n1 = PointCount(pts)
    If n0 = 0 Then
        Note "  Eckpunkte: keine am Original, nicht verglichen"
        CheckVertices = True
        Exit Function
    End If
    If n1 = 0 Then
        Note "  Eckpunkte: keine oder nicht lesbar (Original " & n0 & ")"
        Exit Function
    End If
    Dim i As Long, j As Long
    Dim worst As Double, best As Double, d As Double
    worst = 0#
    For i = 0 To n0 - 1
        best = 1E+30
        For j = 0 To n1 - 1
            d = Dist(pts(3 * j), pts(3 * j + 1), pts(3 * j + 2), _
                2# * c - pts0(3 * i), pts0(3 * i + 1), pts0(3 * i + 2))
            If d < best Then best = d
        Next j
        If best > worst Then worst = best
    Next i
    Note "  Eckpunkte: " & n1 & " (Original " & n0 & "), größter Abstand zum gespiegelten Original-Eckpunkt " & Mm(worst)
    CheckVertices = (n1 = n0) And (worst <= TOL_POINT)
End Function

Private Function OriginalUnchanged(ByVal p As SldWorks.PartDoc, ByVal bodyName As String, ByVal mp0 As Variant) As Boolean
    Dim b As SldWorks.Body2
    Set b = FindSolid(p, bodyName)
    If b Is Nothing Then
        Note "  Original: Körper " & bodyName & " nicht mehr gefunden"
        Exit Function
    End If
    Dim mp As Variant
    mp = b.GetMassProperties(1#)
    If IsEmpty(mp) Then
        Note "  Original: Masseeigenschaften nicht lesbar"
        Exit Function
    End If
    Dim dVol As Double, dCg As Double
    dVol = Abs(mp(3) - mp0(3)) / Abs(mp0(3))
    dCg = Dist(mp(0), mp(1), mp(2), mp0(0), mp0(1), mp0(2))
    OriginalUnchanged = (dVol <= TOL_REL) And (dCg <= TOL_POINT)
    Note "  Original: " & IIf(OriginalUnchanged, "unverändert", "VERÄNDERT") & " (Volumen " & Vol(mp(3)) & _
        ", Schwerpunkt um " & Mm(dCg) & " verschoben)"
End Function

Private Function CopyState(ByVal cp As SldWorks.Body2) As String
    On Error Resume Next
    Err.Clear
    Dim v As Variant
    v = cp.GetMassProperties(1#)
    If Err.Number <> 0 Then
        CopyState = "nicht mehr nutzbar " & ErrText()
    ElseIf IsEmpty(v) Then
        CopyState = "nicht mehr nutzbar (keine Masseeigenschaften)"
    Else
        CopyState = "noch nutzbar (Volumen " & Vol(v(3)) & ")"
    End If
End Function

' ---- Körper und Geometrie -----------------------------------------------------

Private Function FirstSolid(ByVal p As SldWorks.PartDoc) As SldWorks.Body2
    Dim v As Variant
    v = p.GetBodies2(SOLID_BODY, True)
    If IsEmpty(v) Then Exit Function
    Set FirstSolid = v(LBound(v))
End Function

Private Function SolidCount(ByVal p As SldWorks.PartDoc) As Long
    Dim v As Variant
    v = p.GetBodies2(SOLID_BODY, False)
    If IsEmpty(v) Then
        SolidCount = 0
    Else
        SolidCount = UBound(v) - LBound(v) + 1
    End If
End Function

Private Function FindSolid(ByVal p As SldWorks.PartDoc, ByVal bodyName As String) As SldWorks.Body2
    Dim v As Variant, i As Long
    Dim b As SldWorks.Body2
    v = p.GetBodies2(SOLID_BODY, False)
    If IsEmpty(v) Then Exit Function
    For i = LBound(v) To UBound(v)
        Set b = v(i)
        If b.Name = bodyName Then
            Set FindSolid = b
            Exit Function
        End If
    Next i
End Function

Private Function SolidNames(ByVal p As SldWorks.PartDoc) As Collection
    Dim col As New Collection
    Dim v As Variant, i As Long
    Dim b As SldWorks.Body2
    v = p.GetBodies2(SOLID_BODY, False)
    If Not IsEmpty(v) Then
        For i = LBound(v) To UBound(v)
            Set b = v(i)
            col.Add b.Name, b.Name
        Next i
    End If
    Set SolidNames = col
End Function

Private Function NewSolid(ByVal p As SldWorks.PartDoc, ByVal namesBefore As Collection) As SldWorks.Body2
    Dim v As Variant, i As Long
    Dim b As SldWorks.Body2
    v = p.GetBodies2(SOLID_BODY, False)
    If IsEmpty(v) Then Exit Function
    For i = LBound(v) To UBound(v)
        Set b = v(i)
        If Not HasKey(namesBefore, b.Name) Then
            Set NewSolid = b
            Exit Function
        End If
    Next i
End Function

Private Function HasKey(ByVal col As Collection, ByVal key As String) As Boolean
    Dim x As Variant
    On Error Resume Next
    Err.Clear
    x = col(key)
    HasKey = (Err.Number = 0)
    Err.Clear
End Function

' Exact extent in X (extreme points), the body box only as fallback.
Private Sub ExtentX(ByVal b As SldWorks.Body2, xMin As Double, xMax As Double)
    Dim box As Variant
    Dim x As Double, y As Double, z As Double
    box = b.GetBodyBox
    xMin = box(0)
    xMax = box(3)
    If b.GetExtremePoint(-1#, 0#, 0#, x, y, z) Then xMin = x
    If b.GetExtremePoint(1#, 0#, 0#, x, y, z) Then xMax = x
End Sub

' All vertex coordinates as one flat array x0, y0, z0, x1, ...
Private Function VertexPoints(ByVal b As SldWorks.Body2) As Variant
    Dim vs As Variant
    On Error Resume Next
    vs = b.GetVertices
    If Err.Number <> 0 Or IsEmpty(vs) Then
        Err.Clear
        VertexPoints = Empty
        Exit Function
    End If
    On Error GoTo 0
    Dim n As Long, i As Long, k As Long
    n = UBound(vs) - LBound(vs) + 1
    Dim a() As Double
    ReDim a(3 * n - 1)
    Dim vx As SldWorks.Vertex
    Dim p As Variant
    k = 0
    For i = LBound(vs) To UBound(vs)
        Set vx = vs(i)
        p = vx.GetPoint
        a(k) = p(0)
        a(k + 1) = p(1)
        a(k + 2) = p(2)
        k = k + 3
    Next i
    VertexPoints = a
End Function

Private Function PointCount(ByVal pts As Variant) As Long
    If IsEmpty(pts) Then
        PointCount = 0
    Else
        PointCount = (UBound(pts) + 1) \ 3
    End If
End Function

Private Function Faults(ByVal b As SldWorks.Body2) As Long
    Dim fe As SldWorks.FaultEntity
    On Error Resume Next
    Err.Clear
    Set fe = b.Check3
    If Err.Number <> 0 Then
        Faults = -1
    ElseIf fe Is Nothing Then
        Faults = 0
    Else
        Faults = fe.Count
    End If
    Err.Clear
End Function

Private Function Dist(ByVal x1 As Double, ByVal y1 As Double, ByVal z1 As Double, _
        ByVal x2 As Double, ByVal y2 As Double, ByVal z2 As Double) As Double
    Dist = Sqr((x1 - x2) ^ 2 + (y1 - y2) ^ 2 + (z1 - z2) ^ 2)
End Function

' ---- Protokoll -----------------------------------------------------------------

Private Sub Note(ByVal s As String)
    logText = logText & s & vbCrLf
    Debug.Print s
End Sub

Private Sub SaveLog()
    Dim f As Integer
    logPath = Environ("TEMP") & "\spiegelwerk-test-b54-b55.txt"
    f = FreeFile
    Open logPath For Output As #f
    Print #f, logText
    Close #f
    Shell "notepad.exe """ & logPath & """", vbNormalFocus
End Sub

Private Function ErrText() As String
    ErrText = "(" & Err.Number & ": " & Err.Description & ")"
    Err.Clear
End Function

Private Function Verdict(ByVal ok As Boolean) As String
    If ok Then
        Verdict = "bestanden"
    Else
        Verdict = "NICHT bestanden"
    End If
End Function

Private Function Mm(ByVal m As Double) As String
    Mm = Format(m * 1000#, "0.0000") & " mm"
End Function

Private Function Pt(ByVal x As Double, ByVal y As Double, ByVal z As Double) As String
    Pt = "(" & Format(x * 1000#, "0.0000") & "; " & Format(y * 1000#, "0.0000") & "; " & _
        Format(z * 1000#, "0.0000") & ") mm"
End Function

Private Function Vol(ByVal v As Double) As String
    Vol = Format(v * 1000000000#, "0.000") & " mm3"
End Function

Private Function ArrText(ByVal v As Variant) As String
    Dim i As Long, s As String
    If IsEmpty(v) Then
        ArrText = "(leer)"
        Exit Function
    End If
    For i = LBound(v) To UBound(v)
        If i > LBound(v) Then s = s & "  "
        s = s & Format(v(i), "0.######")
    Next i
    ArrText = "[" & s & "]"
End Function
