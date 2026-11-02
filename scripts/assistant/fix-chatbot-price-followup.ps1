param([string]$ChatbotDirectory = $env:CHATBOTLOCAL_DIRECTORY)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AssistantPaths.Common.ps1')
$taskRoot = Join-Path (Resolve-AssistantChatbotDirectory -Path $ChatbotDirectory) 'src/ChatbotLocal.Web/Services/ApiV1'
$taskFile=Join-Path $taskRoot 'AssistantCatalogClarification.cs'
$taskText=[IO.File]::ReadAllText($taskFile).Replace("`r`n","`n")
$taskOld='    public static bool TryQuery(ScopedCatalogMemory? memory, string message, out string query)'
$taskNew=@'
    public static bool IsPriceFollowUp(string message)
    {
        var text = new ProductTextNormalizer().NormalizeProductReference(message);
        if (!Regex.IsMatch(text, @"\b(?:gia|bao nhieu|bao nhiu|ban buon|si|le)\b")) return false;
        text = Regex.Replace(text, ProductTextNormalizer.RemovableSaleUnitsPattern, " ");
        text = Regex.Replace(text, @"\b(?:gia|si|le|ban|buon|bao|nhieu|nhiu|tien|ca|hai|2|loai|cho|em|toi|minh|anh|chi|giup|voi|luon|nhe|nha)\b", " ");
        return string.IsNullOrWhiteSpace(text);
    }

    public static bool TryQuery(ScopedCatalogMemory? memory, string message, out string query)
'@
if(-not $taskText.Contains($taskOld)){throw 'Expected clarification block missing'}
[IO.File]::WriteAllText($taskFile,$taskText.Replace($taskOld,$taskNew),[Text.UTF8Encoding]::new($false))
$taskFile=Join-Path $taskRoot 'ProtectedChatService.cs'
$taskText=[IO.File]::ReadAllText($taskFile).Replace("`r`n","`n")
$taskOld='var route = memory is not null && AssistantCatalogClarification.IsAllUnitsFollowUp(message)'
$taskNew='var route = memory is not null && (AssistantCatalogClarification.IsAllUnitsFollowUp(message) || memory.Choices.Length == 1 && AssistantCatalogClarification.IsPriceFollowUp(message))'
if(-not $taskText.Contains($taskOld)){throw 'Expected route block missing'}
[IO.File]::WriteAllText($taskFile,$taskText.Replace($taskOld,$taskNew),[Text.UTF8Encoding]::new($false))
Write-Output 'Price-only followups use the already selected product and freshly verified quotes.'
