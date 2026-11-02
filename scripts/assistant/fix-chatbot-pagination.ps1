param([string]$ChatbotDirectory = $env:CHATBOTLOCAL_DIRECTORY)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AssistantPaths.Common.ps1')
$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskSource = Join-Path (Resolve-AssistantChatbotDirectory -Path $ChatbotDirectory) 'src/ChatbotLocal.Web/Services/ApiV1'
$taskBackup = Join-Path $taskWorkspace 'Logs/assistant-integration-20261012/baseline'
$taskChanges=@()
function Prepare-Change([string]$name,[string]$old,[string]$new){
    $taskFile=Join-Path $taskSource $name
    $taskText=[IO.File]::ReadAllText($taskFile).Replace("`r`n","`n")
    if(-not $taskText.Contains($old)){throw "Expected source block not found in $name"}
    $script:taskChanges+=@{File=$taskFile;Name=$name;Content=$taskText.Replace($old,$new)}
}
Prepare-Change 'AssistantDataModels.cs' 'CatalogIdentity[] Choices, string Query, QuoteItem[]? SelectedUnits = null, string? PendingQuoteText = null);' 'CatalogIdentity[] Choices, string Query, QuoteItem[]? SelectedUnits = null, string? PendingQuoteText = null, bool HasMoreMatches = false);'
$taskOld=@'
                        state.ScopedCatalog = null;
                        context.References.Add(new("catalog", source, context.Identity.Scope));
                        return Complete(state, message, "Dạ, có nhiều sản phẩm phù hợp. Mình bổ sung hãng, dung tích hoặc tên cụ thể giúp em.", true);
'@
$taskNew=@'
                        // Keep the grounded query for short refinements; a partial page is never a definitive selection.
                        state.ScopedCatalog = new(context.Identity.Scope, clock.GetUtcNow().AddMinutes(options.CurrentValue.ContextTtlMinutes),
                            variants.Select(v => v.Product).ToArray(), route.Query, PendingQuoteText: route.Kind == "quote" ? quoteText : memory?.PendingQuoteText,
                            HasMoreMatches: true);
                        context.References.Add(new("catalog", source, context.Identity.Scope));
                        return Complete(state, message, "Dạ, có nhiều sản phẩm phù hợp. Mình bổ sung hãng, dung tích hoặc tên cụ thể giúp em.", true);
'@
Prepare-Change 'ProtectedChatService.cs' $taskOld $taskNew
$taskOld=@'
        if (fragment.Split(' ').Any(t => !known.Contains(t) && !Regex.IsMatch(t, @"^\d+(?:p\d+)?(?:ml|l|kg|g)$"))) { return false; }
'@
$taskNew=@'
        var partialDescriptor = memory.HasMoreMatches && (Regex.IsMatch(fragment, @"^[a-z][a-z0-9]{0,39}$")
            || Regex.IsMatch(fragment, @"^(?:co|it|khong) duong$") || Regex.IsMatch(fragment, @"^\d+(?:p\d+)?(?:ml|l|kg|g)$"));
        if (!partialDescriptor && fragment.Split(' ').Any(t => !known.Contains(t) && !Regex.IsMatch(t, @"^\d+(?:p\d+)?(?:ml|l|kg|g)$"))) { return false; }
'@
Prepare-Change 'AssistantCatalogClarification.cs' $taskOld $taskNew
foreach($taskChange in $taskChanges){
    New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
    Copy-Item -LiteralPath $taskChange.File -Destination (Join-Path $taskBackup ('ChatbotLocal__'+$taskChange.Name))
    [IO.File]::WriteAllText($taskChange.File,$taskChange.Content,[Text.UTF8Encoding]::new($false))
}
Write-Output 'Applied three scoped pagination/context fixes; originals saved in GaoApp integration baseline.'
