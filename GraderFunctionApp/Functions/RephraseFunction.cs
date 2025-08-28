using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using GraderFunctionApp.Interfaces;
using System.Text.Json;

namespace GraderFunctionApp.Functions
{
    public class RephraseFunction
    {
        private readonly ILogger<RephraseFunction> _logger;
        private readonly IStorageService _storageService;

        public RephraseFunction(ILogger<RephraseFunction> logger, IStorageService storageService)
        {
            _logger = logger;
            _storageService = storageService;
        }

        [Function(nameof(RephraseFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonSerializer.Deserialize<RephraseRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.MainCharacter) || string.IsNullOrEmpty(request.NpcName) || string.IsNullOrEmpty(request.OriginalMessage))
                {
                    return new BadRequestObjectResult(new { status = "ERROR", message = "main_character, npc_name, and original_message are required" });
                }

                _logger.LogInformation("RephraseFunction called for NPC: {npc}, Character: {character}", request.NpcName, request.MainCharacter);

                // Get NPC character background
                var npcCharacter = await _storageService.GetNPCCharacterAsync(request.NpcName);
                if (npcCharacter == null)
                {
                    return new NotFoundObjectResult(new { status = "ERROR", message = $"NPC '{request.NpcName}' not found" });
                }

                // Create rewrite prompt with NPC background
                var rewritePrompt = CreateRewritePrompt(request.OriginalMessage, npcCharacter.Background, request.MainCharacter);

                return new JsonResult(new 
                { 
                    status = "OK",
                    npc_name = request.NpcName,
                    main_character = request.MainCharacter,
                    original_message = request.OriginalMessage,
                    npc_background = npcCharacter.Background,
                    rewrite_prompt = rewritePrompt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RephraseFunction");
                return new ObjectResult(new { status = "ERROR", message = "Internal server error: " + ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        private string CreateRewritePrompt(string originalMessage, string npcBackground, string mainCharacter)
        {
            return $@"You are an NPC in an educational game. Your character background is: {npcBackground}

The main character's name is: {mainCharacter}

Please rewrite the following message to match your character's personality, tone, and speaking style. Keep the core information intact but make it sound like it's coming from your character:

Original message: ""{originalMessage}""

Rewritten message (stay in character):";
        }
    }

    public class RephraseRequest
    {
        public string MainCharacter { get; set; } = string.Empty;
        public string NpcName { get; set; } = string.Empty;
        public string OriginalMessage { get; set; } = string.Empty;
    }
}
