#Requires -Version 5.1
<#
.SYNOPSIS
    Static safety and compatibility checks for the installer/driver scripts.
    These tests do not touch the Driver Store or the real trackpad.
#>

BeforeDiscovery {
    $script:scriptFiles = @(
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..") -Filter "*.ps1" -File
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..\scripts") -Filter "*.ps1" -File
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..\..\scripts") -Filter "*.ps1" -File
    ) | ForEach-Object { @{ Name = $_.Name; Path = $_.FullName } }

    $script:installerScriptFiles = @(
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..") -Filter "*.ps1" -File
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..\scripts") -Filter "*.ps1" -File
    ) | ForEach-Object { @{ Name = $_.Name; Path = $_.FullName } }

}

BeforeAll {
    $files = @(
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..") -Filter "*.ps1" -File
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..\scripts") -Filter "*.ps1" -File
        Get-ChildItem -Path (Join-Path $PSScriptRoot "..\..\scripts") -Filter "*.ps1" -File
    )

    $script:texts = @{}
    foreach ($file in $files) {
        $script:texts[$file.Name] = Get-Content -Path $file.FullName -Raw
    }
}

Describe "PowerShell compatibility" {
    It "<Name> parses without errors" -ForEach $script:scriptFiles {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile(
            $Path, [ref]$tokens, [ref]$errors) | Out-Null

        $errors.Count | Should -Be 0
    }

    It "<Name> declares PowerShell 5.1" -ForEach $script:installerScriptFiles {
        $text = $script:texts[$Name]
        $text | Should -Match "#Requires -Version 5\.1"
    }
}

Describe "Driver install safety" {
    It "uses pnputil /add-driver without /force" {
        $text = $script:texts["Install-Driver.ps1"]

        $text | Should -Match "pnputil\.exe /add-driver"
        $text | Should -Not -Match "pnputil\.exe[^\r\n]*?/force"
    }

    It "accepts the already-present package exit code 259" {
        $text = $script:texts["Install-Driver.ps1"]

        $text | Should -Match '\$LASTEXITCODE -ne 259'
        $text | Should -Match 'Driver package already present'
    }

    It "verifies the catalog signature before installing" {
        $text = $script:texts["Install-Driver.ps1"]

        $text | Should -Match "Get-AuthenticodeSignature"
        $text | Should -Match '\$signature\.Status -ne "Valid"'
    }

    It "enables non-admin access for USB and Bluetooth" {
        $text = $script:texts["Install-Driver.ps1"]

        $text | Should -Match "AmtPtpDeviceUsbUm\\Parameters"
        $text | Should -Match "AmtPtpHidFilter\\Parameters"
        ([regex]::Matches($text, "AllowNonAdminAccess")).Count | Should -BeGreaterThan 1
    }

    It "supports skipping the device restart" {
        $text = $script:texts["Install-Driver.ps1"]

        $text | Should -Match '\[switch\]\$NoRestart'
        $text | Should -Match 'if \(-not \$NoRestart\)'
    }
}

Describe "Driver uninstall safety" {
    It "defaults to a dry run" {
        $text = $script:texts["Uninstall-Driver.ps1"]

        $text | Should -Match '\[switch\]\$Execute'
        $text | Should -Match 'if \(-not \$Execute\)'
        $text | Should -Match "DRY RUN"
    }

    It "guards against a connected trackpad unless -Force is supplied" {
        $text = $script:texts["Uninstall-Driver.ps1"]

        $text | Should -Match 'if \(\$devices -and -not \$Force\)'
    }

    It "exports a backup before deleting the package" {
        $text = $script:texts["Uninstall-Driver.ps1"]

        $exportIndex = $text.IndexOf("/export-driver")
        $deleteIndex = $text.IndexOf("/delete-driver")

        $exportIndex | Should -BeGreaterThan 0
        $deleteIndex | Should -BeGreaterThan $exportIndex
    }

    It "does not pass /force to pnputil" {
        $text = $script:texts["Uninstall-Driver.ps1"]

        $text | Should -Not -Match "pnputil\.exe[^\r\n]*?/force"
    }
}

Describe "Stop-App fail-open" {
    It "releases the left mouse button more than once" {
        $text = $script:texts["Stop-App.ps1"]

        $text | Should -Match "1\.\.3 \| ForEach-Object"
        $text | Should -Match "\[OpenTrackpadInput\]::LeftUp\(\)"
    }

    It "restores a stale three-finger slide backup" {
        $text = $script:texts["Stop-App.ps1"]

        $text | Should -Match "three-finger-slide\.backup"
        $text | Should -Match "ThreeFingerSlideEnabled"
    }
}

Describe "Installer package" {
    It "requires Windows 11 x64 and admin" {
        $iss = Get-Content -Path (Join-Path $PSScriptRoot "..\OpenTrackpad.iss") -Raw

        $iss | Should -Match "MinVersion=10\.0\.22000"
        $iss | Should -Match "ArchitecturesAllowed=x64compatible"
        $iss | Should -Match "PrivilegesRequired=admin"
    }
}
