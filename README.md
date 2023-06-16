# Disabling W3C trace propagation in .NET

This repo demonstrates adding a new attribute `[IsPublicEndpoint]` that you can add to your controllers/actions that will stop all Trace Propagtion and protect your telemetry from inheriting rogue headers.

## To test.


```shell

curl -H "traceparent: 00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" http://localhost:5143/WeatherForecast
```