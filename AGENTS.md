# Repository Guidelines

## Project Structure & Module Organization
The solution root holds `spot-submission-service.sln`, the `spot-submission-service/` Web API project, and Terraform IaC in `terraform/`. API entry points and routing live in `spot-submission-service/Program.cs`, with DTOs (`CreateSpotSubmissionRequest.cs`, `CreateSpotSubmissionResponse.cs`) and persistence logic (`SpotSubmissionRepository.cs`, `SpotSubmission.cs`) alongside. Environment-specific configuration is split across `appsettings.{Environment}.json`, while `Properties/launchSettings.json` defines local profiles. Docker packaging is handled via the root `docker-compose.yml` and the project-level `Dockerfile`.

## Build, Test, and Development Commands
- `dotnet restore spot-submission-service/spot-submission-service.csproj` installs NuGet dependencies.
- `dotnet build spot-submission-service.sln` compiles the API against `net9.0`.
- `dotnet run --project spot-submission-service/spot-submission-service.csproj` starts the local Minimal API at the configured ports.
- `dotnet test` will execute test projects once added; prefer a `tests/SpotSubmissionService.Tests` convention.
- `docker-compose up --build makangoapi` runs the containerized service with production settings.

## Coding Style & Naming Conventions
Follow the default C# style with four-space indentation and camelCase locals, PascalCase types, and async method names suffixed with `Async`. The project enables nullable reference types and implicit usings—keep those settings intact. New endpoints should be grouped logically in `Program.cs` using Minimal API mapping, and repository extensions should remain cohesive around DynamoDB operations. Keep configuration keys in `appsettings.*` snake_case only when matching existing AWS conventions.

## Testing Guidelines
No automated tests exist yet; add xUnit projects under `tests/` and name files `<Feature>Tests.cs`. Focus coverage on repository behavior, using DynamoDB Local or an in-memory stub to avoid hitting live AWS during CI. Run `dotnet test --collect:"XPlat Code Coverage"` when measuring coverage, and upload reports if a PR introduces critical data-path changes. Document any manual verification (e.g., curling `/spots/submissions`) in the PR until automated tests land.

## Commit & Pull Request Guidelines
Recent commits use short, imperative summaries (e.g., `fix terraform typo`); keep messages under 60 chars, optionally add body lines for context or follow-up tasks. Pull requests should include: a concise summary of intent, linked Jira/GitHub issue, local test or curl output, and notes on Terraform or configuration changes. Add screenshots of Swagger or other UI surfaces when user-visible behavior shifts.

## Security & Configuration Tips
Store AWS credentials outside source control and rely on the AWS default profile or environment variables (`AWS_PROFILE`, `AWS_REGION`). Secrets belong in secure stores, not `appsettings.*`; prefer dotnet user-secrets for local development. When changing Terraform, run `terraform plan` in `terraform/` and attach the plan output to PRs so reviewers can validate infrastructure diffs.
