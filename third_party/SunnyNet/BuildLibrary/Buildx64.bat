@echo off
set CGO_ENABLED=1
set GOOS=windows
set GOARCH=amd64
set tmpPath=%~dp0
cd %tmpPath:~0,1%:
for %%I in ("%tmpPath%..\") do set "parentPath=%%~fI"
cd %parentPath%
@echo on
go build -buildmode=c-shared  -ldflags "-s -w" -o "%tmpPath%Library\windows\x64\Sunny64.dll"