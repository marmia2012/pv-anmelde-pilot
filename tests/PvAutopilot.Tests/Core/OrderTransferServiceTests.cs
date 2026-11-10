using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Orders;
using PvAutopilot.Tests.Sources.HubSpot;

namespace PvAutopilot.Tests.Core;

internal sealed class FakeOrderSource : ISignedOrderSource
{
    public SignedOrder? Order { get; set; }
    public byte[]? Pdf { get; set; }
    public Exception? ThrowOnRead { get; set; }
    public List<(string Project, string? Signature)> Reads { get; } = [];
    public List<DocumentRef> Downloads { get; } = [];

    public Task<SignedOrder?> GetSignedOrderAsync(string planningProjectId, string? signatureRequestId, CancellationToken ct)
    {
        Reads.Add((planningProjectId, signatureRequestId));
        return ThrowOnRead is null ? Task.FromResult(Order) : throw ThrowOnRead;
    }

    public Task<byte[]?> DownloadAsync(DocumentRef document, CancellationToken ct)
    {
        Downloads.Add(document);
        return Task.FromResult(Pdf);
    }
}

internal sealed class FakeOrderSink : ICrmOrderSink
{
    public OrderPublishResult Result { get; set; } = new() { Written = true, LineItemCount = 2, PropertyNames = ["auftrag_kwp"], PdfStored = true };
    public Exception? Throw { get; set; }
    public List<(SignedOrder Order, byte[]? Pdf)> Published { get; } = [];

    public Task<OrderPublishResult> PublishAsync(SignedOrder order, byte[]? signedPdf, CancellationToken ct)
    {
        Published.Add((order, signedPdf));
        return Throw is null ? Task.FromResult(Result) : throw Throw;
    }
}

internal sealed class OrderAlertRecorder : IOperatorAlert
{
    public List<(string Subject, string Body)> Sent { get; } = [];
    public Exception? Throw { get; set; }

    public Task SendAsync(string subject, string body, byte[]? screenshot, CancellationToken ct)
    {
        if (Throw is not null)
            throw Throw;
        Sent.Add((subject, body));
        return Task.CompletedTask;
    }
}

public class OrderTransferServiceTests
{
    private const string Project = "reonic-proj-1";
    private const string Sig = "sig-1";
    private const string Deal = "4711";

    private readonly FakeOrderSource _source = new();
    private readonly FakeOrderSink _sink = new();
    private readonly RecordingTasks _tasks = new();
    private readonly OrderAlertRecorder _alert = new();
    private readonly InMemoryCrmWriteLedger _ledger = new();
    private readonly CapturingLogger<OrderTransferService> _log = new();

    private OrderTransferService Service() => new(_source, _sink, _tasks, _alert, _ledger, _log);

    private static readonly DocumentRef Pdf = new(DocumentKind.Auftrag, "reonic-signature", "https://files.example/x.pdf", "Auftrag unterschrieben.pdf");

    private static SignedOrder Order(string? deal = Deal, string? signature = Sig, bool signed = true, string? variant = "v1",
        int positions = 2, IReadOnlyList<string>? problems = null, DocumentRef? pdf = null) => new()
    {
        PlanningProjectId = Project,
        SignatureRequestId = signature,
        IsSigned = signed,
        CrmProjectId = deal,
        VariantId = variant,
        Positions = Enumerable.Range(0, positions).Select(i => new OrderPosition("module", "Marke", $"Modul {i}", null, null, 1, 100m, 19m)).ToList(),
        SignedPdf = pdf,
        Problems = problems ?? [],
    };

    // Projekt nicht gefunden

    [Fact]
    public async Task Unknown_project_alerts_exactly_once_even_on_the_second_call()
    {
        _source.Order = null;

        var first = await Service().TransferAsync(Project, Sig, default);
        var second = await Service().TransferAsync(Project, Sig, default);

        var alert = Assert.Single(_alert.Sent);
        Assert.Contains(Project, alert.Subject);
        Assert.Equal(OrderSkipReasons.ProjectNotFound, first.SkipReason);
        Assert.Equal(OrderSkipReasons.ProjectNotFound, second.SkipReason);
        Assert.False(first.Written);
        Assert.Contains("alert:order:reonic-proj-1:not-found", _ledger.Records.Keys);
        Assert.Empty(_sink.Published);
        Assert.Empty(_tasks.Calls);
        Assert.Empty(_source.Downloads);
    }

