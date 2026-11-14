param([string]$BaseUrl='http://localhost:5208',[string]$ExpectedSha='',[string]$Output='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Net.Http
$handler=New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect=$false
$client=New-Object System.Net.Http.HttpClient($handler)
$client.Timeout=[TimeSpan]::FromSeconds(45)
$results=@()
try {
    foreach($item in @(@('/api/v1/system/health',200),@('/api/v1/system/readiness',200),@('/',200),@('/ResearchValidation',302),@('/api/v1/operations/readiness',401),@('/api/v1/authority/workbench',401),@('/api/v1/research/trend/readiness',401))){
        $response=$client.GetAsync($BaseUrl.TrimEnd('/')+$item[0]).GetAwaiter().GetResult()
        $status=[int]$response.StatusCode
        if($status -ne $item[1]){throw "Unexpected status $status for $($item[0])"}
        if($item[0] -eq '/api/v1/system/health'){
            $health=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            if($health.version -ne '3.2.2'){throw 'Version mismatch'}
            if($ExpectedSha -and $health.commitSha -ne $ExpectedSha){throw 'Source SHA mismatch'}
        }
        if($status -eq 302 -and $response.Headers.Location.OriginalString -notmatch '/Account/Login'){throw 'Unexpected login redirect'}
        $results+=@{path=$item[0];status=$status;result='PASS'}
        $response.Dispose()
    }
    $report=@{at=[DateTimeOffset]::UtcNow.ToString('o');baseUrl=$BaseUrl;checks=$results;authenticatedAcceptance='BLOCKED_NO_ISOLATED_ACCOUNTS';expectedSha=$ExpectedSha}
    $json=$report | ConvertTo-Json -Depth 8
    if($Output){$json | Set-Content -LiteralPath $Output -Encoding UTF8}
    $json
}finally{$client.Dispose();$handler.Dispose()}
