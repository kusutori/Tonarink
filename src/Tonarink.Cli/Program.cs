using Tonarink.Cli;

if (args is ["--cli-daemon", var profile])
    return await StandaloneCliRuntime.RunHostAsync(Path.GetFullPath(profile));

var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tonarink.Cli");
return await CliClient.RunAsync(args, new(dataDirectory, Environment.ProcessPath!, Integrated: false));
