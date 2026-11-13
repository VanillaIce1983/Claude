Attribute VB_Name = "Modul_B57"
' =============================================================================
' Spiegelwerk - Live-Test B57 (mit B59) fuer den Blech-Ersatzweg (Schritt S7)
'
' B57: Laesst sich der gespiegelte, als Koerper eingefuegte Blechkoerper
'      (B54/B55) wieder in Blech verwandeln, wenn er schon runde Biegungen hat?
'      A1/A2: In Blech umwandeln (FeatureManager.InsertConvertToSheetMetal2,
'             "Biegungen finden" an, nur die feste Flaeche gewaehlt), feste
'             Flaeche = gespiegelte gewaehlte Flaeche bzw. ihre Gegenflaeche.
'      B1/B2: Biegungen einfuegen (PartDoc.InsertBends2) mit denselben Flaechen.
'      Erwartet: Blechkoerper, Biegungen erkannt, Geometrie unveraendert.
'      Jeder Versuch laeuft in einem eigenen neuen Teil.
' B59: Dasselbe Umwandeln im Testteil neben dem Original: bleiben das Original
'      und seine Blechparameter unveraendert?
'
' Ablauf:
'  1. Testteil oeffnen: ein Blechteil mit runden Biegungen, gefaltet,
'     Rollback-Leiste am Ende. Eine grosse EBENE Flaeche des Blechs anklicken,
'     am besten die feste Flaeche der Abwicklung. Sie muss gewaehlt bleiben.
'  2. Extras > Makro > Neu..., Namen vergeben (z. B. test_b57), speichern. Im
'     VBA-Editor Datei > Datei importieren... (Strg+M), diese Datei waehlen.
'  3. Modul "Modul_B57" doppelklicken, in "Sub Spiegelwerk_Test_B57" klicken,
'     F5.
'  4. Am Ende oeffnet sich der Editor mit dem Protokoll. Den ganzen Text
'     zurueckschicken.
'  5. Die vier neuen Teile und das Testteil ohne Speichern schliessen.
'
' Keine Spiegel-Funktion von SOLIDWORKS, nur eine Koerpertransformation
' (direkte Matrix, in B54 bestaetigt). Laengen intern in Metern, im Protokoll
' in mm. Das Protokoll wird Zeile fuer Zeile geschrieben.
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
Private Const SEL_FACES As Long = 2           ' swSelFACES
Private Const BA_KFACTOR As Long = 2          ' swBendAllowanceKFactor
Private Const BA_DIRECT As Long = 3           ' swBendAllowanceDirect
Private Const SUPPRESS As Long = 0            ' swSuppressFeature
Private Const UNSUPPRESS As Long = 1          ' swUnSuppressFeature
Private Const THIS_CONFIG As Long = 1         ' swThisConfiguration
Private Const NO_REBUILD As Long = 1          ' swDontRebuildActiveDoc
Private Const RIP_GAP As Double = 0.0001      ' 0,1 mm
Private Const TOL_POINT As Double = 0.000001  ' 1 Mikrometer fuer Punkte
Private Const TOL_FACE As Double = 0.00001    ' 10 Mikrometer: Punkt liegt auf einer Flaeche
Private Const TOL_REL As Double = 0.00001     ' relativ fuer Volumen und Schwerpunkt

Private swApp As SldWorks.SldWorks
Private mathUtil As SldWorks.MathUtility
Private logPath As String

' Sheet-metal values of the original (its first SheetMetal feature).
Private smName As String
Private smT As Double, smR As Double, smK As Double, smBA As Double
Private smKind As Long, smRelief As Long, smRatio As Double, smAuto As Boolean

