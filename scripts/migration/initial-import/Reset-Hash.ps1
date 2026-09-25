# Loads the row fingerprint helper without opening any SQL connection.
function Initialize-GaoResetHasher {
    if ('GaoResetHasher' -as [type]) { return }
    $source = @'
using System;
using System.Data.Common;
using System.Security.Cryptography;
public static class GaoResetHasher {
    public static string Read(DbConnection c, DbTransaction t, string query) {
        using(var cmd=c.CreateCommand()) {
            cmd.Transaction=t; cmd.CommandText=query; cmd.CommandTimeout=3600;
            using(var r=cmd.ExecuteReader()) { return HashReader(r); }
        }
    }
    public static string HashReader(DbDataReader r) {
        using(var sha=SHA256.Create()) {
            long count=0;
            while(r.Read()) {
                byte[] b=(byte[])r[0]; sha.TransformBlock(b,0,b.Length,b,0); count++;
            }
            sha.TransformFinalBlock(new byte[0],0,0);
            return count.ToString()+":"+BitConverter.ToString(sha.Hash).Replace("-","");
        }
    }
}
'@
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        # Windows PowerShell 5.1's C# compiler does not reference System.Data
        # automatically, even when its types are already loaded in PowerShell.
        Add-Type -TypeDefinition $source -ReferencedAssemblies @(
            'System.dll', 'System.Core.dll', 'System.Data.dll', 'System.Xml.dll'
        )
    } else {
        Add-Type -TypeDefinition $source
    }
}
