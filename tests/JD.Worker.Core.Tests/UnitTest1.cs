using System;
using System.IO;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using TinyBDD;
using TinyBDD.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace JD.Worker.Core.Tests;

[Feature("Job State Machine")]
public partial class JobStateMachineTests : TinyBddXunitBase
{
    private readonly JobStateMachine _stateMachine = new();

    public JobStateMachineTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Scenario("Valid transition from Received to Accepted")]
    [Fact]
    public async Task Transition_Received_To_Accepted()
    {
        await Given("a job in Received state", () => CreateJob(JobState.Received))
            .When("transitioning to Accepted", job => _stateMachine.Transition(job, JobState.Accepted))
            .Then("transition should succeed", result => result.Success)
            .And("job state should be Accepted", result => result.Job.State == JobState.Accepted)
            .AssertPassed();
    }

    [Scenario("Invalid transition from Received to Running")]
    [Fact]
    public async Task Reject_Invalid_Transition()
    {
        await Given("a job in Received state", () => CreateJob(JobState.Received))
            .When("transitioning to Running", job => _stateMachine.Transition(job, JobState.Running))
            .Then("transition should fail", result => !result.Success)
            .And("error should be present", result => !string.IsNullOrWhiteSpace(result.Error))
            .AssertPassed();
    }

    [Scenario("PatternKit state machine permits terminal job finalization")]
    [Fact]
    public async Task Transition_Succeeded_To_Finalized()
    {
        await Given("a job in Succeeded state", () => CreateJob(JobState.Succeeded))
            .When("transitioning to Finalized", job => _stateMachine.Transition(job, JobState.Finalized, "cleanup complete"))
            .Then("transition should succeed", result => result.Success)
            .And("job state should be Finalized", result => result.Job.State == JobState.Finalized)
            .And("event should capture the terminal transition", result =>
                result.Event?.FromState == JobState.Succeeded &&
                result.Event.ToState == JobState.Finalized &&
                result.Event.Message == "cleanup complete")
            .AssertPassed();
    }

    private static JobRecord CreateJob(JobState state) => new()
    {
        JobId = Guid.NewGuid().ToString("N"),
        State = state,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };
}

[Feature("Step Runners")]
public partial class StepRunnerTests : TinyBddXunitBase
{
    public StepRunnerTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Scenario("Shell runner executes a command")]
    [Fact]
    public async Task ShellRunner_Executes_Command()
    {
        var runner = new ShellStepRunner();
        var context = CreateWorkspace();
        var step = new StepDefinition
        {
            Name = "shell",
            Type = StepType.Shell,
            Command = "echo hello"
        };

        await Given("a shell step", () => step)
            .When(
                "executed",
                (Func<StepDefinition, Task<IStepResult>>)(s => runner.ExecuteAsync(s, context, default)))
            .Then("it should succeed", result => result.Success)
            .AssertPassed();
    }

    [Scenario("Process runner executes a command")]
    [Fact]
    public async Task ProcessRunner_Executes_Command()
    {
        var runner = new ProcessStepRunner();
        var context = CreateWorkspace();
        var step = new StepDefinition
        {
            Name = "process",
            Type = StepType.Process,
            Command = "dotnet",
            Arguments = ["--version"]
        };

        await Given("a process step", () => step)
            .When(
                "executed",
                (Func<StepDefinition, Task<IStepResult>>)(s => runner.ExecuteAsync(s, context, default)))
            .Then("it should succeed", result => result.Success)
            .AssertPassed();
    }

    private static IWorkspaceContext CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "jdworker-tests", Guid.NewGuid().ToString("N"));
        var paths = new WorkspacePaths(
            root,
            Path.Combine(root, "work"),
            Path.Combine(root, "artifacts"),
            Path.Combine(root, "logs"));

        Directory.CreateDirectory(paths.WorkDir);
        Directory.CreateDirectory(paths.ArtifactsDir);
        Directory.CreateDirectory(paths.LogsDir);

        return new WorkspaceContext(
            "job-test",
            1,
            paths,
            new NullLogSink(),
            new SecretRedactor(),
            secretResolver: null);
    }
}
