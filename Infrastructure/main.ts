import { AzureFunctionWindowsConstruct } from "azure-common-construct/patterns/AzureFunctionWindowsConstruct";
import { PublishMode } from "azure-common-construct/patterns/PublisherConstruct";
import { App, TerraformOutput, TerraformStack } from "cdktn";
import { AzurermProvider } from "cdktf-azure-providers/.gen/providers/azurerm/provider";
import { AzureadProvider } from "./.gen/providers/azuread/provider";
import { AzapiProvider } from "./.gen/providers/azapi/provider";
import { ResourceGroup } from "cdktf-azure-providers/.gen/providers/azurerm/resource-group";
import { StorageAccount } from "cdktf-azure-providers/.gen/providers/azurerm/storage-account";
import { UserAssignedIdentity } from "cdktf-azure-providers/.gen/providers/azurerm/user-assigned-identity";
import { LogAnalyticsWorkspace } from "cdktf-azure-providers/.gen/providers/azurerm/log-analytics-workspace";
import { Bytes } from "cdktf-azure-providers/.gen/providers/random/bytes";
import { Construct } from "constructs";
import path = require("path");

// Import custom constructs
import { GradingEngineStorageConstruct } from "./constructs/GradingEngineStorageConstruct";
import { AzureADApplicationConstruct } from "./constructs/AzureADApplicationConstruct";
import { StaticWebAppConstruct } from "./constructs/StaticWebAppConstruct";
import { BuildDeploymentConstruct } from "./constructs/BuildDeploymentConstruct";

import * as dotenv from "dotenv";
dotenv.config({ path: __dirname + "/.env" });

// Extract constants for reuse
const PREFIX = "GradingEngineAssignment";
const ENVIRONMENT = "dev";
const LOCATION = "EastAsia";
const RESOURCE_NAMES = {
  resourceGroup: "GradingEngineAssignmentResourceGroup",
  gradingIdentity: "azure-isekai-grader-identity",
  functionStorage: "azureisekaigrading2026",
  functionPlan: "azure-isekai-grading-plan",
  functionInsights: "azure-isekai-grader-insights",
  staticWebApp: "azure-isekai-grading-web",
  staticWebAppInsights: "azure-isekai-web-insights",
  logAnalyticsWorkspace: "azure-isekai-grading-workspace",
};
const FUNCTION_NAMES = [
  "GraderFunction",
  "GameTaskFunction",
  "PassTaskFunction",
  "StudentRegistrationFunction"
];

// Main stack using constructs
class AzureAutomaticGradingEngineGraderStack extends TerraformStack {
  constructor(scope: Construct, name: string) {
    super(scope, name);
    
    this.configureProviders();

    const resourceGroup = this.createResourceGroup();
    const monitoringWorkspace = this.createMonitoringWorkspace(resourceGroup);
    const gradingIdentity = this.createGradingIdentity(resourceGroup);
    const proxySigningKey = new Bytes(this, "ProxySigningKey", {
      length: 32,
    }).base64;
    const azureFunctionConstruct = this.createAzureFunction(
      resourceGroup,
      gradingIdentity,
      monitoringWorkspace,
      proxySigningKey
    );
    const storageConstruct = this.createStorageResources(azureFunctionConstruct.storageAccount);
    
    // Add storage dependencies to function
    storageConstruct.getAllResources().forEach((resource) =>
      azureFunctionConstruct.node.addDependency(resource)
    );

    const staticWebAppConstruct = new StaticWebAppConstruct(
      this,
      "StaticWebApp",
      resourceGroup,
      azureFunctionConstruct.functionUrls!,
      FUNCTION_NAMES,
      proxySigningKey,
      monitoringWorkspace.id,
      RESOURCE_NAMES.staticWebAppInsights,
      RESOURCE_NAMES.staticWebApp
    );

    const azureADConstruct = new AzureADApplicationConstruct(
      this,
      "AzureAD",
      staticWebAppConstruct.staticWebApp.defaultHostName,
      PREFIX
    );

    new BuildDeploymentConstruct(this, "BuildDeploy", azureFunctionConstruct);

    this.createOutputs(
      azureFunctionConstruct.functionUrls!,
      azureADConstruct.application,
      azureADConstruct.applicationPassword,
      staticWebAppConstruct.staticWebApp,
      azureADConstruct.studentGroup.objectId,
      gradingIdentity,
      proxySigningKey
    );
  }

  private configureProviders() {
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
    return new ResourceGroup(this, "ResourceGroup", {
      location: LOCATION,
      name: RESOURCE_NAMES.resourceGroup,
    });
  }

