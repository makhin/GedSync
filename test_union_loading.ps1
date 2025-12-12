# PowerShell test script
$env:GENI_ACCESS_TOKEN = (Get-Content "geni_token.json" | ConvertFrom-Json).access_token

# Create a simple test config
$config = @{
    anchorGed = "@I1@"
    anchorGeni = "34828568625"
    gedcomPath = "makhin.ged"
    tokenFile = "geni_token.json"
    dryRun = $true
    verbose = $true
} | ConvertTo-Json

$config | Out-File -Encoding UTF8 "test_config.json"

# Run the sync with --dry-run to just load and display data
& ".\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe" sync `
    --config "test_config.json" `
    --gedcom "makhin.ged" `
    --anchor-ged "@I1@" `
    --anchor-geni "34828568625" `
    --token-file "geni_token.json" `
    --dry-run `
    --verbose 2>&1 | Select-String -Pattern "union"
