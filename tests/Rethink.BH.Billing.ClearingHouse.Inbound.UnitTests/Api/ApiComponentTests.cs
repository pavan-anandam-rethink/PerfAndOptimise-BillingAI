using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.OpenApi.Models;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Api;

public sealed class ApiComponentTests
{
    [Fact]
    public async Task Controller_maps_invalid_to_400_with_the_legacy_text()
    {
        var (controller, logger) = Controller(InboundDownloadResult.Invalid(99));

        var result = await controller.DownloadEdiDataFromSftp(99, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Invalid clearing house ID.");
        logger.Sink.SingleEvent(3000).State["ClearingHouseId"].Should().Be(99);
        logger.Sink.SingleEvent(3001).State["Controller"].Should().Be(nameof(ClearingHouseClaimProcessorController));
    }

    [Fact]
    public async Task Controller_maps_empty_to_204()
    {
        var (controller, logger) = Controller(InboundDownloadResult.Empty(1));

        var result = await controller.DownloadEdiDataFromSftp(1, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>().Which.StatusCode.Should().Be(204);
        logger.Sink.SingleEvent(3002).State["ClearingHouseId"].Should().Be(1);
    }

    [Fact]
    public async Task Controller_maps_failures_to_500_with_counts_only()
    {
        var run = InboundDownloadResult.FromOutcomes(1, [
            InboundFileOutcome.Completed("a.x12", InboundFileStatus.RemoteDeleted),
            InboundFileOutcome.Failed("b.x12", InboundFileStatus.Failed, "socket reset"),
            InboundFileOutcome.Failed("c.x12", InboundFileStatus.Stored, "duplicate"),
        ]);
        var (controller, logger) = Controller(run);

        var result = await controller.DownloadEdiDataFromSftp(1, CancellationToken.None);

        var failed = result.Should().BeOfType<ObjectResult>().Subject;
        failed.StatusCode.Should().Be(500);
        failed.Value.Should().Be(new DownloadFailureCounts(2, 3));
        var log = logger.Sink.SingleEvent(3003);
        log.Level.Should().Be(LogLevel.Error);
        log.State["LeftOnSftp"].Should().Be(2);
        log.State["FilesSeen"].Should().Be(3);
        log.Message.Should().NotContain("socket reset").And.NotContain("b.x12");
    }

    [Fact]
    public async Task Controller_maps_connection_failure_to_500_with_zero_counts()
    {
        var (controller, _) = Controller(InboundDownloadResult.ConnectionFailed(1));

        var result = await controller.DownloadEdiDataFromSftp(1, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.Value.Should().Be(new DownloadFailureCounts(0, 0));
    }

    [Fact]
    public async Task Controller_maps_completed_to_200_and_sends_the_command()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(new DownloadInboundFilesCommand(8), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboundDownloadResult.FromOutcomes(8, [InboundFileOutcome.Completed("a.x12", InboundFileStatus.RemoteDeleted)]));
        var logger = new CapturingLogger<ClearingHouseClaimProcessorController>();
        var controller = new ClearingHouseClaimProcessorController(mediator.Object, logger);

        var result = await controller.DownloadEdiDataFromSftp(8, CancellationToken.None);

        result.Should().BeOfType<OkResult>().Which.StatusCode.Should().Be(200);
        logger.Sink.SingleEvent(3004).State["Stored"].Should().Be(1);
        mediator.Verify(m => m.Send(new DownloadInboundFilesCommand(8), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Legacy_key_middleware_copies_xapikey_when_the_application_key_is_missing()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["XApiKey"] = "legacy-key";
        var called = false;

        await new LegacyApiKeyMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
        context.Request.Headers["x-application-key"].ToString().Should().Be("legacy-key");
    }

    [Fact]
    public async Task Legacy_key_middleware_keeps_an_existing_application_key()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-application-key"] = "current-key";
        context.Request.Headers["XApiKey"] = "legacy-key";

        await new LegacyApiKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Request.Headers["x-application-key"].ToString().Should().Be("current-key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Legacy_key_middleware_ignores_blank_or_missing_legacy_keys(string? legacy)
    {
        var context = new DefaultHttpContext();
        if (legacy is not null)
        {
            context.Request.Headers["XApiKey"] = legacy;
        }

        await new LegacyApiKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Request.Headers.ContainsKey("x-application-key").Should().BeFalse();
        await FluentActions.Invoking(() => new LegacyApiKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Exception_handler_logs_unknown_exceptions_once_with_trace_fields_and_returns_500()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(Mappers(), logger);
        var context = Context("corr-123");
        var failure = new InvalidOperationException("Key Vault secret 'x' was not found for BillingApiKey.");

        var handled = await handler.TryHandleAsync(context, failure, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);
        var problem = await ReadBody(context);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("An error occurred while processing your request.");
        problem.GetRawText().Should().NotContain("Key Vault");
        var log = logger.Sink.SingleEvent(3020);
        log.Level.Should().Be(LogLevel.Error);
        log.Exception.Should().BeSameAs(failure);
        log.State["CorrelationId"].Should().Be("corr-123");
        log.State["TraceIdentifier"].Should().Be("trace-1");
        log.State["Path"].Should().Be("/ClearingHouseClaimProcessor/downloadEdiDataFromSftp");
        log.State["Method"].Should().Be("POST");
        log.State["Controller"].Should().Be("ClearingHouseClaimProcessor");
        log.State["Action"].Should().Be("DownloadEdiDataFromSftp");
    }

    [Fact]
    public async Task Exception_handler_maps_validation_to_400_and_logs_a_warning()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var context = Context(null);

        await new GlobalExceptionHandler(Mappers(), logger).TryHandleAsync(context, new ValidationException("Id is required."), CancellationToken.None);

        context.Response.StatusCode.Should().Be(400);
        var problem = await ReadBody(context);
        problem.GetProperty("errors").GetProperty("Validation")[0].GetString().Should().Be("Id is required.");
        problem.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7231#section-6.5.1");
        logger.Sink.SingleEvent(3021).Level.Should().Be(LogLevel.Warning);
        logger.Sink.ByEventId(3020).Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(NotImplementedException))]
    public async Task Exception_handler_maps_not_implemented_to_501(Type exceptionType)
    {
        var context = Context(null);
        var exception = (Exception)Activator.CreateInstance(exceptionType, "nope")!;

        await new GlobalExceptionHandler(Mappers(), new CapturingLogger<GlobalExceptionHandler>()).TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(501);
        var problem = await ReadBody(context);
        problem.GetProperty("title").GetString().Should().Be("Not implemented");
        problem.GetProperty("detail").GetString().Should().Be("nope");
    }

    [Fact]
    public async Task Exception_handler_leaves_client_aborts_to_the_host()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = Context(null);
        context.RequestAborted = aborted.Token;

        var handled = await new GlobalExceptionHandler(Mappers(), logger).TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeFalse();
        logger.Sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Exception_handler_treats_server_side_cancellation_as_a_failure()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var context = Context(null);

        var handled = await new GlobalExceptionHandler(Mappers(), logger).TryHandleAsync(context, new TaskCanceledException("HttpClient timeout"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);
        logger.Sink.SingleEvent(3020).Exception.Should().BeOfType<TaskCanceledException>();
        await FluentActions.Invoking(() => new GlobalExceptionHandler([], logger).TryHandleAsync(null!, new InvalidOperationException(), CancellationToken.None).AsTask()).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => new GlobalExceptionHandler([], logger).TryHandleAsync(context, null!, CancellationToken.None).AsTask()).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Problem_mappers_declare_what_they_handle()
    {
        var validation = new ValidationProblemMapper();
        var notImplemented = new NotImplementedProblemMapper();

        validation.IsClientError.Should().BeTrue();
        validation.CanMap(new ValidationException()).Should().BeTrue();
        validation.CanMap(new InvalidOperationException()).Should().BeFalse();
        notImplemented.IsClientError.Should().BeTrue();
        notImplemented.CanMap(new NotSupportedException()).Should().BeTrue();
        notImplemented.CanMap(new InvalidOperationException()).Should().BeFalse();
        FluentActions.Invoking(() => validation.Map(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => notImplemented.Map(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Trace_filter_logs_safe_ids_status_and_elapsed_time()
    {
        var logger = new CapturingLogger<RequestTraceFilter>();
        var time = new FakeTimeProvider();
        var filter = new RequestTraceFilter(time, logger);
        var executing = Executing(new Dictionary<string, object?> { ["clearingHouseId"] = 1 });

        await filter.OnActionExecutionAsync(executing, () =>
        {
            time.Advance(TimeSpan.FromMilliseconds(75));
            return Task.FromResult(Executed(executing, new NoContentResult()));
        });

        var started = logger.Sink.SingleEvent(3010);
        started.State["SafeIds"].Should().Be("ClearingHouseId=1");
        started.State["Controller"].Should().Be("ClearingHouseClaimProcessor");
        started.State["Method"].Should().Be("POST");
        var finished = logger.Sink.SingleEvent(3011);
        finished.State["StatusCode"].Should().Be(204);
        finished.State["ElapsedMilliseconds"].Should().Be(75L);
        await FluentActions.Invoking(() => filter.OnActionExecutionAsync(null!, () => Task.FromResult(Executed(executing, null)))).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => filter.OnActionExecutionAsync(executing, null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Trace_filter_status_prefers_result_then_unhandled_exception_then_response()
    {
        var executing = Executing([]);

        RequestTraceFilter.GetStatusCode(Executed(executing, new OkResult())).Should().Be(200);
        var unhandled = Executed(executing, null);
        unhandled.Exception = new InvalidOperationException();
        RequestTraceFilter.GetStatusCode(unhandled).Should().Be(500);
        var handled = Executed(executing, new EmptyResult());
        handled.Exception = new InvalidOperationException();
        handled.ExceptionHandled = true;
        executing.HttpContext.Response.StatusCode = 202;
        RequestTraceFilter.GetStatusCode(handled).Should().Be(202);
        FluentActions.Invoking(() => RequestTraceFilter.GetStatusCode(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Safe_id_reader_reads_only_allow_listed_ids_from_arguments_route_and_query()
    {
        var executing = Executing(new Dictionary<string, object?>
        {
            ["request"] = new SampleRequest(7, "Jane Doe", 3),
            ["memberId"] = " ",
            ["ignored"] = null,
            ["ssn"] = "123-45-6789",
        });
        executing.RouteData.Values["AccountInfoId"] = 55;
        executing.HttpContext.Request.QueryString = new QueryString("?memberId=12&FunderId=999&patientName=x");

        var ids = SafeIdReader.Read(executing);

        ids.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["FunderId"] = 7,
            ["ClearingHouseId"] = 3,
            ["AccountInfoId"] = 55,
            ["MemberId"] = "12",
        });
        SafeIdReader.Format(executing).Should().NotContain("Jane").And.NotContain("123-45").And.NotContain("patientName");
        FluentActions.Invoking(() => SafeIdReader.Read(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Host_settings_read_the_legacy_keys_with_defaults()
    {
        var empty = new ConfigurationBuilder().Build();
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppVirtualDirectory"] = "/clearinghouse",
            ["Swagger:Show"] = "true",
            ["Cors:AllowedOrigins:0"] = "https://portal.example.test",
        }).Build();

        HostSettings.VirtualDirectory(empty).Should().Be("/");
        HostSettings.ShowSwagger(empty).Should().BeFalse();
        HostSettings.AllowedCorsOrigins(empty).Should().BeEmpty();
        HostSettings.VirtualDirectory(configured).Should().Be("/clearinghouse");
        HostSettings.ShowSwagger(configured).Should().BeTrue();
        HostSettings.AllowedCorsOrigins(configured).Should().Equal("https://portal.example.test");
        FluentActions.Invoking(() => HostSettings.VirtualDirectory(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => HostSettings.ShowSwagger(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => HostSettings.AllowedCorsOrigins(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Path_prefix_filter_prefixes_every_path()
    {
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/ClearingHouseClaimProcessor/downloadEdiDataFromSftp"] = new OpenApiPathItem(),
                ["/healthcheck/live"] = new OpenApiPathItem(),
            },
        };

        new PathPrefixDocumentFilter("/clearinghouse").Apply(document, null!);

        document.Paths.Keys.Should().BeEquivalentTo("/clearinghouse/ClearingHouseClaimProcessor/downloadEdiDataFromSftp", "/clearinghouse/healthcheck/live");
        FluentActions.Invoking(() => new PathPrefixDocumentFilter("/x").Apply(null!, null!)).Should().Throw<ArgumentNullException>();
    }

    private static (ClearingHouseClaimProcessorController Controller, CapturingLogger<ClearingHouseClaimProcessorController> Logger) Controller(InboundDownloadResult result)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<DownloadInboundFilesCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var logger = new CapturingLogger<ClearingHouseClaimProcessorController>();
        return (new ClearingHouseClaimProcessorController(mediator.Object, logger), logger);
    }

    private static IProblemMapper[] Mappers() => [new ValidationProblemMapper(), new NotImplementedProblemMapper()];

    private static DefaultHttpContext Context(string? correlationId)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Method = "POST";
        context.Request.Path = "/ClearingHouseClaimProcessor/downloadEdiDataFromSftp";
        context.Request.RouteValues["controller"] = "ClearingHouseClaimProcessor";
        context.Request.RouteValues["action"] = "DownloadEdiDataFromSftp";
        if (correlationId is not null)
        {
            context.Request.Headers["X-Correlation-ID"] = correlationId;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: CancellationToken.None);
        return document.RootElement.Clone();
    }

    private static ActionExecutingContext Executing(Dictionary<string, object?> arguments)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/ClearingHouseClaimProcessor/downloadEdiDataFromSftp";
        var routeData = new RouteData();
        routeData.Values["controller"] = "ClearingHouseClaimProcessor";
        routeData.Values["action"] = "DownloadEdiDataFromSftp";
        var actionContext = new ActionContext(httpContext, routeData, new ControllerActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, controller: new object());
    }

    private static ActionExecutedContext Executed(ActionExecutingContext executing, IActionResult? result)
        => new(executing, new List<IFilterMetadata>(), controller: new object()) { Result = result };

    private sealed record SampleRequest(int FunderId, string PatientName, int ClearingHouseId);
}
