param([string]$GaoAppUrl, [switch]$AutoStart, [switch]$Start)
$ErrorActionPreference = 'Stop'
$installRoot = $PSScriptRoot
$executable = Join-Path $installRoot 'GaoApp.LabelPrintClient.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Giai nen toan bo goi in client truoc khi cau hinh.' }
if (-not $GaoAppUrl) { $GaoAppUrl = Read-Host 'Dia chi trang GaoApp (vi du https://gao.tenmien.vn)' }
$appUri = [Uri]$GaoAppUrl
if (-not $appUri.IsAbsoluteUri -or ($appUri.Scheme -ne 'https' -and -not ($appUri.Scheme -eq 'http' -and $appUri.IsLoopback))) {
    throw 'Dung HTTPS cua GaoApp, hoac HTTP localhost khi phat trien. Khong nhap dia chi may in.'
}
$origin = $appUri.GetLeftPart([UriPartial]::Authority)
$settingsPath = Join-Path $installRoot 'appsettings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$randomBytes = New-Object byte[] 32
$randomSource = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $randomSource.GetBytes($randomBytes) } finally { $randomSource.Dispose() }
$pairingToken = [BitConverter]::ToString($randomBytes).Replace('-', '')
$settings.LabelClient.AllowedOrigins = @($origin)
$settings.LabelClient.PairingToken = $pairingToken
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
$fileAcl = New-Object System.Security.AccessControl.FileSecurity
$fileAcl.SetAccessRuleProtection($true, $false)
$userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$fileAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($userSid, 'FullControl', 'Allow')))
$systemSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
$fileAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($systemSid, 'FullControl', 'Allow')))
Set-Acl -LiteralPath $settingsPath -AclObject $fileAcl
if ($AutoStart) {
    $startupFolder = [Environment]::GetFolderPath('Startup')
    $shortcutPath = Join-Path $startupFolder 'GaoApp Label Print Client.lnk'
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.WindowStyle = 7
    $shortcut.Save()
}
Write-Host ('Da cau hinh cho: ' + $origin)
Write-Host 'Ma ket noi (dan vao In tem khuyen mai tren may client nay):'
Write-Host $pairingToken
Write-Host 'Khoi dong bo ket noi bang GaoApp.LabelPrintClient.exe. Khong can QZ Tray hay ket noi SQL.'
if ($Start) { Start-Process -FilePath $executable -WorkingDirectory $installRoot -WindowStyle Hidden }
