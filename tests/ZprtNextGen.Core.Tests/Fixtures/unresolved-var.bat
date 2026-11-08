@echo off
start "" /min "C:\Program Files\ZPRT\engine\bin\winws.exe" --wf-tcp=80,443 %UNKNOWN% --filter-lists="%LISTS%list-general.txt"
