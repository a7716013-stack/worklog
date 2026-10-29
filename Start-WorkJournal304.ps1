param([int]$Port = 5194)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$dll = Join-Path $PSScriptRoot 'artifacts/build304/bin/WorkJournal.Web/release/WorkJournal.Web.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build first: dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build304' }
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne '3.0.4') { throw 'Expected local build 3.0.4' }
& dotnet $dll --environment Development --contentRoot (Join-Path $PSScriptRoot 'src/WorkJournal.Web') --urls "http://localhost:$Port" --StrategyCenter:Enabled false --RadarTracking:SchedulerEnabled false --InvestmentInsights:SchedulerEnabled false --ConnectionStrings:JournalDatabase 'Server=.\SQLEXPRESS;Database=WorkJournal;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;'
