# Cross-NPC Interaction Test Scenario

## 🎮 **Test Scenario: Student Talks to Multiple NPCs**

### **Setup**
- Student: Alice (alice@example.com)
- NPCs: TrainerA, TrainerB, TrainerC
- Game: azure-learning

### **Scenario 1: Task Assignment with Different NPCs**

#### **Step 1: Alice talks to TrainerA**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerA&email=alice@example.com
Response: {
  "status": "OK",
  "message": "New task: Create Virtual Networks. Set up 2 VNets in different regions...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Virtual Networks",
  "score": 0,
  "completed_tasks": 0
}
```
**Result**: Alice gets a task from TrainerA

#### **Step 2: Alice talks to TrainerB (while having active task with TrainerA)**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerB&email=alice@example.com
Response: {
  "status": "OK",
  "message": "Hello there! I see you're working with TrainerA on a task. You should complete that first before I can help you with anything new.",
  "next_game_phrase": "BUSY_WITH_OTHER_NPC",
  "score": 0,
  "completed_tasks": 0,
  "additional_data": {
    "activeTaskNPC": "TrainerA",
    "activeTaskName": "Create Virtual Networks"
  }
}
```
**Result**: TrainerB politely redirects Alice back to TrainerA

#### **Step 3: Alice tries to get grading from TrainerC (wrong NPC)**
```
Request: GET /api/grader?game=azure-learning&npc=TrainerC&email=alice@example.com
Response: {
  "status": "OK",
  "message": "I can't grade work I didn't assign! You need to go back to TrainerA for grading.",
  "next_game_phrase": "WRONG_NPC_FOR_GRADING",
  "score": 0,
  "completed_tasks": 0
}
```
**Result**: TrainerC refuses to grade TrainerA's assignment

#### **Step 4: Alice returns to TrainerA for grading**
```
Request: GET /api/grader?game=azure-learning&npc=TrainerA&email=alice@example.com
Response: {
  "status": "OK",
  "message": "Task 'Create Virtual Networks' not completed yet. 2/5 tests passed. Please fix the issues and try again.",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Virtual Networks",
  "score": 0,
  "completed_tasks": 0,
  "additional_data": {
    "testResults": {...},
    "passedTests": 2,
    "totalTests": 5
  }
}
```
**Result**: TrainerA grades the work (fails, task remains active)

#### **Step 5: Alice completes task and returns to TrainerA**
```
Request: GET /api/grader?game=azure-learning&npc=TrainerA&email=alice@example.com
Response: {
  "status": "OK",
  "message": "Congratulations! You completed 'Create Virtual Networks' and earned 50 points!",
  "next_game_phrase": "READY_FOR_NEXT",
  "task_completed": true,
  "score": 50,
  "completed_tasks": 1,
  "easter_egg_url": "https://example.com/congratulations"
}
```
**Result**: Task completed, Alice is now free to work with other NPCs

#### **Step 6: Alice can now talk to TrainerB for a new task**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerB&email=alice@example.com
Response: {
  "status": "OK",
  "message": "New task: Create Storage Accounts. Set up blob storage with proper access controls...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Storage Accounts",
  "score": 50,
  "completed_tasks": 1
}
```
**Result**: TrainerB assigns a new task since Alice is free

## 🎯 **Random Chat Responses**

### **Task Assignment Casual Responses** (when busy with other NPC)
- "Hello there! I see you're working with {otherNPC} on a task. You should complete that first before I can help you with anything new."
- "Hi! Looks like {otherNPC} has given you something to work on. Focus on that task first, then come back to see me!"
- "Greetings! I notice you have an active task with {otherNPC}. One task at a time - finish that one first!"
- "Hey! You're already busy with {otherNPC}'s assignment. Complete that before taking on more work!"
- "Hello! I can see {otherNPC} is keeping you busy. Finish up with them first, then we can chat!"
- "Nice to meet you! But I can see you're already working on something. One step at a time!"
- "Hi there! You look busy with your current task. Come back when you're ready for something new!"
- "Hello! I'd love to help, but you should finish your current assignment first. Good luck!"
- "Greetings! Focus on your current task for now. I'll be here when you're ready for the next challenge!"
- "Hey! Looks like you have your hands full already. Complete your current work first!"

### **Grading Refusal Responses** (when trying to grade wrong NPC's task)
- "I can't grade work I didn't assign! You need to go back to {otherNPC} for grading."
- "That's not my assignment to check! {otherNPC} gave you that task, so they should grade it."
- "I'm not the right NPC for grading that work. Go back to {otherNPC}!"
- "Wrong NPC! {otherNPC} assigned that task, so they need to check your work."
- "I can only grade my own assignments. {otherNPC} is waiting to check your work!"
- "I can't help with grading work I didn't assign. Find the right NPC!"
- "That's not my task to grade! Go back to whoever gave you the assignment."
- "I only grade my own assignments. You're talking to the wrong NPC!"
- "I can't check work I didn't assign. Find the NPC who gave you that task!"
- "Wrong teacher! I can only grade assignments I gave out myself."

## 🔧 **Technical Implementation**

### **Database State Management**
- Each NPC has its own GameState record: `{email}-{game}-{npc}`
- System checks ALL user's GameState records to find active tasks
- Cross-references current NPC with task-owning NPC

### **Response Types**
- `TASK_ASSIGNED`: Normal task assignment
- `BUSY_WITH_OTHER_NPC`: Student has active task with different NPC
- `WRONG_NPC_FOR_GRADING`: Student trying to grade with wrong NPC
- `READY_FOR_NEXT`: Student completed task, ready for new assignment

### **Frontend Handling**
- Plugin detects special response types
- Shows appropriate messages without changing local state
- Provides helpful guidance to students

## 🎓 **Educational Benefits**

1. **Realistic Interaction**: Mimics real classroom where you can't work on multiple assignments simultaneously
2. **Clear Guidance**: Students get helpful messages directing them to the right NPC
3. **Task Focus**: Encourages students to complete one task before starting another
4. **Natural Flow**: Matches intuitive expectations of how teacher-student interactions work

## 🧪 **Testing Commands**

```javascript
// Test cross-NPC scenario
async function testCrossNPC() {
  // Get task from TrainerA
  await fetch('/api/game-task?game=azure-learning&npc=TrainerA');
  
  // Try to get task from TrainerB (should get casual response)
  await fetch('/api/game-task?game=azure-learning&npc=TrainerB');
  
  // Try to grade with TrainerC (should get refusal)
  await fetch('/api/grader?game=azure-learning&npc=TrainerC');
  
  // Grade with correct TrainerA
  await fetch('/api/grader?game=azure-learning&npc=TrainerA');
}
```

This system ensures students have a natural, intuitive experience while maintaining proper task management and preventing confusion about which NPC assigned which task.
