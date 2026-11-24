# Test apps for publish and deploy

Four small apps that cover every kind of target `deploy` handles. They are not part of `PublishTool.slnx`, and
`Directory.Build.props` here keeps the repository-wide Sonar package out of them.

| Project | Kind | Hosted as | Publish kind (ProjectScanner) |
|---|---|---|---|
| CoreWebApp | ASP.NET Core, net10.0 | IIS site (ASP.NET Core Module V2, no managed code app pool) | Sdk |
| FrameworkWebApp | ASP.NET, .NET Framework 4.8, legacy web project | IIS site (v4.0 app pool) | LegacyWeb |
| CoreServiceApp | console exe, net10.0, `ServiceBase` | Windows service `PublishToolTest.CoreService` | Sdk |
| FrameworkServiceApp | WinExe, .NET Framework 4.8 (SDK-style), `ServiceBase` | Windows service `PublishToolTest.FrameworkService` | Sdk |

Every app reports `version.txt` and the `AppVersion` MSBuild property (set it as an environment variable before
publishing to change every assembly). The web apps answer `GET /` with both values and the files of their
`wwwroot` / `Content` directory, the services append them to `Logs\heartbeat.log` next to the exe every 2 seconds.
`obsolete.txt` in the web apps exists to be deleted in a second publish, and `Logs\` checks that deploy never
touches files that are not in the package.

## End-to-end test on one machine

Requires IIS with ASP.NET 4.8 and the ASP.NET Core Hosting Bundle (10.x), MSBuild on `PATH` and the dev build
(`dotnet build PublishTool.slnx -c Debug`, which builds both `PublishTool-dev.exe` and `PublishToolCli-dev.exe`). Paths below use `C:\PublishToolTest`; run from the
repository root.

1. Copy the apps into a throwaway git repository (the branch name becomes the package prefix):

   ```powershell
   New-Item -ItemType Directory C:\PublishToolTest\repo, C:\PublishToolTest\publish, C:\PublishToolTest\share, C:\PublishToolTest\deploy
   Copy-Item -Recurse TestApps\CoreWebApp, TestApps\FrameworkWebApp, TestApps\CoreServiceApp, TestApps\FrameworkServiceApp, TestApps\Directory.Build.props C:\PublishToolTest\repo
   Set-Content C:\PublishToolTest\repo\.gitignore "bin/`nobj/"
   git -C C:\PublishToolTest\repo init -b uat; git -C C:\PublishToolTest\repo add -A; git -C C:\PublishToolTest\repo commit -m v1
   ```

2. Configure and publish version 1 (wizard: publish `C:\PublishToolTest\publish`, shared `C:\PublishToolTest\share`,
   deploy `C:\PublishToolTest\deploy`, all four projects publishable):

   ```powershell
   .\PublishTool\bin\Debug\net11.0\PublishTool-dev.exe config -dir C:\PublishToolTest\repo
   .\PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe publish -dir C:\PublishToolTest\repo
   ```

3. In an elevated PowerShell, create the IIS sites (ports 8081, 8082) and the two services:

   ```powershell
   .\TestApps\Setup-TestServer.ps1
   ```

4. Elevated: deploy the package. The wizard asks for the targets the setup script printed (leave the service
   name empty for the two web apps). Then start the services, which did not exist before:

   ```powershell
   .\PublishTool\bin\Debug\net11.0\PublishTool-dev.exe deploy -dir (Get-ChildItem C:\PublishToolTest\deploy -Directory | Sort-Object Name | Select-Object -Last 1).FullName
   Start-Service PublishToolTest.CoreService, PublishToolTest.FrameworkService
   ```

   Check: `curl.exe http://localhost:8081` and `curl.exe http://localhost:8082` show version 1 and `obsolete.txt`;
   `Get-Content C:\PublishToolTest\targets\CoreServiceApp\Logs\heartbeat.log -Tail 2` shows `build=1`.

