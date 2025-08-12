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

    this.configureProvider();

    const prefix = "GradingEngineAssignment";
    const environment = "dev";

    const resourceGroup = new ResourceGroup(this, `${prefix}ResourceGroup`, {
      location: "EastAsia",
      name: `${prefix}ResourceGroup`,
    });

    const azureFunctionConstruct = this.createAzureFunction(
      prefix,
      environment,
      resourceGroup
    );

    const {
      testResultsBlobContainer,
      subscriptionTable,
      credentialTable,
      passTestTable,
      failTestTable,
    } = this.createStorageResources(prefix, azureFunctionConstruct.storageAccount.name);

    // Ensure dependencies
    [testResultsBlobContainer, subscriptionTable, credentialTable, passTestTable, failTestTable].forEach(
      (dep) => azureFunctionConstruct.node.addDependency(dep)
    );

    const buildTestProjectResource = this.createBuildResource(prefix, azureFunctionConstruct);

    this.createFileSharePublisher(
      prefix,
      azureFunctionConstruct,
      buildTestProjectResource
    );

    this.createOutputs(prefix, azureFunctionConstruct);
  }

  private configureProvider() {
    new AzurermProvider(this, "AzureRm", {
      subscriptionId: process.env.AZURE_SUBSCRIPTION_ID,
      features: [{ resourceGroup: [{ preventDeletionIfContainsResources: false }] }],
    });
  }

  private createAzureFunction(prefix: string, environment: string, resourceGroup: ResourceGroup) {
    const appSettings = {
      AZURE_OPENAI_ENDPOINT: process.env.AZURE_OPENAI_ENDPOINT!,
      AZURE_OPENAI_API_KEY: process.env.AZURE_OPENAI_API_KEY!,
      DEPLOYMENT_OR_MODEL_NAME: process.env.DEPLOYMENT_OR_MODEL_NAME!,
    };

    const azureFunctionConstruct = new AzureFunctionWindowsConstruct(this, `${prefix}AzureFunctionConstruct`, {
      functionAppName: process.env.FUNCTION_APP_NAME!,
      environment,
      prefix,
      resourceGroup,
      appSettings,
      vsProjectPath: path.join(__dirname, "..", "GraderFunctionApp/"),
      publishMode: PublishMode.Always,
      functionNames: ["AzureGraderFunction", "GameTaskFunction", "PassTaskFunction"],
    });
    azureFunctionConstruct.functionApp.siteConfig.cors.allowedOrigins = ["*"];
    return azureFunctionConstruct;
  }

  private createStorageTable(prefix: string, name: string, storageAccountName: string) {
    return new StorageTable(this, `${prefix}${name}Table`, {
      name,
      storageAccountName,
    });
  }

  private createStorageResources(prefix: string, storageAccountName: string) {
    const testResultsBlobContainer = new StorageContainer(this, `${prefix}TestResultsContainer`, {
      name: "test-results",
      storageAccountName,
      containerAccessType: "private",
    });

    const subscriptionTable = this.createStorageTable(prefix, "Subscription", storageAccountName);
    const credentialTable = this.createStorageTable(prefix, "Credential", storageAccountName);
    const passTestTable = this.createStorageTable(prefix, "PassTests", storageAccountName);
    const failTestTable = this.createStorageTable(prefix, "FailTests", storageAccountName);

    return { testResultsBlobContainer, subscriptionTable, credentialTable, passTestTable, failTestTable };
  }

  private createBuildResource(prefix: string, azureFunctionConstruct: AzureFunctionWindowsConstruct) {
    const buildTestProjectResource = new Resource(this, `${prefix}BuildFunctionAppResource`, {
      triggers: { build_hash: "${timestamp()}" },
      dependsOn: [azureFunctionConstruct.publisher!.publishResource!],
    });

    buildTestProjectResource.addOverride("provisioner", [{
      "local-exec": {
        working_dir: path.join(__dirname, "..", "AzureProjectTest/"),
        command: "dotnet publish -p:PublishProfile=FolderProfile",
      },
    }]);
    return buildTestProjectResource;
  }

  private createFileSharePublisher(prefix: string, azureFunctionConstruct: AzureFunctionWindowsConstruct, buildResource: Resource) {
    const testOutputFolder = path.join(__dirname, "..", "AzureProjectTest", "bin", "Release", "net8.0", "publish", "win-x64");
    const publisher = new AzureFunctionFileSharePublisherConstruct(this, `${prefix}AzureFunctionFileSharePublisherConstruct`, {
      functionApp: azureFunctionConstruct.functionApp,
      functionFolder: "Tests",
      localFolder: testOutputFolder,
      storageAccount: azureFunctionConstruct.storageAccount,
    });
    publisher.node.addDependency(buildResource);
    return publisher;
  }

  private createOutputs(prefix: string, azureFunctionConstruct: AzureFunctionWindowsConstruct) {
    ["AzureGraderFunction", "GameTaskFunction", "PassTaskFunction"].forEach(fn => {
      new TerraformOutput(this, `${prefix}${fn}Url`, {
        value: azureFunctionConstruct.functionUrls![fn],
      });
    });
  }
}

const app = new App({ skipValidation: true });
new AzureAutomaticGradingEngineGraderStack(
  app,
  "AzureAutomaticGradingEngineGrader"
);
app.synth();
