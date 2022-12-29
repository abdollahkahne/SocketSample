using System.Security.Cryptography;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetworkFundamental
{
    public class SocketProgramming
    {
        public static async Task WorkWithSocket()
        {
            // The socket must be initialized with protocol and network address information. The constructor for the Socket class has parameters that specify the address family, socket type, and protocol type that the socket uses to make connections. 
            //  When connecting a client socket to a server socket, the client will use an IPEndPoint object to specify the network address of the server.
            // The IPEndPoint is constructed with an IPAddress and its corresponding port number.
            // We Also Have DnsEndPoint which is derived from Endpoint like IPEndpoint. The difference is using IPAddress or HostName String
            var hostEntry = await Dns.GetHostEntryAsync(Dns.GetHostName());
            var ipAddress = hostEntry.AddressList[0];
            var endpoint = new IPEndPoint(ipAddress, 11_000); // We only specified port of destination. What is port used for recieving response?
            // With the endPoint object created, create a client socket to connect to the server. Once the socket is connected, it can send and receive data from the server socket connection.
            using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine($"Local Port of Socket When Socket Created:{(socket.LocalEndPoint as IPEndPoint)?.Port}");// Local Port is not yet specified
            await socket.ConnectAsync(endpoint); // Create connection first (due to TCP we should have connection first)
            Console.WriteLine($"Local Port of Socket When Socket Connected:{(socket.LocalEndPoint as IPEndPoint)?.Port}");// Local Port set when socket start connection
            while (true)
            {
                var message = "Hi Freinds. I am trying to learn socket!👋!\n The message should be very large just to test size of buffer which is 1024.\n Does that prevent big message from sending or receiving completely? <|EOM|>";
                message = string.Join("\n\n\n", Enumerable.Range(0, 20000).Select(i => i + "- " + message));
                var messageData = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(messageData, SocketFlags.None);
                Console.WriteLine($"Socket client sent message: \"{message}\"");

                // After sending message we should recieve acknowledge and try to send message again if we have not receive ack
                var buffer = new Byte[1024];
                var received = await socket.ReceiveAsync(buffer, SocketFlags.None);// recieved has size of data in byte received
                var response = Encoding.UTF8.GetString(buffer, 0, received);
                if (response == "<|ACK|>")
                {
                    Console.WriteLine($"Socket client received acknowledgment: \"{response}\"");
                    break;
                }
            }
            // We should gracefully shut down the connection at the end of TCP 
            socket.Shutdown(SocketShutdown.Both); // shuts down both send and receive operations.
        }
        public static async Task WorkWithTCPClient()
        {
            // represent a network endpoint as an IPEndPoint object. The IPEndPoint is constructed with an IPAddress and its corresponding port number
            var hostEntry = await Dns.GetHostEntryAsync(Dns.GetHostName());// Dns.GetHostName() return name of PC not localhost itself
            var destinationIP = hostEntry.AddressList[0];
            var endpoint = new IPEndPoint(destinationIP, 11_000);
            // The TcpClient class requests data from an internet resource using TCP. The methods and properties of TcpClient abstract the details for creating a Socket for requesting and receiving data using TCP
            var client = new TcpClient();
            // The TCP protocol establishes a connection with a remote endpoint and then uses that connection to send and receive data packets. 
            await client.ConnectAsync(endpoint);
            var stream = client.GetStream();
            while (true)
            {
                var message = "Hi Freinds. I am trying to learn TCP Client!👋!<|EOM|>";
                var messageData = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(messageData);
                Console.WriteLine($"Socket client sent message: \"{message}\"");
                var buffer = new Byte[1024];
                var received = await stream.ReadAsync(buffer);// With larger messages, or messages with an indeterminate length, the client should use the buffer more appropriately and read in a while loop.
                var response = Encoding.UTF8.GetString(buffer, 0, received);
                if (response == "<|ACK|>")
                {
                    Console.WriteLine($"Socket client received acknowledgment: \"{response}\"");
                    break;
                }
            }
            client.Close();
            // client.Client.Shutdown(SocketShutdown.Both);
        }
        public static async Task WorkWithUDPClient()
        {
            // Create a UDP client which uses local port 11000
            var udpClient = new UdpClient(11_000);
            // udpClient.Connect(IPAddress.Loopback, 11_000); // Is this necessary for udp clients? No it is not necessary if you specify endpoint at send method
            var msg = "Is Any Body there";
            var msgPayload = Encoding.UTF8.GetBytes(msg);
            await udpClient.SendAsync(msgPayload, new IPEndPoint(IPAddress.Any, 11_000));
            var response = await udpClient.ReceiveAsync();
            var responseText = Encoding.UTF8.GetString(response.Buffer);
            Console.WriteLine(response.RemoteEndPoint + ":" + responseText);
            udpClient.Close();
        }
    }
}
// Note: IPEndpoint
// TCP/IP uses a network address and a service port number to uniquely identify a service. The network address identifies a specific network destination; the port number identifies the specific service on that device to connect to. The combination of network address and service port is called an endpoint