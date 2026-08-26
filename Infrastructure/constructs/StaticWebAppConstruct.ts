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
    proxySigningKey: string,
    workspaceId: string,
    appInsightsName: string,
    staticWebAppName: string,
    adminEmails: string
  ) {
    super(scope, id);

    this.appInsights = new ApplicationInsights(this, "AppInsights", {
      name: appInsightsName,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
      applicationType: "web",
      workspaceId,
    });

    const appSettings = {
      ...functionNames.reduce((settings, fn) => {
        settings[`${fn}Url`] = functionUrls[fn] ?? "";
        return settings;
      }, {} as Record<string, string>),
      APPLICATIONINSIGHTS_CONNECTION_STRING: this.appInsights.connectionString,
      APPINSIGHTS_INSTRUMENTATIONKEY: this.appInsights.instrumentationKey,
      GRADER_PROXY_SIGNING_KEY: proxySigningKey,
      ADMIN_EMAILS: adminEmails,
    };

    this.staticWebApp = new StaticWebApp(this, "StaticWebApp", {
      name: staticWebAppName,
      resourceGroupName: resourceGroup.name,
      location: resourceGroup.location,
      skuTier: "Standard",
      skuSize: "Standard",
      appSettings,
      lifecycle: {
        ignoreChanges: ["app_settings"],
      },
    });
  }
}
