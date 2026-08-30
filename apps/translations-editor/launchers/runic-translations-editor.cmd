@echo off
setlocal
if "%~1"=="" (
  "%~dp0Runic.Translations.Editor.exe" edit "%CD%"
) else (
  "%~dp0Runic.Translations.Editor.exe" %*
)
exit /b %ERRORLEVEL%
