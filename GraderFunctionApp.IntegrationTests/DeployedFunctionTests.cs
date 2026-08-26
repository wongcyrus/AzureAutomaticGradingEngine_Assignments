using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace GraderFunctionApp.IntegrationTests;

[Category("DeployedFunction")]
public class DeployedFunctionTests
{
    private const string DefaultTestEmail = "deployment-test@example.com";

    private HttpClient client = null!;
    private byte[] signingKey = null!;
    private string testEmail = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var baseUrl = Environment.GetEnvironmentVariable("FUNCTION_APP_BASE_URL");
        var functionKey = Environment.GetEnvironmentVariable("AZURE_FUNCTION_KEY");
        var proxySigningKey = Environment.GetEnvironmentVariable(
            "GRADER_PROXY_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(functionKey) ||
            string.IsNullOrWhiteSpace(proxySigningKey))
        {
            Assert.Ignore(
                "Set FUNCTION_APP_BASE_URL, AZURE_FUNCTION_KEY, and GRADER_PROXY_SIGNING_KEY to run deployed Function tests.");
            return;
        }

        signingKey = Convert.FromBase64String(proxySigningKey);
        var configuredTestEmail = Environment.GetEnvironmentVariable(
            "GRADER_TEST_EMAIL");
        testEmail = string.IsNullOrWhiteSpace(configuredTestEmail)
            ? DefaultTestEmail
            : configuredTestEmail.Trim().ToLowerInvariant();
        client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        client?.Dispose();
    }

    [Test]
    public async Task StudentRegistration_Get_ReturnsEncodedHtmlForm()
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "api/StudentRegistrationFunction?email=%3Cvictim%40example.com%3E");
        var content = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(content, Does.Contain("<form method=\"post\">"));
            Assert.That(content, Does.Contain($"value=\"{testEmail}\""));
            Assert.That(content, Does.Not.Contain("victim@example.com"));
        }
    }

    [Test]
    public async Task StudentRegistration_InvalidPost_ReturnsBadRequest()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = testEmail,
            ["subscriptionId"] = "not-a-guid"
        });

        using var response = await SendAsync(
            HttpMethod.Post,
            "api/StudentRegistrationFunction",
            content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Grader_GetWithoutSubscription_ReturnsHtmlForm()
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "api/GraderFunction");
        var content = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(content, Does.Contain("subscriptionId"));
        }
    }

    [Test]
    public async Task Grader_GetWithInvalidSubscription_ReturnsBadRequest()
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "api/GraderFunction?subscriptionId=invalid&filter=test");
        var content = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(content, Does.Contain("valid subscriptionId"));
        }
    }

    [Test]
    public async Task Grader_GetWithTestSubscription_RunsAzureResourceTests()
    {
        var subscriptionId = Environment.GetEnvironmentVariable(
            "AZURE_TEST_SUBSCRIPTION_ID");
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            Assert.Ignore(
                "Set AZURE_TEST_SUBSCRIPTION_ID to run live Azure resource tests.");
            return;
        }

        var filter = Environment.GetEnvironmentVariable("AZURE_TEST_FILTER");
        var relativeUrl =
            $"api/GraderFunction?subscriptionId={Uri.EscapeDataString(subscriptionId)}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            relativeUrl += $"&filter={Uri.EscapeDataString(filter)}";
        }

        using var response = await SendAsync(
            HttpMethod.Get,
            relativeUrl);
        var content = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), content);

        var result = XDocument.Parse(content).Root;
        var expectedTestCount = Environment.GetEnvironmentVariable(
            "AZURE_EXPECTED_TEST_COUNT") ?? "40";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Attribute("result")?.Value, Is.EqualTo("Passed"));
            Assert.That(
                result?.Attribute("total")?.Value,
                Is.EqualTo(expectedTestCount),
                $"Unexpected executed test count for {relativeUrl}");
            Assert.That(
                result?.Attribute("passed")?.Value,
                Is.EqualTo(expectedTestCount));
            Assert.That(result?.Attribute("failed")?.Value, Is.EqualTo("0"));
        }
    }

    [Test]
    public async Task PassTask_Get_UsesSignedIdentity()
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "api/PassTaskFunction?email=victim%40example.com");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                document.RootElement.GetProperty("success").GetBoolean(),
                Is.True);
        }
    }

    [Test]
    public async Task MessageStats_GetWithSignedStudent_ReturnsForbidden()
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "api/pregeneratedmessagestats",
            signedEmail: "ordinary-student@example.com");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task FunctionKeyWithoutSignedIdentity_ReturnsUnauthorized()
    {
        using var response = await client.GetAsync("api/PassTaskFunction");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content = null,
        string? signedEmail = null)
    {
        var absoluteUrl = new Uri(client.BaseAddress!, relativeUrl);
        signedEmail ??= testEmail;
        var timestamp = DateTimeOffset.UtcNow
            .ToUnixTimeMilliseconds()
            .ToString();
        var canonical = string.Join(
            '\n',
            method.Method.ToUpperInvariant(),
            absoluteUrl.PathAndQuery,
            timestamp,
            signedEmail);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(
                signingKey,
                Encoding.UTF8.GetBytes(canonical)));

        using var request = new HttpRequestMessage(method, absoluteUrl)
        {
            Content = content
        };
        request.Headers.Add("x-grader-email", signedEmail);
        request.Headers.Add("x-grader-timestamp", timestamp);
        request.Headers.Add("x-grader-signature", signature);
        return await client.SendAsync(request);
    }
}
