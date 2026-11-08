# Kaloree backend workspace

This folder is dedicated to the Kaloree backend within the parent `kaloree app` workspace.

- Keep backend application code, database migrations, workers, tests, infrastructure configuration and backend documentation here.
- The stack is C# / ASP.NET Core with PostgreSQL, targeting Railway deployment.
- The Flutter client belongs in the sibling `../kaloree mobile/` folder. Keep mobile code and platform configuration there.
- Read `README.md` and the relevant files in `docs/` before making changes. Preserve the distinction between implemented functionality, verified behaviour and externally configured integrations.
- Keep secrets out of source control and enforce account ownership and server-side premium entitlements.