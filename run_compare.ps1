& "./GedcomGeniSync.Cli/bin/Debug/net8.0/GedcomGeniSync.Cli.exe" `
    compare `
    --source myheritage.ged `
    --dest geni.ged `
    --output compare_output.json `
    --anchor-source '@I1@' `
    --anchor-dest '@I6000000207139336972@' `
    --threshold 50 `
    --verbose
