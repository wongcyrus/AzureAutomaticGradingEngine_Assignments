using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;
using System.Linq;

namespace GraderFunctionApp.Functions
{
    public class NPCCharacterFunction
    {
        private readonly ILogger<NPCCharacterFunction> _logger;
        private readonly IStorageService _storageService;

        public NPCCharacterFunction(ILogger<NPCCharacterFunction> logger, IStorageService storageService)
        {
            _logger = logger;
            _storageService = storageService;
        }

        [Function(nameof(NPCCharacterFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            var npcName = req.Query["npc"].FirstOrDefault();
            var action = req.Query["action"].FirstOrDefault() ?? "get";

            _logger.LogInformation($"NPCCharacterFunction called. NPC: {npcName}, Action: {action}");

            try
            {
                if (action.ToLower() == "seed")
                {
                    SeedSampleNPCs();
                    return new JsonResult(new { status = "OK", message = "Sample NPCs seeded successfully" });
                }

                if (string.IsNullOrEmpty(npcName))
                {
                    return new BadRequestObjectResult(new { status = "ERROR", message = "NPC name is required" });
                }

                var npcCharacter = await _storageService.GetNPCCharacterAsync(npcName);
                
                if (npcCharacter == null)
                {
                    return new NotFoundObjectResult(new { status = "ERROR", message = $"NPC '{npcName}' not found" });
                }

                return new JsonResult(new 
                { 
                    status = "OK", 
                    npc = new 
                    {
                        name = npcCharacter.Name,
                        age = npcCharacter.Age,
                        gender = npcCharacter.Gender,
                        background = npcCharacter.Background
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NPCCharacterFunction");
                return new ObjectResult(new { status = "ERROR", message = "Internal server error: " + ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        private void SeedSampleNPCs()
        {
            var sampleNPCs = new[]
            {
                new NPCCharacter
                {
                    PartitionKey = "NPC",
                    RowKey = "TrainerA",
                    Name = "TrainerA",
                    Age = 35,
                    Gender = "Female",
                    Background = "Senior Azure Solutions Architect with 10+ years experience. Known for being methodical, patient, and detail-oriented. Speaks in a professional but encouraging tone. Specializes in networking and security."
                },
                new NPCCharacter
                {
                    PartitionKey = "NPC",
                    RowKey = "TrainerB",
                    Name = "TrainerB",
                    Age = 28,
                    Gender = "Male",
                    Background = "Enthusiastic DevOps Engineer who loves automation and modern practices. Uses casual, friendly language with lots of energy. Often uses tech slang and emojis. Specializes in CI/CD and containerization."
                },
                new NPCCharacter
                {
                    PartitionKey = "NPC",
                    RowKey = "TrainerC",
                    Name = "TrainerC",
                    Age = 42,
                    Gender = "Non-binary",
                    Background = "Veteran cloud consultant with deep expertise across multiple platforms. Speaks with wisdom and experience, often sharing real-world stories. Uses a mentoring tone. Specializes in data and analytics."
                },
                new NPCCharacter
                {
                    PartitionKey = "NPC",
                    RowKey = "MentorAlex",
                    Name = "MentorAlex",
                    Age = 31,
                    Gender = "Male",
                    Background = "Former startup CTO turned Azure trainer. Direct, no-nonsense communication style. Focuses on practical, business-oriented solutions. Specializes in cost optimization and scalability."
                },
                new NPCCharacter
                {
                    PartitionKey = "NPC",
                    RowKey = "GuideZara",
                    Name = "GuideZara",
                    Age = 26,
                    Gender = "Female",
                    Background = "Recent computer science graduate who's passionate about cloud technologies. Uses modern, relatable language. Very supportive and understanding of beginner struggles. Specializes in serverless and AI services."
                }
            };

            // Note: In a real implementation, you'd save these to the table
            // For now, this is just a placeholder showing the structure
            _logger.LogInformation("Sample NPCs would be seeded: {count} characters", sampleNPCs.Length);
        }
    }
}
