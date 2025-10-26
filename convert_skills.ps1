using namespace System.Text.Json
using namespace System.Text.Json.Serialization

Add-Type -Path "I:\Skua\Skua.Core\bin\Release\net6.0\Skua.Core.dll"
Add-Type -Path "I:\Skua\Skua.Core.Models\bin\Release\net6.0\Skua.Core.Models.dll"
Add-Type -Path "I:\Skua\Skua.Core.Interfaces\bin\Release\net6.0\Skua.Core.Interfaces.dll"

$textFile = "C:\Users\bowli\AppData\Roaming\Skua\UserAdvancedSkills.txt"
$jsonFile = "C:\Users\bowli\AppData\Roaming\Skua\UserAdvancedSkills.json"

if (Test-Path $textFile) {
    $content = Get-Content $textFile -Raw
    $jsonConfig = [Skua.Core.Skills.AdvancedSkillsParser]::ParseTextToJson($content)
    
    $options = [JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $options.PropertyNamingPolicy = [JsonNamingPolicy]::CamelCase
    
    $json = [JsonSerializer]::Serialize($jsonConfig, $options)
    Set-Content -Path $jsonFile -Value $json -Encoding UTF8
    
    Write-Host "Converted $textFile to $jsonFile"
} else {
    Write-Host "File not found: $textFile"
}
