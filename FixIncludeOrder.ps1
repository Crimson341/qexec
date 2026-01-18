# PowerShell script to fix include order in Skua C# scripts
param(
    [string]$FilePath = "C:\Users\bowli\AppData\Roaming\Skua\Scripts\Other\Classes\0AllClasses.cs"
)

function Get-Dependencies {
    param([string]$File)
    
    if (!(Test-Path $File)) { return @() }
    
    $content = Get-Content $File -Raw
    $includes = @()
    
    # Extract all cs_include lines
    $matches = [regex]::Matches($content, '//cs_include\s+(.+?)\.cs')
    foreach ($match in $matches) {
        $includes += $match.Groups[1].Value + ".cs"
    }
    
    return $includes
}

function Build-DependencyGraph {
    param([string]$RootFile, [string]$ScriptsRoot)
    
    $visited = @{}
    $dependencies = @{}
    $queue = @($RootFile)
    
    while ($queue.Length -gt 0) {
        $current = $queue[0]
        $queue = $queue[1..($queue.Length-1)]
        
        if ($visited.ContainsKey($current)) { continue }
        $visited[$current] = $true
        
        $fullPath = Join-Path $ScriptsRoot $current
        $deps = Get-Dependencies $fullPath
        $dependencies[$current] = $deps
        
        foreach ($dep in $deps) {
            if (!$visited.ContainsKey($dep)) {
                $queue += $dep
            }
        }
    }
    
    return $dependencies
}

function Topological-Sort {
    param([hashtable]$Dependencies)
    
    $sorted = @()
    $visited = @{}
    $temp = @{}
    
    function Visit($node) {
        if ($temp.ContainsKey($node)) {
            Write-Warning "Circular dependency detected involving: $node"
            return
        }
        if ($visited.ContainsKey($node)) { return }
        
        $temp[$node] = $true
        
        if ($Dependencies.ContainsKey($node)) {
            foreach ($dep in $Dependencies[$node]) {
                Visit $dep
            }
        }
        
        $temp.Remove($node)
        $visited[$node] = $true
        $sorted = @($node) + $sorted
    }
    
    foreach ($node in $Dependencies.Keys) {
        if (!$visited.ContainsKey($node)) {
            Visit $node
        }
    }
    
    return $sorted
}

# Main execution
Write-Host "Analyzing dependencies in: $FilePath"

$scriptsRoot = Split-Path (Split-Path $FilePath) -Parent
$content = Get-Content $FilePath -Raw

# Extract current includes
$includeMatches = [regex]::Matches($content, '//cs_include\s+(.+?)\.cs')
$currentIncludes = @()
foreach ($match in $includeMatches) {
    $currentIncludes += $match.Groups[1].Value + ".cs"
}

Write-Host "Found $($currentIncludes.Length) includes to sort"

# Build dependency graph
$dependencies = Build-DependencyGraph "0AllClasses.cs" $scriptsRoot

# Sort topologically  
$sortedOrder = Topological-Sort $dependencies

# Filter to only includes that were in the original file
$sortedIncludes = @()
foreach ($file in $sortedOrder) {
    $includeLine = "Scripts/" + $file.Replace("\", "/").Replace(".cs", ".cs")
    if ($currentIncludes -contains $file) {
        $sortedIncludes += "//cs_include $includeLine"
    }
}

# Replace the includes section
$newContent = $content -replace '(?s)(#region\s+includes\s*\n)(.*?)(\n#endregion)', "`$1$($sortedIncludes -join "`n")`$3"

# Backup original
$backup = $FilePath + ".backup"
Copy-Item $FilePath $backup
Write-Host "Created backup: $backup"

# Write sorted version
Set-Content $FilePath $newContent -Encoding UTF8
Write-Host "Includes sorted and written to: $FilePath"
Write-Host "Total includes processed: $($sortedIncludes.Length)"