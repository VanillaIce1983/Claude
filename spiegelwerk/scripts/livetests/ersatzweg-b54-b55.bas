Attribute VB_Name = "Modul_B54_B55"
' =============================================================================
' Spiegelwerk - Live-Test B54/B55 fuer den Blech-Ersatzweg (Schritt S7)
'
' B54: Nimmt SOLIDWORKS eine Spiegelung (Determinante -1) als Transformation
'      einer Koerperkopie an (IBody2.Copy2 + IBody2.ApplyTransform)?
'      Geprueft an einer schraegen Ebene in zwei Schreibweisen: direkt und
'      als (-M, Skalierung -1).
' B55: Wird der gespiegelte Koerper mit IPartDoc.CreateFeatureFromBody3 ein
'      eigener, gueltiger Koerper ohne externe Bezuege - im selben Teil
'      (getrennt vom Original und am Original anliegend) und in einem neuen
'      Teil? Ist die temporaere Kopie danach noch nutzbar?
'
' Ablauf:
'  1. Ein Testteil oeffnen, am besten ein einfaches Blechteil mit einem
'     Koerper (z. B. Platte mit einer Biegung), gefaltet, Rollback-Leiste am
'     Ende. Das Makro fuegt dem Teil Koerper hinzu und legt ein neues Teil
'     an: beides NICHT speichern.
'  2. Extras > Makro > Neu..., Namen vergeben, speichern. Im VBA-Editor
'     Datei > Datei importieren... (Strg+M) und diese Datei waehlen. Links
'     erscheint unter "Module" das Modul "Modul_B54_B55".
'  3. Das Modul doppelklicken, in "Sub Spiegelwerk_Test" klicken, F5.
'  4. Am Ende oeffnet sich der Editor mit dem Protokoll
'     (%TEMP%\spiegelwerk-test-b54-b55.txt). Den ganzen Text zurueckschicken.
'  5. Testteil und neues Teil ohne Speichern schliessen.
'
' Das Makro benutzt keine Spiegel-Funktion von SOLIDWORKS (kein
' Spiegeln-Feature, kein MirrorPart), nur eine Koerpertransformation.
' Laengen intern in Metern, im Protokoll in mm. Das Protokoll wird Zeile fuer
' Zeile geschrieben: bleibt SOLIDWORKS haengen, steht der letzte Schritt drin.
' =============================================================================
Option Explicit

' Plane n . p = d with unit normal n.
Private Type MPlane
    nx As Double
    ny As Double
    nz As Double
    d As Double
End Type

Private Const DOC_PART As Long = 1            ' swDocPART
Private Const SOLID_BODY As Long = 0          ' swSolidBody
Private Const BODY_CHECK As Long = 1          ' swCreateFeatureBodyCheck
Private Const TEMPLATE_PART As Long = 8       ' swDefaultTemplatePart
Private Const TOL_POINT As Double = 0.000001  ' 1 Mikrometer fuer Punkte
Private Const TOL_REL As Double = 0.00001     ' relativ fuer Volumen und Schwerpunkt

Private swApp As SldWorks.SldWorks
Private mathUtil As SldWorks.MathUtility
Private logPath As String

