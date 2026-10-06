$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$chat=[Reflection.Assembly]::LoadFile((Join-Path $root 'bin\ChatGPTProxyLauncher.exe'))
$rdc=[Reflection.Assembly]::LoadFile((Join-Path $root 'bin\RDCTray.exe'))
$flags=[Reflection.BindingFlags]'NonPublic,Static'
function Assert($condition,$message){if(-not $condition){throw $message}}
foreach($assembly in @($chat,$rdc)){
    $parse=$assembly.GetType('ProxyPortOptions').GetMethod('Parse')
    foreach($case in @(@(@(),7897),@(@('--port=1080'),1080),@(@('--port=1'),1),@(@('--port=65535'),65535))){
        $actual=$parse.Invoke($null,[object[]]@(,[string[]]$case[0]))
        Assert ($actual -eq $case[1]) 'Valid proxy port did not round-trip'
    }
    foreach($bad in @(@('--port=0'),@('--port=65536'),@('--port=-1'),@('--port=1080;whoami'),@('--port=1','--port=2'),@('--port'),@('--port= 1080'))){
        $rejected=$false
        try{$null=$parse.Invoke($null,[object[]]@(,[string[]]$bad))}catch{$rejected=$_.Exception.InnerException -is [ArgumentException]}
        Assert $rejected 'Invalid proxy argument accepted'
    }
}
$valid=$rdc.GetType('TrayContext').GetMethod('ValidVersion',$flags)
foreach($bad in @("0.2.52`n",'0.2.52&whoami','../0.2.52','0.2.52/../../x')){
    Assert (-not $valid.Invoke($null,@($bad))) 'Unsafe version accepted'
}
Assert ($valid.Invoke($null,@('0.2.52'))) 'Stable version rejected'
$program=$chat.GetType('Program')
$make=$program.GetMethod('PackageCommand',$flags)
$encode=$program.GetMethod('EncodedArguments',$flags)
$read=$program.GetMethod('ReadProcess',$flags)
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Run-TestProcess([string]$command,[int]$timeout=10000){
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$shell
    $info.Arguments=$encode.Invoke($null,@($command))
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $p=[Diagnostics.Process]::Start($info)
    try{return $read.Invoke($null,@($p,$timeout))}finally{$p.Dispose()}
}
# Stub the package API: no real client is launched. Metacharacters must remain data.
$path="C:\Test folder\O'Brien\`$env:TEMP``x & %TEMP%\launcher.exe"
$command=$make.Invoke($null,@($path,1080,$false))
$stub='function Invoke-CommandInDesktopPackage { param($PackageFamilyName,$AppId,$Command,[Alias("Args")]$PackageArgs,$ErrorAction) @{Path=$Command;Arguments=$PackageArgs} | ConvertTo-Json -Compress }; '
$result=Run-TestProcess ($stub+$command) | ConvertFrom-Json
Assert ($result.Path -ceq $path) 'Package path was interpreted as code'
Assert ($result.Arguments -eq '--in-package --port=1080') 'Port lost during package handoff'
$output=Run-TestProcess '1..2000 | ForEach-Object { [Console]::Error.WriteLine(("x" * 100)) }; [Console]::Out.Write("OK")'
Assert ($output -eq 'OK') 'Concurrent output draining failed'
$timedOut=$false
try{$null=Run-TestProcess 'Start-Sleep -Seconds 10' 200}catch{
    $e=$_.Exception
    while($e.InnerException){$e=$e.InnerException}
    $timedOut=$e -is [TimeoutException]
}
Assert $timedOut 'Process timeout was ineffective'
# Exercise the RDC direct Node argument boundary without invoking an installer.
$quote=$rdc.GetType('TrayContext').GetMethod('Quote',$flags)
$node=Join-Path $env:ProgramFiles 'nodejs\node.exe'
if(-not(Test-Path -LiteralPath $node)){throw 'Node.js is required for the RDC argument regression test'}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$node
$info.Arguments='-e '+$quote.Invoke($null,@('process.stdout.write(JSON.stringify(process.argv.slice(1)))'))+' '+$quote.Invoke($null,@($path))
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
$p=[Diagnostics.Process]::Start($info)
try{$roundTrip=$read.Invoke($null,@($p,10000)) | ConvertFrom-Json}finally{$p.Dispose()}
Assert (@($roundTrip)[0] -ceq $path) 'Node CLI path was expanded or split'
Write-Output 'PASS: ports, versions, literal package/Node paths, concurrent pipes and timeout. No installed application started or stopped.'
