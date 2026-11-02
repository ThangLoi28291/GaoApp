param(
    [int]$Port = 5219,
    [string]$GaoAppUrl = 'http://localhost:7051',
    [string]$ChatbotDirectory = $env:CHATBOTLOCAL_DIRECTORY
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssistantPaths.Common.ps1')
$ChatbotDirectory = Resolve-AssistantChatbotDirectory -Path $ChatbotDirectory
$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskEvidence = Join-Path $taskWorkspace 'Logs/assistant-integration-20261012'
$taskRoot = Join-Path $taskEvidence 'local-ui'
$taskDll = Join-Path $taskEvidence 'chatbot-build/bin/ChatbotLocal.Web/debug/ChatbotLocal.Web.dll'
$taskTemplate = Join-Path $taskEvidence 'chatbot-runtime/runtime.json'
$taskConfigFile = Join-Path $taskRoot 'runtime.json'
$taskPidFile = Join-Path $taskRoot 'process.json'
$taskHost = Join-Path $ChatbotDirectory 'src/ChatbotLocal.Web'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid local port.' }
$taskSource = [Uri]$GaoAppUrl
if (-not $taskSource.IsLoopback -or $taskSource.Scheme -notin @('http', 'https')) { throw 'GaoApp URL must be local.' }
if (-not (Test-Path -LiteralPath $taskDll) -or -not (Test-Path -LiteralPath $taskTemplate)) {
    throw 'The local acceptance build/config is missing. Prepare the matching ChatbotLocal build first.'
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Local port $Port is already in use; no existing process was stopped."
}
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$taskExistingRuntime = Test-Path -LiteralPath $taskConfigFile
if (-not $taskExistingRuntime) {
    $taskConfig = Get-Content -LiteralPath $taskTemplate -Raw | ConvertFrom-Json
    $taskConfig.AssistantApi.AuthorizationBaseUrl = $GaoAppUrl.TrimEnd('/')
    $taskConfig.AssistantData.BaseUrl = $GaoAppUrl.TrimEnd('/')
} else {
    $taskConfig = Get-Content -LiteralPath $taskConfigFile -Raw | ConvertFrom-Json
    if ($taskConfig.AssistantApi.AuthorizationBaseUrl -ne $GaoAppUrl.TrimEnd('/') -or $taskConfig.AssistantData.BaseUrl -ne $GaoAppUrl.TrimEnd('/')) {
        throw 'The existing local runtime points to another GaoApp URL. Review its config before restarting.'
    }
}
Update-AssistantLocalRuntimePaths -Configuration $taskConfig -ChatbotDirectory $ChatbotDirectory -RuntimeDirectory $taskRoot -ExistingRuntime:$taskExistingRuntime
$taskConfig | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskConfigFile -Encoding utf8
$env:CHATBOTLOCAL_CONFIG_FILE = $taskConfigFile
$taskLaunch = @{
    FilePath = (Get-Command dotnet).Source
    ArgumentList = @(('"' + $taskDll + '"'), '--environment=Development', "--urls=http://127.0.0.1:$Port")
    WorkingDirectory = $taskHost
    WindowStyle = 'Hidden'
    PassThru = $true
    RedirectStandardOutput = Join-Path $taskRoot 'stdout.log'
    RedirectStandardError = Join-Path $taskRoot 'stderr.log'
}
$taskProcess = Start-Process @taskLaunch
[pscustomobject]@{ProcessId=$taskProcess.Id;Port=$Port;GaoAppUrl=$GaoAppUrl;ConfigurationFile=$taskConfigFile;StartedUtc=[DateTime]::UtcNow} |
    ConvertTo-Json | Set-Content -LiteralPath $taskPidFile -Encoding utf8
Write-Output "ChatbotLocal local started: PID $($taskProcess.Id), http://127.0.0.1:$Port."
Write-Output "GaoApp: $GaoAppUrl/admin. Refresh the page to load the assistant."
