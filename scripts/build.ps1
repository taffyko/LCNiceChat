param (
	[Switch] $Release = $false,
	[bool] $HideManagerGameObject = !$Release
)
Set-Location "$PSScriptRoot\.."
[xml]$csproj = Get-Content .\*.csproj
$name = $csproj.Project.PropertyGroup.AssemblyName

if ($Release) {
	dotnet build -c Release
	if (!$?) { exit 1 }
	Copy-Item ".\bin\Release\netstandard2.1\$name.dll" "..\..\BepInEx\plugins\"
	Copy-Item ".\bin\Release\netstandard2.1\$name.pdb" "..\..\BepInEx\plugins\"
	Remove-Item "..\..\BepInEx\scripts\$name*.dll" -ErrorAction SilentlyContinue
	Remove-Item "..\..\BepInEx\scripts\$name*.pdb" -ErrorAction SilentlyContinue
} else {
	dotnet build
	if (!$?) { exit 1 }
	$null = mkdir -Force "..\..\BepInEx\scripts"
	Copy-Item ".\bin\Debug\netstandard2.1\$name.dll" "..\..\BepInEx\scripts\"
	Copy-Item ".\bin\Debug\netstandard2.1\$name.pdb" "..\..\BepInEx\scripts\"
	Remove-Item "..\..\BepInEx\plugins\$name.dll" -ErrorAction SilentlyContinue
	Remove-Item "..\..\BepInEx\plugins\$name.pdb" -ErrorAction SilentlyContinue
}

$cfg = "..\..\BepInEx\config\BepInEx.cfg"
if ($HideManagerGameObject -eq $true) {
	(Get-Content $cfg) -replace "^HideManagerGameObject = false","HideManagerGameObject = true" | Set-Content $cfg
} elseif ($HideManagerGameObject -eq $false) {
	(Get-Content $cfg) -replace "^HideManagerGameObject = true","HideManagerGameObject = false" | Set-Content $cfg
}