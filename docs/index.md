# Documentation

Choose a role or task instead of reading the documentation in repository order.

## Understand the Three Rosters

| Data set | Purpose | Managed by | Student impact |
| --- | --- | --- | --- |
| `Infrastructure/students.txt` | Grants Azure Isekai sign-in through the Entra group | Deployment operator | Allows application sign-in only |
| Teacher dashboard CSV | Defines which students appear in one teacher-owned class | Teacher | Reporting visibility only |
| `SubscriptionRegistrations` | Binds one signed-in student to one Azure subscription | Student registration flow | Selects the subscription used for grading |

These data sets are independent. See
[Student access and roster management](operations/student-access.md).

## Start by Role

### Students

- [Same-tenant onboarding](getting-started/student-same-tenant.md)
- [Cross-tenant onboarding](getting-started/student-cross-tenant.md)
- [Verify grading access](guides/verify-grading-access.md)
- [Subscription registration](guides/subscription-registration.md)

### Teachers

- [Teacher onboarding](getting-started/teacher.md)
- [Class performance dashboard](guides/teacher-dashboard.md)
- [Frequently asked questions](faq.md)

### Operators

- [Deployment](operations/deployment.md)
- [Student access and roster management](operations/student-access.md)
- [Subscription reassignment](operations/subscription-reassignment.md)
- [Troubleshooting](operations/troubleshooting.md)

### Developers

- [Development guide](development/development.md)
- [API reference](reference/api.md)
- [Configuration reference](reference/configuration.md)
- [Storage schema](reference/storage-schema.md)

## Architecture and Explanation

- [Technical design](architecture/technical-design.md)
- [Static Web Apps API topology](architecture/static-web-apps-api.md)

## Manual Recovery Procedures

Use these only when the maintained Cloud Shell launchers cannot be used:

- [Manual same-tenant direct RBAC](manual/direct-rbac.md)
- [Manual cross-tenant Azure Lighthouse](manual/lighthouse.md)

## Repository-Specific Documentation

- [Project overview](../README.md)
- [Azure Isekai frontend](../azure-isekai/README.md)
- [Public grading tests](../AzureProjectTestLib/README.md)
- [Azure common construct package](../packages/azure-common-construct/README.md)
- [CDK Terrain Azure providers](../packages/cdktf-azure-providers/README.md)