Sub Spiegelwerk_Test_B57()
    Set swApp = Application.SldWorks
    Set mathUtil = swApp.GetMathUtility

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
    Dim selMgr As SldWorks.SelectionMgr
    Set selMgr = model.SelectionManager
    If selMgr.GetSelectedObjectCount2(-1) < 1 Then
        MsgBox "Bitte zuerst eine grosse ebene Flaeche des Blechs anklicken.", vbExclamation
        Exit Sub
    End If
    If selMgr.GetSelectedObjectType3(1, -1) <> SEL_FACES Then
        MsgBox "Das Gewaehlte ist keine Flaeche. Bitte eine ebene Flaeche des Blechs anklicken.", vbExclamation
        Exit Sub
    End If
    Dim selFace As SldWorks.Face2
    Set selFace = selMgr.GetSelectedObject6(1, -1)
    Dim pick As Variant
    pick = selMgr.GetSelectionPoint2(1, -1)
    Dim body As SldWorks.Body2
    Set body = selFace.GetBody
    If body Is Nothing Or IsEmpty(pick) Then
        MsgBox "Flaeche oder Klickpunkt nicht lesbar. Bitte die Flaeche im Grafikbereich anklicken.", vbExclamation
        Exit Sub
    End If

    StartLog "spiegelwerk-test-b57-" & SafeName(model.GetTitle) & ".txt"
    Dim errNo As Long, errDesc As String
    On Error GoTo Failed

    Dim bodyName As String
    bodyName = body.Name
    Note "Spiegelwerk Live-Test B57/B59"
    Note "SOLIDWORKS-Version: " & swApp.RevisionNumber
    Note "Teil: " & model.GetTitle & ", Volumenkoerper: " & SolidCount(part)
    Note "Getesteter Koerper: " & bodyName & IIf(body.IsSheetMetal, " (Blech)", " (kein Blech)")

    Dim surf As SldWorks.Surface
    Set surf = selFace.GetSurface
    If Not surf.IsPlane Then
        Note "Abbruch: Die gewaehlte Flaeche ist nicht eben."
        GoTo Done
    End If
    If Not ReadSheetMetal(model) Then
        Note "Abbruch: Kein Blech-Feature (SheetMetal) im Teil gefunden."
        GoTo Done
    End If
    Note "Blechparameter des Originals (" & smName & "): " & SMText()
    NoteOriginalBends model

    Dim mp0 As Variant
    mp0 = body.GetMassProperties(1#)
    If IsEmpty(mp0) Then
        Note "Abbruch: Masseeigenschaften des Originals nicht lesbar."
        GoTo Done
    End If
    Note "Original: Volumen " & Vol(mp0(3)) & ", Schwerpunkt " & Pt(mp0(0), mp0(1), mp0(2))

    ' Fixed point: the click point moved onto the chosen face; opposite point one thickness away.
    Dim cpt As Variant
    cpt = selFace.GetClosestPointOn(pick(0), pick(1), pick(2))
    Dim fixP As Variant, oppP As Variant, nv As Variant
    fixP = Array(cpt(0), cpt(1), cpt(2))
    nv = selFace.Normal
    Note "Gewaehlte Flaeche: Punkt " & Pt(fixP(0), fixP(1), fixP(2)) & ", Normale (" & _
        Format(nv(0), "0.0000") & "; " & Format(nv(1), "0.0000") & "; " & Format(nv(2), "0.0000") & ")"
    oppP = OppositePoint(body, fixP, nv, smT)

    Dim obl As MPlane
    obl = ObliquePlane(body)
    Dim fixM As Variant, oppM As Variant
    fixM = MirrorPoints(fixP, obl)
    oppM = Empty
    If Not IsEmpty(oppP) Then oppM = MirrorPoints(oppP, obl)
    Note "Argumente Umwandeln: Dicke " & Mm(smT) & ", Radius " & Mm(smR) & ", Rissspalt " & Mm(RIP_GAP) & _
        ", Entlastung " & ReliefKind() & " mit Verhaeltnis " & ReliefRatioArg() & ", Ueberlappung 1 / 0,5, Koerper nicht behalten"
    Note "Argumente Biegungen einfuegen: Radius " & Mm(smR) & ", K-Faktor " & KArg() & ", Biegezugabe " & BAArg() & _
        ", automatische Entlastung " & IIf(smAuto, "ja", "nein") & ", Versatzverhaeltnis " & OffsetArg()

    ' ---- B57 ---------------------------------------------------------------
    Note ""
    Note "=== B57: Gespiegelten Koerper wieder in Blech verwandeln (je ein neues Teil) ==="
    Dim a1 As Boolean, a2 As Boolean, b1 As Boolean, b2 As Boolean
    a1 = TestRoute("A1: Umwandeln, feste Flaeche = gespiegelte gewaehlte Flaeche", True, part, bodyName, obl, fixM)
    a2 = TestRoute("A2: Umwandeln, feste Flaeche = gespiegelte Gegenflaeche", True, part, bodyName, obl, oppM)
    b1 = TestRoute("B1: Biegungen einfuegen, feste Flaeche = gespiegelte gewaehlte Flaeche", False, part, bodyName, obl, fixM)
    b2 = TestRoute("B2: Biegungen einfuegen, feste Flaeche = gespiegelte Gegenflaeche", False, part, bodyName, obl, oppM)

    ' ---- B59 ---------------------------------------------------------------
    Note ""
    Note "=== B59: Umwandeln im Testteil neben dem Original ==="
    Dim s59 As Boolean, ran59 As Boolean
    If a1 Then
        ran59 = True
        s59 = TestSamePart(model, bodyName, obl, fixM, mp0, "A1")
    ElseIf a2 Then
        ran59 = True
        s59 = TestSamePart(model, bodyName, obl, oppM, mp0, "A2")
    Else
        Note "uebersprungen: weder A1 noch A2 bestanden"
    End If

    Note ""
    Note "=== Ergebnis ==="
    Note "B57 A1 Umwandeln, gewaehlte Flaeche:       " & Verdict(a1)
    Note "B57 A2 Umwandeln, Gegenflaeche:            " & Verdict(a2)
    Note "B57 B1 Biegungen einfuegen, gew. Flaeche:  " & Verdict(b1)
    Note "B57 B2 Biegungen einfuegen, Gegenflaeche:  " & Verdict(b2)
    If ran59 Then
        Note "B59 Umwandeln im selben Teil:              " & Verdict(s59)
    Else
        Note "B59 Umwandeln im selben Teil:              nicht gelaufen"
    End If
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
        "Bitte den ganzen Text zurueckschicken und alle Teile ohne Speichern schliessen.", vbInformation
End Sub

' ---- Tests ------------------------------------------------------------------

' One attempt in a new part: mirrored copy, import, convert or Insert Bends, check.
Private Function TestRoute(ByVal label As String, ByVal convert As Boolean, ByVal part As SldWorks.PartDoc, _
        ByVal bodyName As String, pl As MPlane, ByVal fixM As Variant) As Boolean
    Note ""
    Note "- " & label
    If IsEmpty(fixM) Then
        Note "  Keine feste Flaeche bekannt: uebersprungen"
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
    Set cp = MirroredCopy(src, pl, applied)
    If cp Is Nothing Or Not applied Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim doc As SldWorks.ModelDoc2
    Set doc = NewPart()
    If doc Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim np As SldWorks.PartDoc
    Set np = doc
    If InsertBody(np, cp) Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim nb As SldWorks.Body2
    Set nb = FirstSolid(np)
    If nb Is Nothing Then
        Note "  Kein Koerper im neuen Teil"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    TestRoute = ConvertAndCheck(doc, nb, convert, fixM)
End Function

' B59: the same convert in the test part, next to the original.
Private Function TestSamePart(ByVal model As SldWorks.ModelDoc2, ByVal bodyName As String, pl As MPlane, _
        ByVal fixM As Variant, ByVal mp0 As Variant, ByVal route As String) As Boolean
    Dim part As SldWorks.PartDoc
    Set part = model
    Dim errs As Long
    Dim act As Object
    Set act = swApp.ActivateDoc3(model.GetTitle, False, NO_REBUILD, errs)
    Note "- Umwandeln wie " & route & " im Testteil (aktiviert: " & IIf(act Is Nothing, "nein", "ja") & ")"
    Dim sigBefore As String
    sigBefore = SMSignature(part)
    Note "  Blechparameter des Originals vorher: " & sigBefore

    Dim src As SldWorks.Body2
    Set src = FindSolid(part, bodyName)
    If src Is Nothing Then
        Note "  Original-Koerper " & bodyName & " nicht mehr gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim applied As Boolean
    Dim cp As SldWorks.Body2
    Set cp = MirroredCopy(src, pl, applied)
    If cp Is Nothing Or Not applied Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim namesBefore As Collection
    Set namesBefore = SolidNames(part)
    If InsertBody(part, cp) Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim nb As SldWorks.Body2
    Set nb = NewSolid(part, namesBefore)
    If nb Is Nothing Then
        Note "  Eingefuegter Koerper nicht gefunden"
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim convOk As Boolean
    convOk = ConvertAndCheck(model, nb, True, fixM)
    Dim origOk As Boolean
    origOk = OriginalUnchanged(part, bodyName, mp0)
    Dim sigAfter As String
    sigAfter = SMSignature(part)
    Note "  Blechparameter des Originals nachher: " & sigAfter & IIf(sigAfter = sigBefore, " (unveraendert)", " (VERAENDERT)")
    Note "  Volumenkoerper im Testteil jetzt: " & SolidCount(part)
    TestSamePart = convOk And origOk And (sigAfter = sigBefore)
    Note "  => B59 " & Verdict(TestSamePart)
End Function

' Convert (or Insert Bends) the imported body nb with the face at fixM fixed, then check the result.
Private Function ConvertAndCheck(ByVal doc As SldWorks.ModelDoc2, ByVal nb As SldWorks.Body2, _
        ByVal convert As Boolean, ByVal fixM As Variant) As Boolean
    Dim p As SldWorks.PartDoc
    Set p = doc
    Dim mpB As Variant, ptsB As Variant
    mpB = nb.GetMassProperties(1#)
    ptsB = VertexPoints(nb)
    Note "  Eingefuegter Koerper " & nb.Name & ": Volumen " & Vol(mpB(3)) & ", Eckpunkte " & PointCount(ptsB) & _
        IIf(nb.IsSheetMetal, ", Blech", ", kein Blech")
    Dim ff As SldWorks.Face2
    Set ff = FaceAt(nb, fixM)
    If ff Is Nothing Then
        Note "  => " & Verdict(False)
        Exit Function
    End If

    Dim before As Collection
    Set before = FeatureNames(doc)
    RunConvert doc, ff, convert, 0
    If Not convert And Not AnyNew(doc, before) Then
        Note "  nichts entstanden: zweiter Versuch mit Markierung 1"
        Set ff = FaceAt(nb, fixM)
        If Not ff Is Nothing Then RunConvert doc, ff, convert, 1
    End If

    Dim bends As Long, sheet As Boolean
    Dim flat As SldWorks.Feature
    NoteNewFeatures doc, before, bends, sheet, flat

    Dim res As SldWorks.Body2
    Set res = BodyNear(p, mpB)
    If res Is Nothing Then
        Note "  Am Ort des eingefuegten Koerpers ist kein Koerper mehr"
        Note "  => " & Verdict(False)
        Exit Function
    End If
    Dim isSM As Boolean
    isSM = res.IsSheetMetal
    Note "  Koerper danach: " & res.Name & IIf(isSM, " (Blech)", " (kein Blech)")
    Dim geomOk As Boolean
    geomOk = CheckSame(res, mpB, ptsB)
    Note "  Biegungen erkannt (groesste Zahl von Unterfeatures mit ""Bend"" im Typ): " & bends
    If convert And Not flat Is Nothing Then CheckFlatten doc, flat

    ConvertAndCheck = sheet And isSM And geomOk And (bends > 0)
    Note "  => " & Verdict(ConvertAndCheck)
End Function

Private Sub RunConvert(ByVal doc As SldWorks.ModelDoc2, ByVal ff As SldWorks.Face2, ByVal convert As Boolean, _
        ByVal mark As Long)
    Dim ok As Boolean, e As String
    If Not SelectFace(doc, ff, mark) Then
        Note "  Feste Flaeche liess sich nicht waehlen (Markierung " & mark & ")"
        Exit Sub
    End If
    If convert Then
        Note "  InsertConvertToSheetMetal2 (Markierung " & mark & ") ..."
        On Error Resume Next
        Err.Clear
        ok = doc.FeatureManager.InsertConvertToSheetMetal2(smT, False, True, smR, RIP_GAP, ReliefKind(), _
            ReliefRatioArg(), 1, 0.5, False)
        If Err.Number <> 0 Then e = ErrText()
        On Error GoTo 0
    Else
        Dim np As SldWorks.PartDoc
        Set np = doc
        Note "  InsertBends2 (Markierung " & mark & ") ..."
        On Error Resume Next
        Err.Clear
        ok = np.InsertBends2(smR, "", KArg(), BAArg(), smAuto, OffsetArg(), True)
        If Err.Number <> 0 Then e = ErrText()
        On Error GoTo 0
    End If
    doc.ClearSelection2 True
    If e <> "" Then Note "  Laufzeitfehler " & e
    Note "  Rueckgabewert " & IIf(ok, "True", "False") & " (zaehlt nicht: entscheidend ist, was entstanden ist)"
End Sub

' Unsuppresses the new flat pattern, rebuilds, reads its error code, folds again.
Private Sub CheckFlatten(ByVal doc As SldWorks.ModelDoc2, ByVal flat As SldWorks.Feature)
    Dim ok As Boolean, code As Long, warn As Boolean, e As String
    Note "  Abwickeln ..."
    On Error Resume Next
    Err.Clear
    ok = flat.SetSuppression2(UNSUPPRESS, THIS_CONFIG, Empty)
    doc.EditRebuild3
    code = flat.GetErrorCode2(warn)
    If Err.Number <> 0 Then e = ErrText()
    Note "  Abwicklung: entdrueckt " & IIf(ok, "ja", "nein") & ", Fehlercode " & code & IIf(warn, " (Warnung)", "") & _
        IIf(e <> "", " " & e, "")
    Err.Clear
    flat.SetSuppression2 SUPPRESS, THIS_CONFIG, Empty
    doc.EditRebuild3
    Note "  wieder gefaltet"
End Sub

' ---- Blech-Werte ----------------------------------------------------------------

Private Function ReadSheetMetal(ByVal doc As SldWorks.ModelDoc2) As Boolean
    Dim f As SldWorks.Feature, sf As SldWorks.Feature, hit As SldWorks.Feature
    Set f = doc.FirstFeature
    Do While Not f Is Nothing
        If f.GetTypeName2 = "SheetMetal" Then
            Set hit = f
            Exit Do
        End If
        Set sf = f.GetFirstSubFeature
        Do While Not sf Is Nothing
            If sf.GetTypeName2 = "SheetMetal" Then
                Set hit = sf
                Exit Do
            End If
            Set sf = sf.GetNextSubFeature
        Loop
        If Not hit Is Nothing Then Exit Do
        Set f = f.GetNextFeature
    Loop
    If hit Is Nothing Then Exit Function
    smName = hit.Name
    Dim d As SldWorks.SheetMetalFeatureData
    Set d = hit.GetDefinition
    smT = d.Thickness
    smR = d.BendRadius
    smK = d.KFactor
    smBA = d.BendAllowance
    smKind = d.BendAllowanceType
    smRelief = d.AutoReliefType
    smRatio = d.ReliefRatio
    smAuto = d.UseAutoRelief
    ReadSheetMetal = True
End Function

Private Function SMText() As String
    SMText = "Dicke " & Mm(smT) & ", Radius " & Mm(smR) & ", Biegezugabe-Art " & smKind & ", K-Faktor " & _
        Format(smK, "0.0000") & ", Biegezugabe " & Mm(smBA) & ", automatische Entlastung " & IIf(smAuto, "ja", "nein") & _
        ", Entlastungsart " & smRelief & ", Verhaeltnis " & Format(smRatio, "0.0000")
End Function

' Thickness, radius, K-factor and allowance kind of the original's SheetMetal feature, as text.
Private Function SMSignature(ByVal p As SldWorks.PartDoc) As String
    Dim f As SldWorks.Feature, d As SldWorks.SheetMetalFeatureData
    Set f = p.FeatureByName(smName)
    If f Is Nothing Then
        SMSignature = "Feature " & smName & " nicht gefunden"
        Exit Function
    End If
    Set d = f.GetDefinition
    SMSignature = "Dicke " & Mm(d.Thickness) & ", Radius " & Mm(d.BendRadius) & ", K-Faktor " & _
        Format(d.KFactor, "0.0000") & ", Biegezugabe-Art " & d.BendAllowanceType
End Function

Private Function ReliefKind() As Long
    If smRelief >= 1 And smRelief <= 5 Then
        ReliefKind = smRelief
    Else
        ReliefKind = 1
    End If
End Function

Private Function ReliefRatioArg() As Double
    If smRatio > 0 Then
        ReliefRatioArg = smRatio
    Else
        ReliefRatioArg = 0.5
    End If
End Function

Private Function KArg() As Double
    If smKind = BA_KFACTOR Then
        KArg = smK
    ElseIf smKind = BA_DIRECT Then
        KArg = -1#
    Else
        KArg = 0.5
    End If
End Function

Private Function BAArg() As Double
    If smKind = BA_DIRECT Then
        BAArg = smBA
    Else
        BAArg = -1#
    End If
End Function

Private Function OffsetArg() As Double
    If smRatio >= 0.05 And smRatio <= 2# Then
        OffsetArg = smRatio
    Else
        OffsetArg = 0.5
    End If
End Function

' ---- Features -----------------------------------------------------------------

Private Function FeatureNames(ByVal doc As SldWorks.ModelDoc2) As Collection
    Dim col As New Collection, f As SldWorks.Feature
    Set f = doc.FirstFeature
    On Error Resume Next
    Do While Not f Is Nothing
        col.Add f.Name, f.Name
        Set f = f.GetNextFeature
    Loop
    Err.Clear
    Set FeatureNames = col
End Function

Private Function AnyNew(ByVal doc As SldWorks.ModelDoc2, ByVal before As Collection) As Boolean
    Dim f As SldWorks.Feature
    Set f = doc.FirstFeature
    Do While Not f Is Nothing
        If Not HasKey(before, f.Name) Then
            AnyNew = True
            Exit Function
        End If
        Set f = f.GetNextFeature
    Loop
End Function

' Logs every new top-level feature with its sub-features; bends = largest number of
' sub-features with "Bend" in their type under one feature.
Private Sub NoteNewFeatures(ByVal doc As SldWorks.ModelDoc2, ByVal before As Collection, bends As Long, _
        sheet As Boolean, flat As SldWorks.Feature)
    Dim f As SldWorks.Feature, typ As String, n As Long, tally As String, found As Boolean
    Dim code As Long, warn As Boolean
    bends = 0
    sheet = False
    Set flat = Nothing
    Set f = doc.FirstFeature
    Do While Not f Is Nothing
        If Not HasKey(before, f.Name) Then
            found = True
            typ = f.GetTypeName2
            tally = SubTally(f, n)
            If n > bends Then bends = n
            If typ = "SolidToSheetMetal" Or typ = "SheetMetal" Then sheet = True
            If typ = "FlatPattern" Then Set flat = f
            code = f.GetErrorCode2(warn)
            Note "  neu: " & f.Name & " (" & typ & ")" & IIf(code <> 0, ", Fehlercode " & code, "") & tally
        End If
        Set f = f.GetNextFeature
    Loop
    If Not found Then Note "  Keine neuen Features"
End Sub

Private Function SubTally(ByVal f As SldWorks.Feature, n As Long) As String
    Dim d As Object, sf As SldWorks.Feature, t As String, k As Variant, s As String
    n = 0
    Set d = CreateObject("Scripting.Dictionary")
    Set sf = f.GetFirstSubFeature
    Do While Not sf Is Nothing
        t = sf.GetTypeName2
        If d.Exists(t) Then
            d(t) = d(t) + 1
        Else
            d.Add t, 1
        End If
        If InStr(1, t, "Bend", vbTextCompare) > 0 Then n = n + 1
        Set sf = sf.GetNextSubFeature
    Loop
    If d.Count = 0 Then Exit Function
    For Each k In d.Keys
        If s <> "" Then s = s & ", "
        s = s & k & " x" & d(k)
    Next k
    SubTally = " [Unterfeatures: " & s & "]"
End Function

Private Sub NoteOriginalBends(ByVal doc As SldWorks.ModelDoc2)
    Dim f As SldWorks.Feature, n As Long, total As Long, flatN As Long, tally As String
    Set f = doc.FirstFeature
    Do While Not f Is Nothing
        tally = SubTally(f, n)
        If f.GetTypeName2 = "FlatPattern" Then
            flatN = flatN + n
        Else
            total = total + n
        End If
        Set f = f.GetNextFeature
    Loop
    Note "Biegungen im Original (Unterfeatures mit ""Bend"" im Typ): " & total & ", in der Abwicklung: " & flatN
End Sub

' ---- Flaechen ------------------------------------------------------------------

' The face of b nearest to the point (within TOL_FACE), or Nothing.
Private Function FaceAt(ByVal b As SldWorks.Body2, ByVal pt As Variant) As SldWorks.Face2
    Dim vf As Variant, i As Long, f As SldWorks.Face2, c As Variant, d As Double, best As Double
    Dim hit As SldWorks.Face2
    best = 1E+30
    vf = b.GetFaces
    If IsEmpty(vf) Then
        Note "  Koerper hat keine lesbaren Flaechen"
        Exit Function
    End If
    For i = LBound(vf) To UBound(vf)
        Set f = vf(i)
        c = f.GetClosestPointOn(pt(0), pt(1), pt(2))
        If Not IsEmpty(c) Then
            d = Dist(c(0), c(1), c(2), pt(0), pt(1), pt(2))
            If d < best Then
                best = d
                Set hit = f
            End If
        End If
    Next i
    Note "  Feste Flaeche: Abstand des gespiegelten Punkts zur naechsten Flaeche " & Mm(best)
    If best <= TOL_FACE Then
        Set FaceAt = hit
    Else
        Note "  Keine Flaeche an diesem Punkt"
    End If
End Function

' A point one thickness from p along the face normal (either side) that lies on another face of b.
Private Function OppositePoint(ByVal b As SldWorks.Body2, ByVal p As Variant, ByVal nv As Variant, _
        ByVal t As Double) As Variant
    Dim s As Long, k As Long, i As Long
    Dim q(2) As Double, nl As Double, best As Double, d As Double, dp As Double
    Dim vf As Variant, f As SldWorks.Face2, c As Variant
    OppositePoint = Empty
    nl = Sqr(nv(0) ^ 2 + nv(1) ^ 2 + nv(2) ^ 2)
    vf = b.GetFaces
    If nl = 0 Or IsEmpty(vf) Then
        Note "Gegenflaeche: Normale oder Flaechen nicht lesbar"
        Exit Function
    End If
    For s = -1 To 1 Step 2
        For k = 0 To 2
            q(k) = p(k) + s * t * nv(k) / nl
        Next k
        best = 1E+30
        For i = LBound(vf) To UBound(vf)
            Set f = vf(i)
            c = f.GetClosestPointOn(p(0), p(1), p(2))
            If Not IsEmpty(c) Then
                dp = Dist(c(0), c(1), c(2), p(0), p(1), p(2))
                ' Faces through p are the chosen face (or touch it): not the opposite one.
                If dp > TOL_FACE Then
                    c = f.GetClosestPointOn(q(0), q(1), q(2))
                    If Not IsEmpty(c) Then
                        d = Dist(c(0), c(1), c(2), q(0), q(1), q(2))
                        If d < best Then best = d
                    End If
                End If
            End If
        Next i
        If best <= TOL_FACE Then
            OppositePoint = Array(q(0), q(1), q(2))
            Note "Gegenflaeche: Punkt " & Pt(q(0), q(1), q(2)) & ", " & IIf(s < 0, "gegen die", "in") & _
                " Richtung der Flaechennormalen"
            Exit Function
        End If
    Next s
    Note "Gegenflaeche nicht gefunden: kein Punkt im Abstand der Blechdicke auf einer anderen Flaeche"
End Function

Private Function SelectFace(ByVal doc As SldWorks.ModelDoc2, ByVal f As SldWorks.Face2, ByVal mark As Long) As Boolean
    Dim sm As SldWorks.SelectionMgr, sd As SldWorks.SelectData, ent As SldWorks.Entity
    Set sm = doc.SelectionManager
    Set sd = sm.CreateSelectData
    sd.Mark = mark
    doc.ClearSelection2 True
    Set ent = f
    SelectFace = ent.Select4(False, sd)
End Function

' ---- Ebene und Spiegeln ---------------------------------------------------------

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
    Note "Schraege Spiegelebene: Normale (1; 2; 3)/Wurzel(14), " & Mm(dMin - pl.d) & " vor dem Koerper (nach " & src & ")"
    ObliquePlane = pl
End Function

' Reflection p' = H p + 2 d n with H = I - 2 n n^T, scale 1 (the direct form confirmed in B54).
Private Function ReflectArray(pl As MPlane) As Variant
    Dim a(15) As Double, n(2) As Double
    Dim i As Long, j As Long
    n(0) = pl.nx
    n(1) = pl.ny
    n(2) = pl.nz
    For i = 0 To 2
        For j = 0 To 2
            If i = j Then
                a(3 * i + j) = 1# - 2# * n(i) * n(j)
            Else
                a(3 * i + j) = -2# * n(i) * n(j)
            End If
        Next j
        a(9 + i) = 2# * pl.d * n(i)
    Next i
    a(12) = 1#
    ReflectArray = a
End Function

Private Function MirroredCopy(ByVal src As SldWorks.Body2, pl As MPlane, applied As Boolean) As SldWorks.Body2
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
    Set xf = mathUtil.CreateTransform(ReflectArray(pl))
    If Err.Number <> 0 Then e = ErrText()
    If e <> "" Or xf Is Nothing Then
        Note "  CreateTransform fehlgeschlagen " & e
        Exit Function
    End If
    Err.Clear
    applied = cp.ApplyTransform(xf)
    If Err.Number <> 0 Then
        e = ErrText()
        applied = False
        Note "  ApplyTransform: Laufzeitfehler " & e
        Exit Function
    End If
    Note "  Gespiegelte Kopie: ApplyTransform meldet " & IIf(applied, "True", "False")
    Set MirroredCopy = cp
End Function

Private Function NewPart() As SldWorks.ModelDoc2
    Dim tpl As String, doc As SldWorks.ModelDoc2, e As String
    tpl = swApp.GetUserPreferenceStringValue(TEMPLATE_PART)
    If tpl = "" Then
        Note "  Keine Standardvorlage fuer Teile eingestellt"
        Exit Function
    End If
    Note "  NewDocument ..."
    On Error Resume Next
    Err.Clear
    Set doc = swApp.NewDocument(tpl, 0, 0#, 0#)
    If Err.Number <> 0 Then e = ErrText()
    On Error GoTo 0
    If e <> "" Then Note "  NewDocument: Laufzeitfehler " & e
    If doc Is Nothing Then
        Note "  Neues Teil liess sich nicht anlegen"
        Exit Function
    End If
    Note "  Neues Teil: " & doc.GetTitle
    Set NewPart = doc
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
    Note "  Eingefuegt als " & f.Name & " (" & f.GetTypeName2 & ")"
    Set InsertBody = f
End Function

' ---- Vergleichen ---------------------------------------------------------------

' The body after the convert compared with the imported body before it (same place, same shape).
Private Function CheckSame(ByVal b As SldWorks.Body2, ByVal mpB As Variant, ByVal ptsB As Variant) As Boolean
    Dim mp As Variant, pts As Variant
    mp = b.GetMassProperties(1#)
    If IsEmpty(mp) Then
        Note "  Masseeigenschaften danach nicht lesbar"
        Exit Function
    End If
    Dim size As Double, dVol As Double, dCg As Double
    size = Abs(mpB(3)) ^ (1# / 3#)
    dVol = Abs(mp(3) - mpB(3)) / Abs(mpB(3))
    dCg = Dist(mp(0), mp(1), mp(2), mpB(0), mpB(1), mpB(2))
    pts = VertexPoints(b)
    Dim lost As Double, extra As Double
    lost = WorstMatch(ptsB, pts)
    extra = WorstMatch(pts, ptsB)
    Note "  Geometrie danach: Volumen " & Vol(mp(3)) & " (relativ " & Format(dVol, "0.0E+00") & "), Schwerpunkt um " & _
        Mm(dCg) & " verschoben, Eckpunkte " & PointCount(pts) & " (vorher " & PointCount(ptsB) & ")"
    Note "  Eckpunkte: groesster Abstand eines alten zum naechsten neuen " & MmOrNone(lost) & _
        ", eines neuen zum naechsten alten " & MmOrNone(extra)
    CheckSame = (dVol <= TOL_REL) And (dCg <= TOL_REL * size + TOL_POINT) And (lost <= TOL_POINT)
    Note "  Geometrie " & IIf(CheckSame, "unveraendert", "VERAENDERT")
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

' The solid whose centroid is nearest to the centroid in mp (within 1 mm), or Nothing.
Private Function BodyNear(ByVal p As SldWorks.PartDoc, ByVal mp As Variant) As SldWorks.Body2
    Dim v As Variant, i As Long, b As SldWorks.Body2, m As Variant, d As Double, best As Double
    best = 0.001
    v = p.GetBodies2(SOLID_BODY, False)
    If IsEmpty(v) Then Exit Function
    For i = LBound(v) To UBound(v)
        Set b = v(i)
        m = b.GetMassProperties(1#)
        If Not IsEmpty(m) Then
            d = Dist(m(0), m(1), m(2), mp(0), mp(1), mp(2))
            If d <= best Then
                best = d
                Set BodyNear = b
            End If
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

Private Function Dist(ByVal x1 As Double, ByVal y1 As Double, ByVal z1 As Double, _
        ByVal x2 As Double, ByVal y2 As Double, ByVal z2 As Double) As Double
    Dist = Sqr((x1 - x2) ^ 2 + (y1 - y2) ^ 2 + (z1 - z2) ^ 2)
End Function

' ---- Protokoll -----------------------------------------------------------------

Private Sub StartLog(ByVal fileName As String)
    Dim f As Integer
    logPath = Environ("TEMP") & "\" & fileName
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

' Letters and digits only, for the log file name.
Private Function SafeName(ByVal s As String) As String
    Dim i As Long, ch As String, r As String
    For i = 1 To Len(s)
        ch = Mid(s, i, 1)
        If ch Like "[A-Za-z0-9]" Then
            r = r & ch
        Else
            r = r & "_"
        End If
    Next i
    SafeName = Left(r, 40)
End Function

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

Private Function MmOrNone(ByVal m As Double) As String
    If m >= 1E+29 Then
        MmOrNone = "(keine Eckpunkte)"
    Else
        MmOrNone = Mm(m)
    End If
End Function

Private Function Pt(ByVal x As Double, ByVal y As Double, ByVal z As Double) As String
    Pt = "(" & Format(x * 1000#, "0.0000") & "; " & Format(y * 1000#, "0.0000") & "; " & _
        Format(z * 1000#, "0.0000") & ") mm"
End Function

Private Function Vol(ByVal v As Double) As String
    Vol = Format(v * 1000000000#, "0.000") & " mm3"
End Function
