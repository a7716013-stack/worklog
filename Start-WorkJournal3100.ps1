param([int]$Port=5202)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$dll=Join-Path $PSScriptRoot 'artifacts/preview3100/WorkJournal.Api.dll'
if(!(Test-Path -LiteralPath $dll)){throw 'Publish WorkJournal.Api to artifacts/preview3100 first.'}
if([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne '3.1.0'){throw 'Expected version 3.1.0'}
& dotnet $dll --environment Development --contentRoot (Join-Path $PSScriptRoot 'src/WorkJournal.Web') --urls "http://localhost:$Port" --Api:HostWebUi true --StrategyCenter:Enabled false --RadarTracking:SchedulerEnabled false --InvestmentInsights:SchedulerEnabled false --ConnectionStrings:JournalDatabase 'Server=.\SQLEXPRESS;Database=WorkJournal;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;'
