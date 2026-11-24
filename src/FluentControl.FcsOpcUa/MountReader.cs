using Opc.Ua;
using Opc.Ua.Client;

namespace FluentControl.FcsOpcUa;

internal static class MountReader
{
    internal static DataValue ValidateSample(Signal signal, DataValue sample, int staleMilliseconds)
    {
        bool valid = signal.ValueType switch {
            "number" => sample.Value is double number && double.IsFinite(number),
            "integer" => sample.Value is long,
            "boolean" => sample.Value is bool,
            "string" => sample.Value is string { Length: <= 4096 },
            _ => false
        };
        if (!valid && (sample.Value != null || StatusCode.IsGood(sample.StatusCode)))
            return new DataValue { StatusCode = StatusCodes.BadTypeMismatch, SourceTimestamp = sample.SourceTimestamp };
        if (StatusCode.IsGood(sample.StatusCode) && (sample.SourceTimestamp == DateTime.MinValue ||
            sample.SourceTimestamp > DateTime.UtcNow.AddSeconds(5) || DateTime.UtcNow - sample.SourceTimestamp > TimeSpan.FromMilliseconds(staleMilliseconds)))
            sample.StatusCode = StatusCodes.BadNoCommunication;
        return sample;
    }
    public static async Task<ISession> Connect(ApplicationConfiguration config, string endpoint, CancellationToken cancellation = default)
    {
        var description = await CoreClientUtils.SelectEndpointAsync(config, endpoint, true, 5000, UaConfiguration.Telemetry, cancellation);
        if (description == null || description.SecurityMode != MessageSecurityMode.SignAndEncrypt || description.SecurityPolicyUri != SecurityPolicies.Basic256Sha256)
            throw new InvalidDataException("Mount requires Basic256Sha256 SignAndEncrypt.");
        var configured = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(config));
        return await new DefaultSessionFactory(UaConfiguration.Telemetry).CreateAsync(config, configured, false, true, config.ApplicationName, 15000, new UserIdentity(new AnonymousIdentityToken()), null, cancellation);
    }
    public static async Task Run(Options options, Mount mount, ApplicationConfiguration config, ResourceManager resources, CancellationToken cancellation)
    {
        void Fail() { foreach (var signal in mount.Signals) resources.Fail(signal.Key(mount.SystemId, mount.GatewayId), StatusCodes.BadNotConnected); }
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var session = await Connect(config, mount.Endpoint, cancellation);
                ushort ns = session.NamespaceUris.GetIndex(ResourceManager.NamespaceUri) is int index && index > 0 ? checked((ushort)index) : throw new InvalidDataException("Missing FCS namespace.");
                NodeId Id(string key) => new(key, ns);
                if ((string)(await session.ReadValueAsync(Id("FCS/SystemId"), cancellation)).Value != mount.SystemId || (string)(await session.ReadValueAsync(Id("FCS/GatewayId"), cancellation)).Value != mount.GatewayId ||
                    (string)(await session.ReadValueAsync(Id("FCS/Profile"), cancellation)).Value != "fcs-opcua/1") throw new InvalidDataException("Mount identity mismatch.");
                using var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = options.PollMilliseconds, KeepAliveCount = 3, LifetimeCount = 12 };
                // First preview deliberately accepts only the upstream's own resources.
                // Imported nodes cannot be recursively re-shared or form a forwarding loop.
                foreach (var signal in mount.Signals)
                {
                    var key = signal.Key(mount.SystemId, mount.GatewayId);
                    if ((string)(await session.ReadValueAsync(Id(key + "/ExportKind"), cancellation)).Value != "local" ||
                        (string)(await session.ReadValueAsync(Id(key + "/OriginSystem"), cancellation)).Value != mount.SystemId ||
                        (string)(await session.ReadValueAsync(Id(key + "/OriginGateway"), cancellation)).Value != mount.GatewayId ||
                        (string)(await session.ReadValueAsync(Id(key + "/Unit"), cancellation)).Value != signal.Unit ||
                        (string)(await session.ReadValueAsync(Id(key + "/ValueType"), cancellation)).Value != signal.ValueType)
                        throw new InvalidDataException("Mount schema or export policy mismatch.");
                    var item = new MonitoredItem(subscription.DefaultItem) { StartNodeId = Id(key), AttributeId = Attributes.Value, SamplingInterval = options.PollMilliseconds,
                        QueueSize = 1, DiscardOldest = true, Filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp } };
                    item.Notification += (_, _) => {
                        foreach (var sample in item.DequeueValues())
                        {
                            resources.Update(key, ValidateSample(signal, sample, options.StaleMilliseconds));
                        }
                    };
                    subscription.AddItem(item);
                }
                session.AddSubscription(subscription); await subscription.CreateAsync(cancellation);
                foreach (var item in subscription.MonitoredItems)
                    if (ServiceResult.IsBad(item.Status.Error)) throw new ServiceResultException(item.Status.Error);
                while (!cancellation.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellation);
                    if (!session.Connected || session.KeepAliveStopped) throw new IOException("Mount disconnected.");
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellation.IsCancellationRequested)
            {
                Fail(); Console.Error.WriteLine($"Mount {mount.SystemId} unavailable ({e.GetType().Name}); retrying.");
                await Task.Delay(5000, cancellation);
            }
        }
    }
}