    [Fact]
    public void Alert_keys_are_stable()
    {
        Assert.Equal("alert:order:p:not-found", OrderTransferService.NotFoundAlertKey("p"));
        Assert.Equal("alert:order:p:s:no-deal", OrderTransferService.NoDealAlertKey("p", "s"));
        Assert.Equal("alert:order:p:none:no-deal", OrderTransferService.NoDealAlertKey("p", null));
        Assert.Equal("alert:order:p:s:deal-not-found", OrderTransferService.DealNotFoundAlertKey("p", "s"));
        Assert.Equal("order-problems:s", OrderTransferService.ProblemsTaskKey("s"));
        Assert.Equal("order-not-signed:p:s", OrderTransferService.NotSignedTaskKey("p", "s"));
    }

    // Kein Deal

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Order_without_deal_alerts_once_and_touches_no_sink(string? deal)
    {
        _source.Order = Order(deal: deal, problems: ["Reonic: irgendwas"]);

        var first = await Service().TransferAsync(Project, Sig, default);
        await Service().TransferAsync(Project, Sig, default);

        var alert = Assert.Single(_alert.Sent);
        Assert.Contains(Project, alert.Subject);
        Assert.Equal(OrderSkipReasons.NoDeal, first.SkipReason);
        Assert.Null(first.CrmProjectId);
        Assert.Contains($"alert:order:{Project}:{Sig}:no-deal", _ledger.Records.Keys);
        Assert.Empty(_sink.Published);
        Assert.Empty(_tasks.Calls);
        Assert.Empty(_source.Downloads);
    }

    [Fact]
    public async Task Alerts_are_per_signature()
    {
        _source.Order = Order(deal: null, signature: "sig-a");
        await Service().TransferAsync(Project, "sig-a", default);
        _source.Order = Order(deal: null, signature: "sig-b");
        await Service().TransferAsync(Project, "sig-b", default);

        Assert.Equal(2, _alert.Sent.Count);
    }

    // Nicht unterschrieben

    [Fact]
    public async Task Not_signed_creates_a_task_at_the_deal_and_publishes_nothing()
    {
        _source.Order = Order(signed: false, positions: 0, variant: null, problems: ["Reonic: Signaturanfrage hat Status „expired“"], pdf: Pdf);

        var result = await Service().TransferAsync(Project, Sig, default);

        var task = Assert.Single(_tasks.Calls);
        Assert.Equal(Deal, task.CrmProjectId);
        Assert.Equal($"order-not-signed:{Project}:{Sig}", task.Key);
        Assert.Equal(OrderTransferService.NotSignedTaskTitle, task.Title);
        Assert.Contains("expired", task.Body);
        Assert.Empty(_sink.Published);
        Assert.Empty(_source.Downloads);
        Assert.Empty(_alert.Sent);
        Assert.Equal(OrderSkipReasons.NotSigned, result.SkipReason);
        Assert.False(result.Written);
    }

    [Fact]
    public async Task Signed_order_without_any_signature_id_counts_as_not_signed()
    {
        _source.Order = Order(signature: null);

        var result = await Service().TransferAsync(Project, null, default);

        Assert.Equal(OrderSkipReasons.NotSigned, result.SkipReason);
        Assert.Equal($"order-not-signed:{Project}:none", Assert.Single(_tasks.Calls).Key);
        Assert.Empty(_sink.Published);
    }

    // Kein Positionen

    [Fact]
    public async Task Missing_variant_publishes_nothing_and_creates_one_problem_task()
    {
        _source.Order = Order(variant: null, positions: 0, problems: ["Reonic: unterschriebene Variante nicht gefunden"], pdf: Pdf);

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Equal(OrderSkipReasons.VariantMissing, result.SkipReason);
        Assert.Empty(_sink.Published);
        Assert.Empty(_source.Downloads);
        var task = Assert.Single(_tasks.Calls);
        Assert.Equal(Deal, task.CrmProjectId);
        Assert.Equal($"order-problems:{Sig}", task.Key);
        Assert.Contains("Variante nicht gefunden", task.Body);
    }

