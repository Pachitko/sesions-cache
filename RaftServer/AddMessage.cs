using DotNext.Runtime.Serialization;
using DotNext.IO;

namespace RaftServer;

public sealed class ResultMessage
{
    public int Result { get; set; }
}

public sealed class AddMessage : ISerializable<AddMessage>
{
    public const string Name = "Add";
    public long? Length => 8;

    public int X { get; init; }
    public int Y { get; init; }

    public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token) where TWriter : notnull, IAsyncBinaryWriter
    {
        await writer.WriteAsync(BitConverter.GetBytes(X), token: token);
        await writer.WriteAsync(BitConverter.GetBytes(Y), token: token);
    }

    public static async ValueTask<AddMessage> ReadFromAsync<TReader>(TReader reader, CancellationToken token) where TReader : notnull, IAsyncBinaryReader
    {
        return new AddMessage
        {
            X = await reader.ReadBigEndianAsync<int>(token),
            Y = await reader.ReadBigEndianAsync<int>(token)
        };
    }
}