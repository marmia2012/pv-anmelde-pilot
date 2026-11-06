using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Host.Endpoints;

namespace PvAutopilot.Tests.Host;

public class AdminProcessEventsTests
{
    private readonly FakeStore _store = new();
    private readonly FakeScheduler _scheduler = new();

    private async Task<int?> Post(Guid id, string json)
    {
        var handler = new ProcessTriggerHandler(_store, _scheduler, new NoCrmStatus(), new FixedTime(TestData.Now),
            NullLogger<ProcessTriggerHandler>.Instance);
        var result = await AdminProcessEvents.PostAsync(id, new MemoryStream(Encoding.UTF8.GetBytes(json)), _store, handler,
            new FixedTime(TestData.Now), default);
        return ((IStatusCodeHttpResult)result).StatusCode;
    }

    [Fact]
    public async Task Fact_is_recorded_for_the_process_and_a_run_is_scheduled()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        var status = await Post(process.Id, """{"kind": "einspeisezusageReceived", "date": "2026-11-12", "reference": " NB-4711 "}""");

        Assert.Equal(StatusCodes.Status202Accepted, status);
        Assert.Equal(new DateOnly(2026, 11, 12), process.EinspeisezusageDatum);
        Assert.Equal("NB-4711", process.NetzbetreiberReferenz);
        Assert.Equal([process.Id], _scheduler.Enqueued);
    }

    [Fact]
    public async Task Admin_event_counts_as_occurring_now()
    {
        // Ein Mensch meldet eine neue Rückfrage, nachdem die letzte geschlossen wurde: nie als veraltet verwerfen.
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now.AddHours(-2)));
        process.RecordRueckfrage(TestData.Now.AddHours(-2));
        process.CloseRueckfrage(TestData.Now.AddMinutes(-1));

        Assert.Equal(StatusCodes.Status202Accepted, await Post(process.Id, """{"kind": "RueckfrageReceived"}"""));
        Assert.Equal(TestData.Now, process.RueckfrageOffenSeit);
    }

    [Fact]
    public async Task Approval_without_waiting_stage_is_a_conflict()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        Assert.Equal(StatusCodes.Status409Conflict, await Post(process.Id, """{"kind": "ReviewApproved", "submission": "Netzanmeldung"}"""));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Unknown_process_is_not_found() =>
        Assert.Equal(StatusCodes.Status404NotFound, await Post(Guid.NewGuid(), """{"kind": "Cancelled"}"""));

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("""{"kind": "Gibtsnicht"}""")]
    [InlineData("""{"kind": 2}""")]
    [InlineData("""{"date": "2026-11-12"}""")]
    [InlineData("""{"kind": "Start"}""")]
    [InlineData("""{"kind": "ReviewApproved"}""")]
    [InlineData("""{"kind": "RueckfrageResolved"}""")]
    [InlineData("""{"kind": "RueckfrageResolved", "resolution": "Vielleicht"}""")]
    [InlineData("""{"kind": "InbetriebnahmeReported", "date": "12.11.2026"}""")]
    public async Task Invalid_requests_are_rejected(string json)
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        Assert.Equal(StatusCodes.Status400BadRequest, await Post(process.Id, json));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task Too_long_reference_is_rejected()
    {
        var process = _store.Add(new AnmeldeProcess("crm-1", TestData.Now));

        Assert.Equal(StatusCodes.Status400BadRequest,
            await Post(process.Id, $$"""{"kind": "EinspeisezusageReceived", "reference": "{{new string('x', 201)}}"}"""));
    }

    private sealed class FakeStore : IProcessStore
    {
        private readonly List<AnmeldeProcess> _processes = [];

        public AnmeldeProcess Add(AnmeldeProcess process)
        {
            _processes.Add(process);
            return process;
        }

        public Task<AnmeldeProcess?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_processes.SingleOrDefault(p => p.Id == id));

        public Task<AnmeldeProcess> GetOrCreateAsync(string crmProjectId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AnmeldeProcess?> FindByCrmProjectAsync(string crmProjectId, CancellationToken ct) =>
            Task.FromResult(_processes.SingleOrDefault(p => p.CrmProjectId == crmProjectId));

        public Task<IReadOnlyList<AnmeldeProcess>> ListOpenAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AnmeldeProcess>>(_processes);

        public Task SaveAsync(AnmeldeProcess process, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoCrmStatus : ICrmStatusSink
    {
        public Task PublishAsync(AnmeldeProcess process, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeScheduler : IProcessScheduler
    {
        public List<Guid> Enqueued { get; } = [];
        public void Enqueue(Guid processId) => Enqueued.Add(processId);
    }
}
