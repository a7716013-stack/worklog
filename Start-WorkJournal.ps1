$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& 'C:\Program Files\dotnet\dotnet.exe' run --project src\WorkJournal.Web --launch-profile http

