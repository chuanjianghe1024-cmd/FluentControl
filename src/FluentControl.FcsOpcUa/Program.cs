using FluentControl.FcsOpcUa;
using System.Runtime.InteropServices;

if (args.Length != 2 || args[0] is not ("--config" or "--init"))
{
    Console.Error.WriteLine("FCS OPC UA preview: --init <config.json> | --config <config.json>");
    return 2;
}
try
{
    var options = Options.Load(args[1]);
    var configuration = await UaConfiguration.Create(options);
    if (args[0] == "--init")
    {
        Console.WriteLine("Application certificate initialized. Exchange public certificates via PKI trusted/certs before connecting.");
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); });
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    using var server = new UaServer(options);
    await server.StartAsync(configuration);
    using var reader = new GatewayReader(options, server.Resources);
    Console.WriteLine($"FCS OPC UA preview running: {options.SystemId}/{options.GatewayId}; read-only, explicit exports, no cloud upload.");
    try
    {
        await Task.WhenAll(new[] { reader.Run(cancellation.Token), server.Resources.Watchdog(options.StaleMilliseconds, cancellation.Token) }
            .Concat(options.Mounts.Select(m => MountReader.Run(options, m, configuration, server.Resources, cancellation.Token))));
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    finally { await server.StopAsync(); }
    return 0;
}
catch (Exception e)
{
    // Do not expose configuration tokens or remote response bodies in logs.
    Console.Error.WriteLine($"OPC UA adapter failed ({e.GetType().Name}). Check configuration, PKI and endpoint availability.");
    return 1;
}
