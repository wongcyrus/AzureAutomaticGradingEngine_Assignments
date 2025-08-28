import { Construct } from "constructs";
import { Application } from "../.gen/providers/azuread/application";
import { ApplicationPasswordA } from "../.gen/providers/azuread/application-password";

export class AzureADApplicationConstruct extends Construct {
  public readonly application: Application;
  public readonly applicationPassword: ApplicationPasswordA;

  constructor(scope: Construct, id: string, staticWebAppHostName: string, prefix: string) {
    super(scope, id);

    this.application = new Application(this, "Application", {
      displayName: `${prefix}Application`,
      signInAudience: "AzureADMyOrg",
      web: {
        redirectUris: [
          `https://${staticWebAppHostName}/.auth/login/aadb2c/callback`,
        ],
        implicitGrant: {
          accessTokenIssuanceEnabled: true,
          idTokenIssuanceEnabled: true,
        },
      },
    });

    this.applicationPassword = new ApplicationPasswordA(this, "ApplicationPwd", {
      applicationId: this.application.id,
      displayName: "Application cred",
    });
  }
}
