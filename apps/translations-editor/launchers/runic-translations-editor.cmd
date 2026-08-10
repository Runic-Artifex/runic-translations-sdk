@echo off
setlocal
if "%~1"=="" (
  "%~dp0RunicTranslations.Editor.exe" edit "%CD%"
) else (
  "%~dp0RunicTranslations.Editor.exe" %*
)
exit /b %ERRORLEVEL%
