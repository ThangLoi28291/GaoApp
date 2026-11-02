# Resolve external ChatbotLocal source without tying developer tools to a drive.
function Resolve-AssistantChatbotDirectory {
    param(
        [string]$Path = $env:CHATBOTLOCAL_DIRECTORY,
        [string]$WorkspaceDirectory = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')))
    )

    $taskWorkspacePath = [IO.Path]::GetFullPath($WorkspaceDirectory)
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $taskSelected = if ([IO.Path]::IsPathRooted($Path)) {
            [IO.Path]::GetFullPath($Path)
        } else {
            [IO.Path]::GetFullPath((Join-Path $taskWorkspacePath $Path))
        }
    } else {
        $taskSibling = [IO.Path]::GetFullPath((Join-Path $taskWorkspacePath '../ChatbotLocal'))
        # Also support the existing <drive>:/repo/ChatbotLocal layout on the
        # workspace's current drive; never fall back to another drive.
        $taskDriveRepository = Join-Path ([IO.Path]::GetPathRoot($taskWorkspacePath)) 'repo/ChatbotLocal'
        if (Test-Path -LiteralPath (Join-Path $taskSibling 'src/ChatbotLocal.Web/ChatbotLocal.Web.csproj') -PathType Leaf) {
            $taskSelected = $taskSibling
        } elseif (Test-Path -LiteralPath (Join-Path $taskDriveRepository 'src/ChatbotLocal.Web/ChatbotLocal.Web.csproj') -PathType Leaf) {
            $taskSelected = $taskDriveRepository
        } else {
            throw "ChatbotLocal source is missing. Expected '$taskSibling' or '$taskDriveRepository'. Set CHATBOTLOCAL_DIRECTORY or pass -ChatbotDirectory to its checkout."
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $taskSelected 'src/ChatbotLocal.Web/ChatbotLocal.Web.csproj') -PathType Leaf)) {
        throw "ChatbotLocal source is missing at '$taskSelected'. Set CHATBOTLOCAL_DIRECTORY or pass -ChatbotDirectory to its checkout."
    }
    return $taskSelected
}

function Update-AssistantLocalRuntimePaths {
    param(
        [Parameter(Mandatory)]$Configuration,
        [Parameter(Mandatory)][string]$ChatbotDirectory,
        [Parameter(Mandatory)][string]$RuntimeDirectory,
        [switch]$ExistingRuntime
    )

    # Rebase only the launcher's conventional paths. Explicit custom paths stay
    # configured as supplied; the launcher never moves databases or credentials.
    foreach ($taskDatabase in @(
        @('AssistantApi', 'history.db'),
        @('AssistantOperations', 'operations.db'),
        @('ProductDatabase', 'legacy-products.db'),
        @('Conversation', 'legacy-conversations.db')
    )) {
        $taskSectionProperty = $Configuration.PSObject.Properties[$taskDatabase[0]]
        if ($null -eq $taskSectionProperty -or $null -eq $taskSectionProperty.Value) { continue }
        $taskSection = $taskSectionProperty.Value
        $taskPathProperty = $taskSection.PSObject.Properties['DatabasePath']
        if ($null -eq $taskPathProperty) { continue }
        $taskExistingPath = [string]$taskPathProperty.Value
        $taskDatabaseName = $taskDatabase[1]
        $taskSuffix = '(^|/)Logs/assistant-integration-20261012/(local-ui|chatbot-runtime)/' + [regex]::Escape($taskDatabaseName) + '$'
        if ($taskExistingPath.Replace('\', '/') -notmatch $taskSuffix) { continue }
        $taskCurrentPath = Join-Path $RuntimeDirectory $taskDatabaseName
        if ($ExistingRuntime -and (Test-Path -LiteralPath $taskExistingPath -PathType Leaf) -and
            -not (Test-Path -LiteralPath $taskCurrentPath -PathType Leaf)) {
            throw "Existing chatbot database remains at '$taskExistingPath', but '$taskCurrentPath' is missing. Move the complete source with its Logs data or configure the intended database explicitly before restarting."
        }
        $taskPathProperty.Value = $taskCurrentPath
    }
    foreach ($taskSourceFile in @(
        @('Gemini', 'ApiKeyFilePath', 'src/ChatbotLocal.Web/App_Data/gemini-api-key.txt'),
        @('Gemini', 'SecretDirectory', 'src/ChatbotLocal.Web/App_Data'),
        @('ProductDatabase', 'SeedDataPath', 'src/ChatbotLocal.Web/Data/Seed/demo-products.json')
    )) {
        $taskSectionProperty = $Configuration.PSObject.Properties[$taskSourceFile[0]]
        if ($null -eq $taskSectionProperty -or $null -eq $taskSectionProperty.Value) { continue }
        $taskSection = $taskSectionProperty.Value
        $taskProperty = $taskSection.PSObject.Properties[$taskSourceFile[1]]
        if ($null -eq $taskProperty) { continue }
        $taskRelativePath = $taskSourceFile[2]
        if (([string]$taskProperty.Value).Replace('\', '/').TrimEnd('/').EndsWith('/' + $taskRelativePath, [StringComparison]::OrdinalIgnoreCase)) {
            $taskProperty.Value = Join-Path $ChatbotDirectory $taskRelativePath
        }
    }
}
