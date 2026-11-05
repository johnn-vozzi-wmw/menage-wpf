@echo off
rem Protótipo das correções (feature/correcoes-uso). Dados isolados: não toca no Menage instalado.
rem Para trazer seus projetos: copie o conteúdo de %LOCALAPPDATA%\Menage para %LOCALAPPDATA%\MenagePrototipo (com o protótipo fechado).
start "" "%~dp0src\Menage.App\bin\Debug\net8.0-windows\Menage.exe" --dados "%LOCALAPPDATA%\MenagePrototipo"