  private createMonitoringWorkspace(resourceGroup: ResourceGroup) {
    return new LogAnalyticsWorkspace(this, "MonitoringWorkspace", {
      name: RESOURCE_NAMES.logAnalyticsWorkspace,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
      sku: "PerGB2018",
      retentionInDays: 30,
    });
  }

  private createGradingIdentity(resourceGroup: ResourceGroup) {
    return new UserAssignedIdentity(this, "GradingIdentity", {
      name: RESOURCE_NAMES.gradingIdentity,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
    });
  }

  private createAzureFunction(
    resourceGroup: ResourceGroup,
    gradingIdentity: UserAssignedIdentity,
    monitoringWorkspace: LogAnalyticsWorkspace,
    proxySigningKey: string
  ) {
    const appSettings = {
      AZURE_OPENAI_ENDPOINT: process.env.AZURE_OPENAI_ENDPOINT!,
      AZURE_OPENAI_API_KEY: process.env.AZURE_OPENAI_API_KEY!,
      DEPLOYMENT_OR_MODEL_NAME: process.env.DEPLOYMENT_OR_MODEL_NAME!,
      AZURE_CLIENT_ID: gradingIdentity.clientId,
      ASSIGNMENT_RESOURCE_GROUP: "projProd",
      GRADER_PROXY_SIGNING_KEY: proxySigningKey,
    };

    const azureFunctionConstruct = new AzureFunctionWindowsConstruct(
      this,
      "AzureFunctionConstruct",
      {
        functionAppName: process.env.FUNCTION_APP_NAME!,
        storageAccountName: RESOURCE_NAMES.functionStorage,
        servicePlanName: RESOURCE_NAMES.functionPlan,
        applicationInsightsName: RESOURCE_NAMES.functionInsights,
        applicationInsightsWorkspaceId: monitoringWorkspace.id,
        environment: ENVIRONMENT,
        prefix: PREFIX,
        resourceGroup,
        appSettings,
        vsProjectPath: path.join(__dirname, "..", "GraderFunctionApp/"),
        publishMode: PublishMode.Always,
        functionNames: [...FUNCTION_NAMES],
      }
    );

    azureFunctionConstruct.functionApp.putIdentity({
      type: "UserAssigned",
      identityIds: [gradingIdentity.id],
    });

    return azureFunctionConstruct;
  }

  private createStorageResources(storageAccount: StorageAccount) {
    return new GradingEngineStorageConstruct(
      this,
      "StorageResources",
      storageAccount
    );
  }

  private createOutputs(
    functionUrls: Record<string, string>,
    application: any,
    applicationPassword: any,
    staticWebApp: any,
    studentGroupObjectId: string,
    gradingIdentity: UserAssignedIdentity,
    proxySigningKey: string
  ) {
    FUNCTION_NAMES.forEach((fn) => {
      new TerraformOutput(this, `${PREFIX}${fn}Url`, {
        value: functionUrls[fn],
        sensitive: true,
      });
    });

    new TerraformOutput(this, "grader_proxy_signing_key", {
      value: proxySigningKey,
      sensitive: true,
    });

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_CLIENT_ID", {
      value: application.clientId,
      sensitive: true,
    }).overrideLogicalId("AADB2C_PROVIDER_CLIENT_ID");

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_CLIENT_SECRET", {
      value: applicationPassword.value,
      sensitive: true,
    }).overrideLogicalId("AADB2C_PROVIDER_CLIENT_SECRET");

    new TerraformOutput(this, "Output_AADB2C_PROVIDER_AUTHORITY", {
      value: `https://${application.publisherDomain}.b2clogin.com/${application.publisherDomain}/v2.0/`,
    }).overrideLogicalId("AADB2C_PROVIDER_AUTHORITY");

    new TerraformOutput(this, "static_web_app_default_host_name", {
      value: `https://${staticWebApp.defaultHostName}`,
    });

    new TerraformOutput(this, "static_web_app_name", {
      value: staticWebApp.name,
    });

    new TerraformOutput(this, "static_web_app_resource_group_name", {
      value: staticWebApp.resourceGroupName,
    });

    new TerraformOutput(this, "student_group_object_id", {
      value: studentGroupObjectId,
    });

    new TerraformOutput(this, "grading_identity_client_id", {
      value: gradingIdentity.clientId,
    });

    new TerraformOutput(this, "grading_identity_principal_id", {
      value: gradingIdentity.principalId,
    });

    new TerraformOutput(this, "grading_identity_tenant_id", {
      value: gradingIdentity.tenantId,
    });

    new TerraformOutput(this, "static_web_app_api_key", {
      value: staticWebApp.apiKey,
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
