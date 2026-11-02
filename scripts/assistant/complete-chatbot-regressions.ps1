param([string]$ChatbotDirectory = $env:CHATBOTLOCAL_DIRECTORY)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AssistantPaths.Common.ps1')
$taskRoot = Resolve-AssistantChatbotDirectory -Path $ChatbotDirectory
$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskBackup = Join-Path $taskWorkspace 'Logs/assistant-integration-20261012/baseline'
$taskFile = Join-Path $taskRoot 'src/ChatbotLocal.Web/Services/ApiV1/AssistantCatalogClarification.cs'
$taskText = [IO.File]::ReadAllText($taskFile)
$taskOld = '(?:gia|si|le|ban|buon|bao|nhieu|nhiu|tien|ca|hai|2|loai|cho|em|toi|minh|anh|chi|giup|voi|luon|nhe|nha)'
$taskNew = '(?:gia|si|le|ban|buon|bao|nhieu|nhiu|tien|ca|hai|2|loai|va|cho|em|toi|minh|anh|chi|giup|voi|luon|nhe|nha)'
if ($taskText.Contains($taskOld)) {
    [IO.File]::WriteAllText($taskFile, $taskText.Replace($taskOld, $taskNew), [Text.UTF8Encoding]::new($false))
}
$taskFile = Join-Path $taskRoot 'tests/ChatbotLocal.Tests/AssistantCatalogClarificationTests.cs'
$taskText = [IO.File]::ReadAllText($taskFile)
if (-not $taskText.Contains('PartialCatalogCanRefineBeyondTheFirstPage')) {
    New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
    Copy-Item -LiteralPath $taskFile -Destination (Join-Path $taskBackup 'ChatbotLocal__AssistantCatalogClarificationTests.cs')
    $taskAddition = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'chatbot-regression-additions.txt'))
    $taskIndex = $taskText.LastIndexOf('}')
    if ($taskIndex -lt 0) { throw 'Test class end missing' }
    $taskText = $taskText.Insert($taskIndex, $taskAddition)
    [IO.File]::WriteAllText($taskFile, $taskText, [Text.UTF8Encoding]::new($false))
}
Write-Output 'ChatbotLocal regression tests added for partial catalogs and price-only followups.'
