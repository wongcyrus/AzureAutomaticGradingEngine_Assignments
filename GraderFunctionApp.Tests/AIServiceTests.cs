using Azure;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenAI.Chat;
using System.Runtime.Caching;

namespace GraderFunctionApp.Tests;

[NonParallelizable]
public class AIServiceTests
{
    private readonly Dictionary<string, string?> originalEnvironment = new();

    [SetUp]
    public void SetUp()
    {
        foreach (var name in OpenAiEnvironmentNames)
        {
            originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var pair in originalEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [TestCase("", 20, "female", "engineer", "")]
    [TestCase("?!", 20, "female", "engineer", "Tek, ?!")]
    [TestCase("Create a resource", 20, "", "engineer", "Tek, Create a resource")]
    [TestCase("Create a resource", 0, "female", "engineer", "Tek, Create a resource")]
    [TestCase("Create a resource", 201, "female", "engineer", "Tek, Create a resource")]
    public async Task PersonalizeNPCMessageAsync_InvalidInput_UsesSafeFallback(
        string message,
        int age,
        string gender,
        string background,
        string expected)
    {
        var service = CreateService();

        var result = await service.PersonalizeNPCMessageAsync(message, age, gender, background);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_PreGeneratedMessage_ReturnsCachedValue()
    {
        var preGenerated = Substitute.For<IPreGeneratedMessageService>();
        preGenerated.GetPreGeneratedNPCMessageAsync("Create a resource", 20, "female", "engineer")
            .Returns("Cached message");
        var service = CreateService(preGenerated);

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Cached message"));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_NoOpenAiConfiguration_UsesFallback()
    {
        var service = CreateService();

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_PreGeneratedLookupFailure_UsesFallback()
    {
        var preGenerated = Substitute.For<IPreGeneratedMessageService>();
        preGenerated.GetPreGeneratedNPCMessageAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("cache unavailable"));
        var service = CreateService(preGenerated);

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_InvalidEndpoint_UsesFallback()
    {
        SetOpenAiConfiguration(endpoint: "not-a-valid-uri");
        var service = CreateService();

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_SuccessfulCompletion_ReturnsTrimmedResponse()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient((object)new string?[] { "  Personalized response  " });
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Personalized response"));
        Assert.That(chatClient.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_EmptyPrimaryResponse_UsesFallbackCompletion()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(
            (object)Array.Empty<string?>(),
            (object)new string?[] { "Fallback personalized response" });
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Fallback personalized response"));
        Assert.That(chatClient.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_EmptyPrimaryAndFallbackResponse_UsesPrefixFallback()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(
            (object)Array.Empty<string?>(),
            (object)new string?[] { string.Empty });
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
        Assert.That(chatClient.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_EmptyTextResponse_UsesPrefixFallback()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient((object)new string?[] { "   " });
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [TestCase(400, "content_filter")]
    [TestCase(429, "too_many_requests")]
    [TestCase(500, "server_error")]
    public async Task PersonalizeNPCMessageAsync_RequestFailure_UsesPrefixFallback(int status, string errorCode)
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(
            new RequestFailedException(status, "Azure OpenAI failed", errorCode, null));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [Test]
    public async Task PersonalizeNPCMessageAsync_UnexpectedException_UsesPrefixFallback()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(new InvalidOperationException("boom"));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.PersonalizeNPCMessageAsync(
            "Create a resource", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("Tek, Create a resource"));
    }

    [TestCase("")]
    [TestCase(null)]
    public async Task RephraseInstructionAsync_EmptyInput_ReturnsInput(string? instruction)
    {
        var service = CreateService();

        var result = await service.RephraseInstructionAsync(instruction!);

        Assert.That(result, Is.EqualTo(instruction));
    }

    [Test]
    public async Task RephraseInstructionAsync_PreGeneratedMessage_ReturnsCachedValue()
    {
        var preGenerated = Substitute.For<IPreGeneratedMessageService>();
        preGenerated.GetPreGeneratedInstructionAsync("Create a resource").Returns("Cached instruction");
        var service = CreateService(preGenerated);

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Cached instruction"));
    }

    [Test]
    public async Task RephraseInstructionAsync_NoOpenAiConfiguration_ReturnsOriginal()
    {
        var service = CreateService();

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_PreGeneratedLookupFailure_ReturnsOriginal()
    {
        var preGenerated = Substitute.For<IPreGeneratedMessageService>();
        preGenerated.GetPreGeneratedInstructionAsync(Arg.Any<string>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("cache unavailable"));
        var service = CreateService(preGenerated);

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_CachedValue_ReturnsCachedResult()
    {
        const string instruction = "Cache this instruction";
        using var cache = new MemoryCache(nameof(RephraseInstructionAsync_CachedValue_ReturnsCachedResult));
        cache.Set(
            new CacheItem(BuildInstructionCacheKey(instruction, version: 1), "Cached instruction"),
            new CacheItemPolicy());
        var service = CreateService(
            chatClientFactory: new StubChatClientFactory(createException: new InvalidOperationException("should not create client")),
            tokenCache: cache,
            instructionVersionSelector: () => 1);

        var result = await service.RephraseInstructionAsync(instruction);

        Assert.That(result, Is.EqualTo("Cached instruction"));
    }

    [Test]
    public async Task RephraseInstructionAsync_InvalidEndpoint_ReturnsOriginal()
    {
        SetOpenAiConfiguration(endpoint: "not-a-valid-uri");
        var service = CreateService();

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_EmptyResponse_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient((object)Array.Empty<string?>());
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_EmptyTextResponse_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient((object)new string?[] { string.Empty });
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_SuccessfulCompletion_CachesResult()
    {
        const string instruction = "Provision a storage account";
        using var cache = new MemoryCache(nameof(RephraseInstructionAsync_SuccessfulCompletion_CachesResult));
        SetOpenAiConfiguration();
        var liveChatClient = new SequenceChatClient((object)new string?[] { "Rephrased instruction" });
        var liveService = CreateService(
            chatClientFactory: new StubChatClientFactory(liveChatClient),
            tokenCache: cache,
            instructionVersionSelector: () => 1);

        var firstResult = await liveService.RephraseInstructionAsync(instruction);

        var cachedService = CreateService(
            chatClientFactory: new StubChatClientFactory(createException: new InvalidOperationException("cache should satisfy request")),
            tokenCache: cache,
            instructionVersionSelector: () => 1);
        var cachedResult = await cachedService.RephraseInstructionAsync(instruction);

        Assert.That(firstResult, Is.EqualTo("Rephrased instruction"));
        Assert.That(cachedResult, Is.EqualTo("Rephrased instruction"));
        Assert.That(liveChatClient.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RephraseInstructionAsync_RequestFailedException_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(
            new RequestFailedException(500, "Azure OpenAI failed", "server_error", null));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_ArgumentException_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var service = CreateService(
            chatClientFactory: new StubChatClientFactory(createException: new ArgumentException("bad endpoint")));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_HttpRequestException_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(new HttpRequestException("network failure"));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_TaskCanceledException_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(new TaskCanceledException("timeout"));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    [Test]
    public async Task RephraseInstructionAsync_UnexpectedException_ReturnsOriginal()
    {
        SetOpenAiConfiguration();
        var chatClient = new SequenceChatClient(new InvalidOperationException("unexpected"));
        var service = CreateService(chatClientFactory: new StubChatClientFactory(chatClient));

        var result = await service.RephraseInstructionAsync("Create a resource");

        Assert.That(result, Is.EqualTo("Create a resource"));
    }

    private static AIService CreateService(
        IPreGeneratedMessageService? preGenerated = null,
        IAIServiceChatClientFactory? chatClientFactory = null,
        ObjectCache? tokenCache = null,
        Func<int>? instructionVersionSelector = null)
    {
        var services = new ServiceCollection();
        if (preGenerated != null)
        {
            services.AddSingleton(preGenerated);
        }

        return new AIService(
            NullLogger<AIService>.Instance,
            services.BuildServiceProvider(),
            null,
            chatClientFactory,
            tokenCache,
            instructionVersionSelector);
    }

    private static void SetOpenAiConfiguration(
        string endpoint = "https://example.openai.azure.com/",
        string apiKey = "test-api-key",
        string deployment = "test-deployment")
    {
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", endpoint);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", apiKey);
        Environment.SetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME", deployment);
    }

    private static string BuildInstructionCacheKey(string instruction, int version)
    {
        var instructionHash = MessageCacheKeyHelper.ComputeHash(instruction);
        return $"instruction_{instructionHash}_{version}";
    }

    private static readonly string[] OpenAiEnvironmentNames =
    [
        "AZURE_OPENAI_ENDPOINT",
        "AZURE_OPENAI_API_KEY",
        "DEPLOYMENT_OR_MODEL_NAME"
    ];

    private sealed class StubChatClientFactory(
        IAIServiceChatClient? chatClient = null,
        Exception? createException = null) : IAIServiceChatClientFactory
    {
        public IAIServiceChatClient Create(Uri endpoint, string apiKey, string deploymentOrModelName)
        {
            if (createException != null)
            {
                throw createException;
            }

            return chatClient ?? throw new InvalidOperationException("No chat client was configured for this test.");
        }
    }

    private sealed class SequenceChatClient(params object[] outcomes) : IAIServiceChatClient
    {
        private readonly Queue<object> outcomes = new(outcomes);

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string?>> CompleteChatAsync(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options)
        {
            CallCount++;

            var outcome = outcomes.Count > 0 ? outcomes.Dequeue() : Array.Empty<string?>();
            if (outcome is Exception exception)
            {
                throw exception;
            }

            return Task.FromResult((IReadOnlyList<string?>)outcome);
        }
    }
}
