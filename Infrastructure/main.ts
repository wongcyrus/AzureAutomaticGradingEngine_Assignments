import { AzureFunctionFileSharePublisherConstruct } from "azure-common-construct/patterns/AzureFunctionFileSharePublisherConstruct";
import { AzureFunctionWindowsConstruct } from "azure-common-construct/patterns/AzureFunctionWindowsConstruct";
import { PublishMode } from "azure-common-construct/patterns/PublisherConstruct";
import { App, TerraformOutput, TerraformStack } from "cdktf";
import { AzurermProvider } from "cdktf-azure-providers/.gen/providers/azurerm/provider";
import { AzureadProvider } from "./.gen/providers/azuread/provider";
import { AzapiProvider } from "./.gen/providers/azapi/provider";
import { Application } from "./.gen/providers/azuread/application";
import { ApplicationPasswordA } from "./.gen/providers/azuread/application-password";
import { ActionsSecret } from "./.gen/providers/github/actions-secret";
import { GithubProvider } from "./.gen/providers/github/provider";
import { ResourceGroup } from "cdktf-azure-providers/.gen/providers/azurerm/resource-group";
import { StorageContainer } from "cdktf-azure-providers/.gen/providers/azurerm/storage-container";
import { StorageTable } from "cdktf-azure-providers/.gen/providers/azurerm/storage-table";
import { Resource } from "cdktf-azure-providers/.gen/providers/null/resource";
import { StaticWebApp } from "cdktf-azure-providers/.gen/providers/azurerm/static-web-app";
import { Construct } from "constructs";
import path = require("path");

import * as dotenv from "dotenv";
dotenv.config({ path: __dirname + "/.env" });

// Extract constants for reuse
const PREFIX = "GradingEngineAssignment";
const ENVIRONMENT = "dev";
const LOCATION = "EastAsia";

class AzureAutomaticGradingEngineGraderStack extends TerraformStack {
  constructor(scope: Construct, name: string) {
    super(scope, name);
    this.configureProvider();

    const resourceGroup = this.createResourceGroup();
    const staticSite = this.createStaticWebApp(resourceGroup);
    const { application, applicationPassword } = this.createAzureADApplication(staticSite);
    this.configureGitHubSecrets(application, applicationPassword, staticSite);

    const azureFunctionConstruct = this.createAzureFunction(PREFIX, ENVIRONMENT, resourceGroup);
    const storageResources = this.createStorageResources(PREFIX, azureFunctionConstruct.storageAccount.name);
    Object.values(storageResources).forEach(dep => azureFunctionConstruct.node.addDependency(dep));

    const buildTestProjectResource = this.createBuildResource(PREFIX, azureFunctionConstruct);
    this.createFileSharePublisher(PREFIX, azureFunctionConstruct, buildTestProjectResource);
    this.createOutputs(PREFIX, azureFunctionConstruct);
  }

  private configureProvider() {
    new AzurermProvider(this, "AzureRm", {
      subscriptionId: process.env.AZURE_SUBSCRIPTION_ID,
      features: [
        { resourceGroup: [{ preventDeletionIfContainsResources: false }] },
      ],
    });

    new AzureadProvider(this, "azuread", {});
    new AzapiProvider(this, "azapi", {});
  }

  private createResourceGroup() {
    return new ResourceGroup(this, `${PREFIX}ResourceGroup`, {
      location: LOCATION,
      name: `${PREFIX}ResourceGroup`,
    });
  }

  private createStaticWebApp(resourceGroup: ResourceGroup) {
    return new StaticWebApp(this, `${PREFIX}StaticWebApp`, {
      name: `${PREFIX}StaticWebApp`,
      resourceGroupName: resourceGroup.name,
      location: resourceGroup.location,
      skuTier: "Free",
      skuSize: "Free",
      repositoryUrl: process.env.STATIC_WEBAPP_REPO_URL,
      repositoryBranch: "main",
      repositoryToken: process.env.GITHUB_TOKEN,
    });
  }

  private createAzureADApplication(staticSite: StaticWebApp) {
    const application = new Application(this, "Application", {
      displayName: `${PREFIX}Application`,
      signInAudience: "AzureADMyOrg",
      web: {
        redirectUris: [`https://${staticSite.defaultHostName}/.auth/login/aadb2c/callback`],
        implicitGrant: { accessTokenIssuanceEnabled: true, idTokenIssuanceEnabled: true },
      },
    });

    const applicationPassword = new ApplicationPasswordA(this, "ApplicationPwd", {
      applicationId: application.id,
      displayName: "Application cred",
    });

    return { application, applicationPassword };
  }

