[CmdletBinding()]
param([string]$OutputDirectory,[switch]$IncludeImages)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$releaseRoot=Split-Path -Parent $PSCommandPath
$repo=[IO.Path]::GetFullPath((Join-Path $releaseRoot '../../..'))
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $repo ('.artifacts/handoff-WIN-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$out=[IO.Path]::GetFullPath($OutputDirectory)
$package=Join-Path $out 'MIGRATION';$source=Join-Path $out 'SOURCE'
function Copy-One([string]$from,[string]$to){[void][IO.Directory]::CreateDirectory((Split-Path -Parent $to));Copy-Item -LiteralPath $from -Destination $to}
foreach($name in @('config.json','Run-Step.ps1','Verify-Package.ps1','Invoke-DatabaseOperation.ps1','HUONG-DAN.md')){Copy-One (Join-Path $releaseRoot $name) (Join-Path $package $name)}
$migration=Join-Path $repo 'scripts/migration'
$initialFiles=@('Invoke-Migration.ps1','00-Preview-Reset.ps1','00-DryRun-Reset-LowLog.ps1','00-Commit-Reset-LowLog.ps1','00-Verify-After-Failed-Reset.ps1','Reset-Hash.ps1','Reset-Plan.ps1','Reset-Truncate-Plan.ps1','TABLE-PLAN.json','Test-Reset-Plan-Offline.ps1','Test-Reset-Truncate-Offline.ps1','REHEARSAL-CURRENT-20260923.md')
foreach($name in $initialFiles){Copy-One (Join-Path $migration ('initial-import/'+$name)) (Join-Path $package ('scripts/migration/initial-import/'+$name))}
foreach($folder in @('initial-import/01-products','initial-import/02-customers','initial-import/03-sales','initial-import/03-return-archive','initial-import/04-inventory','invoice-input-stock','invoices','product-images')){
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $migration $folder) -File){
  if($file.Extension -in @('.ps1','.sql','.json','.md','.py')){Copy-One $file.FullName (Join-Path $package ('scripts/migration/'+$folder+'/'+$file.Name))}
 }
}
# Build outputs contain only tooling dependencies here, not copied runtime settings.
foreach($pair in @(@('invoice','invoice-verifier'),@('returns','return-verifier'))){
 $build=Join-Path $repo ('.artifacts/release-build/'+$pair[0])
 if(-not (Test-Path -LiteralPath (Join-Path $build 'Verifier.dll'))){throw "Build missing: $build"}
 foreach($file in Get-ChildItem -LiteralPath $build -File -Recurse){
  $relative=$file.FullName.Substring($build.Length+1)
  if($relative -match '^(wwwroot|App_Data|Logs|uploads|scripts|Properties)[\\/]' -or $file.Name -like 'appsettings*'){continue}
  if($file.Extension -eq '.dll' -or $file.Name -like '*.deps.json' -or $file.Name -like '*.runtimeconfig.json'){
   Copy-One $file.FullName (Join-Path $package ('tools/'+$pair[1]+'/'+$relative))
  }
 }
}
foreach($name in @('GaoApp.sln','global.json','.gitignore')){Copy-One (Join-Path $repo $name) (Join-Path $source $name)}
# rg respects .gitignore; copy all current source, including untracked source edits.
$sourceFolders=@('GaoApp.Domain','GaoApp.Application','GaoApp.Infrastructure','GaoApp.Web','GaoApp.Migrator','GaoApp.LabelPrintServer','GaoApp.Tests','GaoApp.Tests.Browser','eng')
$files=& rg --files --hidden @sourceFolders
if($LASTEXITCODE -ne 0){throw 'Source file enumeration failed.'}
foreach($relative in $files){
 if($relative -match '(?i)(^|[\\/])(bin|obj|App_Data|uploads|Logs|node_modules|TestResults|PublishProfiles)([\\/]|$)|(^|[\\/])(\.env[^\\/]*|secrets[^\\/]*|appsettings\.[^\\/]*local[^\\/]*|testlocal\.runsettings)$|\.(bak|mdf|ldf|zip|7z|pfx|p12|pem|key|log|user|suo|pdb)$'){continue}
 Copy-One (Join-Path $repo $relative) (Join-Path $source $relative)
}
# Separate user secrets identity prevents this portable source from silently using
# the rehearsal project's secret store. No secrets are read or exported.
foreach($project in Get-ChildItem -LiteralPath $source -Filter '*.csproj' -Recurse){
 $text=[IO.File]::ReadAllText($project.FullName)
 $text=[regex]::Replace($text,'<UserSecretsId>[^<]+</UserSecretsId>','<UserSecretsId>GaoApp-WIN-HU6RO2EMIJF-Cutover</UserSecretsId>')
 [IO.File]::WriteAllText($project.FullName,$text,[Text.UTF8Encoding]::new($false))
}
$connection='Server=WIN-HU6RO2EMIJF\SQLEXPRESS;Database=GaoAppDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True'
foreach($settings in Get-ChildItem -LiteralPath $source -Filter 'appsettings*.json' -Recurse){
 $json=Get-Content -LiteralPath $settings.FullName -Raw|ConvertFrom-Json
 if($json.PSObject.Properties.Name -contains 'ConnectionStrings'){
  foreach($p in $json.ConnectionStrings.PSObject.Properties){$p.Value=$connection}
 }
 if($json.PSObject.Properties.Name -contains 'Storage'){$json.Storage.UploadRoot='C:\GaoAppData\Uploads';$json.Storage.CreateIfMissing=$false}
 if($json.PSObject.Properties.Name -contains 'SeedData'){
  $json.SeedData.EnableDemoSeed=$false;$json.SeedData.EnableDefaultAdminSeed=$false
 }
 if($json.PSObject.Properties.Name -contains 'DataProtection'){
  foreach($p in $json.DataProtection.PSObject.Properties){if($p.Name -in @('CertificatePassword','CertificatePath')){$p.Value=''}}
 }
 $json|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $settings.FullName -Encoding UTF8
}
# Ship the reviewed migration source alongside the solution as well; runnable
# entry point remains the separate MIGRATION package.
Copy-Item -LiteralPath (Join-Path $package 'scripts') -Destination (Join-Path $source 'scripts') -Recurse
foreach($folder in @('initial-import/Verifier','invoices/Verifier','invoice-input-stock/ReadVerifier')){
 foreach($f in Get-ChildItem -LiteralPath (Join-Path $migration $folder) -File){if($f.Extension -in @('.cs','.csproj')){Copy-One $f.FullName (Join-Path $source ('scripts/migration/'+$folder+'/'+$f.Name))}}
}
Copy-One (Join-Path $releaseRoot 'HUONG-DAN.md') (Join-Path $source 'HUONG-DAN.md')
Copy-One (Join-Path $releaseRoot 'HUONG-DAN.md') (Join-Path $out 'HUONG-DAN.md')
function Write-Checksums([string]$folder){
 $entries=@(foreach($f in Get-ChildItem -LiteralPath $folder -Recurse -File|Sort-Object FullName){
  $relative=$f.FullName.Substring($folder.Length+1).Replace('\','/')
  if($relative -in @('config.json','checksums.json')){continue}
  [pscustomobject]@{Path=$relative;Bytes=$f.Length;Sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}
 })
 [ordered]@{CreatedAtUtc=[datetime]::UtcNow.ToString('o');Files=$entries}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $folder 'checksums.json') -Encoding UTF8
}
Write-Checksums $package
Copy-One (Join-Path $releaseRoot 'Verify-Package.ps1') (Join-Path $source 'Verify-Package.ps1')
Write-Checksums $source
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($name in @('MIGRATION','SOURCE')){
 $zip=Join-Path $out ('GAOAPP-'+$name+'-WIN-HU6RO2EMIJF-20260923.zip')
 [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $out $name),$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 Write-Output "Created: $zip"
}
if($IncludeImages){
 $imageRoot='C:\GaoAppData\Uploads\legacy-data'
 $imageZip=Join-Path $out 'GAOAPP-IMAGES-20260923.zip'
 [IO.Compression.ZipFile]::CreateFromDirectory($imageRoot,$imageZip,[IO.Compression.CompressionLevel]::Fastest,$true)
 Write-Output "Created: $imageZip"
}
Get-ChildItem -LiteralPath $out -Filter '*.zip'|ForEach-Object{[pscustomobject]@{Name=$_.Name;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}|Export-Csv -LiteralPath (Join-Path $out 'ZIP-SHA256.csv') -NoTypeInformation -Encoding UTF8
Write-Output "RELEASE_BUILT: $out"
