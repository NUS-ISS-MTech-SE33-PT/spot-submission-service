# BH-07 Strict JWT Validation

## Goal

Ensure authenticated submission and moderation APIs only trust validated Cognito access tokens.

## Architecture Review Impact

- Shared API Gateway and Cognito now live in the separate `terraform/` repository.
- The submission service no longer owns edge JWT routing; moderation protection must be kept in sync with shared API Gateway.
- Both mobile and admin web Cognito app clients can now reach the edge authorizer, so the service allowlist must include both client IDs by default.

## Functional Design

- Keep ASP.NET Core `JwtBearer` as the single authentication path.
- Validate:
  - JWT signature
  - issuer
  - lifetime
  - allowed `client_id` for both active app clients
  - `token_use == access`
  - presence of `sub`
- Continue to enforce `AdminOnly` authorization for moderation endpoints.
- Resolve user identity from the authenticated principal instead of decoding the raw bearer token.

## Test Design

- `GET /spots/submissions/health` remains public and returns `200`.
- `GET /moderation/submissions` returns `401` with no token.
- `GET /moderation/submissions` returns `401` when `client_id` is not allowed.
- `GET /moderation/submissions` returns `401` when `token_use` is `id`.
- `GET /moderation/submissions` returns `200` for a valid admin access token.
- `GET /moderation/submissions` returns `200` for the secondary allowed app client when it also carries the `admin` group.
- Unit tests confirm `JwtSubjectResolver` only returns `sub` from an authenticated principal.

## TODO List

- Replace permissive JWT handling with strict claim validation.
- Remove the fallback that parsed an unvalidated bearer token.
- Keep the default allowlist aligned with shared Cognito mobile + admin web client IDs.
- Keep moderation authorization layered on top of validated JWT authentication.
- Add integration tests for valid and invalid JWT cases.
- Run `dotnet test` and `dotnet build` before commit.
