$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AssistantPaths.Common.ps1')

function Assert-AssistantPath([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Synthetic fixtures remain under .artifacts for inspection; no real runtime,
# credentials, database or process is read or changed by this test.
$taskRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskTestRoot = Join-Path $taskRepository ('.artifacts/assistant-paths/' + [Guid]::NewGuid().ToString('N'))
try {
    $taskWorkspace = Join-Path $taskTestRoot 'relocated/GaoApp'
    $taskSibling = Join-Path $taskTestRoot 'relocated/ChatbotLocal'
    $taskProject = Join-Path $taskSibling 'src/ChatbotLocal.Web/ChatbotLocal.Web.csproj'
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskProject), $taskWorkspace -Force | Out-Null
    Set-Content -LiteralPath $taskProject -Value '<Project />'
    Assert-AssistantPath ((Resolve-AssistantChatbotDirectory -WorkspaceDirectory $taskWorkspace -Path '') -eq $taskSibling) 'The default must follow the relocated sibling checkout.'
    Assert-AssistantPath ((Resolve-AssistantChatbotDirectory -WorkspaceDirectory $taskWorkspace -Path '../ChatbotLocal') -eq $taskSibling) 'An explicit relative checkout must resolve from the GaoApp workspace.'
    Assert-AssistantPath ((Resolve-AssistantChatbotDirectory -WorkspaceDirectory $taskWorkspace -Path $taskSibling) -eq $taskSibling) 'An explicit absolute checkout must remain supported.'
    $taskMissingRejected = $false
    try { Resolve-AssistantChatbotDirectory -WorkspaceDirectory $taskWorkspace -Path 'missing' | Out-Null }
    catch { $taskMissingRejected = $_.Exception.Message.Contains('CHATBOTLOCAL_DIRECTORY') -and $_.Exception.Message.Contains((Join-Path $taskWorkspace 'missing')) }
    Assert-AssistantPath $taskMissingRejected 'A missing explicit checkout must report the selected path and override.'

    $taskOldRuntime = Join-Path $taskTestRoot 'old/Logs/assistant-integration-20261012/local-ui'
    $taskRuntime = Join-Path $taskWorkspace 'Logs/assistant-integration-20261012/local-ui'
    $taskConfig = [pscustomobject]@{
        AssistantApi = [pscustomobject]@{ DatabasePath = (Join-Path $taskOldRuntime 'history.db') }
        AssistantOperations = [pscustomobject]@{ DatabasePath = (Join-Path $taskOldRuntime 'operations.db') }
        ProductDatabase = [pscustomobject]@{ DatabasePath = (Join-Path $taskOldRuntime 'legacy-products.db'); SeedDataPath = '/old/ChatbotLocal/src/ChatbotLocal.Web/Data/Seed/demo-products.json' }
        Conversation = [pscustomobject]@{ DatabasePath = '/custom/history.db' }
        Gemini = [pscustomobject]@{ ApiKeyFilePath = '/custom/gemini-key.txt'; SecretDirectory = '/old/ChatbotLocal/src/ChatbotLocal.Web/App_Data' }
    }
    Update-AssistantLocalRuntimePaths -Configuration $taskConfig -ChatbotDirectory $taskSibling -RuntimeDirectory $taskRuntime -ExistingRuntime
    Assert-AssistantPath ($taskConfig.AssistantApi.DatabasePath -eq (Join-Path $taskRuntime 'history.db')) 'History must follow the relocated Logs directory.'
    Assert-AssistantPath ($taskConfig.AssistantOperations.DatabasePath -eq (Join-Path $taskRuntime 'operations.db')) 'Operations must follow the relocated Logs directory.'
    Assert-AssistantPath ($taskConfig.ProductDatabase.SeedDataPath -eq (Join-Path $taskSibling 'src/ChatbotLocal.Web/Data/Seed/demo-products.json')) 'Seed data must follow the selected ChatbotLocal checkout.'
    Assert-AssistantPath ($taskConfig.Gemini.SecretDirectory -eq (Join-Path $taskSibling 'src/ChatbotLocal.Web/App_Data')) 'Default secret directory must follow the selected checkout.'
    Assert-AssistantPath ($taskConfig.Gemini.ApiKeyFilePath -eq '/custom/gemini-key.txt') 'An explicit custom credential path must be preserved.'
    Assert-AssistantPath ($taskConfig.Conversation.DatabasePath -eq '/custom/history.db') 'An explicit custom database path must be preserved.'

    New-Item -ItemType Directory -Path $taskOldRuntime -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $taskOldRuntime 'history.db') -Value 'synthetic fixture data; not a database'
    $taskConfig.AssistantApi.DatabasePath = Join-Path $taskOldRuntime 'history.db'
    $taskDataRejected = $false
    try { Update-AssistantLocalRuntimePaths -Configuration $taskConfig -ChatbotDirectory $taskSibling -RuntimeDirectory $taskRuntime -ExistingRuntime }
    catch { $taskDataRejected = $_.Exception.Message.Contains('Existing chatbot database remains') }
    Assert-AssistantPath $taskDataRejected 'Startup must not replace an existing database with a new empty location.'
    $taskSparseConfig = [pscustomobject]@{ Gemini = $null; AssistantApi = [pscustomobject]@{ Enabled = $true } }
    Update-AssistantLocalRuntimePaths -Configuration $taskSparseConfig -ChatbotDirectory $taskSibling -RuntimeDirectory $taskRuntime
    Assert-AssistantPath (@($taskSparseConfig.PSObject.Properties).Count -eq 2 -and @($taskSparseConfig.AssistantApi.PSObject.Properties).Count -eq 1) 'Missing optional sections and DatabasePath must remain untouched under StrictMode.'
    Write-Output 'Passed 12 assistant relocation path checks.'
} finally {
    Write-Output "Synthetic fixtures: $taskTestRoot"
}
