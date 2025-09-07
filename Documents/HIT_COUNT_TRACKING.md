# Pre-Generated Message Hit Count Tracking

## Overview
Added hit count tracking to the PreGeneratedMessages table to monitor cache effectiveness.

## New Features

### 1. Model Updates
- Added `HitCount` (int) property to track usage
- Added `LastUsedAt` (DateTime?) property to track last access time

### 2. Service Enhancements
- `IncrementHitCountAsync()`: Safely increments hit count with optimistic concurrency control
- `GetHitCountStatsAsync()`: Retrieves comprehensive statistics
- `ResetHitCountsAsync()`: Resets all hit counts (useful for testing/maintenance)

### 3. New HTTP Endpoints
- `GET /api/pregeneratedmessagestats` - Get hit count statistics
- `POST /api/pregeneratedmessagestats/reset` - Reset all hit counts

## Statistics Provided
- **Total Statistics**: Messages count, total hits, overall hit rate, unused messages
- **Instruction Statistics**: Message-specific hit counts and rates
- **NPC Statistics**: NPC message-specific hit counts and rates
- **Most/Least Used Messages**: Identifies high and low usage patterns

## Usage Examples

### Get Statistics
```bash
curl -X GET "https://your-function-app.azurewebsites.net/api/pregeneratedmessagestats?code=YOUR_FUNCTION_KEY"
```

### Reset Hit Counts
```bash
curl -X POST "https://your-function-app.azurewebsites.net/api/pregeneratedmessagestats/reset?code=YOUR_FUNCTION_KEY"
```

## Sample Response
```json
{
  "timestamp": "2025-09-05T10:30:00.000Z",
  "statistics": {
    "total": {
      "messages": 1250,
      "hits": 3420,
      "hitRate": 0.75,
      "unusedMessages": 145
    },
    "instructions": {
      "messages": 50,
      "hits": 890,
      "hitRate": 0.82
    },
    "npc": {
      "messages": 1200,
      "hits": 2530,
      "hitRate": 0.73
    },
    "mostUsedMessage": {
      "messageType": "npc",
      "hitCount": 45,
      "lastUsedAt": "2025-09-05T09:15:00.000Z",
      "generatedAt": "2025-09-04T08:00:00.000Z",
      "originalMessage": "Ready for your next Azure assignment?"
    },
    "leastUsedMessage": {
      "messageType": "instruction",
      "hitCount": 0,
      "lastUsedAt": null,
      "generatedAt": "2025-09-04T08:05:00.000Z",
      "originalMessage": "Configure advanced networking features..."
    }
  }
}
```

## Benefits
1. **Cost Analysis**: Track how much money is saved by using pre-generated messages
2. **Usage Patterns**: Identify which messages are most/least popular
3. **Optimization**: Remove unused messages to reduce storage costs
4. **Performance**: Measure cache effectiveness
5. **Maintenance**: Reset counts for fresh analysis periods

## Implementation Details
- Uses optimistic concurrency control to handle concurrent updates
- Graceful error handling - hit count failures don't break main functionality
- Efficient queries with appropriate filters for statistics
- Thread-safe increment operations
