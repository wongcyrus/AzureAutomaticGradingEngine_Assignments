# Configuration Reference

## Infrastructure Environment

Copy `Infrastructure/.env.template` to `Infrastructure/.env`.

| Setting | Purpose |
| --- | --- |
| `AZURE_SUBSCRIPTION_ID` | Subscription that hosts the grading platform |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `AZURE_OPENAI_API_KEY` | Server-side Azure OpenAI key |
| `DEPLOYMENT_OR_MODEL_NAME` | Azure OpenAI deployment name |
| `FUNCTION_APP_NAME` | Deterministic Function App name |
| `ADMIN_EMAILS` | Comma-, semicolon-, or newline-separated operator allowlist |

Do not commit `.env`.

## Function App Settings

| Setting | Purpose |
| --- | --- |
| `AzureWebJobsStorage` | Function storage connection |
| `AZURE_CLIENT_ID` | User-assigned grading identity |
| `ASSIGNMENT_RESOURCE_GROUP` | Student assignment resource group |
| `GRADER_PROXY_SIGNING_KEY` | Validates managed-proxy identity assertions |
| `ADMIN_EMAILS` | Backend operator authorization |

Storage names are declared in `GraderFunctionApp/appsettings.json` under
`Storage`.

## Static Web Apps Settings

`Infrastructure/deploy-static-web-app.sh` owns the complete production map:

- Entra client ID and secret;
- student game/grader/progress/registration Function URLs;
- message-cache Function URLs;
- registration and class-performance admin Function URLs;
- `GRADER_PROXY_SIGNING_KEY`;
- `ADMIN_EMAILS`.

Function URLs and signing values are sensitive Terraform outputs. The deploy
script is their supported consumer.

## Public Browser Configuration

No Function key, signing key, student email, subscription ID, or Azure OpenAI
credential belongs in frontend JavaScript or static configuration.

See [Static Web Apps API topology](../architecture/static-web-apps-api.md).
