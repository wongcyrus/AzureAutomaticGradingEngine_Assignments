# NPC Variety System Test Scenario

## 🎯 **Objective: Prevent Same NPC from Giving Consecutive Tasks**

### **Problem Solved**
- Students were getting all tasks from the same NPC
- This created a monotonous learning experience
- We want to encourage students to interact with different NPCs for variety

### **Solution Implemented**
- Track which NPC assigned each completed task in `PassTestEntity`
- Check last completed task's NPC before assigning new tasks
- Encourage students to try different NPCs with friendly messages

## 🎮 **Test Scenario: NPC Task Distribution**

### **Setup**
- Student: Bob (bob@example.com)
- NPCs: TrainerA, TrainerB, TrainerC
- Game: azure-learning

### **Scenario: Task Variety Enforcement**

#### **Step 1: Bob gets first task from TrainerA**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerA&email=bob@example.com
Response: {
  "status": "OK",
  "message": "New task: Create Virtual Networks. Set up 2 VNets in different regions...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Virtual Networks",
  "score": 0,
  "completed_tasks": 0
}
```
**Result**: TrainerA assigns first task (no previous NPC to check)

#### **Step 2: Bob completes task with TrainerA**
```
Request: GET /api/grader?game=azure-learning&npc=TrainerA&email=bob@example.com
Response: {
  "status": "OK",
  "message": "Congratulations! You completed 'Create Virtual Networks' and earned 50 points!",
  "next_game_phrase": "READY_FOR_NEXT",
  "task_completed": true,
  "score": 50,
  "completed_tasks": 1
}
```
**Database Update**: 
- `PassTestEntity` created with `AssignedByNPC = "TrainerA"`
- `TaskName = "Create Virtual Networks"`
- `PassedAt = current timestamp`

#### **Step 3: Bob tries to get another task from TrainerA (should be discouraged)**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerA&email=bob@example.com
Response: {
  "status": "OK",
  "message": "You just completed my task! Why don't you try talking to other trainers for some variety?",
  "next_game_phrase": "ENCOURAGE_VARIETY",
  "score": 50,
  "completed_tasks": 1,
  "additional_data": {
    "lastTaskNPC": "TrainerA",
    "suggestion": "Try talking to a different NPC for variety"
  }
}
```
**Result**: TrainerA politely encourages Bob to try other NPCs

#### **Step 4: Bob talks to TrainerB (should get new task)**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerB&email=bob@example.com
Response: {
  "status": "OK",
  "message": "New task: Create Storage Accounts. Set up blob storage with proper access controls...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Storage Accounts",
  "score": 50,
  "completed_tasks": 1
}
```
**Result**: TrainerB assigns new task since Bob's last task was from TrainerA

#### **Step 5: Bob completes task with TrainerB**
```
Request: GET /api/grader?game=azure-learning&npc=TrainerB&email=bob@example.com
Response: {
  "status": "OK",
  "message": "Congratulations! You completed 'Create Storage Accounts' and earned 75 points!",
  "next_game_phrase": "READY_FOR_NEXT",
  "task_completed": true,
  "score": 125,
  "completed_tasks": 2
}
```
**Database Update**: 
- New `PassTestEntity` created with `AssignedByNPC = "TrainerB"`
- `TaskName = "Create Storage Accounts"`

#### **Step 6: Bob tries TrainerB again (should be discouraged)**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerB&email=bob@example.com
Response: {
  "status": "OK",
  "message": "I think you should explore what other NPCs have to offer before coming back to me!",
  "next_game_phrase": "ENCOURAGE_VARIETY",
  "score": 125,
  "completed_tasks": 2,
  "additional_data": {
    "lastTaskNPC": "TrainerB",
    "suggestion": "Try talking to a different NPC for variety"
  }
}
```
**Result**: TrainerB encourages variety

#### **Step 7: Bob talks to TrainerC (should get new task)**
```
Request: GET /api/game-task?game=azure-learning&npc=TrainerC&email=bob@example.com
Response: {
  "status": "OK",
  "message": "New task: Create Application Insights. Set up monitoring and logging...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "Create Application Insights",
  "score": 125,
  "completed_tasks": 2
}
```
**Result**: TrainerC assigns new task

