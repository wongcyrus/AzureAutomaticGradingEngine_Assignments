/**
 * Test script for the Game API System
 * Run this in the browser console when the game is loaded
 * 
 * Note: Credentials are now automatically retrieved from Azure storage,
 * so no manual credential input is required for testing.
 */

class GameAPITester {
    constructor() {
        this.baseUrl = window.location.origin;
        this.testEmail = 'test@example.com';
        this.testGame = 'azure-learning';
        this.testNPC = 'TestNPC';
    }

    async testGameTaskAPI() {
        console.log('🧪 Testing Game Task API...');
        
        try {
            const url = `${this.baseUrl}/api/game-task?game=${this.testGame}&npc=${this.testNPC}`;
            const response = await fetch(url);
            const data = await response.json();
            
            console.log('✅ Game Task API Response:', data);
            
            if (data.status === 'OK') {
                console.log(`📋 Task: ${data.task_name || 'No task assigned'}`);
                console.log(`🎯 Score: ${data.score || 0}`);
                console.log(`✅ Completed Tasks: ${data.completed_tasks || 0}`);
            }
            
            return data;
        } catch (error) {
            console.error('❌ Game Task API Error:', error);
            return null;
        }
    }

    async testGraderAPI() {
        console.log('🧪 Testing Grader API...');
        
        try {
            const url = `${this.baseUrl}/api/grader?game=${this.testGame}&npc=${this.testNPC}`;
            const response = await fetch(url);
            const data = await response.json();
            
            console.log('✅ Grader API Response:', data);
            
            if (data.status === 'OK') {
                console.log(`📝 Message: ${data.message}`);
                console.log(`🔄 Next Phase: ${data.next_game_phrase}`);
                if (data.task_completed) {
                    console.log('🎉 Task Completed!');
                }
            } else {
                console.log(`⚠️ Status: ${data.status} - ${data.message}`);
            }
            
            return data;
        } catch (error) {
            console.error('❌ Grader API Error:', error);
            return null;
        }
    }

    async testFullGameFlow() {
        console.log('🎮 Testing Full Game Flow...');
        console.log('Note: This simulates the simplified 2-phase interaction');
        
        // Step 1: Get a task
        console.log('\n📚 Step 1: Getting a task from NPC...');
        const taskResponse = await this.testGameTaskAPI();
        if (!taskResponse || taskResponse.status !== 'OK') {
            console.log('❌ Failed to get task, stopping flow test');
            return;
        }

        if (taskResponse.next_game_phrase === 'ALL_COMPLETED') {
            console.log('🏆 All tasks completed! No more tasks available.');
            return;
        }

        // Step 2: Test grading (this would normally happen after student works on Azure)
        console.log('\n🔍 Step 2: Testing grading (simulating completed work)...');
        await new Promise(resolve => setTimeout(resolve, 2000)); // Wait 2 seconds
        
        const gradingResult = await this.testGraderAPI();
        
        if (gradingResult && gradingResult.task_completed) {
            console.log('🎉 Task completed successfully!');
            console.log('💡 In real usage, student would now talk to NPC again for next task');
        } else if (gradingResult && gradingResult.status === 'OK') {
            console.log('⚠️ Task not completed yet - student needs to fix issues and try again');
            console.log('💡 In real usage, student would work on fixes and talk to NPC again');
        } else {
            console.log('❌ Grading failed - this might be expected if no Azure resources are set up');
        }

        console.log('\n🎉 Simplified game flow test completed!');
        console.log('💡 Real flow: 1) Talk to NPC → Get task, 2) Work on Azure, 3) Talk to NPC → Get graded');
    }

    async testNPCPlugin(npcName = 'TestNPC') {
        console.log(`🤖 Testing NPC Plugin with ${npcName}...`);
        
        // Simulate the plugin command
        if (typeof Game_Interpreter !== 'undefined') {
            const interpreter = new Game_Interpreter();
            interpreter.pluginCommand('NpcK8sPluginCommand', [npcName]);
            console.log('✅ NPC Plugin command executed');
        } else {
            console.log('⚠️ Game_Interpreter not available (not in game context)');
            console.log('💡 This test should be run within the RPG game');
        }
    }

    checkGameState(npcName) {
        if (typeof window.checkGameState === 'function') {
            window.checkGameState(npcName);
        } else {
            console.log('⚠️ checkGameState function not available');
            console.log('💡 Load the game first to access this function');
        }
    }

    resetGameState(npcName) {
        if (typeof window.resetGameState === 'function') {
            window.resetGameState(npcName);
            console.log(`🔄 Reset game state for ${npcName || 'all NPCs'}`);
        } else {
            console.log('⚠️ resetGameState function not available');
            console.log('💡 Load the game first to access this function');
        }
    }

    async testCredentialSystem() {
        console.log('🔐 Testing Credential System...');
        console.log('Note: Credentials are now automatically retrieved from Azure storage');
        
        // Test if the system can handle missing credentials gracefully
        const result = await this.testGraderAPI();
        
        if (result && result.status === 'ERROR' && result.message.includes('credentials')) {
            console.log('✅ System correctly handles missing credentials');
            console.log('💡 To fix: Register your Azure credentials using the StudentRegistrationFunction');
        } else if (result && result.status === 'OK') {
            console.log('✅ Credentials found and working correctly');
        } else {
            console.log('⚠️ Unexpected response from credential test');
        }
    }

