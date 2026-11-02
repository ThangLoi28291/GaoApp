#requires -Version 5.1
# Synthetic files and mocked IIS commands only; never changes local/remote IIS.
$ErrorActionPreference = 'Stop'
$restoreScript = Join-Path $PSScriptRoot 'Restore-GaoAppAssistantIis.ps1'
$testRoot = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('Logs/assistant-publish-20261012/test-' + [Guid]::NewGuid().ToString('N'))
foreach ($folder in @('web', 'config', 'secrets', 'data', 'backups')) {
    New-Item -ItemType Directory -Path (Join-Path $testRoot $folder) -Force | Out-Null
}
$testApp = Join-Path $testRoot 'web'
$testRuntime = Join-Path $testRoot 'config/runtime.json'
$testKey = Join-Path $testRoot 'secrets/private.pem'
$testWebConfig = Join-Path $testApp 'web.config'
$testIssuer = 'https://chonthanh.gaomart.com.vn:8433'
$testParams = @{
    GaoAppDirectory = $testApp; ChatbotRuntimeFile = $testRuntime;
    PrivateKeyFile = $testKey; StoreSettingsDirectory = (Join-Path $testRoot 'data');
    BackupDirectory = (Join-Path $testRoot 'backups')
}
[IO.File]::WriteAllText((Join-Path $testApp 'GaoApp.Web.dll'), 'synthetic assembly')
[IO.File]::WriteAllText($testKey, 'SYNTHETIC_KEY_MUST_NOT_BE_READ')
$runtimeText = @{ AssistantApi = @{ Enabled = $true; Issuer = $testIssuer; PublicKeyFiles = @{ 'gaoapp-assistant-1' = 'public.pem' } }; Gemini = @{ Secret = 'SECRET_MUST_NOT_BE_COPIED' } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($testRuntime, $runtimeText)
$cleanWebConfig = '<configuration><system.webServer><aspNetCore processPath="dotnet" arguments="GaoApp.Web.dll" /></system.webServer></configuration>'
[IO.File]::WriteAllText($testWebConfig, $cleanWebConfig)
$global:assistantIisTestPool = @{}
$global:assistantIisTestCalls = [Collections.Generic.List[string]]::new()
$global:assistantIisTestFailureAt = 0
$global:assistantIisTestMutationCount = 0
$global:assistantIisTestShared = $false
function Import-Module { param($Name, $ErrorAction) if ($Name -ne 'WebAdministration') { throw 'Unexpected module.' } }
function Get-Website {
    param($Name)
    $site = [pscustomobject]@{ Name = 'GaoApp'; applicationPool = 'GaoAppPool'; physicalPath = $testApp }
    if ($Name) { return $site }
    $site
    if ($global:assistantIisTestShared) { [pscustomobject]@{ Name = 'OtherSite'; applicationPool = 'GaoAppPool'; physicalPath = $testApp } }
}
function Get-WebApplication { param($Site) }
function Get-WebAppPoolState { param($Name) [pscustomobject]@{ Value = 'Started' } }
function Get-WebConfigurationProperty {
    param($PSPath, $Filter, $Name)
    if ($Filter -notmatch "add\[@name='GaoAppPool'\]/environmentVariables$") { throw 'Unexpected pool read.' }
    [pscustomobject]@{ Collection = @($global:assistantIisTestPool.Keys | ForEach-Object { [pscustomobject]@{ name = $_; value = $global:assistantIisTestPool[$_] } }) }
}
function Test-Mutation {
    param($Name)
    if ($Name -notlike 'Assistant__*') { throw 'Attempted to modify an unrelated setting.' }
    $global:assistantIisTestMutationCount++
    if ($global:assistantIisTestFailureAt -eq $global:assistantIisTestMutationCount) { throw 'Synthetic IIS write failure.' }
    $global:assistantIisTestCalls.Add($Name)
}
function Set-WebConfigurationProperty {
    param($PSPath, $Filter, $Name, $Value)
    if ($Filter -notmatch "environmentVariables/add\[@name='([^']+)'\]$") { throw 'Invalid variable filter.' }
    $key = $Matches[1]
    Test-Mutation $key
    $global:assistantIisTestPool[$key] = $Value
}
function Add-WebConfigurationProperty {
    param($PSPath, $Filter, $Name, $Value)
    Test-Mutation $Value.name
    $global:assistantIisTestPool[$Value.name] = $Value.value
}
function Remove-WebConfigurationProperty {
    param($PSPath, $Filter, $Name, $AtElement)
    Test-Mutation $AtElement.name
    $global:assistantIisTestPool.Remove($AtElement.name)
}
function Restart-WebAppPool { param($Name) if ($Name -ne 'GaoAppPool') { throw 'Wrong pool.' }; $global:assistantIisTestCalls.Add('RESTART') }
function Assert-True { param($Condition, $Message) if (!$Condition) { throw $Message } }
function Expect-Rejection {
    param([scriptblock]$Action, $Message)
    try { & $Action; throw 'EXPECTED_REJECTION_MISSING' }
    catch { if ($_.Exception.Message -notlike "*$Message*") { throw } }
}
function Reset-Mock {
    $global:assistantIisTestPool = @{ AllowedHosts = 'production.example.test'; ConnectionStrings__DefaultConnection = 'SECRET_MUST_NOT_BE_COPIED' }
    $global:assistantIisTestCalls.Clear(); $global:assistantIisTestFailureAt = 0; $global:assistantIisTestMutationCount = 0; $global:assistantIisTestShared = $false
    [IO.File]::WriteAllText($testWebConfig, $cleanWebConfig)
    [IO.File]::WriteAllText($testRuntime, $runtimeText)
}
Reset-Mock
$tokens = $null; $parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseFile($restoreScript, [ref]$tokens, [ref]$parseErrors)
Assert-True (@($parseErrors).Count -eq 0) 'PowerShell parser error.'
Write-Output 'PASS Windows PowerShell syntax'

& $restoreScript @testParams -WhatIf | Out-Null
Assert-True ($global:assistantIisTestCalls.Count -eq 0 -and (Get-ChildItem (Join-Path $testRoot 'backups') -File).Count -eq 0) 'WhatIf wrote configuration.'
Write-Output 'PASS WhatIf leaves IIS and backup directory untouched'

$result = & $restoreScript @testParams | ConvertFrom-Json
Assert-True ($result.status -eq 'GAOAPP_ASSISTANT_CONFIGURED' -and $global:assistantIisTestPool['Assistant__Enabled'] -eq 'true') 'Assistant not restored.'
Assert-True ($global:assistantIisTestPool['Assistant__Issuer'] -eq $testIssuer) 'Existing issuer changed to the new website port.'
Assert-True ($global:assistantIisTestPool['Assistant__BaseUrl'] -eq 'https://chat.gaomart.com.vn:8444') 'Incorrect backend address.'
Assert-True ($global:assistantIisTestPool.AllowedHosts -eq 'production.example.test' -and $global:assistantIisTestPool.ConnectionStrings__DefaultConnection -eq 'SECRET_MUST_NOT_BE_COPIED') 'Unrelated pool setting changed.'
Assert-True ([IO.File]::ReadAllText($testWebConfig) -ceq $cleanWebConfig) 'web.config was rewritten.'
Assert-True ([IO.File]::ReadAllText($result.backupFile) -notmatch 'SECRET_MUST_NOT_BE_COPIED|SYNTHETIC_KEY_MUST_NOT_BE_READ') 'Secret appeared in backup.'
Assert-True ((@($global:assistantIisTestCalls | Where-Object { $_ -eq 'RESTART' })).Count -eq 1) 'Pool must restart once.'
Write-Output 'PASS restore preserves issuer, unrelated settings, keys and web.config'

$callsBefore = $global:assistantIisTestCalls.Count
$again = & $restoreScript @testParams | ConvertFrom-Json
Assert-True (!$again.changed -and $global:assistantIisTestCalls.Count -eq $callsBefore) 'Repeat unnecessarily changed or restarted IIS.'
# A publish overwrites web.config; these host variables remain in the pool.
[IO.File]::WriteAllText($testWebConfig, $cleanWebConfig)
$afterPublish = & $restoreScript @testParams | ConvertFrom-Json
Assert-True (!$afterPublish.changed -and $global:assistantIisTestPool['Assistant__Enabled'] -eq 'true') 'Publish lost assistant pool config.'
Write-Output 'PASS repeated execution and web.config replacement preserve connection'

Reset-Mock
$global:assistantIisTestPool['Assistant__Enabled'] = 'false'; $global:assistantIisTestFailureAt = 4
Expect-Rejection { & $restoreScript @testParams } 'prior variables were restored'
Assert-True ($global:assistantIisTestPool.Count -eq 3 -and $global:assistantIisTestPool['Assistant__Enabled'] -eq 'false' -and 'RESTART' -notin $global:assistantIisTestCalls) 'Rollback failed or restarted the pool.'
Write-Output 'PASS failed IIS write restores previous values without restarting'

Reset-Mock; $global:assistantIisTestShared = $true
Expect-Rejection { & $restoreScript @testParams } 'shared by another site'
Assert-True ($global:assistantIisTestCalls.Count -eq 0) 'Shared pool was modified.'
Write-Output 'PASS shared pool rejected before mutation'

Reset-Mock
[IO.File]::WriteAllText($testWebConfig, '<configuration><aspNetCore><environmentVariables><environmentVariable name="Assistant__Issuer" value="https://wrong.example.test" /></environmentVariables></aspNetCore></configuration>')
Expect-Rejection { & $restoreScript @testParams } 'Conflicting legacy'
Assert-True ($global:assistantIisTestCalls.Count -eq 0) 'Conflicting ANCM variable was ignored.'
Write-Output 'PASS conflicting web.config variables rejected before mutation'

Reset-Mock
[IO.File]::WriteAllText($testRuntime, ($runtimeText.Replace('gaoapp-assistant-1', 'different-key')))
Expect-Rejection { & $restoreScript @testParams } 'existing GaoApp issuer/key identifier'
Assert-True ($global:assistantIisTestCalls.Count -eq 0) 'Mismatched existing signing key was ignored.'
Write-Output 'PASS existing key identifier required'

Reset-Mock
Expect-Rejection { & $restoreScript @testParams -BaseUrl 'http://chat.gaomart.com.vn:8444' } 'trusted HTTPS'
$insideWeb = @{}; foreach ($entry in $testParams.GetEnumerator()) { $insideWeb[$entry.Key] = $entry.Value }
$insideWeb.StoreSettingsDirectory = Join-Path $testApp 'data'
Expect-Rejection { & $restoreScript @insideWeb } 'outside the published'
Write-Output 'PASS HTTPS and persistent paths required'

Write-Output '9 checks passed; real IIS/AI/database calls: 0'
