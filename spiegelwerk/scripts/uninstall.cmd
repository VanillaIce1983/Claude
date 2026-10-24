@echo off
rem Entfernt die Registrierung des Add-Ins. Als Administrator ausfuehren, SOLIDWORKS vorher schliessen.
setlocal
set "DLL=%~dp0..\src\Spiegelwerk\bin\Release\net48\Spiegelwerk.dll"
if not "%~1"=="" set "DLL=%~1"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /unregister "%DLL%"
