#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][uri]$StoreUrl)
$ErrorActionPreference = 'Stop'
if ($StoreUrl.Scheme -ne 'https' -or $StoreUrl.UserInfo -or $StoreUrl.AbsolutePath -ne '/' -or $StoreUrl.Query -or $StoreUrl.Fragment) {
    throw 'StoreUrl must be the staging store HTTPS origin, for example https://shop.staging.example.com.'
}
$checks = @(
    @{ Path = '/health/live'; Status = 200; Health = $true },
    @{ Path = '/health/ready'; Status = 200; Health = $true },
    @{ Path = '/admin/account/login'; Status = 200 },
    @{ Path = '/Admin/js/pos/pos.payment.js'; Status = 200 },
    @{ Path = '/GaoApp.Web.styles.css'; Status = 200 },
    @{ Path = '/__tenant-hash'; Status = 404 },
    @{ Path = '/appsettings.json'; Status = 404 },
    @{ Path = '/uploads/invoices/probe.xml'; Status = 404 }
)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
# Default certificate validation stays enabled. Never bypass certificate errors on a host test.
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)
try {
    foreach ($check in $checks) {
        $response = $client.GetAsync([uri]::new($StoreUrl, $check.Path)).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne $check.Status) { throw "FAIL $($check.Path): HTTP $([int]$response.StatusCode), expected $($check.Status)." }
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($check.Health) {
                $health = $body | ConvertFrom-Json
                if ($health.status -ne 'Healthy' -or @($health.PSObject.Properties).Count -ne 1) { throw "FAIL $($check.Path): unhealthy or detailed/non-Production response." }
                if (!$response.Headers.Contains('Strict-Transport-Security')) { throw "FAIL $($check.Path): HSTS missing; inspect proxy scheme forwarding." }
            }
            if ($check.Path -eq '/admin/account/login' -and $body -notmatch '__RequestVerificationToken') { throw 'Login page antiforgery token missing.' }
            Write-Host "PASS $($check.Path)"
        }
        finally { $response.Dispose() }
    }
}
finally { $client.Dispose() }
Write-Host 'PASS: public staging smoke checks. Authenticated POS, WebSockets, bank sandbox and load/UAT tests remain separate.'
