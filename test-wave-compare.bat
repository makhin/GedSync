@echo off
REM Standard test parameters for wave-compare
REM Always use these anchors for testing:
REM   Source: I500002 (Александр Владимирович Махин)
REM   Destination: I6000000206529622827

.\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe wave-compare ^
  --source myheritage.ged ^
  --destination geni.ged ^
  --anchor-source I500002 ^
  --anchor-destination I6000000206529622827 ^
  --output results.json ^
  --max-level 1000 ^
  --ignore-photos ^
  %*




./GedcomGeniSync.Cli/bin/Debug/net8.0/GedcomGeniSync.Cli.exe wave-compare --source myheritage.ged --destination geni.ged --output results.json --anchor-source I500002 --anchor-destination I6000000206529622827 --max-level 10 --detailed-log detailed.log

./GedcomGeniSync.Cli/bin/Debug/net8.0/GedcomGeniSync.Cli.exe wave-compare --source myheritage.ged --destination geni.ged --output results.json --anchor-source I171 --anchor-destination I6000000196856527821 --max-level 10 --detailed-log detailed.log