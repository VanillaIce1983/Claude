; Eigene Teile des Windows-Installers (NSIS), eingebunden über "nsis.include" in package.json.

; Das ARM64-Setup bricht auf einem PC ohne ARM ab, statt eine Installation ohne Programmdateien anzulegen.
; customInit läuft in .onInit, also bevor eine vorhandene Fassung entfernt wird.
!macro customInit
  !ifdef APP_ARM64
    !ifndef APP_64
      ${IfNot} ${IsNativeARM64}
        MessageBox MB_OK|MB_ICONSTOP "Dieses Setup ist für Windows auf ARM. Bitte PDFwerk-Setup-${VERSION}-x64.exe verwenden." /SD IDOK
        SetErrorLevel 2
        Quit
      ${EndIf}
    !endif
  !endif
!macroend

; Läuft PDFwerk noch, wird es nicht hart beendet (ungespeicherte Änderungen gingen ohne Rückfrage verloren):
; erst normal schließen lassen, dann fragt PDFwerk selbst nach; ist es danach noch offen, warten, bis es geschlossen ist.
; Gilt für Installer und Deinstaller; im stillen Modus bricht die Installation ab, solange PDFwerk läuft.
!macro customCheckAppRunning
  !insertmacro IS_POWERSHELL_AVAILABLE
  !insertmacro FIND_PROCESS "${APP_EXECUTABLE_FILENAME}" $R0
  ${If} $R0 == 0
    nsExec::Exec `"$CmdPath" /C taskkill /IM "${APP_EXECUTABLE_FILENAME}"`
    Pop $R1
    Sleep 2000
  ${EndIf}
  ${Do}
    !insertmacro FIND_PROCESS "${APP_EXECUTABLE_FILENAME}" $R0
    ${If} $R0 != 0
      ${ExitDo}
    ${EndIf}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "PDFwerk ist noch geöffnet. Bitte die Dokumente speichern, PDFwerk schließen und dann „Wiederholen“ wählen." /SD IDCANCEL IDRETRY +2
    Quit
  ${Loop}
!macroend

; Deinstallieren: zeigt der Standard für .pdf noch auf PDFwerk, den Eintrag entfernen, sonst verweist er ins Leere.
; Beim Aktualisieren schreibt der neue Installer ihn gleich wieder, dann bleibt er.
!macro customUnInstall
  ${ifNot} ${isUpdated}
    ReadRegStr $R0 SHELL_CONTEXT "Software\Classes\.pdf" ""
    ${if} $R0 == "PDFwerk.Dokument"
    ${orIf} $R0 == "PDF-Dokument"
      DeleteRegValue SHELL_CONTEXT "Software\Classes\.pdf" ""
    ${endIf}
  ${endIf}
!macroend
