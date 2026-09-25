"""Package the tested 01-03 dependency chain and only the new archive app sources."""
from pathlib import Path
import hashlib, json, zipfile

root = Path(__file__).resolve().parents[3]
out = root / '.artifacts/GAOAPP-MIGRATION-03-SALES-RETURNS-REHEARSAL-20260923.zip'
source = root / 'scripts/migration/initial-import'
files = [source / name for name in ('README.md', 'TEST-STATUS.md', 'TABLE-PLAN.md', 'TABLE-PLAN.json', 'Invoke-Migration.ps1')]
for step in ('01-products', '02-customers', '03-sales', '03-return-archive'):
    files.extend(p for p in sorted((source / step).glob('*')) if p.name in {'Migration.sql','package.json','Apply-Schema.sql','Apply-Schema.ps1'})
app_files = [
    'GaoApp.Infrastructure/Migrations/20260923180000_AddLegacyReturnArchive.cs',
    'GaoApp.Web/Areas/Admin/Controllers/LegacyReturnArchiveController.cs',
    'GaoApp.Web/Areas/Admin/Models/LegacyReturnArchiveModels.cs',
    'GaoApp.Web/Areas/Admin/Views/LegacyReturnArchive/Index.cshtml',
    'GaoApp.Web/Areas/Admin/Views/LegacyReturnArchive/Details.cshtml',
]
entries = {str(p.relative_to(source)).replace('\\','/'):p for p in files if p.is_file()}
entries.update({'app-source/'+p:root/p for p in app_files})
entries['app-source/INTEGRATION.md'] = source / 'ARCHIVE-INTEGRATION.md'
checksums = {name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in sorted(entries.items())}
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as archive:
    for name,path in entries.items(): archive.write(path,name)
    archive.writestr('SHA256SUMS.json',json.dumps(checksums,indent=2))
with zipfile.ZipFile(out) as archive:
    assert archive.testzip() is None
    for name,digest in checksums.items(): assert hashlib.sha256(archive.read(name)).hexdigest()==digest
print(f'Packaged {len(entries)} files: {out}')
print('SHA256 '+hashlib.sha256(out.read_bytes()).hexdigest())
