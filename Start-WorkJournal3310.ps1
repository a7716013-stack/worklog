$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DOTNET_ENVIRONMENT='Development'
$env:ConnectionStrings__JournalDatabase='Server=.\SQLEXPRESS;Database=WorkJournal_LocalGuest_331;Trusted_Connection=True;TrustServerCertificate=True;'
$env:LocalGuest__Enabled='true'
$env:LocalGuest__Initialize='true'
$env:Scoring__ResearchEnabled='false'
$previewRoot=Join-Path $PSScriptRoot 'artifacts/preview3310'
dotnet (Join-Path $previewRoot 'WorkJournal.Api.dll') --urls http://localhost:5212 --contentRoot $previewRoot
