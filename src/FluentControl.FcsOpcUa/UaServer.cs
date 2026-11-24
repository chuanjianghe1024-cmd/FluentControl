using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace FluentControl.FcsOpcUa;

internal static class UaConfiguration
{
    public static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(_ => { });
    public static async Task<ApplicationConfiguration> Create(Options options)
    {
        Directory.CreateDirectory(options.PkiDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(options.PkiDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string Store(string name) => Path.GetFullPath(Path.Combine(options.PkiDirectory, name));
        var config = new ApplicationConfiguration {
            ApplicationName = "FCS OPC UA " + options.SystemId,
            ApplicationUri = $"urn:fcs:{options.SystemId}:{options.GatewayId}",
            ProductUri = "urn:fluentcontrol:fcs:opcua", ApplicationType = ApplicationType.ClientAndServer,
            SecurityConfiguration = new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = Store("own"), SubjectName = $"CN=FCS-{options.SystemId}-{options.GatewayId}" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Store("trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Store("issuers") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Store("rejected") },
                AutoAcceptUntrustedCertificates = false, RejectSHA1SignedCertificates = true, MinimumCertificateKeySize = 2048,
                AddAppCertToTrustedStore = false
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 5000, MaxMessageSize = 1048576, MaxStringLength = 65536, MaxArrayLength = 4096, MaxByteStringLength = 1048576 },
            ServerConfiguration = new ServerConfiguration {
                BaseAddresses = new StringCollection { options.Endpoint },
                SecurityPolicies = new ServerSecurityPolicyCollection { new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = SecurityPolicies.Basic256Sha256 } },
                UserTokenPolicies = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) },
                MaxSessionCount = 16, MaxSubscriptionCount = 32, MaxMessageQueueSize = 32,
                MaxNotificationQueueSize = 256, MaxNotificationsPerPublish = 256,
                MaxBrowseContinuationPoints = 8,
                OperationLimits = new OperationLimits { MaxNodesPerRead = 256, MaxNodesPerBrowse = 64, MaxMonitoredItemsPerCall = 256 },
                MinPublishingInterval = 100, PublishingResolution = 100
            },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 15000 }
        };
        await config.ValidateAsync(ApplicationType.ClientAndServer);
        var app = new ApplicationInstance(Telemetry) { ApplicationConfiguration = config };
        if (!await app.CheckApplicationInstanceCertificatesAsync(false, 12)) throw new InvalidOperationException("Application certificate unavailable.");
        await config.CertificateValidator.UpdateAsync(config);
        return config;
    }
}
internal sealed class UaServer(Options options) : StandardServer
{
    public ResourceManager Resources { get; private set; } = null!;
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        Resources = new ResourceManager(server, configuration, options);
        return new MasterNodeManager(server, configuration, null, Resources);
    }
    protected override ServerProperties LoadServerProperties() => new() {
        ManufacturerName = "FluentControl", ProductName = "FCS OPC UA Preview", ProductUri = "urn:fluentcontrol:fcs:opcua",
        SoftwareVersion = "0.1.0", BuildNumber = "preview", BuildDate = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)
    };
}
internal sealed class ResourceManager : CustomNodeManager2
{
    public const string NamespaceUri = "urn:fluentcontrol:fcs:opcua:1";
    private readonly Options options;
    private readonly Dictionary<string, BaseDataVariableState> values = new();
    public ResourceManager(IServerInternal server, ApplicationConfiguration configuration, Options options)
        : base(server, configuration, NamespaceUri) { this.options = options; }
    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            var root = Folder(null, "FCS", "FCS");
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var refs)) externalReferences[ObjectIds.ObjectsFolder] = refs = new List<IReference>();
            refs.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            Property(root, "SystemId", options.SystemId);
            Property(root, "GatewayId", options.GatewayId);
            Property(root, "Profile", "fcs-opcua/1");
            var local = Folder(root, "local", options.SystemId);
            AddSignals(local, options.SystemId, options.GatewayId, options.Signals, false);
            var mounts = Folder(root, "mounts", "Mounted systems");
            foreach (var mount in options.Mounts)
                AddSignals(Folder(mounts, "mount/" + mount.SystemId, mount.SystemId), mount.SystemId, mount.GatewayId, mount.Signals, true);
            AddPredefinedNode(SystemContext, root);
        }
    }
    private FolderState Folder(NodeState? parent, string id, string name)
    {
        var node = new FolderState(parent) { NodeId = new NodeId(id, NamespaceIndex), BrowseName = new QualifiedName(name, NamespaceIndex), DisplayName = name,
            TypeDefinitionId = ObjectTypeIds.FolderType, ReferenceTypeId = ReferenceTypeIds.Organizes };
        parent?.AddChild(node); return node;
    }
    private void Property(NodeState parent, string name, string value)
    {
        var p = new PropertyState<string>(parent) { NodeId = new NodeId(parent.NodeId.Identifier + "/" + name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex), DisplayName = name, TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypeIds.HasProperty, DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead, Value = value };
        parent.AddChild(p);
    }
    public static NodeId DataType(Signal s) => s.ValueType switch { "integer" => DataTypeIds.Int64, "boolean" => DataTypeIds.Boolean, "string" => DataTypeIds.String, _ => DataTypeIds.Double };
    public static object Initial(Signal s) => s.ValueType switch { "integer" => 0L, "boolean" => false, "string" => "", _ => 0d };
    private void AddSignals(NodeState parent, string system, string gateway, Signal[] signals, bool mounted)
    {
        var gw = Folder(parent, $"tree/{system}/{gateway}", gateway);
        foreach (var device in signals.GroupBy(s => s.DeviceId))
        {
            var dev = Folder(gw, $"tree/{system}/{gateway}/{device.Key}", device.Key);
            foreach (var sensor in device.GroupBy(s => s.SensorId))
            {
                var sen = Folder(dev, $"tree/{system}/{gateway}/{device.Key}/{sensor.Key}", sensor.Key);
                foreach (var signal in sensor)
                {
                    var key = signal.Key(system, gateway);
                    var v = new BaseDataVariableState(sen) { NodeId = new NodeId(key, NamespaceIndex),
                        BrowseName = new QualifiedName(signal.SignalId, NamespaceIndex), DisplayName = signal.SignalId,
                        ReferenceTypeId = ReferenceTypeIds.Organizes, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                        DataType = DataType(signal), ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                        Value = Initial(signal), StatusCode = StatusCodes.BadWaitingForInitialData, Timestamp = DateTime.MinValue };
                    sen.AddChild(v); values.Add(key, v);
                    Property(v, "OriginSystem", system); Property(v, "OriginGateway", gateway);
                    Property(v, "Unit", signal.Unit); Property(v, "ValueType", signal.ValueType);
                    Property(v, "ExportKind", mounted ? "mounted" : "local");
                    Property(v, "TimestampBasis", "origin-gateway-receipt");
                }
            }
        }
    }
    public void Update(string key, DataValue sample)
    {
        lock (Lock)
        {
            var v = values[key];
            if (sample.Value != null) v.Value = sample.Value;
            v.StatusCode = sample.StatusCode; v.Timestamp = sample.SourceTimestamp;
            v.ClearChangeMasks(SystemContext, false);
        }
    }
    public void Fail(string key, uint status)
    {
        lock (Lock) { var v = values[key]; v.StatusCode = status; v.ClearChangeMasks(SystemContext, false); }
    }
    public async Task Watchdog(int staleMilliseconds, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(500, cancellation);
            lock (Lock)
                foreach (var v in values.Values)
                    if (StatusCode.IsGood(v.StatusCode) && DateTime.UtcNow - v.Timestamp > TimeSpan.FromMilliseconds(staleMilliseconds))
                    { v.StatusCode = StatusCodes.BadNoCommunication; v.ClearChangeMasks(SystemContext, false); }
        }
    }
}
