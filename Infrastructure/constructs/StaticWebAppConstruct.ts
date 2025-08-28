import { Construct } from "constructs";
import { ResourceGroup } from "cdktf-azure-providers/.gen/providers/azurerm/resource-group";
import { StaticWebApp } from "cdktf-azure-providers/.gen/providers/azurerm/static-web-app";
import { ApplicationInsights } from "cdktf-azure-providers/.gen/providers/azurerm/application-insights";

export class StaticWebAppConstruct extends Construct {
  public readonly staticWebApp: StaticWebApp;
  public readonly appInsights: ApplicationInsights;

  constructor(
    scope: Construct, 
    id: string, 
    resourceGroup: ResourceGroup,
    functionUrls: Record<string, string>,
    functionNames: string[],
    prefix: string
  ) {
    super(scope, id);

    this.appInsights = new ApplicationInsights(this, "AppInsights", {
      name: `${prefix.toLowerCase()}-appinsights-staticwebapp`,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
      applicationType: "web",
    });

    const appSettings = {
      ...functionNames.reduce((settings, fn) => {
        settings[`${fn}Url`] = functionUrls[fn] ?? "";
        return settings;
      }, {} as Record<string, string>),
      APPLICATIONINSIGHTS_CONNECTION_STRING: this.appInsights.connectionString,
      APPINSIGHTS_INSTRUMENTATIONKEY: this.appInsights.instrumentationKey,
    };

    this.staticWebApp = new StaticWebApp(this, "StaticWebApp", {
      name: `${prefix}StaticWebApp`,
      resourceGroupName: resourceGroup.name,
      location: resourceGroup.location,
      skuTier: "Free",
      skuSize: "Free",
      repositoryUrl: process.env.STATIC_WEBAPP_REPO_URL,
      repositoryBranch: "main",
      repositoryToken: process.env.GITHUB_TOKEN,
      appSettings,
    });
  }
}
