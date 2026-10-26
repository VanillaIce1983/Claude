@echo off
rem Registriert Spiegelwerk direkt aus dem Build-Ordner (Entwicklung). Anwender nehmen das MSI
rem (siehe README). Als Administrator ausfuehren.
rem Aufruf: install.cmd [Pfad\zu\Spiegelwerk.dll]
setlocal
net session >nul 2>&1 || (echo Bitte als Administrator ausfuehren. & exit /b 1)
set "DLL=%~dp0..\src\Spiegelwerk\bin\Release\net48\Spiegelwerk.dll"
if not "%~1"=="" set "DLL=%~1"
if not exist "%DLL%" (
  echo Spiegelwerk.dll nicht gefunden: %DLL%
  echo Zuerst bauen: dotnet build src\Spiegelwerk\Spiegelwerk.csproj -c Release
  exit /b 1
)
rem 64-Bit-RegAsm, weil SOLIDWORKS ein 64-Bit-Prozess ist. /codebase traegt den Pfad der DLL ein;
rem dabei schreibt SpiegelwerkAddin.Register die Schluessel unter HKLM\SOFTWARE\SolidWorks\Addins
rem und den Autostart unter HKCU - fuer das Konto, unter dem dieses Skript laeuft. Die Warnung RA0000
rem (nicht signierte Assembly mit /codebase) ist erwartet.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /codebase "%DLL%"
if errorlevel 1 exit /b 1
echo Fertig. SOLIDWORKS neu starten; das Add-In steht unter Extras ^> Zusatzanwendungen.
echo Lief das Skript unter einem anderen Administratorkonto: dort "Beim Start" einmal ankreuzen.
