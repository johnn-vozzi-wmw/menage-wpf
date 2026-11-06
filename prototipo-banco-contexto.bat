@echo off
rem Prototipo do redesenho do Contexto do banco (feature/banco-contexto-redesenho). Dados isolados: nao toca no Menage instalado.
start "" "%~dp0src\Menage.App\bin\Debug\net8.0-windows\Menage.exe" --dados "%LOCALAPPDATA%\MenagePrototipo"