## 🎯 **Variety Encouragement Messages**

### **10 Random Responses for Task Assignment**
1. "You just completed my task! Why don't you try talking to other trainers for some variety?"
2. "I think you should explore what other NPCs have to offer before coming back to me!"
3. "You've been working with me recently. Go see what challenges the other trainers have!"
4. "Time to mix things up! Try getting a task from a different trainer this time."
5. "I just gave you a task recently. Let's give other trainers a chance to teach you something new!"
6. "You should diversify your learning! Go talk to other NPCs for different perspectives."
7. "I've been keeping you busy lately. Why not see what the other trainers are up to?"
8. "Let's spread the learning around! Try working with a different NPC for your next challenge."
9. "You've mastered my recent assignment. Time to learn from other experts around here!"
10. "I think you'd benefit from working with different trainers. Go explore what others have to offer!"

## 🔧 **Technical Implementation**

### **Database Schema Updates**
```sql
PassTestEntity {
  PartitionKey: string (email)
  RowKey: string (test identifier)
  Email: string
  TestName: string
  TaskName: string          -- NEW: Name of completed task
  AssignedByNPC: string     -- NEW: NPC who assigned the task
  PassedAt: DateTimeOffset
  Mark: int
}

FailTestEntity {
  PartitionKey: string (email)
  RowKey: string (test identifier)
  Email: string
  TestName: string
  TaskName: string          -- NEW: Name of failed task
  AssignedByNPC: string     -- NEW: NPC who assigned the task
  FailedAt: DateTimeOffset
}
```

### **Logic Flow**
1. **Task Assignment Request** → Check `GetLastTaskNPCAsync(email)`
2. **If Last NPC == Current NPC** → Return `ENCOURAGE_VARIETY` response
3. **If Last NPC != Current NPC** → Assign new task normally
4. **Task Completion/Failure** → Save both `PassTestEntity` and `FailTestEntity` with NPC name
5. **NPC Tracking** → Both successful and failed attempts are tracked for complete audit trail

### **API Response Types**
- `TASK_ASSIGNED`: Normal task assignment
- `ENCOURAGE_VARIETY`: Same NPC discouraging consecutive tasks
- `READY_FOR_NEXT`: Task completed, ready for next
- `ALL_COMPLETED`: All tasks finished

## 🎓 **Educational Benefits**

### **Learning Variety**
- **Different Teaching Styles**: Each NPC can have unique personality and approach
- **Diverse Perspectives**: Students learn from multiple "instructors"
- **Engagement**: Prevents monotony, keeps students interested
- **Exploration**: Encourages students to explore the game world

### **Real-World Simulation**
- **Multiple Mentors**: Mimics real workplace with different team leads
- **Skill Diversification**: Different NPCs can specialize in different Azure services
- **Networking**: Encourages interaction with various characters

## 🧪 **Testing Commands**

```javascript
// Test NPC variety system
async function testNPCVariety() {
  // Get task from TrainerA
  let response1 = await fetch('/api/game-task?game=azure-learning&npc=TrainerA');
  console.log('TrainerA first task:', response1);
  
  // Complete task (simulate)
  let grade1 = await fetch('/api/grader?game=azure-learning&npc=TrainerA');
  console.log('TrainerA grading:', grade1);
  
  // Try TrainerA again (should discourage)
  let response2 = await fetch('/api/game-task?game=azure-learning&npc=TrainerA');
  console.log('TrainerA second attempt:', response2);
  
  // Try TrainerB (should allow)
  let response3 = await fetch('/api/game-task?game=azure-learning&npc=TrainerB');
  console.log('TrainerB task:', response3);
}
```

## 📊 **Expected Outcomes**

### **Student Behavior Changes**
- Students naturally interact with multiple NPCs
- More balanced task distribution across NPCs
- Increased exploration of game world
- Better engagement with different learning content

### **System Benefits**
- Prevents NPC favoritism
- Ensures content variety
- Creates more realistic learning environment
- Improves overall game experience

This system creates a natural, friendly way to encourage students to interact with different NPCs while maintaining the educational flow and preventing any single NPC from dominating the learning experience.
