# MAK-76 Implementation Note

## What Was Done
- Enforced authenticated access for `POST /spots/submissions` in application code using API Gateway-injected `x-user-sub`.
- Added server-side ownership validation for uploaded photo references (`photoUrl` + `photoStorageKey`) against the authenticated user scope.
- Persisted `submittedBy` for submission records.
- Split Terraform routes into public routes and JWT-protected user routes for `presign` and `submission` endpoints.

## Implemented
- Ownership validation / IDOR prevention for submission photo references.
- Server-side enforcement of authenticated user identity for submission creation.
- Route-level JWT enforcement in Terraform for:
  - `POST /spots/submissions/photos/presign`
  - `POST /spots/submissions`

## Pending / Notes
- Moderation route role-based authorization (`/moderation/*`) is intentionally deferred to the next sprint.
- `terraform validate` could not be completed locally because the module reads `terraform_remote_state` from S3 and no AWS credentials were available in this environment.
