@echo off
setlocal
cd /d "%~dp0"
rem Con cualquier argumento (por ejemplo "COMPILAR.bat ci") no se pausa al final: lo usa GitHub Actions.
set "NOPAUSE=%~1"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: No se ha encontrado csc.exe de .NET Framework 4.
  goto :fail
)
if not exist "bin" mkdir "bin"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
 /out:"bin\CopyexBackupAgent.exe" /win32manifest:"app.manifest" /win32icon:"Assets\CopyexBackupAgent.ico" ^
 /resource:"Assets\LogoCopyexHD.png",CopyexBackupAgent.Logo.png ^
 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Security.dll ^
 /recurse:*.cs
if errorlevel 1 (
  echo.
  echo *** ERROR DE COMPILACION ***
  goto :fail
)
copy /Y "App.config" "bin\CopyexBackupAgent.exe.config" >nul
echo.
echo Generado: bin\CopyexBackupAgent.exe

set "NSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if not exist "%NSIS%" set "NSIS=%ProgramFiles%\NSIS\makensis.exe"
if exist "%NSIS%" (
  "%NSIS%" /V2 "Instalador.nsi"
  if errorlevel 1 (
    echo *** ERROR AL CREAR EL INSTALADOR ***
    goto :fail
  )
  echo Generado: CopyexBackupAgent_Setup.exe
) else (
  echo NSIS no encontrado: no se genera el instalador.
)
if "%NOPAUSE%"=="" pause
exit /b 0

:fail
if "%NOPAUSE%"=="" pause
exit /b 1
