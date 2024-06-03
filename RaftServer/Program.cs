using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Messaging;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using RaftServer;

var builder = WebApplication.CreateBuilder(args);

builder
    .JoinCluster((memberConfig, configuration, env) =>
    {
        memberConfig.PublicEndPoint = new Uri("http://localhost:5109");
    });

builder.Services.UseInMemoryConfigurationStorage(x =>
{
    x.Add(new UriEndPoint(new Uri("http://localhost:5109")));
});

var app = builder.Build();

app.UseConsensusProtocolHandler();

app.MapGet("/", async (
    [FromServices] IMessageBus messageBus) =>
{
    var client = new MessagingClient(messageBus.Leader!).RegisterMessage<AddMessage>(AddMessage.Name);
    await client.SendSignalAsync(new AddMessage { X = 40, Y = 2 }); // send one-way message
});

app.Run();