param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPackage = Join-Path $taskRepository 'artifacts\sandboxie-mcp-win-x64'
if ((Test-Path -LiteralPath $taskPackage) -and (Get-ChildItem -LiteralPath $taskPackage -Force | Select-Object -First 1)) {
    throw 'The package directory already contains files. Use a fresh checkout or move the previous package first.'
}
Push-Location -LiteralPath $taskRepository
try {
    & $Dotnet publish src/SandboxieMcp.Server -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o $taskPackage
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    foreach ($taskFile in @('README.md', 'LICENSE', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', 'SECURITY.md')) {
        Copy-Item -LiteralPath (Join-Path $taskRepository $taskFile) -Destination $taskPackage
    }
    Copy-Item -LiteralPath (Join-Path $taskRepository 'config') -Destination $taskPackage -Recurse
    Copy-Item -LiteralPath (Join-Path $taskRepository 'docs') -Destination $taskPackage -Recurse
    Copy-Item -LiteralPath (Join-Path $taskRepository 'licenses') -Destination $taskPackage -Recurse
    $taskAssets = Get-Content -LiteralPath 'src/SandboxieMcp.Server/obj/project.assets.json' -Raw | ConvertFrom-Json
    $taskCaches = @($taskAssets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name })
    $taskFramework = @($taskAssets.project.frameworks.PSObject.Properties)[0].Name
    $taskRuntimeConfig = Get-Content -LiteralPath (Join-Path $taskRepository ('src/SandboxieMcp.Server/bin/Release/' + $taskFramework + '/win-x64/sandboxie-mcp.runtimeconfig.json')) -Raw | ConvertFrom-Json
    $taskRuntimeVersion = ($taskRuntimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).version
    if (!$taskRuntimeVersion) { throw 'The published .NET runtime version is missing.' }
    $taskRuntimePath = $null
    foreach ($taskCache in $taskCaches) {
        $taskCandidate = Join-Path $taskCache ('microsoft.netcore.app.runtime.win-x64/' + $taskRuntimeVersion)
        if (Test-Path -LiteralPath $taskCandidate) { $taskRuntimePath = $taskCandidate; break }
    }
    if (!$taskRuntimePath) { throw 'The restored .NET runtime package is missing.' }
    Copy-Item -LiteralPath (Join-Path $taskRuntimePath 'LICENSE.TXT') -Destination (Join-Path $taskPackage '.NET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $taskRuntimePath 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $taskPackage '.NET-ThirdPartyNotices.txt')
    $taskDependencies = foreach ($taskLibrary in $taskAssets.libraries.PSObject.Properties) {
        if ($taskLibrary.Value.type -ne 'package') { continue }
        $taskPackagePath = $null
        foreach ($taskCache in $taskCaches) {
            $taskCandidate = Join-Path $taskCache $taskLibrary.Value.path
            if (Test-Path -LiteralPath $taskCandidate) { $taskPackagePath = $taskCandidate; break }
        }
        if (!$taskPackagePath) { throw ('The restored package is missing: ' + $taskLibrary.Name) }
        $taskNuspecPath = Join-Path $taskPackagePath ($taskLibrary.Name.Split('/')[0].ToLowerInvariant() + '.nuspec')
        [xml]$taskNuspec = Get-Content -LiteralPath $taskNuspecPath -Raw
        $taskLicense = $taskNuspec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="license"]')
        if (!$taskLicense) { throw ('The package license declaration is missing: ' + $taskLibrary.Name) }
        $taskLicenseDirectory = Join-Path (Join-Path $taskPackage 'licenses') $taskLibrary.Value.path
        if ($taskLicense.GetAttribute('type') -eq 'file') {
            $taskLicenseSource = [IO.Path]::GetFullPath((Join-Path $taskPackagePath $taskLicense.InnerText))
            $taskPackagePrefix = [IO.Path]::GetFullPath($taskPackagePath).TrimEnd('\') + '\'
            if (!$taskLicenseSource.StartsWith($taskPackagePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'A package license path escapes its package.' }
            New-Item -ItemType Directory -Path $taskLicenseDirectory -Force | Out-Null
            Copy-Item -LiteralPath $taskLicenseSource -Destination $taskLicenseDirectory
        }
        foreach ($taskNoticeFile in (Get-ChildItem -LiteralPath $taskPackagePath -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD[-_]PARTY[-_]NOTICES)(\..*)?$' })) {
            New-Item -ItemType Directory -Path $taskLicenseDirectory -Force | Out-Null
            Copy-Item -LiteralPath $taskNoticeFile.FullName -Destination $taskLicenseDirectory
        }
        [ordered]@{ name = $taskLibrary.Name.Split('/')[0]; version = $taskLibrary.Name.Split('/')[1];
            sha512 = $taskLibrary.Value.sha512; license = $taskLicense.InnerText; licenseType = $taskLicense.GetAttribute('type');
            copyright = [string]$taskNuspec.package.metadata.copyright;
            projectUrl = [string]$taskNuspec.package.metadata.projectUrl }
    }
    $taskDependencies = @($taskDependencies) + @([ordered]@{
        name = 'Microsoft.NETCore.App.Runtime.win-x64'; version = $taskRuntimeVersion; license = 'MIT'; licenseType = 'expression';
        sha512 = (Get-Content -LiteralPath (Join-Path $taskRuntimePath ('microsoft.netcore.app.runtime.win-x64.' + $taskRuntimeVersion + '.nupkg.sha512')) -Raw).Trim();
        copyright = 'Copyright (c) .NET Foundation and Contributors'; projectUrl = 'https://dot.net/'
    })
    $taskDependencies | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskPackage 'DEPENDENCIES.json') -Encoding UTF8
    $taskVersion = & (Join-Path $taskPackage 'sandboxie-mcp.exe') --version
    if ($LASTEXITCODE) { throw 'Published executable verification failed.' }
    $taskArchive = Join-Path $taskRepository ('artifacts\sandboxie-mcp-' + $taskVersion.Trim() + '-win-x64.zip')
    if (Test-Path -LiteralPath $taskArchive) { throw 'The release ZIP already exists.' }
    Compress-Archive -LiteralPath $taskPackage -DestinationPath $taskArchive
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $taskRepository 'artifacts\SHA256SUMS.txt'),
        ($taskHash + '  ' + [IO.Path]::GetFileName($taskArchive) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Output $taskArchive
}
finally { Pop-Location }