    [Fact]
    public async Task Variant_without_positions_publishes_nothing_and_says_why()
    {
        _source.Order = Order(positions: 0);

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Equal(OrderSkipReasons.VariantMissing, result.SkipReason);
        Assert.Empty(_sink.Published);
        Assert.Contains("ohne Positionen", Assert.Single(_tasks.Calls).Body);
        Assert.Contains(result.Problems, p => p.Contains("ohne Positionen"));
    }

    // Deal in HubSpot nicht gefunden

    [Fact]
    public async Task Deal_not_found_in_hubspot_alerts_once_even_on_the_second_call()
    {
        _source.Order = Order(problems: ["Reonic: kein unterschriebenes Auftrags-PDF (signedPdfUrl)"]);
        _sink.Result = new OrderPublishResult { SkipReason = OrderSkipReasons.DealNotFound };

        var first = await Service().TransferAsync(Project, Sig, default);
        await Service().TransferAsync(Project, Sig, default);

        var alert = Assert.Single(_alert.Sent);
        Assert.Contains(Deal, alert.Subject);
        Assert.Equal(OrderSkipReasons.DealNotFound, first.SkipReason);
        Assert.Contains($"alert:order:{Project}:{Sig}:deal-not-found", _ledger.Records.Keys);
        Assert.Equal(2, _sink.Published.Count);
        // Ohne Deal gibt es auch keine Aufgabe am Deal.
        Assert.Empty(_tasks.Calls);
    }

    // Probleme

    [Fact]
    public async Task Problems_create_exactly_one_task_with_the_signature_key_and_still_publish()
    {
        _source.Order = Order(problems: ["Reonic-Katalog: Komponente „Marke X“ nicht gefunden", "Reonic-Angebot: Position „Y“ ohne Bruttopreis"]);

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Single(_sink.Published);
        var task = Assert.Single(_tasks.Calls);
        Assert.Equal(Deal, task.CrmProjectId);
        Assert.Equal($"order-problems:{Sig}", task.Key);
        Assert.Equal(OrderTransferService.ProblemsTaskTitle, task.Title);
        Assert.Contains("Marke X", task.Body);
        Assert.Contains("ohne Bruttopreis", task.Body);
        Assert.Equal(2, result.Problems.Count);
        Assert.True(result.Written);
    }

    [Fact]
    public async Task Repeated_run_asks_for_the_same_task_key_so_the_sink_creates_it_once()
    {
        _source.Order = Order(problems: ["Reonic: Problem"]);

        await Service().TransferAsync(Project, Sig, default);
        await Service().TransferAsync(Project, Sig, default);

        Assert.Single(_tasks.Keys);
        Assert.Equal($"order-problems:{Sig}", _tasks.Keys[0]);
    }

    [Fact]
    public async Task Without_problems_no_task_is_created()
    {
        _source.Order = Order();

        await Service().TransferAsync(Project, Sig, default);

        Assert.Empty(_tasks.Calls);
        Assert.Empty(_alert.Sent);
        Assert.Single(_sink.Published);
    }

    [Fact]
    public async Task Second_signature_leaves_the_task_to_the_sink()
    {
        _source.Order = Order(problems: ["Reonic: Problem"]);
        _sink.Result = new OrderPublishResult { SkipReason = OrderSkipReasons.SecondSignature, LineItemCount = 2 };

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Empty(_tasks.Calls);
        Assert.Equal(OrderSkipReasons.SecondSignature, result.SkipReason);
        Assert.False(result.Written);
        Assert.Empty(_alert.Sent);
    }

