@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4.x x64 compiler not found.
  exit /b 1
)
if not exist bin mkdir bin
if not exist bin exit /b 1
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:src\DesktopJelly.manifest /out:bin\DesktopJelly.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll src\DesktopJelly.cs src\JellyLayer.cs src\PerformanceLog.cs src\LocalAudioPlayer.cs
if errorlevel 1 exit /b 1
if exist reference-audio.wav (
  copy /y reference-audio.wav bin\reference-audio.wav >nul
  if errorlevel 1 exit /b 1
)
echo Built: bin\DesktopJelly.exe
