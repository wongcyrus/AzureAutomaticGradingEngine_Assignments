import { AzureFunctionFileSharePublisherConstruct } from "azure-common-construct/patterns/AzureFunctionFileSharePublisherConstruct";
import { AzureFunctionWindowsConstruct } from "azure-common-construct/patterns/AzureFunctionWindowsConstruct";
import { PublishMode } from "azure-common-construct/patterns/PublisherConstruct";
import { App, TerraformOutput, TerraformStack } from "cdktf";
import { AzurermProvider } from "cdktf-azure-providers/.gen/providers/azurerm/provider";
import { ResourceGroup } from "cdktf-azure-providers/.gen/providers/azurerm/resource-group";
import { StorageContainer } from "cdktf-azure-providers/.gen/providers/azurerm/storage-container";
import { StorageTable } from "cdktf-azure-providers/.gen/providers/azurerm/storage-table";
import { Resource } from "cdktf-azure-providers/.gen/providers/null/resource";
import { Construct } from "constructs";
import path = require("path");

import * as dotenv from "dotenv";
dotenv.config({ path: __dirname + "/.env" });

class AzureAutomaticGradingEngineGraderStack extends TerraformStack {
  constructor(scope: Construct, name: string) {
    super(scope, name);

    new AzurermProvider(this, "AzureRm", {
      subscriptionId: process.env.AZURE_SUBSCRIPTION_ID,
      features: [
        {
          resourceGroup: [
            {
              preventDeletionIfContainsResources: false,
            },
          ],
        },
      ],
    });

    const prefix = "GradingEngineAssignment";
    const environment = "dev";

    const resourceGroup = new ResourceGroup(this, prefix + "ResourceGroup", {
      location: "EastAsia",
      name: prefix + "ResourceGroup",
    });

    const appSettings = {
      AZURE_OPENAI_ENDPOINT: process.env.AZURE_OPENAI_ENDPOINT!,
      AZURE_OPENAI_API_KEY: process.env.AZURE_OPENAI_API_KEY!,
      DEPLOYMENT_OR_MODEL_NAME: process.env.DEPLOYMENT_OR_MODEL_NAME!,
    };

    const azureFunctionConstruct = new AzureFunctionWindowsConstruct(
      this,
      prefix + "AzureFunctionConstruct",
      {
        functionAppName: process.env.FUNCTION_APP_NAME!,
        environment,
        prefix,
        resourceGroup,
        appSettings,
        vsProjectPath: path.join(__dirname, "..", "GraderFunctionApp/"),
        publishMode: PublishMode.Always,
        functionNames: [
          "AzureGraderFunction",
          "GameTaskFunction",
          "PassTaskFunction",
        ],
      }
    );
    azureFunctionConstruct.functionApp.siteConfig.cors.allowedOrigins = ["*"];

    // Create blob container for test result XML files
    const testResultsBlobContainer = new StorageContainer(
      this,
      prefix + "TestResultsContainer",
      {
        name: "test-results",
        storageAccountName: azureFunctionConstruct.storageAccount.name,
        containerAccessType: "private",
      }
    );

    const subscriptionTable = new StorageTable(this, prefix + "SubscriptionTable", {
      name: "Subscription",
      storageAccountName: azureFunctionConstruct.storageAccount.name,
    });
    // Create table for lab credentials
    const credentialTable = new StorageTable(
      this,
      prefix + "CredentialTable",
      {
        name: "Credential",
        storageAccountName: azureFunctionConstruct.storageAccount.name,
      }
    );

    // Create table for pass test records (email + task -> current time)
    const passTestTable = new StorageTable(this, prefix + "PassTestTable", {
      name: "PassTests",
      storageAccountName: azureFunctionConstruct.storageAccount.name,
    });

    // Create table for fail test records (email + task + time)
    const failTestTable = new StorageTable(this, prefix + "FailTestTable", {
      name: "FailTests",
      storageAccountName: azureFunctionConstruct.storageAccount.name,
    });

    // Add dependency to ensure tables are created before function deployment
    azureFunctionConstruct.node.addDependency(testResultsBlobContainer);
    azureFunctionConstruct.node.addDependency(subscriptionTable);
    azureFunctionConstruct.node.addDependency(credentialTable);
    azureFunctionConstruct.node.addDependency(passTestTable);
    azureFunctionConstruct.node.addDependency(failTestTable);

    const buildTestProjectResource = new Resource(
      this,
      prefix + "BuildFunctionAppResource",
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
    // Upload the published test artifacts (self-contained) to the file share
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
    const azureFunctionFileSharePublisherConstruct =
      new AzureFunctionFileSharePublisherConstruct(
        this,
        prefix + "AzureFunctionFileSharePublisherConstruct",
        {
          functionApp: azureFunctionConstruct.functionApp,
          functionFolder: "Tests",
          localFolder: testOutputFolder,
          storageAccount: azureFunctionConstruct.storageAccount,
        }
      );
    azureFunctionFileSharePublisherConstruct.node.addDependency(
      buildTestProjectResource
    );

    new TerraformOutput(this, prefix + "AzureGraderFunctionUrl", {
      value: azureFunctionConstruct.functionUrls!["AzureGraderFunction"],
    });
    new TerraformOutput(this, prefix + "GameTaskFunctionUrl", {
      value: azureFunctionConstruct.functionUrls!["GameTaskFunction"],
    });
    new TerraformOutput(this, prefix + "PassTaskFunctionUrl", {
      value: azureFunctionConstruct.functionUrls!["PassTaskFunction"],
    });
  }
}

const app = new App({ skipValidation: true });
new AzureAutomaticGradingEngineGraderStack(
  app,
  "AzureAutomaticGradingEngineGrader"
);
app.synth();
