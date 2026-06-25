# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/p3rpc.difficultyEditor/*" -Force -Recurse -ErrorAction SilentlyContinue
dotnet publish "./p3rpc.difficultyEditor.csproj" -c Release -o "$env:RELOADEDIIMODS/p3rpc.difficultyEditor" /p:OutputPath="./bin/Release"

# Restore Working Directory
Pop-Location
