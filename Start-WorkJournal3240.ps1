param([int]$Port=5208)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$dll=Join-Path $PSScriptRoot 'artifacts/preview3240/WorkJournal.Api.dll'
if(!(Test-Path -LiteralPath $dll)){throw 'Publish WorkJournal.Api to artifacts/preview3240 first.'}
if([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne '3.2.4'){throw 'Expected version 3.2.4'}
& dotnet $dll --environment Development --contentRoot (Join-Path $PSScriptRoot 'artifacts/preview3240') --urls "http://localhost:$Port" --Api:HostWebUi true --StrategyCenter:Enabled false --RadarTracking:SchedulerEnabled false --InvestmentInsights:SchedulerEnabled false --ConnectionStrings:JournalDatabase 'Server=.\SQLEXPRESS;Database=WorkJournal;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;'
