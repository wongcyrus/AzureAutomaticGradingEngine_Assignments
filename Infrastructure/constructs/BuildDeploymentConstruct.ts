import { Construct } from "constructs";
import { AzureFunctionWindowsConstruct } from "azure-common-construct/patterns/AzureFunctionWindowsConstruct";
import { AzureFunctionFileSharePublisherConstruct } from "azure-common-construct/patterns/AzureFunctionFileSharePublisherConstruct";
import { Resource } from "cdktf-azure-providers/.gen/providers/null/resource";
import path = require("path");

export class BuildDeploymentConstruct extends Construct {
  public readonly buildResource: Resource;
  public readonly publisher: AzureFunctionFileSharePublisherConstruct;

  constructor(
    scope: Construct, 
    id: string, 
    azureFunctionConstruct: AzureFunctionWindowsConstruct
  ) {
    super(scope, id);

    this.buildResource = new Resource(this, "BuildFunctionAppResource", {
      triggers: { build_hash: "${timestamp()}" },
      dependsOn: [azureFunctionConstruct.publisher!.publishResource!],
    });

    this.buildResource.addOverride("provisioner", [
      {
        "local-exec": {
          working_dir: path.join(__dirname, "..", "..", "AzureProjectTest/"),
          command: "dotnet publish -p:PublishProfile=FolderProfile",
        },
      },
    ]);

    const testOutputFolder = path.join(
      __dirname,
      "..",
      "..",
      "AzureProjectTest",
      "bin",
      "Release",
      "net8.0",
      "publish",
      "win-x64"
    );

    this.publisher = new AzureFunctionFileSharePublisherConstruct(
      this,
      "FileSharePublisher",
      {
        functionApp: azureFunctionConstruct.functionApp,
        functionFolder: "Tests",
        localFolder: testOutputFolder,
        storageAccount: azureFunctionConstruct.storageAccount,
      }
    );
    
    this.publisher.node.addDependency(this.buildResource);
  }
}
