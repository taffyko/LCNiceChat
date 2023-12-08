Set-Location "$PSScriptRoot\.."
dotnet build
if ($?) {
	Remove-Item "..\..\BepInEx\plugins\NiceChat.dll" -ErrorAction SilentlyContinue
	Copy-Item ".\bin\Debug\netstandard2.1\NiceChat.dll" "..\..\BepInEx\scripts"
	$cfg = "..\..\BepInEx\config\BepInEx.cfg"
	(Get-Content $cfg) -replace "^HideManagerGameObject = false","HideManagerGameObject = true" | Set-Content $cfg
}