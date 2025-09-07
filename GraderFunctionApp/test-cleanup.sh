#!/bin/bash

# PreGeneratedMessages Testing Cleanup Script
# This script demonstrates how to use the cleanup functionality for testing

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

# Default values
OPERATION="stats"

# Help function
show_help() {
    echo -e "${MAGENTA}PreGeneratedMessages Testing Cleanup Script${NC}"
    echo -e "${MAGENTA}==============================================${NC}"
    echo ""
    echo "Usage: $0 -u <FUNCTION_APP_URL> -k <FUNCTION_KEY> [-o <OPERATION>]"
    echo ""
    echo "Required parameters:"
    echo "  -u    Function App URL (e.g., https://your-app.azurewebsites.net)"
    echo "  -k    Function Key"
    echo ""
    echo "Optional parameters:"
    echo "  -o    Operation to perform:"
    echo "        stats        - Show current statistics (default)"
    echo "        reset        - Reset hit counts only"
    echo "        clear        - Clear all messages (DESTRUCTIVE)"
    echo "        refresh      - Regenerate all messages"
    echo "        full-cleanup - Complete cleanup and regeneration"
    echo "  -h    Show this help"
    echo ""
}

# Parse command line arguments
while getopts "u:k:o:h" opt; do
    case $opt in
        u)
            FUNCTION_APP_URL="$OPTARG"
            ;;
        k)
            FUNCTION_KEY="$OPTARG"
            ;;
        o)
            OPERATION="$OPTARG"
            ;;
        h)
            show_help
            exit 0
            ;;
        \?)
            echo "Invalid option: -$OPTARG" >&2
            show_help
            exit 1
            ;;
    esac
done

# Check required parameters
if [ -z "$FUNCTION_APP_URL" ] || [ -z "$FUNCTION_KEY" ]; then
    echo -e "${RED}Error: Function App URL and Function Key are required${NC}" >&2
    show_help
    exit 1
fi

BASE_URL="$FUNCTION_APP_URL"
KEY="$FUNCTION_KEY"

# Function to show statistics
show_stats() {
    echo -e "${GREEN}Getting PreGeneratedMessage Statistics...${NC}"
    
    response=$(curl -s -X GET "$BASE_URL/api/pregeneratedmessagestats?code=$KEY")
    if [ $? -eq 0 ]; then
        echo -e "${YELLOW}Statistics Retrieved:${NC}"
        echo "$response" | jq -r '
            "Total Messages: " + (.statistics.total.messages | tostring) + 
            "\nTotal Hits: " + (.statistics.total.hits | tostring) + 
            "\nHit Rate: " + ((.statistics.total.hitRate * 100) | tostring | .[0:5]) + "%" + 
            "\nUnused Messages: " + (.statistics.total.unusedMessages | tostring) + 
            "\nInstruction Messages: " + (.statistics.instructions.messages | tostring) + 
            "\nNPC Messages: " + (.statistics.npc.messages | tostring)'
    else
        echo -e "${RED}Failed to get statistics${NC}" >&2
        return 1
    fi
}

# Function to reset hit counts
reset_hit_counts() {
    echo -e "${GREEN}Resetting Hit Counts...${NC}"
    
    response=$(curl -s -X POST "$BASE_URL/api/pregeneratedmessagestats/reset?code=$KEY")
    if [ $? -eq 0 ]; then
        timestamp=$(echo "$response" | jq -r '.timestamp')
        echo -e "${YELLOW}Hit counts reset successfully at $timestamp${NC}"
    else
        echo -e "${RED}Failed to reset hit counts${NC}" >&2
        return 1
    fi
}

