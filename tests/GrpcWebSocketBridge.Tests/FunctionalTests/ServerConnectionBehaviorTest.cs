using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace GrpcWebSocketBridge.Tests.FunctionalTests
{
    public class ServerConnectionBehaviorTest : UseTestServerTestBase
    {
        //protected override AspNetCoreServerTestHostOptions? DefaultHostOptions => new AspNetCoreServerTestHostOptions() {DefaultChannel = ChannelKind.InsecureHttp2};

        public ServerConnectionBehaviorTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public async Task DisconnectFromClient_Duplex_Normally()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterService_DisconnectFromClient_Duplex_Normally>>();

            var channel = host.CreateChannel(disposeHttpClient: true);
            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // Establish connection and wait for headers.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);

            // Complete the client request stream.
            await duplex.RequestStream.CompleteAsync().WithCancellation(TimeoutToken);

            // Disconnect from the server by the client.
            duplex.Dispose();
            channel.Dispose();
            await Task.Delay(10);

            // Shutdown the server.
            await host.ShutdownAsync();

            // Validate server logs.
            var events = host.GetAllLogEvents();
            events.Should().NotContain(x => x.LogLevel >= LogLevel.Error);
        }

        class GreeterService_DisconnectFromClient_Duplex_Normally : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await context.WriteResponseHeadersAsync(new Metadata());
            }
        }

        [Fact]
        public async Task DisconnectFromClient_Duplex_Abort_RequestReading()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterService_DisconnectFromClient_Duplex_Abort_RequestReading>>();

            var channel = host.CreateChannel(disposeHttpClient: true);
            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // Establish connection and wait for headers.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);

            // Disconnect from the server by the client.
            duplex.Dispose();
            channel.Dispose();
            await Task.Delay(10);

            // Shutdown the server.
            await host.ShutdownAsync();

            // Validate server logs.
            var events = host.GetAllLogEvents();
            //events.Should().Contain(x => x.Message.Contains("Error reading message."));
            events.Should().Contain(x => x.Message.Contains("Error when executing service method"));
            events.Should().NotContain(x => x.Message.Contains("unhandled exception was thrown"));
        }

        class GreeterService_DisconnectFromClient_Duplex_Abort_RequestReading : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await context.WriteResponseHeadersAsync(new Metadata());
                await requestStream.MoveNext(context.CancellationToken);
            }
        }

        [Fact]
        public async Task DisconnectFromClient_Duplex_Abort_ResponseWriting()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterService_DisconnectFromClient_Duplex_Abort_ResponseWriting>>();

            var signal = host.CreateSignal(nameof(GreeterService_DisconnectFromClient_Duplex_Abort_ResponseWriting));
            var channel = host.CreateChannel(disposeHttpClient: true);
            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // Establish connection and wait for headers.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);

            // Disconnect from the server by the client.
            duplex.Dispose();
            channel.Dispose();
            await Task.Delay(500);

            // Signal to the server to continue processing.
            signal.Set();

            await Task.Delay(500);

            // Shutdown the server.
            await host.ShutdownAsync();

            // Validate server logs.
            var events = host.GetAllLogEvents();
            events.Should().Contain(x => x.Message.Contains("Error when executing service method"));
            events.Should().NotContain(x => x.Message.Contains("unhandled exception was thrown"));
        }

        class GreeterService_DisconnectFromClient_Duplex_Abort_ResponseWriting : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var signal = context.GetHostStaticItem<WaitHandle>(nameof(GreeterService_DisconnectFromClient_Duplex_Abort_ResponseWriting));

                await context.WriteResponseHeadersAsync(new Metadata());

                signal.WaitOne();

                await responseStream.WriteAsync(new HelloReply());
                await responseStream.WriteAsync(new HelloReply());
            }
        }
    }
}
