@echo off
rem Compila con el csc.exe que trae Windows (.NET Framework 4)
set CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0AzureDreamsTool.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "%~dp0src\*.cs"
if errorlevel 1 (echo ERROR de compilacion & exit /b 1) else (echo Compilado OK)
