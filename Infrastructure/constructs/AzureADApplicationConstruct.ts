import { Construct } from "constructs";
import { Application } from "../.gen/providers/azuread/application";
import { ApplicationPasswordA } from "../.gen/providers/azuread/application-password";
import { ServicePrincipal } from "../.gen/providers/azuread/service-principal";

export class AzureADApplicationConstruct extends Construct {
  public readonly application: Application;
  public readonly applicationPassword: ApplicationPasswordA;
  public readonly servicePrincipal: ServicePrincipal;

  constructor(scope: Construct, id: string, staticWebAppHostName: string, prefix: string) {
    super(scope, id);

    this.application = new Application(this, "Application", {
      displayName: `${prefix}Application`,
      signInAudience: "AzureADMyOrg",
      web: {
        redirectUris: [
          `https://${staticWebAppHostName}/.auth/login/aad/callback`,
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

    this.servicePrincipal = new ServicePrincipal(this, "ServicePrincipal", {
      clientId: this.application.clientId,
      appRoleAssignmentRequired: false,
    });
  }
}
