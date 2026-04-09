# scaffold.ps1 — AspireDemo solution scaffold
$ErrorActionPreference = "Stop"
$sln = "AspireDemo.sln"

function Add-Project($template, $name) {
    if (-not (Test-Path $name)) {
        Write-Host "Creating $name ($template)" -ForegroundColor Cyan
        dotnet new $template -n $name -o $name
        dotnet sln $sln add "$name/$name.csproj"
    } else {
        Write-Host "Skipping $name (exists)" -ForegroundColor DarkGray
    }
}

function Add-Ref($from, $to) {
    Write-Host "Reference: $from -> $to" -ForegroundColor Yellow
    dotnet add "$from/$from.csproj" reference "$to/$to.csproj"
}

# Projects
Add-Project "classlib"  "AspireDemo.Contracts"
Add-Project "worker"    "AspireDemo.Classifier"
Add-Project "worker"    "AspireDemo.Handler"
Add-Project "xunit"     "AspireDemo.Tests"

# References
Add-Ref "AspireDemo.Classifier" "AspireDemo.Contracts"
Add-Ref "AspireDemo.Classifier" "AspireDemo.ServiceDefaults"
Add-Ref "AspireDemo.Handler"    "AspireDemo.Contracts"
Add-Ref "AspireDemo.Handler"    "AspireDemo.ServiceDefaults"
Add-Ref "AspireDemo.Web"        "AspireDemo.Contracts"
Add-Ref "AspireDemo.Tests"      "AspireDemo.AppHost"
Add-Ref "AspireDemo.Tests"      "AspireDemo.Contracts"
Add-Ref "AspireDemo.AppHost"    "AspireDemo.Classifier"
Add-Ref "AspireDemo.AppHost"    "AspireDemo.Handler"

# Clean default Class1.cs from Contracts
Remove-Item "AspireDemo.Contracts/Class1.cs" -ErrorAction SilentlyContinue

dotnet build
Write-Host "`nDone." -ForegroundColor Green