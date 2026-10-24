# FortuPurr AuthApi — Render

## Service configuration

- Root directory: repository root
- Runtime: Docker
- Dockerfile path: `Dockerfile`
- Health check path: `/health`
- Container port: `10000`

The root Dockerfile builds `FortuPurr.AuthApi.csproj`. Do not select the legacy Dockerfile in the folder whose name contains a trailing space.

## Environment configuration

Set OAuth credentials as Render environment variables using ASP.NET Core double-underscore syntax:

- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Authentication__Discord__ClientId`
- `Authentication__Discord__ClientSecret`
- `Authentication__GitHub__ClientId`
- `Authentication__GitHub__ClientSecret`

For email verification, set `Email__Smtp__Host`, `Email__Smtp__Username`, `Email__Smtp__AppPassword`, `Email__Smtp__From`, and optionally `Email__Smtp__Port` (587 by default).

OAuth providers can now be left unconfigured without preventing service startup; their login routes return HTTP 503 until their credentials are configured. Email verification returns HTTP 503 if SMTP is not configured.

## OAuth callback URLs

Configure provider callback URLs using the public Render host:

- `/signin-google`
- `/signin-discord`
- `/signin-github`

Never commit OAuth credentials or SMTP credentials to the repository. The `/health` endpoint is an unauthenticated service health probe, not an authentication test.
