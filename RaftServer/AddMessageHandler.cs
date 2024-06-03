namespace RaftServer;

using DotNext.Net.Cluster.Messaging;
using System.Threading;
using System.Threading.Tasks;

[Message<AddMessage>(AddMessage.Name)]
public class AddMessageHandler : MessageHandler
{
    public Task MethodName(AddMessage input, CancellationToken token)
    {
        return Task.CompletedTask;
    }
    
    public Task<ResultMessage> AddAsync(AddMessage message, CancellationToken token)
    {
        return Task.FromResult<ResultMessage>(new ResultMessage { Result = message.X + message.Y });
    }
}