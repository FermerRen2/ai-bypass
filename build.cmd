@echo off
rem Сборка AiBypass компилятором из состава Windows (.NET Framework 4.x).
rem Ничего устанавливать не нужно: csc.exe уже лежит в системе.
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Не найден csc.exe — нужен .NET Framework 4.x.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /out:AiBypass.exe /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\AiBypass.cs

if errorlevel 1 (
  echo.
  echo Сборка не удалась.
  exit /b 1
)

echo.
echo Готово: AiBypass.exe
