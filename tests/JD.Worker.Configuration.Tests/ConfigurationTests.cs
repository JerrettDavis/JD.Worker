using System;
using System.Collections.Generic;
using System.Linq;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using TinyBDD;
using TinyBDD.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace JD.Worker.Configuration.Tests;

[Feature("Configuration Parsing")]
public partial class ConfigurationParsingTests : TinyBddXunitBase
{
    private readonly IConfigParser _jsonParser = new JsonConfigParser();
    private readonly IConfigParser _yamlParser = new YamlConfigParser();

    public ConfigurationParsingTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Scenario("Parse valid JSON worker configuration")]
    [Fact]
    public async Task Parse_Valid_Json_Worker_Config()
    {
        await Given("a valid JSON worker configuration", GetValidJsonConfig)
            .When("the configuration is parsed", json => _jsonParser.Parse<WorkerConfig>(json))
            .Then("parsing should succeed", result => result.IsSuccess)
            .And("worker id should be set correctly", result => result.Value?.Worker.Id == "test-worker-001")
            .And("default pool should be configured", result =>
                result.Value?.Worker.Pools.Any(p => p.Name == "default") == true)
            .AssertPassed();
    }

    [Scenario("Parse JSON with environment variable interpolation")]
    [Fact]
    public async Task Parse_Json_With_Environment_Variables()
    {
        Environment.SetEnvironmentVariable("TEST_WORKER_ID", "env-worker-123");

        try
        {
            await Given("a JSON config with environment variable reference", () =>
                """
                {
                    "worker": {
                        "id": "${TEST_WORKER_ID}",
                        "pools": [{ "name": "default", "concurrency": 4 }]
                    }
                }
                """)
                .When("the configuration is parsed", json => _jsonParser.Parse<WorkerConfig>(json))
                .Then("the environment variable should be interpolated", result =>
                    result.Value?.Worker.Id == "env-worker-123")
                .AssertPassed();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_WORKER_ID", null);
        }
    }

    [Scenario("Parse JSON with default value fallback")]
    [Fact]
    public async Task Parse_Json_With_Default_Value()
    {
        await Given("a JSON config with default value syntax", () =>
            """
            {
                "worker": {
                    "id": "${MISSING_VAR:default-worker}",
                    "pools": [{ "name": "default", "concurrency": 4 }]
                }
            }
            """)
            .When("the configuration is parsed", json => _jsonParser.Parse<WorkerConfig>(json))
            .Then("the default value should be used", result =>
                result.Value?.Worker.Id == "default-worker")
            .AssertPassed();
    }

    [Scenario("Reject invalid JSON syntax")]
    [Fact]
    public async Task Reject_Invalid_Json_Syntax()
    {
        await Given("an invalid JSON string", () => "{ invalid json }")
            .When("parsing is attempted", json => _jsonParser.Parse<WorkerConfig>(json))
            .Then("parsing should fail", result => !result.IsSuccess)
            .And("error should be present", result => result.Errors.Count > 0)
            .AssertPassed();
    }

    [Scenario("Parse valid YAML worker configuration")]
    [Fact]
    public async Task Parse_Valid_Yaml_Worker_Config()
    {
        await Given("a valid YAML worker configuration", GetValidYamlConfig)
            .When("the configuration is parsed", yaml => _yamlParser.Parse<WorkerConfig>(yaml))
            .Then("parsing should succeed", result => result.IsSuccess)
            .And("worker id should be set correctly", result =>
                result.Value?.Worker.Id == "yaml-worker-001")
            .And("sandbox mode should be container", result =>
                result.Value?.Worker.Sandbox == SandboxMode.Container)
            .AssertPassed();
    }

    [Scenario("Parse YAML with multiple connectors")]
    [Fact]
    public async Task Parse_Yaml_Multiple_Connectors()
    {
        await Given("a YAML config with multiple CnC connectors", () =>
            """
            worker:
              id: multi-connector-worker
              pools:
                - name: default
                  concurrency: 4

            cnc:
              - name: primary
                type: AzureServiceBus
                settings:
                  connectionString: "${ASB_CONN:dummy}"
              - name: fallback
                type: RabbitMq
                settings:
                  hostName: localhost
            """)
            .When("the configuration is parsed", yaml => _yamlParser.Parse<WorkerConfig>(yaml))
            .Then("two connectors should be configured", result =>
                result.Value?.Cnc.Count == 2)
            .And("first connector should be ASB", result =>
                result.Value?.Cnc[0].Type == "AzureServiceBus")
            .And("second connector should be RabbitMq", result =>
                result.Value?.Cnc[1].Type == "RabbitMq")
            .AssertPassed();
    }

    [Scenario("Parse valid job envelope with all step types")]
    [Fact]
    public async Task Parse_Job_Envelope_All_Step_Types()
    {
        await Given("a job envelope with multiple step types", GetJobEnvelopeWithAllSteps)
            .When("the envelope is parsed", json => _jsonParser.ParseJobEnvelope(json))
            .Then("parsing should succeed", result => result.IsSuccess)
            .And("shell step should be present", result =>
                result.Value?.Payload.Steps.Any(s => s.Type == StepType.Shell) == true)
            .And("docker step should be present", result =>
                result.Value?.Payload.Steps.Any(s => s.Type == StepType.Docker) == true)
            .And("process step should be present", result =>
                result.Value?.Payload.Steps.Any(s => s.Type == StepType.Process) == true)
            .AssertPassed();
    }

    [Scenario("Parse job envelope with retry configuration")]
    [Fact]
    public async Task Parse_Job_Envelope_With_Retry()
    {
        await Given("a job envelope with retry config", () =>
            """
            {
                "envelope": {
                    "jobId": "retry-job",
                    "attempt": 1,
                    "createdUtc": "2025-01-02T10:00:00Z"
                },
                "payload": {
                    "steps": [
                        { "name": "test", "type": "shell", "command": "echo test" }
                    ],
                    "retry": {
                        "maxAttempts": 3,
                        "initialDelay": "30s",
                        "maxDelay": "5m",
                        "backoff": "exponential"
                    }
                }
            }
            """)
            .When("the envelope is parsed", json => _jsonParser.ParseJobEnvelope(json))
            .Then("retry config should be parsed", result =>
                result.Value?.Payload.Retry != null)
            .And("max attempts should be 3", result =>
                result.Value?.Payload.Retry?.MaxAttempts == 3)
            .And("backoff should be exponential", result =>
                result.Value?.Payload.Retry?.Backoff == BackoffStrategy.Exponential)
            .AssertPassed();
    }

    private static string GetValidJsonConfig() =>
        """
        {
            "worker": {
                "id": "test-worker-001",
                "labels": {
                    "environment": "test",
                    "capabilities": ["docker", "dotnet"]
                },
                "pools": [
                    { "name": "default", "concurrency": 4 },
                    { "name": "heavy", "concurrency": 1 }
                ],
                "workspace": {
                    "root": "/var/jdworker/workspaces",
                    "cleanup": "retain-on-failure"
                },
                "sandbox": "container"
            },
            "cnc": [
                {
                    "name": "local",
                    "type": "Local",
                    "settings": {
                        "inboxPath": "./inbox",
                        "outboxPath": "./outbox"
                    }
                }
            ]
        }
        """;

    private static string GetValidYamlConfig() =>
        """
        worker:
          id: yaml-worker-001
          labels:
            environment: production
          pools:
            - name: default
              concurrency: 8
          workspace:
            root: /var/jdworker/workspaces
            cleanup: immediate
          sandbox: container

        cnc:
          - name: primary
            type: Http
            settings:
              baseUrl: https://api.example.com
        """;

    private static string GetJobEnvelopeWithAllSteps() =>
        """
        {
            "envelope": {
                "jobId": "multi-step-job",
                "attempt": 1,
                "createdUtc": "2025-01-02T10:00:00Z",
                "signature": "test-signature",
                "signatureAlgorithm": "HMAC-SHA256",
                "keyId": "test-key"
            },
            "payload": {
                "requestedPool": "default",
                "steps": [
                    {
                        "name": "checkout",
                        "type": "shell",
                        "command": "git clone https://example.com/repo.git",
                        "timeout": "5m"
                    },
                    {
                        "name": "build",
                        "type": "docker",
                        "command": "dotnet build",
                        "image": "mcr.microsoft.com/dotnet/sdk:10.0",
                        "timeout": "30m"
                    },
                    {
                        "name": "deploy",
                        "type": "process",
                        "command": "/usr/local/bin/deploy",
                        "arguments": ["--env", "staging"],
                        "timeout": "10m"
                    }
                ],
                "timeout": "1h"
            }
        }
        """;
}

