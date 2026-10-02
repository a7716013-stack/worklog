param(
    [Parameter(Mandatory = $true)][string]$CredentialsPath,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$credentialFile = Get-Item -LiteralPath $CredentialsPath
if ($credentialFile.PSIsContainer -or $credentialFile.Length -gt 1MB) { throw 'Expected a Google OAuth client JSON file smaller than 1 MB.' }
try { $document = Get-Content -LiteralPath $credentialFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { throw 'Invalid OAuth JSON. Download the Web application client JSON from Google Cloud.' }
$web = $document.web
if ($null -eq $web -or [string]::IsNullOrWhiteSpace($web.client_secret) -or
    [string]$web.client_id -notmatch '^[a-zA-Z0-9-]+\.apps\.googleusercontent\.com$') {
    throw 'Expected a Web application OAuth client, not a Desktop client or service account.'
}
$requiredRedirects = @('https://localhost:7180/signin-google', 'https://localhost:7180/GoogleCalendar/OAuthCallback')
foreach ($redirect in $requiredRedirects) {
    if (@($web.redirect_uris) -cnotcontains $redirect) { throw ('Add this authorized redirect URI in Google Cloud and download JSON again: ' + $redirect) }
}
if ($ValidateOnly) { Write-Output 'OAuth JSON validation passed. No secrets were written.'; return }
$project = Join-Path $PSScriptRoot 'src/WorkJournal.Web/WorkJournal.Web.csproj'
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
$secretValues = @{
    'Authentication:Google:ClientId' = [string]$web.client_id
    'Authentication:Google:ClientSecret' = [string]$web.client_secret
    'Authentication:Google:PublicOrigin' = 'https://localhost:7180'
}
# Send credentials over stdin, not process arguments, shell history, logs, or repository files.
$secretValues | ConvertTo-Json -Compress | & $dotnet user-secrets set --project $project
if ($LASTEXITCODE -ne 0) { throw 'User Secrets import failed.' }
Write-Output 'Google OAuth credentials saved to User Secrets. Restart WorkJournal with the HTTPS profile.'
