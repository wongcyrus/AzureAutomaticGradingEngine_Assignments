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
import { ApplicationInsights } from "cdktf-azure-providers/.gen/providers/azurerm/application-insights";
import { Construct } from "constructs";
import path = require("path");

import * as dotenv from "dotenv";
dotenv.config({ path: __dirname + "/.env" });

// Extract constants for reuse
const PREFIX = "GradingEngineAssignment";
const ENVIRONMENT = "dev";
const LOCATION = "EastAsia";
const FUNCTION_NAMES = [
  "GraderFunction",
  "GameTaskFunction",
  "PassTaskFunction",
];

class AzureAutomaticGradingEngineGraderStack extends TerraformStack {
  private application!: Application;
  private applicationPassword!: ApplicationPasswordA;
  private resourceGroup!: ResourceGroup;
  private staticWebApp!: StaticWebApp;
  private azureFunctionConstruct!: AzureFunctionWindowsConstruct;
  private storageResources!: ReturnType<
    AzureAutomaticGradingEngineGraderStack["createStorageResources"]
  >;

  constructor(scope: Construct, name: string) {
    super(scope, name);
    this.configureProvider();

    this.resourceGroup = this.createResourceGroup();
    this.azureFunctionConstruct = this.createAzureFunction(
      PREFIX,
      ENVIRONMENT,
      this.resourceGroup
    );
    this.storageResources = this.createStorageResources(
      PREFIX,
      this.azureFunctionConstruct.storageAccount.name
    );
    Object.values(this.storageResources).forEach((dep) =>
      this.azureFunctionConstruct.node.addDependency(dep)
    );

    this.staticWebApp = this.createStaticWebApp(this.resourceGroup);
    this.createAzureADApplication(this.staticWebApp);
    this.configureGitHubSecrets();

    const buildTestProjectResource = this.createBuildResource(
      PREFIX,
      this.azureFunctionConstruct
    );
    this.createFileSharePublisher(
      PREFIX,
      this.azureFunctionConstruct,
      buildTestProjectResource
    );
    this.createOutputs(PREFIX);
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
    const urls = [...FUNCTION_NAMES].map(
      (fn) => {
        const functionUrls = this.azureFunctionConstruct?.functionUrls ?? {};
        return { fn, url: functionUrls[fn] ?? "" };
      }
    );

    const appInsights = new ApplicationInsights(this, `${PREFIX}AppInsights`, {
      name: `${PREFIX.toLowerCase()}-appinsights-staticwebapp`,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
      applicationType: "web",
    });

    return new StaticWebApp(this, `${PREFIX}StaticWebApp`, {
      name: `${PREFIX}StaticWebApp`,
      resourceGroupName: resourceGroup.name,
      location: resourceGroup.location,
      skuTier: "Free",
      skuSize: "Free",
      repositoryUrl: process.env.STATIC_WEBAPP_REPO_URL,
      repositoryBranch: "main",
      repositoryToken: process.env.GITHUB_TOKEN,
      appSettings: {
        ...urls.reduce((settings, { fn, url }) => {
          settings[`${fn}Url`] = url;
          return settings;
        }, {} as Record<string, string>),
        APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.connectionString,
        APPINSIGHTS_INSTRUMENTATIONKEY: appInsights.instrumentationKey,
      },
    });
  }

  private createAzureADApplication(staticSite: StaticWebApp) {
    this.application = new Application(this, "Application", {
      displayName: `${PREFIX}Application`,
      signInAudience: "AzureADMyOrg",
      web: {
        redirectUris: [
          `https://${staticSite.defaultHostName}/.auth/login/aadb2c/callback`,
        ],
        implicitGrant: {
          accessTokenIssuanceEnabled: true,
          idTokenIssuanceEnabled: true,
        },
      },
    });

    this.applicationPassword = new ApplicationPasswordA(
      this,
      "ApplicationPwd",
      {
        applicationId: this.application.id,
        displayName: "Application cred",
      }
    );
  }

  private configureGitHubSecrets() {
    const githubProvider = new GithubProvider(this, "GitHubProvider", {
      owner: "wongcyrus",
      token: process.env.GITHUB_TOKEN,
    });

    const secrets = [
      { name: "AADB2C_PROVIDER_CLIENT_ID", value: this.application.id },
      {
        name: "AADB2C_PROVIDER_CLIENT_SECRET",
        value: this.applicationPassword.value,
      },
      {
        name: "AZURE_STATIC_WEB_APPS_API_TOKEN",
        value: this.staticWebApp.apiKey,
      },
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
        publishMode: PublishMode.Always,
        functionNames: [...FUNCTION_NAMES],
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
    const gameStatesTable = this.createStorageTable(
      prefix,
      "GameStates",
      storageAccountName
    );

    return {
      testResultsBlobContainer,
      subscriptionTable,
      credentialTable,
      passTestTable,
      failTestTable,
      gameStatesTable,
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

  private createOutputs(prefix: string) {
    [...FUNCTION_NAMES].forEach(
      (fn) => {
        new TerraformOutput(this, `${prefix}${fn}Url`, {
          value: this.azureFunctionConstruct.functionUrls![fn],
        });
      }
    );

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_CLIENT_ID", {
      value: this.application.id,
      sensitive: true,
    }).overrideLogicalId("AADB2C_PROVIDER_CLIENT_ID");

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_CLIENT_SECRET", {
      value: this.applicationPassword.value,
      sensitive: true,
    }).overrideLogicalId("AADB2C_PROVIDER_CLIENT_SECRET");

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_AUTHORITY", {
      value: `https://${this.application.publisherDomain}.b2clogin.com/${this.application.publisherDomain}/v2.0/`,
    }).overrideLogicalId("AADB2C_PROVIDER_AUTHORITY");

    new TerraformOutput(this, "static_web_app_default_host_name", {
      value: `https://${this.staticWebApp.defaultHostName}`,
    });

    new TerraformOutput(this, "static_web_app_api_key", {
      value: this.staticWebApp.apiKey,
      sensitive: true,
    });
  }
}

const app = new App({ skipValidation: true });
new AzureAutomaticGradingEngineGraderStack(
  app,
  "AzureAutomaticGradingEngineGrader"
);
app.synth();
