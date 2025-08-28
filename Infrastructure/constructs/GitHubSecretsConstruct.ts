import { Construct } from "constructs";
import { Application } from "../.gen/providers/azuread/application";
import { ApplicationPasswordA } from "../.gen/providers/azuread/application-password";
import { ActionsSecret } from "../.gen/providers/github/actions-secret";
import { GithubProvider } from "../.gen/providers/github/provider";

export class GitHubSecretsConstruct extends Construct {
  constructor(
    scope: Construct, 
    id: string, 
    application: Application,
    applicationPassword: ApplicationPasswordA,
    staticWebAppApiKey: string
  ) {
    super(scope, id);

    const githubProvider = new GithubProvider(this, "GitHubProvider", {
      owner: process.env.GITHUB_OWNER!,
      token: process.env.GITHUB_TOKEN!,
    });

    const secrets = [
      { name: "AADB2C_PROVIDER_CLIENT_ID", value: application.id },
      { name: "AADB2C_PROVIDER_CLIENT_SECRET", value: applicationPassword.value },
      { name: "AZURE_STATIC_WEB_APPS_API_TOKEN", value: staticWebAppApiKey },
    ];

    secrets.forEach((secret) => {
      new ActionsSecret(this, secret.name, {
        repository: process.env.STATIC_WEBAPP_REPO!,
        secretName: secret.name,
        plaintextValue: secret.value,
        provider: githubProvider,
      });
    });
  }
}
