# PublishTool for server (deployment)

The next big update of PublishTool should contain a support for server usage. This means usage of the app on the target server where apps published by PublishTool get deployed. For this to work, we need to follow some rules and add functionality.

## Files metadata

First of the changes is adding some kind of metadata file, possibly a JSON with additional info. The main reason for this is the ability to remove files that are no longer used. Such files have to be marked for deletion somewhere, since them just missing in the final deploy dir acts like they just weren't updated.

This metadata file could also hold additional info in the future.

## Built for IIS hosted apps

Since the app is mainly built for a specific use-case (IIS deployed and hosted apps), we should focus on this one in the first version. This adds a simple step before the actual deployment (copying of files) begins, since .NET Core app files can be locked by the process. This can be done using the `iisreset` tool.

## Matching of files, config files, etc.

The files are matched by their path. This means we will have to handle config files for each environment, which is currently not done in our targeted project (config files are handled and updated manually). This will have to be automatable, which means updating needed logic in the actually project.

## Deploy command

For this feature, we will have to implement new `deploy` command. It will handle the IIS, copying of files, deletion of all removed files and finally start of the IIS again (this may require elevated permissions). The deploy command should be run from a directory containing published project directories, i.e. if I published 2 projects called "TestAppWeb" and "TestAppApi", I have to run the command from a directory (usually called in a similar way publish tool names the deploy dirs) that contains 2 subdirs "TestAppWeb" and "TestAppApi". These dirs then contain all the files for publish. Since each app can be deployed in a different path on the server, each project name (dir name) should be mapped to a specific path on the file system and this should be stored similarly to the path config for publish.

### Workflow description

Done locally:
- Publish whatever is needed using current PublishTool workflow

Done on the server:
- Copy the changed published files to the server
- Run PublishTool with the new `deploy` command

### Publish tool deploy command workflow

1. Create a backup of currently deployed files (IMPORTANT!). Only changed files are backed up, no need to make a full copy of the app.
2. Stop IIS processes to unlock .NET Core files
3. Copy files from subdirectories to destination paths (mapped in PublishTool config)
4. Start the IIS again

All steps should report any errors that may occur to the user. When an error occurs in any step after the 1st backup step, the whole publish should be rolled back by returning the original files to the destination dirs and the IIS should be started again.