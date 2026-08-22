import { Construct } from "constructs";
import { Application } from "../.gen/providers/azuread/application";
import { ApplicationPasswordA } from "../.gen/providers/azuread/application-password";
import { ServicePrincipal } from "../.gen/providers/azuread/service-principal";
import { Group } from "../.gen/providers/azuread/group";
import { AppRoleAssignment } from "../.gen/providers/azuread/app-role-assignment";

const DEFAULT_APP_ROLE_ID = "00000000-0000-0000-0000-000000000000";

export class AzureADApplicationConstruct extends Construct {
  public readonly application: Application;
  public readonly applicationPassword: ApplicationPasswordA;
  public readonly servicePrincipal: ServicePrincipal;
  public readonly studentGroup: Group;

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
      appRoleAssignmentRequired: true,
    });

    this.studentGroup = new Group(this, "StudentGroup", {
      displayName: `${prefix}Students`,
      securityEnabled: true,
    });

    new AppRoleAssignment(this, "StudentGroupAppAssignment", {
      appRoleId: DEFAULT_APP_ROLE_ID,
      principalObjectId: this.studentGroup.objectId,
      resourceObjectId: this.servicePrincipal.objectId,
    });
  }
}
