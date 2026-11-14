@echo off
cd /d "%~dp0"
python -m viewer %*
if errorlevel 1 pause
