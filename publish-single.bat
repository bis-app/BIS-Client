@echo off
cd /d "%~dp0"
dotnet publish src\BISClient\BISClient.csproj -c Release -p:PublishProfile=Win64Single
pause
