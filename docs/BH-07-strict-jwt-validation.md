# BH-07 Strict JWT Validation

## Goal

Ensure authenticated submission and moderation APIs only trust validated Cognito access tokens.

## Functional Design

- Keep ASP.NET Core `JwtBearer` as the single authentication path.
- Validate:
  - JWT signature
  - issuer
  - lifetime
  - allowed `client_id`
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
- Unit tests confirm `JwtSubjectResolver` only returns `sub` from an authenticated principal.

## TODO List

- Replace permissive JWT handling with strict claim validation.
- Remove the fallback that parsed an unvalidated bearer token.
- Keep moderation authorization layered on top of validated JWT authentication.
- Add integration tests for valid and invalid JWT cases.
- Run `dotnet test` and `dotnet build` before commit.
