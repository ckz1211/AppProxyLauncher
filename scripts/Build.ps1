$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $csc)){throw 'The Windows x64 .NET Framework C# compiler was not found'}
$shared=Join-Path $root 'src\ProxyPortOptions.cs'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /debug- /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/out:'+ (Join-Path $out 'RDCTray.exe')) (Join-Path $root 'src\RDCTray.cs') $shared
if($LASTEXITCODE -ne 0){throw 'RDCTray build failed'}
& $csc /nologo /target:winexe /platform:x64 /optimize+ /debug- /utf8output /reference:System.Windows.Forms.dll ('/out:'+ (Join-Path $out 'ChatGPTProxyLauncher.exe')) (Join-Path $root 'src\ChatGPTProxyLauncher.cs') $shared
if($LASTEXITCODE -ne 0){throw 'ChatGPTProxyLauncher build failed'}
Write-Output 'Both Windows x64 launchers were rebuilt without debug symbols.'
