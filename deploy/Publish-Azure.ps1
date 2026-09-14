param(
    [string]$ResourceGroup = 'rg-worklog-test',
    [string]$WebAppName = 'worklog-a7716013-test'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
Set-Location -LiteralPath $root
$account = az account show -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Run az login before deployment.' }
if ($account.state -ne 'Enabled') { throw 'Select an enabled Azure subscription.' }
$web = az webapp show -g $ResourceGroup -n $WebAppName -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'The configured Azure web app does not exist.' }
dotnet publish src/WorkJournal.Web -c Release -o artifacts/azure-publish --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Compress-Archive -Path artifacts/azure-publish/* -DestinationPath artifacts/worklog-azure.zip -Force
az webapp deploy -g $ResourceGroup -n $WebAppName --src-path artifacts/worklog-azure.zip --type zip --output none --only-show-errors
if ($LASTEXITCODE -ne 0) { throw 'Azure deployment failed.' }
Write-Output ("Deployed: https://" + $web.defaultHostName)
