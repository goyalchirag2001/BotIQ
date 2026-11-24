param([Parameter(Mandatory=$true)][string]$Url,[Parameter(Mandatory=$true)][string]$ApprovedHost,[Parameter(Mandatory=$true)][switch]$Authorized)
$ErrorActionPreference = 'Stop'
if (-not $Authorized) { throw 'Confirm you are authorised to record this website.' }
$target = [Uri]$Url
if ($target.Scheme -ne 'https' -or $target.Host -ne $ApprovedHost -or $target.UserInfo -or -not $target.IsDefaultPort) { throw 'Use an HTTPS URL on the exact approved hostname, without credentials or a custom port.' }
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1'
if (!(Test-Path $cli)) { throw 'Build BotIQ first to generate the Playwright CLI.' }
$dir = Join-Path $root 'src/BotIQ.Api/App_Data/recordings'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$output = Join-Path $dir ('capture-' + [Guid]::NewGuid().ToString('N') + '.har')
Write-Host 'Record only an authorised workflow. Do not sign in or submit personal data, reservations or payments. Close the recording browser to save the HAR. Raw HAR may contain secrets; keep it local.'
& pwsh -NoProfile -File $cli open "--save-har=$output" "--save-har-glob=https://$ApprovedHost/**" $target.AbsoluteUri
if ($LASTEXITCODE -ne 0) { throw 'Playwright recording failed. Inspect CLI error; respect site and IT access restrictions.' }
if (!(Test-Path $output)) { throw 'HAR not created. Close the recording window normally and check CLI output.' }
Write-Host "Saved locally: $output"
