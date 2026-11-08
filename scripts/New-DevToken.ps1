param([Parameter(Mandatory=$true)][string]$Subject, [Parameter(Mandatory=$true)][string]$SigningKey)
$ErrorActionPreference = 'Stop'
if ($SigningKey.Length -lt 64) { throw 'SigningKey must contain at least 64 characters.' }
function ToBase64Url([byte[]]$Bytes) { [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
$header = ToBase64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$claims = @{ sub=$Subject; iss='kaloree-local'; aud='kaloree-api'; iat=$now; nbf=$now; exp=($now+3600) } | ConvertTo-Json -Compress
$payload = ToBase64Url ([Text.Encoding]::UTF8.GetBytes($claims))
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SigningKey))
try { $signature = ToBase64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload"))) } finally { $hmac.Dispose() }
"$header.$payload.$signature"