[Feature("Configuration Validation")]
public partial class ConfigurationValidationTests : TinyBddXunitBase
{
    private readonly IConfigValidator _validator = new ConfigValidationPipeline();

    public ConfigurationValidationTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Scenario("Validate complete worker configuration")]
    [Fact]
    public async Task Validate_Complete_Config_Succeeds()
    {
        await Given("a complete valid configuration", CreateCompleteConfig)
            .When("the configuration is validated", config => _validator.Validate(config))
            .Then("validation should pass", result => result.IsValid)
            .And("no errors should be present", result => !result.Errors.Any())
            .AssertPassed();
    }

    [Scenario("Reject configuration without pools")]
    [Fact]
    public async Task Reject_Config_Without_Pools()
    {
        await Given("a configuration with no pools", () => new WorkerConfig
        {
            Worker = new WorkerSettings { Id = "test", Pools = [] },
            Cnc = [new CncConnectorConfig { Name = "local", Type = "Local" }]
        })
            .When("the configuration is validated", config => _validator.Validate(config))
            .Then("validation should fail", result => !result.IsValid)
            .And("error should mention pools", result =>
                result.Errors.Any(e => e.Message.Contains("pool", StringComparison.OrdinalIgnoreCase)))
            .AssertPassed();
    }

    [Scenario("Reject configuration with duplicate pool names")]
    [Fact]
    public async Task Reject_Duplicate_Pool_Names()
    {
        await Given("a configuration with duplicate pool names", () => new WorkerConfig
        {
            Worker = new WorkerSettings
            {
                Id = "test",
                Pools =
                [
                    new PoolSettings { Name = "default", Concurrency = 4 },
                    new PoolSettings { Name = "default", Concurrency = 2 }
                ]
            },
            Cnc = [new CncConnectorConfig { Name = "local", Type = "Local" }]
        })
            .When("the configuration is validated", config => _validator.Validate(config))
            .Then("validation should fail", result => !result.IsValid)
            .And("error should mention duplicate", result =>
                result.Errors.Any(e => e.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            .AssertPassed();
    }

    [Scenario("Reject policy with invalid step types")]
    [Fact]
    public async Task Reject_Invalid_Step_Types_In_Policy()
    {
        await Given("a policy configuration with invalid step type", () => new WorkerConfig
        {
            Worker = new WorkerSettings
            {
                Id = "test",
                Pools = [new PoolSettings { Name = "default", Concurrency = 4 }]
            },
            Cnc = [new CncConnectorConfig { Name = "local", Type = "Local" }],
            Policy = new PolicyConfig
            {
                AllowedStepTypes = ["shell", "invalid-type", "docker"]
            }
        })
            .When("the configuration is validated", config => _validator.Validate(config))
            .Then("validation should fail", result => !result.IsValid)
            .And("error should identify the invalid type", result =>
                result.Errors.Any(e => e.Message.Contains("invalid-type", StringComparison.OrdinalIgnoreCase)))
            .AssertPassed();
    }

    [Scenario("Collect multiple validation errors")]
    [Fact]
    public async Task Collect_Multiple_Errors()
    {
        await Given("a configuration with multiple problems", () => new WorkerConfig
        {
            Worker = new WorkerSettings
            {
                Id = "",
                Pools = []
            },
            Cnc = []
        })
            .When("the configuration is validated", config => _validator.Validate(config))
            .Then("validation should fail", result => !result.IsValid)
            .And("multiple errors should be collected", result => result.Errors.Count >= 3)
            .And("error about worker ID should be present", result =>
                result.Errors.Any(e => e.Message.Contains("id", StringComparison.OrdinalIgnoreCase)))
            .And("error about pools should be present", result =>
                result.Errors.Any(e => e.Message.Contains("pool", StringComparison.OrdinalIgnoreCase)))
            .AssertPassed();
    }

    private static WorkerConfig CreateCompleteConfig() => new()
    {
        Worker = new WorkerSettings
        {
            Id = "complete-worker",
            Labels = new()
            {
                ["environment"] = "test"
            },
            Pools =
            [
                new PoolSettings { Name = "default", Concurrency = 4 }
            ],
            Workspace = new WorkspaceSettings
            {
                Root = "/var/jdworker/workspaces",
                Cleanup = CleanupPolicy.Immediate
            },
            Sandbox = SandboxMode.Container
        },
        Cnc =
        [
            new CncConnectorConfig
            {
                Name = "local",
                Type = "Local",
                Settings = new Dictionary<string, object?>
                {
                    ["inboxPath"] = "./inbox"
                }
            }
        ]
    };
}
