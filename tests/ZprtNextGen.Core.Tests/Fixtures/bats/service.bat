@echo off
chcp 65001 > nul

cd /d "%~dp0"

set "BIN=%~dp0bin\"
set "LISTS=%~dp0lists\"
cd /d %BIN%

:: service.bat is not a zapret preset and must be ignored by the importer.
echo service stub
