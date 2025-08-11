using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using GraderFunctionApp.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Azure.Data.Tables;
using Azure;

namespace GraderFunctionApp.Functions
{
    public class StudentRegistrationFunction
    {
        private readonly ILogger _logger;

        public StudentRegistrationFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<StudentRegistrationFunction>();
        }

        [Function("StudentRegistrationFunction")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            return req.Method switch
            {
                var m when m == HttpMethods.Get => HandleGet(req),
                var m when m == HttpMethods.Post => await HandlePostAsync(req),
                _ => new BadRequestResult()
            };
        }

        private IActionResult HandleGet(HttpRequest req)
        {
            string email = req.Query["email"].ToString();
            string form = $@"
<!DOCTYPE html>
<html>
<body>
<form id='form' method='post'>   
    <label for='email'>Email:</label><br>
    <input type='email' id='email' name='email' size='50' value='{email}' required><br>       
    Azure Credentials<br/>
    <textarea name='credentials' required rows='15' cols='100'></textarea>
    <br/>
    <button type='submit'>Register</button>
</form>
</body>
</html>";

            return new ContentResult
            {
                Content = form,
                ContentType = "text/html",
                StatusCode = (int)HttpStatusCode.OK
            };
        }

        private async Task<IActionResult> HandlePostAsync(HttpRequest req)
        {
            string email = req.Form["email"].ToString().Trim().ToLower();
            string credentials = req.Form["credentials"].ToString();

            _logger.LogInformation("Received registration: Email={email}", email);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(credentials))
                return new OkObjectResult("Missing Data and Registration Failed!");

            var appPrincipal = AppPrincipal.FromJson(credentials, _logger)!;
            var subscriptionId = await GetSubscriptionIdAsync(appPrincipal);

            if (subscriptionId == null)
                return new OkObjectResult("Unable to retrieve subscription ID.");

            var subscription = new Subscription { PartitionKey = email, RowKey = subscriptionId };
            var credential = CreateCredential(email, appPrincipal, subscriptionId);

            var (subscriptionTable, credentialTable) = GetTableClients();

            if (await SubscriptionExistsAsync(subscriptionTable, email, subscriptionId))
                return new OkObjectResult("You can only have one Subscription Id");

            if (!await Services.Azure.IsValidSubscriptionReaderRole(credential, subscriptionId))
                return new OkObjectResult("Your service principal is not in the contributor role for your subscription!");

            try
            {
                await subscriptionTable.AddEntityAsync(subscription);
                await credentialTable.AddEntityAsync(credential);
                return new OkObjectResult($"Thank you {email}, your registration has been received for subscription {subscriptionId}");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError($"Failed to save subscription: {ex.Message}");
                return new OkObjectResult($"Registration received but failed to save subscription: {ex.Message}");
            }
        }

        private async Task<bool> SubscriptionExistsAsync(TableClient table, string email, string subscriptionId)
        {
            try
            {
                await table.GetEntityAsync<Subscription>(email, subscriptionId);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
        }

        private static Credential CreateCredential(string email, AppPrincipal appPrincipal, string subscriptionId) =>
            new()
            {
                PartitionKey = email,
                RowKey = email,
                Timestamp = DateTime.Now,
                AppId = appPrincipal.appId,
                DisplayName = appPrincipal.displayName,
                Password = appPrincipal.password,
                Tenant = appPrincipal.tenant,
                SubscriptionId = subscriptionId
            };

        private static (TableClient subscriptionTable, TableClient credentialTable) GetTableClients()
        {
            string connStr = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";
            var serviceClient = new TableServiceClient(connStr);
            return (serviceClient.GetTableClient("Subscription"), serviceClient.GetTableClient("Credential"));
        }

        private static async Task<string?> GetSubscriptionIdAsync(AppPrincipal appPrincipal)
        {
            var azureCredentials = SdkContext.AzureCredentialsFactory
                .FromServicePrincipal(appPrincipal.appId, appPrincipal.password, appPrincipal.tenant, AzureEnvironment.AzureGlobalCloud);

            var authenticated = Microsoft.Azure.Management.Fluent.Azure.Configure().Authenticate(azureCredentials);
            var subscriptions = await authenticated.Subscriptions.ListAsync();
            return subscriptions.FirstOrDefault()?.SubscriptionId;
        }
    }
}