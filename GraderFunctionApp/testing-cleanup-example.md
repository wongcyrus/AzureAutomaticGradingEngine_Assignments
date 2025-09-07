# PreGeneratedMessages Testing Cleanup Guide

This guide shows how to use the new cleanup functionality for testing PreGeneratedMessages.

## Available Cleanup Operations

### 1. View Current Statistics
Get an overview of all pre-generated messages and their usage:
```bash
curl -X GET "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats?code=YOUR_FUNCTION_KEY"
```

**Response:**
```json
{
  "timestamp": "2024-12-07T10:30:00.000Z",
  "statistics": {
    "total": {
      "messages": 150,
      "hits": 75,
      "hitRate": 0.5,
      "unusedMessages": 75
    },
    "instructions": {
      "messages": 50,
      "hits": 30,
      "hitRate": 0.6
    },
    "npc": {
      "messages": 100,
      "hits": 45,
      "hitRate": 0.45
    }
  }
}
```

### 2. Reset Hit Counts (Keep Messages)
Reset usage statistics without deleting the pre-generated messages:
```bash
curl -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats/reset?code=YOUR_FUNCTION_KEY"
```

**What it does:**
- Sets HitCount = 0 for all messages
- Clears LastUsedAt timestamps
- Keeps all pre-generated message content

### 3. Clear All Messages (Testing Only)
⚠️ **DESTRUCTIVE OPERATION** - Deletes ALL pre-generated messages:
```bash
curl -X DELETE "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats/clear?code=YOUR_FUNCTION_KEY"
```

**What it does:**
- Deletes all entries in PreGeneratedMessages table
- Processes in batches of 100 to avoid timeouts
- Includes proper error handling for concurrent operations

**Response:**
```json
{
  "message": "All pre-generated messages cleared successfully",
  "timestamp": "2024-12-07T10:35:00.000Z",
  "warning": "This operation is intended for testing purposes only"
}
```

### 4. Regenerate Messages
After clearing, regenerate fresh messages from current data:
```bash
curl -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/messages/refresh?code=YOUR_FUNCTION_KEY"
```

## Testing Workflow

### Complete Clean Slate Testing
1. **Clear all existing data:**
   ```bash
   curl -X DELETE "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats/clear?code=YOUR_KEY"
   ```

2. **Verify table is empty:**
   ```bash
   curl -X GET "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats?code=YOUR_KEY"
   ```

3. **Regenerate fresh messages:**
   ```bash
   curl -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/messages/refresh?code=YOUR_KEY"
   ```

4. **Verify new messages were created:**
   ```bash
   curl -X GET "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats?code=YOUR_KEY"
   ```

### Performance Testing Reset
When you want to test performance without regenerating messages:
```bash
# Reset hit counts only
curl -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats/reset?code=YOUR_KEY"
```

## Safety Features

### Batch Processing
- The clear operation processes messages in batches of 100
- Includes 500ms delays between batches to avoid overwhelming table storage
- Handles concurrency conflicts gracefully

### Error Handling
- 404 errors (already deleted) are handled gracefully
- Failed deletions are logged but don't stop the process
- Comprehensive logging for debugging

### Warnings
- Clear operation includes warning in response
- HTTP DELETE method used to indicate destructive nature
- Function authorization required

## Monitoring and Logging

All operations generate detailed logs you can monitor:
```bash
az functionapp logs tail --name YOUR_FUNCTION_APP --resource-group YOUR_RG
```

### Key Log Messages
- `"Starting cleanup of all pre-generated messages for testing"`
- `"Found {count} pre-generated messages to delete"`
- `"Batch {batchIndex}/{totalBatches} completed. Deleted {deletedCount}/{totalMessages}"`
- `"Completed cleanup: deleted {deletedCount} pre-generated messages"`

## Integration with CI/CD

You can integrate these endpoints into your testing pipeline:

```yaml
# Example GitHub Actions step
- name: Clear PreGenerated Messages for Testing
  run: |
    curl -X DELETE "${{ secrets.FUNCTION_APP_URL }}/api/pregeneratedmessagestats/clear?code=${{ secrets.FUNCTION_KEY }}"
    
- name: Regenerate Test Messages
  run: |
    curl -X POST "${{ secrets.FUNCTION_APP_URL }}/api/messages/refresh?code=${{ secrets.FUNCTION_KEY }}"
```

## PowerShell Alternative

```powershell
# Clear all messages
$response = Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats/clear?code=YOUR_KEY" -Method Delete
Write-Output $response

# Check results
$stats = Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP.azurewebsites.net/api/pregeneratedmessagestats?code=YOUR_KEY" -Method Get
Write-Output $stats
```

This cleanup functionality provides a comprehensive solution for testing PreGeneratedMessages with proper safety measures and monitoring capabilities.