    [Fact]
    public async Task Preview_without_write_permission_is_reported_with_its_counts()
    {
        _source.Order = Order();
        _sink.Result = new OrderPublishResult
        {
            SkipReason = OrderSkipReasons.WriteDisabled, LineItemCount = 2, PropertyNames = ["a", "b"], PdfStored = true, ForeignLineItems = 3,
        };

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.False(result.Written);
        Assert.Equal(OrderSkipReasons.WriteDisabled, result.SkipReason);
        Assert.Equal((2, true, 3), (result.LineItemCount, result.PdfStored, result.ForeignLineItems));
        Assert.Equal(["a", "b"], result.PropertyNames);
    }

    // PDF

    [Fact]
    public async Task Pdf_is_downloaded_and_handed_to_the_sink()
    {
        _source.Order = Order(pdf: Pdf);
        _source.Pdf = [1, 2, 3];

        await Service().TransferAsync(Project, Sig, default);

        Assert.Equal(Pdf, Assert.Single(_source.Downloads));
        var (order, pdf) = Assert.Single(_sink.Published);
        Assert.Equal([1, 2, 3], pdf);
        Assert.Equal(Deal, order.CrmProjectId);
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public async Task Order_without_pdf_reference_does_not_download()
    {
        _source.Order = Order(pdf: null);
        _source.Pdf = [1];

        await Service().TransferAsync(Project, Sig, default);

        Assert.Empty(_source.Downloads);
        Assert.Null(Assert.Single(_sink.Published).Pdf);
    }

    [Fact]
    public async Task Pdf_that_cannot_be_downloaded_is_a_problem_but_the_rest_is_published()
    {
        _source.Order = Order(pdf: Pdf);
        _source.Pdf = null;

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Null(Assert.Single(_sink.Published).Pdf);
        Assert.Contains("nicht abrufbar", Assert.Single(_tasks.Calls).Body);
        Assert.Contains(result.Problems, p => p.Contains("nicht abrufbar"));
    }

    // Weitergabe

    [Fact]
    public async Task Result_carries_ids_and_sink_outcome()
    {
        _source.Order = Order(deal: " 4711 ");

        var result = await Service().TransferAsync(Project, Sig, default);

        Assert.Equal(Project, result.PlanningProjectId);
        Assert.Equal(Sig, result.SignatureRequestId);
        Assert.Equal(Deal, result.CrmProjectId);
        Assert.True(result.Written);
        Assert.Equal(2, result.LineItemCount);
        Assert.Equal(["auftrag_kwp"], result.PropertyNames);
        Assert.True(result.PdfStored);
    }

    [Fact]
    public async Task Signature_id_is_passed_to_the_source_and_resolved_from_the_order_when_not_given()
    {
        _source.Order = Order(signature: "latest");

        var result = await Service().TransferAsync(Project, null, default);

        Assert.Equal((Project, (string?)null), Assert.Single(_source.Reads));
        Assert.Equal("latest", result.SignatureRequestId);
    }

    // Technische Fehler

    [Fact]
    public async Task Source_failure_is_not_swallowed_and_does_not_alert()
    {
        _source.ThrowOnRead = new HttpRequestException("HTTP 503");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().TransferAsync(Project, Sig, default));

        Assert.Empty(_alert.Sent);
        Assert.Empty(_tasks.Calls);
        Assert.Empty(_ledger.Records);
    }

    [Fact]
    public async Task Sink_failure_is_not_swallowed_and_does_not_alert_or_create_tasks()
    {
        _source.Order = Order(problems: ["Reonic: Problem"]);
        _sink.Throw = new HttpRequestException("HTTP 500");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().TransferAsync(Project, Sig, default));

        Assert.Empty(_alert.Sent);
        Assert.Empty(_tasks.Calls);
    }

    // Logging

    [Fact]
    public async Task Log_has_ids_and_counts_but_no_product_names_or_problem_texts()
    {
        _source.Order = Order(problems: ["Reonic-Katalog: Komponente „Geheimmarke Geheimname“ nicht gefunden"]);

        await Service().TransferAsync(Project, Sig, default);

        Assert.NotEmpty(_log.Messages);
        Assert.DoesNotContain("Geheim", _log.All);
        Assert.DoesNotContain("Marke", _log.All);
        Assert.Contains(Project, _log.All);
        Assert.Contains(Sig, _log.All);
    }
}
