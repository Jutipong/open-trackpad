#Requires -Version 5.1
<#
.SYNOPSIS
    Pester tests for the shared Driver Store helpers.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot "..\scripts\DriverStore.ps1")

    function New-PnputilBlock {
        param(
            [string]$PublishedName,
            [string]$OriginalName = "amtptpdevice.inf",
            [string]$Version = "10/10/2023 2.3.0.0"
        )

        @(
            "Published Name:     $PublishedName"
            "Original Name:      $OriginalName"
            "Provider Name:      Microsoft"
            "Class Name:         HIDClass"
            "Class GUID:         {745a17a0-74d3-11d0-b6fe-00a0c90f57da}"
            "Driver Version:     $Version"
            "Signer Name:        Microsoft Windows Hardware Compatibility Publisher"
        ) -join "`r`n"
    }

    function New-PnputilOutput {
        param([string[]]$Blocks)

        "Microsoft PnP Utility`r`n`r`n" + ($Blocks -join "`r`n`r`n") + "`r`n"
    }
}

Describe "Get-AmtPtpPackages" {
    It "parses a single AmtPtpDevice package" {
        Mock -CommandName pnputil.exe -MockWith {
            New-PnputilOutput -Blocks @(
                (New-PnputilBlock -PublishedName "oem84.inf"))
        }

        $packages = @(Get-AmtPtpPackages)

        $packages.Count | Should -Be 1
        $packages[0].Driver | Should -Be "oem84.inf"
        $packages[0].OriginalFileName | Should -Be "AmtPtpDevice.inf"
        $packages[0].ProviderName | Should -Be "Microsoft"
        $packages[0].Version | Should -Be "10/10/2023 2.3.0.0"
    }

    It "returns every matching package" {
        Mock -CommandName pnputil.exe -MockWith {
            New-PnputilOutput -Blocks @(
                (New-PnputilBlock -PublishedName "oem84.inf")
                (New-PnputilBlock -PublishedName "oem91.inf" -Version "01/01/2024 2.4.0.0"))
        }

        $packages = @(Get-AmtPtpPackages)

        $packages.Count | Should -Be 2
        $packages[1].Driver | Should -Be "oem91.inf"
        $packages[1].Version | Should -Be "01/01/2024 2.4.0.0"
    }

    It "ignores other INF packages" {
        Mock -CommandName pnputil.exe -MockWith {
            New-PnputilOutput -Blocks @(
                (New-PnputilBlock -PublishedName "oem10.inf" -OriginalName "otherdevice.inf"))
        }

        @(Get-AmtPtpPackages).Count | Should -Be 0
    }

    It "ignores matching blocks without a published name" {
        Mock -CommandName pnputil.exe -MockWith {
            "Microsoft PnP Utility`r`n`r`nOriginal Name:      amtptpdevice.inf`r`nProvider Name:      Microsoft`r`n"
        }

        @(Get-AmtPtpPackages).Count | Should -Be 0
    }

    It "returns an empty list when pnputil fails" {
        Mock -CommandName pnputil.exe -MockWith { throw "access denied" }

        $packages = @(Get-AmtPtpPackages -WarningAction SilentlyContinue)

        $packages.Count | Should -Be 0
    }
}
