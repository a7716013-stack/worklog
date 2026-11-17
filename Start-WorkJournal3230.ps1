param([int]$Port=5208)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$dll=Join-Path $PSScriptRoot 'artifacts/preview3230/WorkJournal.Api.dll'
if(!(Test-Path -LiteralPath $dll)){throw 'Publish WorkJournal.Api to artifacts/preview3230 first.'}
if([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne '3.2.3'){throw 'Expected version 3.2.3'}
& dotnet $dll --environment Development --contentRoot (Join-Path $PSScriptRoot 'artifacts/preview3230') --urls "http://localhost:$Port" --Api:HostWebUi true --StrategyCenter:Enabled false --RadarTracking:SchedulerEnabled false --InvestmentInsights:SchedulerEnabled false --ConnectionStrings:JournalDatabase 'Server=.\SQLEXPRESS;Database=WorkJournal;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;'
