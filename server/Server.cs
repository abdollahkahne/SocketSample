using System.Buffers;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO.Pipelines;

namespace Pipeline
{
    public class NetworkStreamSample
    {
        public static async Task SocketServer()
        {
            // This is a server socket so it only need a LocalIPEndpoint to bind to it.
            var endpoint = new IPEndPoint(IPAddress.Any, 11_000);
            using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // since this is a server we should first it bind and listen
            socket.Bind(endpoint);
            socket.Listen(10);

            // after changing status to listening we should wait for a connection to accept
            var client = await socket.AcceptAsync();// No the client is ready and we can use a network stream to work with it for example (We can also its method directly ofcourse)
            // await ProcessStreamUsingPipe(client);
            using var stream = new NetworkStream(client, false);
            // await ProcessStreamAsync(stream);
            // await ProcessStreamLineByLineAsync(stream);
            await ProcessWithPipes(stream);
            var ack = "<|ACK|>";
            var ackBytes = Encoding.UTF8.GetBytes(ack);
            await client.SendAsync(ackBytes, SocketFlags.None);
            // await stream.WriteAsync(ackBytes);


        }
        private static async Task ProcessStreamAsync(NetworkStream stream)
        {
            while (true)
            {
                // To read stream we can use ReadXXX method to read a byte, a buffer 

                var buffer = new Byte[64];
                var received = await stream.ReadAsync(buffer);
                // Console.WriteLine($"Count:{received}");

                var message = Encoding.UTF8.GetString(buffer, 0, received);
                Console.WriteLine(message);
                // if (received == 0) break;
                if (received < buffer.Length) { break; }
                // Question: Buffer size is 1KB here. What happened if sent message is morethan 1KB? 
                // Answer: Here we only can accept first 1KB of message and other bytes do not read! Of course you can continue reading with this buffer to get all message or use a Buffer with more size which we use in the next methods

            }

        }
        private static async Task ProcessStreamLineByLineAsync(NetworkStream stream)
        {
            byte[] buffer = ArrayPool<Byte>.Shared.Rent(8); // with this we rent a byte[1024] array from shared instance of ArrayPool<Byte>. Of course we can build a pool for ourself and use it here if we want a special pool
            int bufferedBytesCount = 0;
            int startIndex = 0;
            int remainingBytesCount = 0;
            int readBytesCount = 0;
            while (readBytesCount >= remainingBytesCount)
            {
                remainingBytesCount = buffer.Length - bufferedBytesCount;
                // if remaining bytes  is zero double size of buffer (we should copy initial buffer content to new buffer and return the old buffer)
                if (remainingBytesCount == 0)
                {
                    var newBuffer = ArrayPool<Byte>.Shared.Rent(buffer.Length * 2); // rent a Buffer with double capacity
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length); // copy content of old buffer to new one
                    ArrayPool<Byte>.Shared.Return(buffer);// Return old buffer to pool
                    buffer = newBuffer; // Here only a reference assignment done
                    remainingBytesCount = buffer.Length - bufferedBytesCount;
                }
                // It is necessary to specify offset and count here other wise it may read as buffer length and overwrite already read byte
                readBytesCount = await stream.ReadAsync(buffer, bufferedBytesCount, remainingBytesCount); // we only read remaining byte count so the network position remains until next read

                bufferedBytesCount += readBytesCount;


                // Break if already reached end of message
                if (readBytesCount == 0)
                {// This means that total message read
                    break;
                }
            }
            // The following only done to separate message line by line and is not necessary for working with network streams!
            int eol = -1;
            do
            {
                // find first index of end of line character byte in byte array (if it find the end of line at start index it retun start index itself not zero!!)
                eol = Array.IndexOf(buffer, (byte)'\n', startIndex, bufferedBytesCount - startIndex);
                if (eol > -1)
                {
                    var lineLength = eol - startIndex;
                    var line = Encoding.UTF8.GetString(buffer, startIndex, lineLength);
                    Console.WriteLine(line);
                    startIndex = eol + 1;
                }
            } while (eol >= 0);
            // The following condition check last line since the last line may not have \n char
            if (startIndex < bufferedBytesCount)
            {
                var line = Encoding.UTF8.GetString(buffer, startIndex, bufferedBytesCount - startIndex);
                Console.WriteLine(line);
            }
            // var message = Encoding.UTF8.GetString(buffer, 0, bufferedBytesCount);
            // Console.WriteLine(message);

        }
        private static async Task ProcessStreamUsingPipe(Socket socket)
        {
            // The Pipe class can be used to create a PipeWriter/PipeReader pair. 
            // All data written into the PipeWriter is available in the PipeReader:
            Pipe pipe = new Pipe();
            // PipeReader pipeReader = pipe.Reader;
            // PipeWriter pipeWriter = pipe.Writer;
            Task writing = FillPipeAsync(socket, pipe.Writer);
            Task reading = ReadPipeAsync(pipe.Reader);
            await Task.WhenAll(writing, reading);
        }
        private static async Task FillPipeAsync(Socket client, PipeWriter writer)
        {
            const int minimumBuffersize = 1;
            while (true)
            {
                // Use writer for getting buffer (Here get as Memory<Byte> instead of Byte[]). This is only can be used once and its size is not fixed. In case the memory exist it return a memory<byte> with size more than minimum size otherwise throw exception
                var buffer = writer.GetMemory(minimumBuffersize);
                try
                {
                    int received = await client.ReceiveAsync(buffer, SocketFlags.None);
                    Console.WriteLine($"Received Count:{received}");


                    // You should Flush the writer to make data available to reader.
                    var result = await writer.FlushAsync();

                    // You must request a new buffer after calling System.IO.Pipelines.PipeWriter.Advance(System.Int32)  to continue writing more data; you cannot write to a previously acquired buffer.
                    writer.Advance(received); // Pipe Writer uses this data to get new buffer. This is necessary to reader can read
                    if (received < buffer.Length) break;

                    if (result.IsCompleted)// indicates the reader is no longer reading data written to the PipeWriter
                    {
                        break;
                    }


                }
                catch (System.Exception)
                {

                    throw;
                }


            }
            await writer.CompleteAsync(); // inform the reader that no data would write to pipe 
        }
        private static async Task ReadPipeAsync(PipeReader reader)
        {

            while (true)
            {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;
                var msg = Encoding.UTF8.GetString(buffer);
                Console.WriteLine(msg);
                reader.AdvanceTo(buffer.End);
                // The memory for the consumed data (first argument) will be released and no longer available. The ReadResult.Buffer previously returned from PipeReader.ReadAsync(CancellationToken) must not be accessed after this call. 
                // The examined data (second argument) communicates to the pipeline when it should signal more data is available. 
                // The examined parameter should be greater than or equal to the examined position in the previous call to `AdvanceTo`. Otherwise, an InvalidOperationException is thrown.


                // Gets a value that indicates whether the end of the data stream has been reached.
                if (result.IsCompleted) break;
            }

            // Marks the current pipe reader instance as being complete, meaning no more data will be read from it.
            await reader.CompleteAsync();
        }
        private static async Task ProcessWithPipes(NetworkStream stream)
        {
            var reader = PipeReader.Create(stream);
            // var ms = new MemoryStream();
            // reader.Complete();
            // await reader.CopyToAsync(ms);
            // Console.WriteLine("Completed");
            try
            {
                bool completed = false;
                // var buff = writer.GetMemory();
                // var flushResult = await writer.FlushAsync();
                // writer.Advance(0);
                // if (flushResult.IsCompleted)
                // {
                //     await writer.CompleteAsync();
                // }

                do
                {
                    // Console.WriteLine("Before Reading Pipe Reader");
                    var result = await reader.ReadAsync();
                    // var success = reader.TryRead(out var result);
                    // Console.WriteLine($"Completed:{result.IsCompleted}, Cancelled:{result.IsCanceled}");
                    // var result = await reader.ReadAtLeastAsync(0);


                    var buffer = result.Buffer;
                    WorkWithSequence(buffer);

                    var nextPosition = buffer.GetPosition(buffer.Length);
                    reader.AdvanceTo(nextPosition);
                    // if (!stream.DataAvailable)
                    if (result.Buffer.Length == 0)
                    {
                        completed = true;
                        Console.WriteLine("Receive Empty Buffer, Client Closed");
                    }
                    // if (buffer.IsSingleSegment)
                    // {
                    //     // Console.WriteLine("Single Segment");
                    //     string message = Encoding.UTF8.GetString(buffer);
                    //     Console.WriteLine(message);
                    // }
                    // else
                    // {
                    //     // Console.WriteLine("Multi Segment");
                    //     foreach (var item in buffer)
                    //     {
                    //         // use span property of memory instead of converting it to Array
                    //         string message = Encoding.UTF8.GetString(item.Span);
                    //         Console.WriteLine(message);
                    //     }

                    // }



                    if (result.IsCompleted) // This is not happen since we have not used writer
                    {
                        completed = true;
                        Console.WriteLine("Stream Reading Completed, Client Closed");
                    }


                } while (!completed);
                await reader.CompleteAsync();


            }
            catch (System.Exception)
            {

                throw;
            }
        }
        private static SequencePosition WorkWithSequence(in ReadOnlySequence<Byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                // Console.WriteLine("Single Segment");
                string message = Encoding.UTF8.GetString(buffer);
                Console.WriteLine(message);
            }
            else
            {
                // Console.WriteLine("Multi Segment");
                foreach (var item in buffer)
                {
                    // use span property of memory instead of converting it to Array
                    string message = Encoding.UTF8.GetString(item.Span);
                    Console.WriteLine(message);
                }

            }
            var reader = new SequenceReader<Byte>(buffer);
            reader.Advance(buffer.Length);
            return reader.Position;

        }
    }
}
