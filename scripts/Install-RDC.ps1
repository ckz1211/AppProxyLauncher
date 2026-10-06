param([Nullable[int]]$Port=$null)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if($null -eq $Port){
    $source=Get-Content -LiteralPath (Join-Path $root 'src\ProxyPortOptions.cs') -Raw
    $match=[regex]::Match($source,'DefaultPort\s*=\s*(\d+)')
    if(-not $match.Success){throw 'Default proxy port could not be read'}
    $Port=[int]$match.Groups[1].Value
}
if($Port -lt 1 -or $Port -gt 65535){throw 'Port must be between 1 and 65535'}
$install=Join-Path $env:LOCALAPPDATA 'RDCTray'
$exe=Join-Path $install 'RDCTray.exe'
if(Get-Process -Name RDCTray -ErrorAction SilentlyContinue){throw 'Exit the existing RDC tray instance before running setup'}
$node=Join-Path $env:ProgramFiles 'nodejs\node.exe'
$npm=Join-Path $env:ProgramFiles 'nodejs\node_modules\npm\bin\npm-cli.js'
if(-not(Test-Path -LiteralPath $node) -or -not(Test-Path -LiteralPath $npm)){throw 'Node.js/npm is required in Program Files\nodejs'}
New-Item -ItemType Directory -Path $install -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin\RDCTray.exe') -Destination $exe -Force
Copy-Item -LiteralPath (Join-Path $root 'src\agent-host.mjs') -Destination (Join-Path $install 'agent-host.mjs') -Force
$proxyRuntime=Join-Path $install 'proxy-runtime'
New-Item -ItemType Directory -Path $proxyRuntime -Force | Out-Null
$package=Join-Path $proxyRuntime 'package.json'
if(-not(Test-Path -LiteralPath $package)){
    [IO.File]::WriteAllText($package,'{"name":"rdc-scoped-proxy-runtime","version":"1.0.0","private":true}',[Text.UTF8Encoding]::new($false))
}
$proxy='http://127.0.0.1:'+$Port
& $node $npm install --prefix $proxyRuntime --registry=https://registry.npmjs.org --strict-ssl=true ('--proxy='+$proxy) ('--https-proxy='+$proxy) --ignore-scripts --no-audit --no-fund --no-update-notifier --save-exact undici@8.11.2
if($LASTEXITCODE -ne 0){throw 'Proxy dependency installation failed'}
Write-Output 'Per-user RDC launcher setup complete. Pairing credentials were not modified. The launcher was not started.'
