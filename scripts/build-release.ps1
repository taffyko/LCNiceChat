Set-Location "$PSScriptRoot\.."
dotnet build -c Release
if ($?) {
	Copy-Item ".\bin\Release\netstandard2.1\NiceChat.dll" "..\..\BepInEx\plugins"
	$cfg = "..\..\BepInEx\config\BepInEx.cfg"
	(Get-Content $cfg) -replace "^HideManagerGameObject = true","HideManagerGameObject = false" | Set-Content $cfg
}