5. Version 2: change both `version.txt`, delete both `obsolete.txt` (and its line in `FrameworkWebApp.csproj`),
   commit, publish with `AppVersion=2`, and deploy elevated. IIS and both running services are stopped and started:

   ```powershell
   Set-Content C:\PublishToolTest\repo\CoreWebApp\version.txt 'core-web 2'
   Set-Content C:\PublishToolTest\repo\FrameworkWebApp\version.txt 'framework-web 2'
   Remove-Item C:\PublishToolTest\repo\CoreWebApp\wwwroot\obsolete.txt, C:\PublishToolTest\repo\FrameworkWebApp\Content\obsolete.txt
   (Get-Content C:\PublishToolTest\repo\FrameworkWebApp\FrameworkWebApp.csproj) | Where-Object { $_ -notmatch 'obsolete' } | Set-Content C:\PublishToolTest\repo\FrameworkWebApp\FrameworkWebApp.csproj
   git -C C:\PublishToolTest\repo commit -am v2
   $env:AppVersion = '2'; .\PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe publish -dir C:\PublishToolTest\repo; Remove-Item Env:AppVersion
   .\PublishTool\bin\Debug\net11.0\PublishTool-dev.exe deploy -dir (Get-ChildItem C:\PublishToolTest\deploy -Directory | Sort-Object Name | Select-Object -Last 1).FullName
   ```

   Check: both sites show version 2 and no `obsolete.txt`, the heartbeat logs show `build=2` and still contain the
   version 1 lines (the `Logs` directory survived).

6. Elevated: roll the version 2 deploy back and check that everything shows version 1 again:

   ```powershell
   .\PublishTool\bin\Debug\net11.0\PublishTool-dev.exe rollback -dir (Get-ChildItem C:\PublishToolTest\deploy -Directory | Sort-Object Name | Select-Object -Last 1).FullName
   ```

7. Optional failure test: deploy version 2 again while a file of the package is locked, which makes the copy fail and
   the automatic restore of that file fail too; then release the lock and run `rollback` (step 6):

   ```powershell
   $lock = [IO.File]::Open('C:\PublishToolTest\targets\CoreWebApp\version.txt', 'Open', 'Read', 'Read')
   .\PublishTool\bin\Debug\net11.0\PublishTool-dev.exe deploy -dir (Get-ChildItem C:\PublishToolTest\deploy -Directory | Sort-Object Name | Select-Object -Last 1).FullName
   $lock.Dispose()
   ```

8. Optional, the same server steps with the CLI (elevated, after step 6 left version 1 deployed). Each command
   prints one line per step; add `--json` to get one result object on stdout:

   ```powershell
   $cli = '.\PublishToolCli\bin\Debug\net11.0\PublishToolCli-dev.exe'
   $package = (Get-ChildItem C:\PublishToolTest\deploy -Directory | Sort-Object Name | Select-Object -Last 1).FullName
   & $cli deploy -dir $package; $LASTEXITCODE               # dry run: plan + warnings, exit 0, nothing changes
   & $cli deploy -dir $package --yes; $LASTEXITCODE         # deploys version 2, exit 0
   & $cli rollback -dir $package; $LASTEXITCODE             # dry run of the rollback, exit 0
   & $cli rollback -dir $package --yes; $LASTEXITCODE       # exit 0, version 1 again
   ```

   If the dry run lists warnings, `deploy --yes` stops with exit 3 before changing anything; repeat it with
   `--accept <the codes printed in brackets>`.

   With the lock of step 7 held, `deploy --yes` exits 4 (restore failed); release the lock and run `rollback --yes`.

9. Clean up (elevated): `.\TestApps\Remove-TestServer.ps1`, then delete `C:\PublishToolTest`,
   `C:\ProgramData\PublishTool\server.json` and the `C:\PublishToolTest\repo` entry of
   `%APPDATA%\PublishTool\config.json`.
