using System.CommandLine;
using System.Linq;
using JD.Worker.Configuration;
using JD.Worker.Connectors.Http;
using JD.Worker.Connectors.Local;
using JD.Worker.Core;
using Microsoft.Extensions.Hosting;

var configOption = new Option<FileInfo>("--config", "Path to worker configuration file")
{
    Required = true
};

var validate = new Command("validate", "Validate a worker configuration file");
validate.Options.Add(configOption);
validate.SetAction(async (ParseResult parseResult) =>
{
    var configFile = parseResult.GetValue(configOption)!;
    var config = await LoadConfigAsync(configFile);
    if (config is null)
    {
        return 2;
    }
    Console.WriteLine("Configuration is valid.");
    return 0;
});

var doctor = new Command("doctor", "Run system diagnostics");
doctor.SetAction((_) =>
{
    Console.WriteLine("Doctor diagnostics are not implemented yet.");
    return 0;
});

var run = new Command("run", "Start worker host");
run.Options.Add(configOption);
run.SetAction(async (ParseResult parseResult) =>
{
    var configFile = parseResult.GetValue(configOption)!;
    var config = await LoadConfigAsync(configFile);
    if (config is null)
    {
        return 2;
    }

    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddWorkerRuntime(config);
            services.AddHttpConnector();
            services.AddLocalConnector();
        })
        .Build();

    await host.RunAsync();
    return 0;
});

var root = new RootCommand("JD.Worker CLI");
root.Subcommands.Add(validate);
root.Subcommands.Add(doctor);
root.Subcommands.Add(run);

return await root.Parse(args).InvokeAsync();

static IConfigParser CreateParser(FileInfo configFile)
{
    return configFile.Extension.ToLowerInvariant() switch
    {
        ".yaml" or ".yml" => new YamlConfigParser(),
        _ => new JsonConfigParser()
    };
}

static void WriteErrors(string header, IReadOnlyList<ParseError> errors)
{
    Console.Error.WriteLine(header);
    foreach (var error in errors)
    {
        if (error.Line is not null && error.Column is not null)
        {
            Console.Error.WriteLine($"- {error.Message} (line {error.Line}, col {error.Column})");
        }
        else
        {
            Console.Error.WriteLine($"- {error.Message}");
        }
    }
}

static async Task<WorkerConfig?> LoadConfigAsync(FileInfo configFile)
{
    if (!configFile.Exists)
    {
        Console.Error.WriteLine($"Config file not found: {configFile.FullName}");
        return null;
    }

    var content = await File.ReadAllTextAsync(configFile.FullName);
    var parser = CreateParser(configFile);
    var parseResult = parser.Parse<WorkerConfig>(content);
    if (!parseResult.IsSuccess)
    {
        WriteErrors("Parsing failed", parseResult.Errors);
        return null;
    }

    var validator = new ConfigValidationPipeline();
    var validation = validator.Validate(parseResult.Value!);
    if (!validation.IsValid)
    {
        WriteErrors("Validation failed", validation.Errors.Select(e => new ParseError(e.Message, null, null)).ToList());
        return null;
    }

    return parseResult.Value;
}
