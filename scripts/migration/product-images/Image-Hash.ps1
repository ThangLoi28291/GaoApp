if (-not ('GaoImageTableHasher' -as [type])) {
    $source=@'
using System;
using System.Data.Common;
using System.Security.Cryptography;
public static class GaoImageTableHasher {
 public static string Read(DbConnection c,DbTransaction t,string query) {
  using(var cmd=c.CreateCommand()) {
   cmd.Transaction=t; cmd.CommandTimeout=1800; cmd.CommandText=query;
   using(var reader=cmd.ExecuteReader()) using(var sha=SHA256.Create()) {
    long count=0;
    while(reader.Read()) { byte[] b=(byte[])reader[0]; sha.TransformBlock(b,0,b.Length,b,0); count++; }
    sha.TransformFinalBlock(new byte[0],0,0);
    return count.ToString()+":"+BitConverter.ToString(sha.Hash).Replace("-","");
   }
  }
 }
}
'@
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll')
    } else { Add-Type -TypeDefinition $source }
}