# Function to clear all messages
clear_all_messages() {
    echo -e "${RED}WARNING: This will delete ALL pre-generated messages!${NC}"
    read -p "Are you sure you want to continue? (type 'YES' to confirm): " confirm
    
    if [ "$confirm" = "YES" ]; then
        echo -e "${GREEN}Clearing all PreGeneratedMessages...${NC}"
        
        response=$(curl -s -X DELETE "$BASE_URL/api/pregeneratedmessagestats/clear?code=$KEY")
        if [ $? -eq 0 ]; then
            timestamp=$(echo "$response" | jq -r '.timestamp')
            warning=$(echo "$response" | jq -r '.warning')
            echo -e "${YELLOW}All messages cleared successfully at $timestamp${NC}"
            echo -e "${RED}Warning: $warning${NC}"
        else
            echo -e "${RED}Failed to clear messages${NC}" >&2
            return 1
        fi
    else
        echo -e "${YELLOW}Operation cancelled.${NC}"
        return 1
    fi
}

# Function to refresh messages
refresh_messages() {
    echo -e "${GREEN}Refreshing PreGeneratedMessages...${NC}"
    
    response=$(curl -s -X POST "$BASE_URL/api/messages/refresh?code=$KEY")
    if [ $? -eq 0 ]; then
        echo -e "${YELLOW}Messages refreshed successfully${NC}"
        total=$(echo "$response" | jq -r '.statistics.totalMessages')
        instruction=$(echo "$response" | jq -r '.statistics.instructionMessages') 
        npc=$(echo "$response" | jq -r '.statistics.npcMessages')
        echo -e "${WHITE}Total Messages: $total${NC}"
        echo -e "${WHITE}Instruction Messages: $instruction${NC}"
        echo -e "${WHITE}NPC Messages: $npc${NC}"
    else
        echo -e "${RED}Failed to refresh messages${NC}" >&2
        return 1
    fi
}

# Function for full cleanup
full_cleanup() {
    echo -e "${GREEN}Performing full cleanup and regeneration...${NC}"
    
    # Show initial stats
    echo -e "\n${CYAN}=== BEFORE CLEANUP ===${NC}"
    show_stats
    
    # Clear all messages
    echo -e "\n${CYAN}=== CLEARING MESSAGES ===${NC}"
    if ! clear_all_messages; then
        echo -e "${RED}Cleanup cancelled or failed.${NC}"
        return 1
    fi
    
    # Verify cleared
    echo -e "\n${CYAN}=== AFTER CLEAR ===${NC}"
    sleep 2  # Give time for operation to complete
    show_stats
    
    # Regenerate messages
    echo -e "\n${CYAN}=== REGENERATING MESSAGES ===${NC}"
    refresh_messages
    
    # Show final stats
    echo -e "\n${CYAN}=== AFTER REGENERATION ===${NC}"
    sleep 5  # Give time for generation to complete
    show_stats
    
    echo -e "\n${GREEN}Full cleanup and regeneration completed!${NC}"
}

# Main execution
echo -e "${MAGENTA}PreGeneratedMessages Testing Cleanup Script${NC}"
echo -e "${MAGENTA}==============================================${NC}"
echo -e "${WHITE}Function App: $FUNCTION_APP_URL${NC}"
echo -e "${WHITE}Operation: $OPERATION${NC}"
echo ""

# Check if jq is available
if ! command -v jq &> /dev/null; then
    echo -e "${RED}Error: jq is required for JSON parsing but not installed${NC}" >&2
    echo "Please install jq: https://stedolan.github.io/jq/download/"
    exit 1
fi

# Execute the requested operation
case $OPERATION in
    "stats")
        show_stats
        ;;
    "reset")
        reset_hit_counts
        sleep 2
        show_stats
        ;;
    "clear")
        clear_all_messages
        sleep 2
        show_stats
        ;;
    "refresh")
        refresh_messages
        sleep 5
        show_stats
        ;;
    "full-cleanup")
        full_cleanup
        ;;
    *)
        echo -e "${RED}Invalid operation: $OPERATION${NC}" >&2
        show_help
        exit 1
        ;;
esac

echo -e "\n${MAGENTA}Script completed.${NC}"
