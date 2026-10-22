using Tonarink.Cli;

if (args is ["--cli-daemon", var profile])
    return await StandaloneCliRuntime.RunHostAsync(Path.GetFullPath(profile));

var assemblyName = typeof(Program).Assembly.GetName().Name
    ?? throw new InvalidOperationException("The CLI assembly name is unavailable.");
var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), assemblyName);
return await CliClient.RunAsync(args, new(dataDirectory, Environment.ProcessPath!, Integrated: false));
