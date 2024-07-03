using System.Threading.Channels;
using Core.Interfaces;
using Core.Models;
using Core.Services;
using Grains.States;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Server.Options;

namespace Server.Grains;

public sealed class GrainSingletonService(
    IPermissionService permissionService,
    ISessionDataRepository sessionDataRepository,
    ILogger<SessionGrain> logger,
    ChannelWriter<SessionState> sessionGrainStateWriter,
    ChannelWriter<SessionDeletion> sessionDeletionWriter,
    IClusterClient clusterClient,
    ISiloStatusOracle siloStatusOracle,
    ILocalSiloDetails localSiloDetails,
    ILocalSessionCache localSessionCache,
    IOptions<ServerOptions> options)
{
    public IPermissionService PermissionService { get; } = permissionService;
    public ISessionDataRepository SessionDataRepository { get; } = sessionDataRepository;
    public ILogger<SessionGrain> Logger { get; } = logger;
    public ChannelWriter<SessionState> SessionGrainStateWriter { get; } = sessionGrainStateWriter;
    public ChannelWriter<SessionDeletion> SessionDeletionWriter { get; } = sessionDeletionWriter;
    public IClusterClient ClusterClient { get; } = clusterClient;
    public ISiloStatusOracle SiloStatusOracle { get; } = siloStatusOracle;
    public ILocalSiloDetails LocalSiloDetails { get; } = localSiloDetails;
    public ILocalSessionCache LocalSessionCache { get; } = localSessionCache;
    public IOptions<ServerOptions> Options { get; } = options;
}