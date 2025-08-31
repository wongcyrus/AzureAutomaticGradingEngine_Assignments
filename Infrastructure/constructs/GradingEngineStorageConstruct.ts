import { Construct } from "constructs";
import { StorageContainer } from "cdktf-azure-providers/.gen/providers/azurerm/storage-container";
import { StorageTable } from "cdktf-azure-providers/.gen/providers/azurerm/storage-table";
import { StorageAccount } from "cdktf-azure-providers/.gen/providers/azurerm/storage-account";

const STORAGE_TABLES = [
  "Subscription",
  "Credential", 
  "PassTests",
  "FailTests",
  "GameStates",
  "NPCCharacter",
  "EasterEgg"
];

export class GradingEngineStorageConstruct extends Construct {
  public readonly tables: Record<string, StorageTable>;
  public readonly container: StorageContainer;

  constructor(scope: Construct, id: string, storageAccount: StorageAccount) {
    super(scope, id);

    // Create blob container for test results
    this.container = new StorageContainer(this, "TestResultsContainer", {
      name: "test-results",
      storageAccountId: storageAccount.id,
      containerAccessType: "private",
    });

    // Create all required tables
    this.tables = {};
    STORAGE_TABLES.forEach(tableName => {
      this.tables[tableName] = new StorageTable(this, `${tableName}Table`, {
        name: tableName,
        storageAccountName: storageAccount.name,
      });
    });
  }



  // Method to get all storage resources for dependency management
  public getAllResources(): (StorageTable | StorageContainer)[] {
    return [this.container, ...Object.values(this.tables)];
  }
}
