# Verify Student Grading Access

Run this verification after either scripted or manual onboarding and before
registering the subscription in Azure Isekai.

## 1. Confirm the Selected Subscription

In Azure Cloud Shell:

```bash
az account show --query '{name:name,id:id,tenant:tenantId,user:user.name}' -o table
```

Stop if the subscription or signed-in email is not the one intended for the
student.

## 2. Run the Read-Only Verifier

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

The verifier does not create, update, or delete Azure resources. It exits `0`
only when all checks pass and exits nonzero when setup is incomplete,
conflicting, or owned by another email.

## 3. Check the Result

Every successful result must show:

- The intended subscription ID and Cloud Shell email.
- Matching `GradingStudentEmail` ownership.
- `Verification passed. Azure Isekai grading access is configured correctly.`

For a same-tenant subscription, it must also show:

```text
Expected access mode: direct
Direct Grader Reader assignments: 1
Direct Grader Website Contributor assignments: 1
Lighthouse subscription Reader authorizations: 0
Lighthouse resource-group Reader authorizations: 0
Lighthouse resource-group Website Contributor authorizations: 0
```

Counts above `1` still indicate access exists, but the teacher should inspect
and remove duplicate assignments.

For a cross-tenant subscription, it must instead show:

```text
Expected access mode: lighthouse
Direct Grader Reader assignments: 0
Direct Grader Website Contributor assignments: 0
Lighthouse subscription Reader authorizations: 1
Lighthouse resource-group Reader authorizations: 1
Lighthouse resource-group Website Contributor authorizations: 1
```

## 4. Resolve Failures

| Verifier result | Action |
| --- | --- |
| Wrong subscription or email | Select the correct subscription/account before rerunning anything. |
| Ownership tag missing | Rerun the correct onboarding method with the intended student email. |
| Ownership belongs to another email | Stop and ask the teacher to investigate; do not overwrite the tag. |
| Direct assignments missing | Same-tenant student: rerun direct onboarding. |
| Lighthouse authorizations missing | Cross-tenant student: rerun Lighthouse onboarding. |
| Lighthouse found in same tenant | Run offboarding to remove stale access, then use direct onboarding. |
| Direct grader RBAC found across tenants | Run offboarding to remove stale access, then use Lighthouse onboarding. |

Azure authorization can take several minutes to propagate. Wait five minutes
and rerun the verifier before changing a setup that otherwise appears correct.

## 5. Complete End-to-End Verification

Passing the student verifier proves that the Azure configuration exists; it
does not impersonate the grading Function's managed identity. The student must
complete the end-to-end check:

1. Register the displayed subscription ID in Azure Isekai using the same email.
2. Sign in to Azure Isekai with that email.
3. Submit the first `projProd` resource-group task for grading.
4. Confirm Azure Isekai returns an executed test result rather than an access,
   registration, or runner error. Because onboarding creates `projProd` in
   `brazilsouth`, its existence and location checks should pass.

The student performs both stages. Do not consider onboarding complete until the
Cloud Shell verifier passes and Azure Isekai successfully grades the student's
resource-group task.

If the student check fails after permissions have propagated, the teacher may
use `scripts/test-deployed-function.sh` as an administrator-only diagnostic.
That script requires Function credentials and is not part of the student
verification workflow.
