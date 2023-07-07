using System.Collections.ObjectModel;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

internal class DisableAllTracePropagator : TraceContextPropagator
{
    public override PropagationContext Extract<T>(PropagationContext currentContext, T carrier, Func<T, string, IEnumerable<string>> getter)
    {
        return new PropagationContext(new ActivityContext(), new Baggage());
    }

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        base.Inject(context, carrier, setter);
    }
}

internal class DisableAllBaggagePropagator : BaggagePropagator
{

    public override PropagationContext Extract<T>(PropagationContext currentContext, T carrier, Func<T, string, IEnumerable<string>> getter)
    {
        return new PropagationContext(new ActivityContext(), new Baggage());
    }

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        base.Inject(context, carrier, setter);
    }
}

internal class DisableAllContextPropagator : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _legacy = CreateDefaultPropagator();
    public override IReadOnlyCollection<string> Fields { get; } = new ReadOnlyCollection<string>(new[] { "traceparent" });
    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter)
    {
        return Enumerable.Empty<KeyValuePair<string, string?>>();
    }

    public override void ExtractTraceIdAndState(object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        traceId = null;
        traceState = null;
        return;
    }

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        _legacy.Inject(activity, carrier, setter);
    }

}

public static class DisableAllInboundPropagationHelpers
{
    public static IServiceCollection DisableInboundTracePropagation(this IServiceCollection services)
    {
        services.Remove(new ServiceDescriptor(typeof(DistributedContextPropagator), typeof(DistributedContextPropagator), ServiceLifetime.Singleton));
        services.AddSingleton<DistributedContextPropagator, DisableAllContextPropagator>();

        services.ConfigureOpenTelemetryTracerProvider((sp, tp) =>{
            Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
                new List<TextMapPropagator>() {
                    new DisableAllTracePropagator(),
                    new DisableAllBaggagePropagator()
                }));
        });
        return services;
    }
}