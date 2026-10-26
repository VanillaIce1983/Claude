@echo off
rem Entfernt die Registrierung aus install.cmd (Entwicklung). Als Administrator ausfuehren, SOLIDWORKS
rem vorher schliessen. Fehlt die DLL oder scheitert RegAsm, werden die Schluessel direkt geloescht.
rem Ein per MSI installiertes Spiegelwerk wird ueber Einstellungen > Apps entfernt.
setlocal
net session >nul 2>&1 || (echo Bitte als Administrator ausfuehren. & exit /b 1)
set "DLL=%~dp0..\src\Spiegelwerk\bin\Release\net48\Spiegelwerk.dll"
if not "%~1"=="" set "DLL=%~1"
set "CLSID={8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}"
if exist "%DLL%" (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /unregister "%DLL%" && goto check
  echo RegAsm ist gescheitert; die Schluessel werden direkt geloescht.
) else (
  echo Spiegelwerk.dll nicht gefunden; die Schluessel werden direkt geloescht.
)
reg delete "HKLM\SOFTWARE\SolidWorks\Addins\%CLSID%" /f /reg:64 >nul 2>&1
reg delete "HKCU\Software\SolidWorks\AddInsStartup\%CLSID%" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Classes\CLSID\%CLSID%" /f /reg:64 >nul 2>&1
reg delete "HKLM\SOFTWARE\Classes\Spiegelwerk.Addin" /f /reg:64 >nul 2>&1
:check
reg query "HKLM\SOFTWARE\SolidWorks\Addins\%CLSID%" /reg:64 >nul 2>&1 && (echo Der Add-In-Schluessel ist noch vorhanden. & exit /b 1)
echo Fertig. Spiegelwerk ist nicht mehr registriert.