  private configureGitHubSecrets(application: Application, applicationPassword: ApplicationPasswordA, staticSite: StaticWebApp) {
    const githubProvider = new GithubProvider(this, "GitHubProvider", {
      owner: "wongcyrus",
      token: process.env.GITHUB_TOKEN,
    });

    const secrets = [
      { name: "AADB2C_PROVIDER_CLIENT_ID", value: application.id },
      { name: "AADB2C_PROVIDER_CLIENT_SECRET", value: applicationPassword.value },
      { name: "AZURE_STATIC_WEB_APPS_API_TOKEN", value: staticSite.apiKey },
    ];

    secrets.forEach(secret => {
      new ActionsSecret(this, secret.name, {
        repository: process.env.STATIC_WEBAPP_REPO!,
        secretName: secret.name,
        plaintextValue: secret.value,
        provider: githubProvider,
      });
    });
  }

  private createAzureFunction(
    prefix: string,
    environment: string,
    resourceGroup: ResourceGroup
  ) {
    const appSettings = {
      AZURE_OPENAI_ENDPOINT: process.env.AZURE_OPENAI_ENDPOINT!,
      AZURE_OPENAI_API_KEY: process.env.AZURE_OPENAI_API_KEY!,
      DEPLOYMENT_OR_MODEL_NAME: process.env.DEPLOYMENT_OR_MODEL_NAME!,
    };

    const azureFunctionConstruct = new AzureFunctionWindowsConstruct(
      this,
      `${prefix}AzureFunctionConstruct`,
      {
        functionAppName: process.env.FUNCTION_APP_NAME!,
        environment,
        prefix,
        resourceGroup,
        appSettings,
        vsProjectPath: path.join(__dirname, "..", "GraderFunctionApp/"),
        publishMode: PublishMode.AfterCodeChange,
        functionNames: [
          "AzureGraderFunction",
          "GameTaskFunction",
          "PassTaskFunction",
        ],
      }
    );
    azureFunctionConstruct.functionApp.siteConfig.cors.allowedOrigins = ["*"];
    return azureFunctionConstruct;
  }

  private createStorageTable(
    prefix: string,
    name: string,
    storageAccountName: string
  ) {
    return new StorageTable(this, `${prefix}${name}Table`, {
      name,
      storageAccountName,
    });
  }

  private createStorageResources(prefix: string, storageAccountName: string) {
    const testResultsBlobContainer = new StorageContainer(
      this,
      `${prefix}TestResultsContainer`,
      {
        name: "test-results",
        storageAccountName,
        containerAccessType: "private",
      }
    );

    const subscriptionTable = this.createStorageTable(
      prefix,
      "Subscription",
      storageAccountName
    );
    const credentialTable = this.createStorageTable(
      prefix,
      "Credential",
      storageAccountName
    );
    const passTestTable = this.createStorageTable(
      prefix,
      "PassTests",
      storageAccountName
    );
    const failTestTable = this.createStorageTable(
      prefix,
      "FailTests",
      storageAccountName
    );

    return {
      testResultsBlobContainer,
      subscriptionTable,
      credentialTable,
      passTestTable,
      failTestTable,
    };
  }

  private createBuildResource(
    prefix: string,
    azureFunctionConstruct: AzureFunctionWindowsConstruct
  ) {
    const buildTestProjectResource = new Resource(
      this,
      `${prefix}BuildFunctionAppResource`,
      {
        triggers: { build_hash: "${timestamp()}" },
        dependsOn: [azureFunctionConstruct.publisher!.publishResource!],
      }
    );

    buildTestProjectResource.addOverride("provisioner", [
      {
        "local-exec": {
          working_dir: path.join(__dirname, "..", "AzureProjectTest/"),
          command: "dotnet publish -p:PublishProfile=FolderProfile",
        },
      },
    ]);
    return buildTestProjectResource;
  }

  private createFileSharePublisher(
    prefix: string,
    azureFunctionConstruct: AzureFunctionWindowsConstruct,
    buildResource: Resource
  ) {
    const testOutputFolder = path.join(
      __dirname,
      "..",
      "AzureProjectTest",
      "bin",
      "Release",
      "net8.0",
      "publish",
      "win-x64"
    );
    const publisher = new AzureFunctionFileSharePublisherConstruct(
      this,
      `${prefix}AzureFunctionFileSharePublisherConstruct`,
      {
        functionApp: azureFunctionConstruct.functionApp,
        functionFolder: "Tests",
        localFolder: testOutputFolder,
        storageAccount: azureFunctionConstruct.storageAccount,
      }
    );
    publisher.node.addDependency(buildResource);
    return publisher;
  }

  private createOutputs(
    prefix: string,
    azureFunctionConstruct: AzureFunctionWindowsConstruct
  ) {
    ["AzureGraderFunction", "GameTaskFunction", "PassTaskFunction"].forEach(
      (fn) => {
        new TerraformOutput(this, `${prefix}${fn}Url`, {
          value: azureFunctionConstruct.functionUrls![fn],
        });
      }
    );
  }
}

const app = new App({ skipValidation: true });
new AzureAutomaticGradingEngineGraderStack(
  app,
  "AzureAutomaticGradingEngineGrader"
);
app.synth();
