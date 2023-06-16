using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Routing.Template;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class IsPublicEndpointAttribute : Attribute
{
}

internal class ProtectedTracePropagator : TraceContextPropagator
{
    private readonly TraceContextHelpers _traceContextHelpers;

    public ProtectedTracePropagator(TraceContextHelpers traceContextHelpers)
    {
        _traceContextHelpers = traceContextHelpers;
    }

    public override PropagationContext Extract<T>(PropagationContext currentContext, T carrier, Func<T, string, IEnumerable<string>> getter)
    {
        if (carrier is HttpRequest request &&
            _traceContextHelpers.IsPublicEndpoint(request.Path))
        {
            return new PropagationContext(new ActivityContext(), Baggage.Current);
        }
        return base.Extract(currentContext, carrier, getter);
    }

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        base.Inject(context, carrier, setter);
    }
}


internal class ProtectedBaggagePropagator : BaggagePropagator
{
    private readonly TraceContextHelpers _traceContextHelpers;
    public ProtectedBaggagePropagator(TraceContextHelpers traceContextHelpers)
    {
        _traceContextHelpers = traceContextHelpers;
    }

    public override PropagationContext Extract<T>(PropagationContext currentContext, T carrier, Func<T, string, IEnumerable<string>> getter)
    {
        if (carrier is HttpRequest request && _traceContextHelpers.IsPublicEndpoint(request.Path))
        {
            return new PropagationContext(new ActivityContext(), Baggage.Current);
        }
        return base.Extract(currentContext, carrier, getter);
    }

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        base.Inject(context, carrier, setter);
    }
}

internal class ProtectedDistributedTraceContext : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _legacy = CreateDefaultPropagator();
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TraceContextHelpers _traceContextHelpers;

    public ProtectedDistributedTraceContext(IHttpContextAccessor httpContextAccessor, TraceContextHelpers traceContextHelpers)
    {
        _httpContextAccessor = httpContextAccessor;
        _traceContextHelpers = traceContextHelpers;
    }

    public override IReadOnlyCollection<string> Fields { get; } = new ReadOnlyCollection<string>(new[] { "traceparent" });
    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter)
    {
        if (IsPublicEndpoint())
        {
            return Enumerable.Empty<KeyValuePair<string, string?>>();
        }
        return _legacy.ExtractBaggage(carrier, getter);
    }

    public override void ExtractTraceIdAndState(object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        if (IsPublicEndpoint())
        {
            traceId = null;
            traceState = null;
            return;
        }
        _legacy.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);
    }

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        _legacy.Inject(activity, carrier, setter);
    }

    private bool IsPublicEndpoint()
    {
        return _traceContextHelpers.IsPublicEndpoint(_httpContextAccessor?.HttpContext?.Request.Path ?? string.Empty);
    }

}

internal class TraceContextHelpers
{
    private readonly EndpointDataSource _endpointDataSource;
    public TraceContextHelpers(EndpointDataSource endpointDataSource)
    {
        _endpointDataSource = endpointDataSource;
    }
    public bool IsPublicEndpoint(string url)
    {
        var routeEndpoints = _endpointDataSource?.Endpoints.Cast<RouteEndpoint>();
        var routeValues = new RouteValueDictionary();

        //To get the matchedEndpoint of the provide url
        var matchedEndpoint = routeEndpoints?
            .Where(e => new TemplateMatcher(
                    TemplateParser.Parse(e?.RoutePattern?.RawText ?? string.Empty),
                    new RouteValueDictionary())
                .TryMatch(url, routeValues))
            .OrderBy(c => c.Order)
            .FirstOrDefault();
        if (matchedEndpoint?.Metadata.GetMetadata<IsPublicEndpointAttribute>() != null)
        {
            return true;
        }
        return false;
    }
}

public static class PublicTracePropagtorExtensions
{
    public static IServiceCollection AddPublicEndpointTracePropagation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Remove(new ServiceDescriptor(typeof(DistributedContextPropagator), typeof(DistributedContextPropagator), ServiceLifetime.Singleton));
        services.AddSingleton<DistributedContextPropagator, ProtectedDistributedTraceContext>();
        services.AddSingleton<TraceContextHelpers>();
        services.AddSingleton<ProtectedTracePropagator>();
        services.AddSingleton<ProtectedBaggagePropagator>();

        services.ConfigureOpenTelemetryTracerProvider((sp, tp) =>{
            Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
                new List<TextMapPropagator>() {
                    sp.GetRequiredService<ProtectedTracePropagator>(),
                    sp.GetRequiredService<ProtectedBaggagePropagator>()
                }));
        });
        return services;
    }
}