Sub Spiegelwerk_Test()
    Set swApp = Application.SldWorks
    Set mathUtil = swApp.GetMathUtility
    StartLog

    Dim model As SldWorks.ModelDoc2
    Set model = swApp.ActiveDoc
    If model Is Nothing Then
        MsgBox "Bitte zuerst ein Testteil oeffnen.", vbExclamation
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
        MsgBox "Das Teil hat keinen sichtbaren Volumenkoerper.", vbExclamation
        Exit Sub
    End If

    Dim errNo As Long, errDesc As String
    On Error GoTo Failed

    Dim bodyName As String
    bodyName = body.Name
    Note "Spiegelwerk Live-Test B54/B55"
    Note "SOLIDWORKS-Version: " & swApp.RevisionNumber
    Note "Teil: " & model.GetTitle & ", Volumenkoerper: " & SolidCount(part)
    Note "Getesteter Koerper: " & bodyName & IIf(body.IsSheetMetal, " (Blech)", " (kein Blech)")

    Dim mp0 As Variant, pts0 As Variant
    mp0 = body.GetMassProperties(1#)
    If IsEmpty(mp0) Then
        Note "Abbruch: Masseeigenschaften des Originals nicht lesbar."
        GoTo Done
    End If
    pts0 = VertexPoints(body)
    Note "Original: Volumen " & Vol(mp0(3)) & ", Schwerpunkt " & Pt(mp0(0), mp0(1), mp0(2)) & _
        ", Eckpunkte " & PointCount(pts0) & ", Fehler laut Check3: " & FaultText(body)
    NoteSymmetry pts0, mp0

    ' Oblique plane beyond the body: B54, B55 separate, new part.
    Dim obl As MPlane
    obl = ObliquePlane(body)
    ' Plane x = xMin through the body's extreme point: B55 touching.
    Dim touch As MPlane
    touch = TouchPlane(body, pts0)

    ' ---- B54 ---------------------------------------------------------------
    Note ""
    Note "=== B54: Spiegelung als Transformation einer Koerperkopie ==="
    Dim okDirect As Boolean, okNeg As Boolean
    okDirect = TestReflection(body, "direkt: Matrix mit Determinante -1, Skalierung 1", False, obl, mp0, pts0)
    okNeg = TestReflection(body, "als (-M, Skalierung -1)", True, obl, mp0, pts0)

    ' ---- B55 ---------------------------------------------------------------
    Note ""
    Note "=== B55: Gespiegelten Koerper als eigenen Koerper einfuegen ==="
    Dim neg As Boolean
    neg = okNeg And Not okDirect
    If okDirect Or okNeg Then
        Note "Schreibweise: " & IIf(neg, "(-M, Skalierung -1)", "direkt")
    Else
        Note "Schreibweise: direkt (B54 nicht bestanden, B55 nur zur Information)"
    End If
    Dim s1 As String, s2 As String, s3 As String
    Dim r1 As Boolean, r2 As Boolean, r3 As Boolean
    r1 = TestInsert(part, bodyName, "selbes Teil, getrennt vom Original (schraege Ebene)", obl, neg, mp0, pts0, s1)
    r2 = TestInsert(part, bodyName, "selbes Teil, am Original anliegend (Ebene x = xMin)", touch, neg, mp0, pts0, s2)
    r3 = TestNewPart(part, bodyName, obl, neg, mp0, pts0, s3)

    Note ""
    Note "=== Ergebnis ==="
    Note "B54 direkt:                  " & Verdict(okDirect)
    Note "B54 (-M, Skalierung -1):     " & Verdict(okNeg)
    Note "B55 selbes Teil, getrennt:   " & Verdict(r1)
    Note "B55 selbes Teil, anliegend:  " & Verdict(r2)
    Note "B55 neues Teil:              " & Verdict(r3)
    Note "Temporaere Kopie nach dem Einfuegen:"
    Note "  getrennt:  " & s1
    Note "  anliegend: " & s2
    Note "  neu:       " & s3
    GoTo Done

Failed:
    errNo = Err.Number
    errDesc = Err.Description
    Note ""
    Note "ABBRUCH: Laufzeitfehler " & errNo & ": " & errDesc
    Resume Done
Done:
    On Error Resume Next
    Note ""
    Note "Ende."
    Shell "notepad.exe """ & logPath & """", vbNormalFocus
    MsgBox "Test fertig. Das Protokoll steht in" & vbCrLf & logPath & vbCrLf & _
        "Bitte den ganzen Text zurueckschicken und beide Teile ohne Speichern schliessen.", vbInformation
End Sub

' ---- Tests ------------------------------------------------------------------

Private Function TestReflection(ByVal src As SldWorks.Body2, ByVal label As String, ByVal neg As Boolean, _
        pl As MPlane, ByVal mp0 As Variant, ByVal pts0 As Variant) As Boolean
    Note "- " & label
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, pl, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim geomOk As Boolean
    geomOk = CheckMirrored(cp, pl, mp0, pts0)
    TestReflection = applied And geomOk
    Note "  => " & Verdict(TestReflection)
End Function

Private Function TestInsert(ByVal part As SldWorks.PartDoc, ByVal bodyName As String, ByVal label As String, _
        pl As MPlane, ByVal neg As Boolean, ByVal mp0 As Variant, ByVal pts0 As Variant, _
        copyInfo As String) As Boolean
    Note "- " & label
    copyInfo = "nicht geprueft"
    Dim src As SldWorks.Body2
    Set src = FindSolid(part, bodyName)
    If src Is Nothing Then
        Note "  Original-Koerper " & bodyName & " nicht mehr gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, pl, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim namesBefore As Collection
    Set namesBefore = SolidNames(part)
    Dim feat As SldWorks.Feature
    Set feat = InsertBody(part, cp)
    copyInfo = CopyState(cp)
    Note "  Temporaere Kopie danach: " & copyInfo
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
        Note "  Volumenkoerper vorher " & namesBefore.Count & ", nachher " & countAfter & _
            ": eigener Koerper " & nb.Name & IIf(nb.IsSheetMetal, " (Blech)", " (kein Blech)")
    Else
        Note "  Volumenkoerper vorher " & namesBefore.Count & ", nachher " & countAfter & ": KEIN eigener Koerper"
    End If

    Dim geomOk As Boolean
    If Not nb Is Nothing Then geomOk = CheckMirrored(nb, pl, mp0, pts0)
    Dim ext As Long
    ext = feat.ListExternalFileReferencesCount
    Note "  Externe Bezuege des neuen Features: " & ext
    Dim origOk As Boolean
    origOk = OriginalUnchanged(part, bodyName, mp0)

    TestInsert = separate And geomOk And (ext = 0) And origOk
    Note "  => " & Verdict(TestInsert)
End Function

Private Function TestNewPart(ByVal part As SldWorks.PartDoc, ByVal bodyName As String, pl As MPlane, _
        ByVal neg As Boolean, ByVal mp0 As Variant, ByVal pts0 As Variant, copyInfo As String) As Boolean
    Note "- neues Teil (Kopie im Testteil gemacht, im neuen Teil eingefuegt)"
    copyInfo = "nicht geprueft"
    Dim tpl As String
    tpl = swApp.GetUserPreferenceStringValue(TEMPLATE_PART)
    Note "  Vorlage: " & tpl
    If tpl = "" Then
        Note "  Keine Standardvorlage fuer Teile eingestellt: uebersprungen"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim src As SldWorks.Body2
    Set src = FindSolid(part, bodyName)
    If src Is Nothing Then
        Note "  Original-Koerper " & bodyName & " nicht mehr gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, pl, neg, applied)
    If cp Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim doc As SldWorks.ModelDoc2, e As String
    Note "  NewDocument ..."
    On Error Resume Next
    Err.Clear
    Set doc = swApp.NewDocument(tpl, 0, 0#, 0#)
    If Err.Number <> 0 Then e = ErrText()
    On Error GoTo 0
    If e <> "" Then Note "  NewDocument: Laufzeitfehler " & e
    If doc Is Nothing Then
        Note "  Neues Teil liess sich nicht anlegen"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim np As SldWorks.PartDoc
    Set np = doc

    Dim feat As SldWorks.Feature
    Set feat = InsertBody(np, cp)
    copyInfo = CopyState(cp)
    Note "  Temporaere Kopie danach: " & copyInfo
    If feat Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim n As Long
    n = SolidCount(np)
    Note "  Volumenkoerper im neuen Teil: " & n
    Dim nb As SldWorks.Body2
    Set nb = FirstSolid(np)
    Dim geomOk As Boolean
    If Not nb Is Nothing Then geomOk = CheckMirrored(nb, pl, mp0, pts0)
    Dim ext As Long
    ext = feat.ListExternalFileReferencesCount
    Note "  Externe Bezuege des neuen Features: " & ext

    TestNewPart = (n = 1) And geomOk And (ext = 0)
    Note "  => " & Verdict(TestNewPart)
End Function

' ---- Ebenen -------------------------------------------------------------------

' Oblique plane with normal (1, 2, 3)/sqrt(14), half the body diagonal plus 10 mm
' in front of the body, so the mirror image lies apart from the original.
Private Function ObliquePlane(ByVal b As SldWorks.Body2) As MPlane
    Dim pl As MPlane, r As Double
    r = Sqr(14#)
    pl.nx = 1# / r
    pl.ny = 2# / r
    pl.nz = 3# / r
    Dim box As Variant
    box = b.GetBodyBox
    Dim size As Double
    size = Dist(box(0), box(1), box(2), box(3), box(4), box(5))
    Dim dMin As Double, src As String
    Dim x As Double, y As Double, z As Double
    If b.GetExtremePoint(-pl.nx, -pl.ny, -pl.nz, x, y, z) Then
        dMin = pl.nx * x + pl.ny * y + pl.nz * z
        src = "Extrempunkt"
    Else
        ' Box corners: never closer than the body itself.
        Dim i As Long, j As Long, k As Long, v As Double
        dMin = 1E+30
        For i = 0 To 1
            For j = 0 To 1
                For k = 0 To 1
                    v = pl.nx * box(3 * i) + pl.ny * box(1 + 3 * j) + pl.nz * box(2 + 3 * k)
                    If v < dMin Then dMin = v
                Next k
            Next j
        Next i
        src = "Ecken der Koerperbox"
    End If
    pl.d = dMin - 0.5 * size - 0.01
    Note "Schraege Spiegelebene: Normale (1; 2; 3)/Wurzel(14), Abstand vom Ursprung " & Mm(pl.d) & _
        ", " & Mm(dMin - pl.d) & " vor dem Koerper (nach " & src & ")"
    ObliquePlane = pl
End Function

' Plane x = xMin through the extreme point, so the mirror image touches the original.
Private Function TouchPlane(ByVal b As SldWorks.Body2, ByVal pts0 As Variant) As MPlane
    Dim pl As MPlane
    pl.nx = 1#
    pl.ny = 0#
    pl.nz = 0#
    Dim x As Double, y As Double, z As Double, src As String
    If b.GetExtremePoint(-1#, 0#, 0#, x, y, z) Then
        pl.d = x
        src = "Extrempunkt"
    Else
        Dim box As Variant
        box = b.GetBodyBox
        pl.d = box(0)
        src = "Koerperbox, Beruehrung nicht sichergestellt"
    End If
    Dim n As Long, i As Long
    n = 0
    For i = 0 To PointCount(pts0) - 1
        If Abs(pts0(3 * i) - pl.d) <= TOL_POINT Then n = n + 1
    Next i
    Note "Anliegende Spiegelebene: x = " & Mm(pl.d) & " (nach " & src & "), Eckpunkte des Originals darin: " & _
        n & " (" & ContactText(n) & ")"
    TouchPlane = pl
End Function

Private Function ContactText(ByVal n As Long) As String
    Select Case n
        Case 0
            ContactText = "keine Eckpunkte in der Ebene, Beruehrung unklar"
        Case 1
            ContactText = "Beruehrung in einem Punkt"
        Case 2
            ContactText = "Beruehrung entlang einer Kante"
        Case Else
            ContactText = "vermutlich Beruehrung auf einer Flaeche"
    End Select
End Function

Private Sub NoteSymmetry(ByVal pts0 As Variant, ByVal mp0 As Variant)
    If PointCount(pts0) = 0 Then Exit Sub
    Dim s As String, axis As Long
    Dim pl As MPlane
    For axis = 0 To 2
        pl.nx = 0#
        pl.ny = 0#
        pl.nz = 0#
        Select Case axis
            Case 0
                pl.nx = 1#
            Case 1
                pl.ny = 1#
            Case 2
                pl.nz = 1#
        End Select
        pl.d = mp0(axis)
        If WorstMatch(MirrorPoints(pts0, pl), pts0) <= TOL_POINT Then s = s & " " & Mid("XYZ", axis + 1, 1)
    Next axis
    If s = "" Then
        Note "Spiegelsymmetrie des Originals zu Ebenen durch den Schwerpunkt: keine"
    Else
        Note "Spiegelsymmetrie des Originals zu Ebenen durch den Schwerpunkt, senkrecht zu:" & s & _
            " (unschaedlich, B54 spiegelt an einer schraegen Ebene)"
    End If
End Sub

' ---- Spiegeln, Einfuegen, Vergleichen ----------------------------------------

' Reflection p' = H p + 2 d n with H = I - 2 n n^T (symmetric, so row or column
' order does not matter). Array of IMathUtility.CreateTransform: 0-8 rotation,
' 9-11 translation, 12 scale, 13-15 unused. neg: rotation -H with scale -1.
Private Function ReflectArray(pl As MPlane, ByVal neg As Boolean) As Variant
    Dim a(15) As Double, n(2) As Double, s As Double
    Dim i As Long, j As Long
    n(0) = pl.nx
    n(1) = pl.ny
    n(2) = pl.nz
    If neg Then s = -1# Else s = 1#
    For i = 0 To 2
        For j = 0 To 2
            If i = j Then
                a(3 * i + j) = s * (1# - 2# * n(i) * n(j))
            Else
                a(3 * i + j) = s * (-2# * n(i) * n(j))
            End If
        Next j
        a(9 + i) = 2# * pl.d * n(i)
    Next i
    a(12) = s
    ReflectArray = a
End Function

Private Function MirroredCopy(ByVal src As SldWorks.Body2, pl As MPlane, ByVal neg As Boolean, _
        applied As Boolean) As SldWorks.Body2
    Dim cp As SldWorks.Body2, xf As SldWorks.MathTransform, e As String
    applied = False
    On Error Resume Next
    Err.Clear
    Set cp = src.Copy2(False)
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Or cp Is Nothing Then
        Note "  Copy2 fehlgeschlagen " & e
        Exit Function
    End If
    Set xf = mathUtil.CreateTransform(ReflectArray(pl, neg))
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Or xf Is Nothing Then
        Note "  CreateTransform fehlgeschlagen " & e
        Exit Function
    End If
    Note "  Transformation zurueckgelesen: " & ArrText(xf.ArrayData)
    Note "  ApplyTransform ..."
    Err.Clear
    applied = cp.ApplyTransform(xf)
    If Err.Number <> 0 Then
        e = ErrText()
        applied = False
        Note "  ApplyTransform: Laufzeitfehler " & e
        Exit Function
    End If
    Note "  ApplyTransform meldet " & IIf(applied, "True", "False")
    Set MirroredCopy = cp
End Function

Private Function InsertBody(ByVal p As SldWorks.PartDoc, ByVal cp As SldWorks.Body2) As SldWorks.Feature
    Dim o As Object, e As String
    Note "  CreateFeatureFromBody3 ..."
    On Error Resume Next
    Err.Clear
    Set o = p.CreateFeatureFromBody3(cp, False, BODY_CHECK)
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Then
        Note "  CreateFeatureFromBody3: Laufzeitfehler " & e
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

' Compares a body with the original mirrored in pl: volume, centroid, every vertex, Check3.
Private Function CheckMirrored(ByVal b As SldWorks.Body2, pl As MPlane, ByVal mp0 As Variant, _
        ByVal pts0 As Variant) As Boolean
    Dim mp As Variant, e As String
    On Error Resume Next
    Err.Clear
    mp = b.GetMassProperties(1#)
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Or IsEmpty(mp) Then
        Note "  Masseeigenschaften nicht lesbar " & e
        Exit Function
    End If
    On Error GoTo 0

    Dim cg As Variant
    cg = MirrorPoints(Array(mp0(0), mp0(1), mp0(2)), pl)
    Dim size As Double, dVol As Double, dCg As Double
    size = Abs(mp0(3)) ^ (1# / 3#)
    dVol = Abs(mp(3) - mp0(3)) / Abs(mp0(3))
    dCg = Dist(mp(0), mp(1), mp(2), cg(0), cg(1), cg(2))
    Note "  Volumen " & Vol(mp(3)) & " (relative Abweichung " & Format(dVol, "0.0E+00") & ")"
    Note "  Schwerpunkt " & Pt(mp(0), mp(1), mp(2)) & ", erwartet " & Pt(cg(0), cg(1), cg(2)) & _
        " (Abstand " & Mm(dCg) & ")"

    Dim volOk As Boolean, cgOk As Boolean, vtxOk As Boolean
    volOk = (mp(3) > 0) And (dVol <= TOL_REL)
    cgOk = dCg <= TOL_REL * size + TOL_POINT
    vtxOk = CheckVertices(b, pl, pts0)
    Dim nf As Long
    nf = Faults(b)
    Note "  Fehler laut Check3: " & FaultText(b)
    CheckMirrored = volOk And cgOk And vtxOk And (nf = 0)
End Function

Private Function CheckVertices(ByVal b As SldWorks.Body2, pl As MPlane, ByVal pts0 As Variant) As Boolean
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
    Dim worst As Double
    worst = WorstMatch(MirrorPoints(pts0, pl), pts)
    Note "  Eckpunkte: " & n1 & " (Original " & n0 & "), groesster Abstand zum gespiegelten Original-Eckpunkt " & Mm(worst)
    CheckVertices = (n1 = n0) And (worst <= TOL_POINT)
End Function

Private Function OriginalUnchanged(ByVal p As SldWorks.PartDoc, ByVal bodyName As String, ByVal mp0 As Variant) As Boolean
    Dim b As SldWorks.Body2
    Set b = FindSolid(p, bodyName)
    If b Is Nothing Then
        Note "  Original: Koerper " & bodyName & " nicht mehr gefunden"
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
    Note "  Original: " & IIf(OriginalUnchanged, "unveraendert", "VERAENDERT") & " (Volumen " & Vol(mp(3)) & _
        ", Schwerpunkt um " & Mm(dCg) & " verschoben)"
End Function

' Is the temporary copy still a usable temporary body after the import?
Private Function CopyState(ByVal cp As SldWorks.Body2) As String
    Dim isTemp As Boolean, t As SldWorks.Body2, s As String, e As String
    Note "  pruefe temporaere Kopie ..."
    On Error Resume Next
    Err.Clear
    isTemp = cp.IsTemporaryBody
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Then
        CopyState = "nicht mehr nutzbar, IsTemporaryBody: Laufzeitfehler " & e
        Exit Function
    End If
    s = "IsTemporaryBody " & IIf(isTemp, "True", "False")
    Set t = cp.Copy2(False)
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Then
        s = s & ", Copy2 Laufzeitfehler " & e
    ElseIf t Is Nothing Then
        s = s & ", Copy2 liefert Nothing"
    Else
        s = s & ", Copy2 geht"
    End If
    If isTemp And Not t Is Nothing Then
        CopyState = "noch nutzbar (" & s & ")"
    Else
        CopyState = "verbraucht oder nicht mehr temporaer (" & s & ")"
    End If
End Function

' ---- Koerper und Geometrie -----------------------------------------------------

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

' All vertex coordinates as one flat array x0, y0, z0, x1, ...
Private Function VertexPoints(ByVal b As SldWorks.Body2) As Variant
    Dim vs As Variant
    On Error Resume Next
    Err.Clear
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
        PointCount = (UBound(pts) - LBound(pts) + 1) \ 3
    End If
End Function

' Points mirrored in pl: p' = p - 2 (n . p - d) n.
Private Function MirrorPoints(ByVal pts As Variant, pl As MPlane) As Variant
    Dim n As Long, i As Long, t As Double, b As Long
    n = PointCount(pts)
    If n = 0 Then
        MirrorPoints = Empty
        Exit Function
    End If
    b = LBound(pts)
    Dim a() As Double
    ReDim a(3 * n - 1)
    For i = 0 To n - 1
        t = 2# * (pl.nx * pts(b + 3 * i) + pl.ny * pts(b + 3 * i + 1) + pl.nz * pts(b + 3 * i + 2) - pl.d)
        a(3 * i) = pts(b + 3 * i) - t * pl.nx
        a(3 * i + 1) = pts(b + 3 * i + 1) - t * pl.ny
        a(3 * i + 2) = pts(b + 3 * i + 2) - t * pl.nz
    Next i
    MirrorPoints = a
End Function

' For every expected point the distance to the nearest actual point; returns the largest.
Private Function WorstMatch(ByVal expected As Variant, ByVal actual As Variant) As Double
    Dim ne As Long, na As Long, i As Long, j As Long
    Dim worst As Double, best As Double, d As Double
    ne = PointCount(expected)
    na = PointCount(actual)
    If ne = 0 Or na = 0 Then
        WorstMatch = 1E+30
        Exit Function
    End If
    worst = 0#
    For i = 0 To ne - 1
        best = 1E+30
        For j = 0 To na - 1
            d = Dist(actual(3 * j), actual(3 * j + 1), actual(3 * j + 2), _
                expected(3 * i), expected(3 * i + 1), expected(3 * i + 2))
            If d < best Then best = d
        Next j
        If best > worst Then worst = best
    Next i
    WorstMatch = worst
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

Private Function FaultText(ByVal b As SldWorks.Body2) As String
    Dim nf As Long
    nf = Faults(b)
    If nf < 0 Then
        FaultText = "nicht lesbar"
    Else
        FaultText = CStr(nf)
    End If
End Function

Private Function Dist(ByVal x1 As Double, ByVal y1 As Double, ByVal z1 As Double, _
        ByVal x2 As Double, ByVal y2 As Double, ByVal z2 As Double) As Double
    Dist = Sqr((x1 - x2) ^ 2 + (y1 - y2) ^ 2 + (z1 - z2) ^ 2)
End Function

' ---- Protokoll -----------------------------------------------------------------

Private Sub StartLog()
    Dim f As Integer
    logPath = Environ("TEMP") & "\spiegelwerk-test-b54-b55.txt"
    On Error Resume Next
    f = FreeFile
    Open logPath For Output As #f
    Close #f
End Sub

' Every line goes to the file at once, so a crash still leaves the last step.
Private Sub Note(ByVal s As String)
    Dim f As Integer
    Debug.Print s
    On Error Resume Next
    f = FreeFile
    Open logPath For Append As #f
    Print #f, s
    Close #f
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