    async testCrossNPCInteraction() {
        console.log('🤝 Testing Cross-NPC Interaction...');
        console.log('This tests what happens when students talk to multiple NPCs');
        
        try {
            // Step 1: Get task from first NPC
            console.log('\n📚 Step 1: Getting task from TrainerA...');
            const taskA = await fetch(`${this.baseUrl}/api/game-task?game=${this.testGame}&npc=TrainerA`);
            const taskAData = await taskA.json();
            console.log('TrainerA Response:', taskAData.message);
            
            if (taskAData.next_game_phrase === 'TASK_ASSIGNED') {
                // Step 2: Try to get task from second NPC while first is active
                console.log('\n🔄 Step 2: Trying to get task from TrainerB (should get casual response)...');
                const taskB = await fetch(`${this.baseUrl}/api/game-task?game=${this.testGame}&npc=TrainerB`);
                const taskBData = await taskB.json();
                console.log('TrainerB Response:', taskBData.message);
                
                if (taskBData.next_game_phrase === 'BUSY_WITH_OTHER_NPC') {
                    console.log('✅ Cross-NPC task assignment correctly handled');
                } else {
                    console.log('⚠️ Expected BUSY_WITH_OTHER_NPC response');
                }
                
                // Step 3: Try to grade with wrong NPC
                console.log('\n🎯 Step 3: Trying to grade with TrainerC (should refuse)...');
                const gradeC = await fetch(`${this.baseUrl}/api/grader?game=${this.testGame}&npc=TrainerC`);
                const gradeCData = await gradeC.json();
                console.log('TrainerC Response:', gradeCData.message);
                
                if (gradeCData.next_game_phrase === 'WRONG_NPC_FOR_GRADING') {
                    console.log('✅ Cross-NPC grading correctly refused');
                } else {
                    console.log('⚠️ Expected WRONG_NPC_FOR_GRADING response');
                }
                
                // Step 4: Grade with correct NPC
                console.log('\n✅ Step 4: Grading with correct TrainerA...');
                const gradeA = await fetch(`${this.baseUrl}/api/grader?game=${this.testGame}&npc=TrainerA`);
                const gradeAData = await gradeA.json();
                console.log('TrainerA Grading Response:', gradeAData.message);
                
            } else {
                console.log('⚠️ No active task to test cross-NPC interaction');
            }
            
            console.log('\n🎉 Cross-NPC interaction test completed!');
            
        } catch (error) {
            console.error('❌ Cross-NPC test error:', error);
        }
    }

    async runAllTests() {
        console.log('🚀 Starting comprehensive Game API tests...');
        console.log('=====================================');
        console.log('🔐 Credentials are automatically retrieved from Azure storage');
        console.log('📝 Make sure you have registered your Azure credentials first');
        console.log('=====================================\n');
        
        await this.testGameTaskAPI();
        console.log('');
        
        await this.testGraderAPI();
        console.log('');
        
        await this.testCredentialSystem();
        console.log('');
        
        await this.testCrossNPCInteraction();
        console.log('');
        
        this.testNPCPlugin();
        console.log('');
        
        this.checkGameState();
        console.log('');
        
        console.log('✨ All tests completed!');
        console.log('=====================================');
        console.log('💡 For full testing, run testFullFlow() after setting up Azure resources');
    }
}

// Create global tester instance
window.gameAPITester = new GameAPITester();

// Provide easy access functions
window.testGameAPI = () => window.gameAPITester.runAllTests();
window.testTaskAPI = () => window.gameAPITester.testGameTaskAPI();
window.testGraderAPI = () => window.gameAPITester.testGraderAPI();
window.testFullFlow = () => window.gameAPITester.testFullGameFlow();
window.testCredentials = () => window.gameAPITester.testCredentialSystem();
window.testCrossNPC = () => window.gameAPITester.testCrossNPCInteraction();

console.log('🎮 Game API Tester loaded!');
console.log('=====================================');
console.log('🔐 IMPORTANT: Credentials are now automatically retrieved from storage');
console.log('📝 Make sure to register your Azure credentials first using the registration system');
console.log('🎯 SIMPLIFIED FLOW: 1) Talk to NPC → Get task, 2) Work on Azure, 3) Talk to NPC → Get graded');
console.log('🤝 CROSS-NPC: System handles multiple NPCs intelligently with casual responses');
console.log('=====================================');
console.log('Available commands:');
console.log('  testGameAPI() - Run all basic tests');
console.log('  testTaskAPI() - Test game task API');
console.log('  testGraderAPI() - Test grader API (no parameters needed)');
console.log('  testFullFlow() - Test complete simplified game flow');
console.log('  testCredentials() - Test credential system');
console.log('  testCrossNPC() - Test cross-NPC interaction handling');
console.log('  gameAPITester.checkGameState() - Check current game state');
console.log('  gameAPITester.resetGameState() - Reset game state');
console.log('=====================================');
