#Requires -Version 5.1
<#
.SYNOPSIS
    Shared Driver Store helpers for the OpenTrackpad installer scripts.
#>

function Get-AmtPtpPackages {
    try {
        $output = & pnputil.exe /enum-drivers 2>&1
        $text = ($output | Out-String)
        $blocks = [regex]::Split($text, "(?:\r?\n){2,}")
        $result = @()

        foreach ($block in $blocks) {
            if ($block -notmatch "Original Name:\s+amtptpdevice\.inf") {
                continue
            }

            $published = [regex]::Match($block, "Published Name:\s+(\S+)").Groups[1].Value
            $provider = [regex]::Match($block, "Provider Name:\s+(.+)").Groups[1].Value.Trim()
            $version = [regex]::Match($block, "Driver Version:\s+(.+)").Groups[1].Value.Trim()

            if ($published) {
                $result += [pscustomobject]@{
                    Driver           = $published
                    OriginalFileName = "AmtPtpDevice.inf"
                    ProviderName     = $provider
                    Version          = $version
                    Date             = $null
                }
            }
        }

        return $result
    }
    catch {
        Write-Warning "Could not enumerate Driver Store packages: $($_.Exception.Message)"
        return @()
    }
}
