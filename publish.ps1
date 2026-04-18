# Publish Sup as self-contained exe for Windows x64
Write-Host "Publishing Sup..." -ForegroundColor Cyan

dotnet publish Sup/Sup.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish/

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Done! Files are in publish/" -ForegroundColor Green
Write-Host "Now open installer.iss in Inno Setup and compile." -ForegroundColor Yellow
