# PreGeneratedMessages Testing Cleanup Script
# This script demonstrates how to use the cleanup functionality for testing

param(
    [Parameter(Mandatory=$true)]
    [string]$FunctionAppUrl,
    
    [Parameter(Mandatory=$true)]
    [string]$FunctionKey,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("stats", "reset", "clear", "refresh", "full-cleanup")]
    [string]$Operation = "stats"
)

$baseUrl = "$FunctionAppUrl"
$key = $FunctionKey

function Show-Stats {
    Write-Host "Getting PreGeneratedMessage Statistics..." -ForegroundColor Green
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/pregeneratedmessagestats?code=$key" -Method Get
        Write-Host "Statistics Retrieved:" -ForegroundColor Yellow
        Write-Host "Total Messages: $($response.statistics.total.messages)" -ForegroundColor White
        Write-Host "Total Hits: $($response.statistics.total.hits)" -ForegroundColor White
        Write-Host "Hit Rate: $([math]::Round($response.statistics.total.hitRate * 100, 2))%" -ForegroundColor White
        Write-Host "Unused Messages: $($response.statistics.total.unusedMessages)" -ForegroundColor White
        Write-Host "Instruction Messages: $($response.statistics.instructions.messages)" -ForegroundColor White
        Write-Host "NPC Messages: $($response.statistics.npc.messages)" -ForegroundColor White
        return $response
    }
    catch {
        Write-Error "Failed to get statistics: $_"
        return $null
    }
}

function Reset-HitCounts {
    Write-Host "Resetting Hit Counts..." -ForegroundColor Green
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/pregeneratedmessagestats/reset?code=$key" -Method Post
        Write-Host "Hit counts reset successfully at $($response.timestamp)" -ForegroundColor Yellow
        return $response
    }
    catch {
        Write-Error "Failed to reset hit counts: $_"
        return $null
    }
}

function Clear-AllMessages {
    Write-Host "WARNING: This will delete ALL pre-generated messages!" -ForegroundColor Red
    $confirm = Read-Host "Are you sure you want to continue? (type 'YES' to confirm)"
    
    if ($confirm -eq "YES") {
        Write-Host "Clearing all PreGeneratedMessages..." -ForegroundColor Green
        try {
            $response = Invoke-RestMethod -Uri "$baseUrl/api/pregeneratedmessagestats/clear?code=$key" -Method Delete
            Write-Host "All messages cleared successfully at $($response.timestamp)" -ForegroundColor Yellow
            Write-Host "Warning: $($response.warning)" -ForegroundColor Red
            return $response
        }
        catch {
            Write-Error "Failed to clear messages: $_"
            return $null
        }
    }
    else {
        Write-Host "Operation cancelled." -ForegroundColor Yellow
        return $null
    }
}

function Refresh-Messages {
    Write-Host "Refreshing PreGeneratedMessages..." -ForegroundColor Green
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/messages/refresh?code=$key" -Method Post
        Write-Host "Messages refreshed successfully" -ForegroundColor Yellow
        Write-Host "Total Messages: $($response.statistics.totalMessages)" -ForegroundColor White
        Write-Host "Instruction Messages: $($response.statistics.instructionMessages)" -ForegroundColor White
        Write-Host "NPC Messages: $($response.statistics.npcMessages)" -ForegroundColor White
        return $response
    }
    catch {
        Write-Error "Failed to refresh messages: $_"
        return $null
    }
}

function Full-Cleanup {
    Write-Host "Performing full cleanup and regeneration..." -ForegroundColor Green
    
    # Show initial stats
    Write-Host "`n=== BEFORE CLEANUP ===" -ForegroundColor Cyan
    Show-Stats
    
    # Clear all messages
    Write-Host "`n=== CLEARING MESSAGES ===" -ForegroundColor Cyan
    $clearResult = Clear-AllMessages
    if ($null -eq $clearResult) {
        Write-Host "Cleanup cancelled or failed." -ForegroundColor Red
        return
    }
    
    # Verify cleared
    Write-Host "`n=== AFTER CLEAR ===" -ForegroundColor Cyan
    Start-Sleep -Seconds 2  # Give time for operation to complete
    Show-Stats
    
    # Regenerate messages
    Write-Host "`n=== REGENERATING MESSAGES ===" -ForegroundColor Cyan
    $refreshResult = Refresh-Messages
    
    # Show final stats
    Write-Host "`n=== AFTER REGENERATION ===" -ForegroundColor Cyan
    Start-Sleep -Seconds 5  # Give time for generation to complete
    Show-Stats
    
    Write-Host "`nFull cleanup and regeneration completed!" -ForegroundColor Green
}

# Main execution
Write-Host "PreGeneratedMessages Testing Cleanup Script" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta
Write-Host "Function App: $FunctionAppUrl" -ForegroundColor White
Write-Host "Operation: $Operation" -ForegroundColor White
Write-Host ""

switch ($Operation) {
    "stats" { Show-Stats }
    "reset" { Reset-HitCounts; Start-Sleep 2; Show-Stats }
    "clear" { Clear-AllMessages; Start-Sleep 2; Show-Stats }
    "refresh" { Refresh-Messages; Start-Sleep 5; Show-Stats }
    "full-cleanup" { Full-Cleanup }
    default { Write-Host "Invalid operation: $Operation" -ForegroundColor Red }
}

Write-Host "`nScript completed." -ForegroundColor Magenta
