using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools_protocol.Network;

internal static class NetworkCaptureSmoke
{
    // TcpClient.GetStream checks Connected again after Socket.Shutdown(Send), even
    // though the existing stream still supports receiving the other TCP direction.
    private static readonly Dictionary<TcpClient,NetworkStream> streams=new Dictionary<TcpClient,NetworkStream>();
    private sealed class CaptureLog
    {
        private readonly object gate=new object();
        private readonly List<CapturedPacket> packets=new List<CapturedPacket>();
        internal void Add(CapturedPacket packet){lock(gate)packets.Add(packet);}
        internal CapturedPacket[] Snapshot(){lock(gate)return packets.ToArray();}
        internal CapturedPacket Wait(Func<CapturedPacket,bool> predicate,string message)
        {
            WaitUntil(()=>Snapshot().Any(predicate),message);
            return Snapshot().First(predicate);
        }
    }

    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>
        {
            string name=new AssemblyName(args.Name).Name,path=Path.Combine(TestPaths.ApplicationBin,name+".dll");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        TestDecoder();
        TestRelay();
        TestInvalidUtf8Relay();
        TestUnavailableUpstream();
        Console.WriteLine("OK: bounded UTF8 packet decoding, exact local bidirectional relay, authentication masking, separate clients, half-close drain, clean stop/restart and unavailable-upstream cleanup");
    }

    private static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);}
    private static void WaitUntil(Func<bool> condition,string message)
    {
        var watch=Stopwatch.StartNew();
        while(!condition())
        {
            if(watch.ElapsedMilliseconds>5000)throw new Exception(message);
            Thread.Sleep(10);
        }
    }
    private static byte[] Bytes(string data){return Encoding.UTF8.GetBytes(data);}
    private static void OverLimit(Action action,string message)
    {
        try{action();}
        catch(InvalidDataException){return;}
        throw new Exception(message);
    }

    private static void TestDecoder()
    {
        var decoder=new NullTerminatedPacketDecoder(64);
        byte[] frames=Bytes("GMé漢\0GA1;4,5\0");
        var decoded=new List<string>();
        for(int i=0;i<frames.Length;i++)decoded.AddRange(decoder.Append(frames,i,1));
        Check(decoded.SequenceEqual(new[]{"GMé漢","GA1;4,5"}),"Fragmented UTF8 or multiple null-terminated frames were corrupted");
        byte[] envelope=Bytes("XXone\0two\0YY");
        Check(decoder.Append(envelope,2,envelope.Length-4).SequenceEqual(new[]{"one","two"}),"Decoder ignored the supplied offset/count");
        Check(decoder.Append(new byte[]{0,0},0,2).Count==0,"Empty protocol frames should be ignored");
        byte[] lineBreaks=Bytes("line1\nline2\r\n\0");
        Check(decoder.Append(lineBreaks,0,lineBreaks.Length).Single()=="line1\nline2","Decoder changed interior line breaks or failed to remove one final protocol CRLF");
        decoder.Append(Bytes("discarded"),0,9);decoder.Reset();
        Check(decoder.Append(Bytes("clean\0"),0,6).Single()=="clean","Reset retained bytes from an unfinished packet");
        var bounded=new NullTerminatedPacketDecoder(4);
        Check(bounded.Append(Bytes("1234"),0,4).Count==0,"Decoder emitted an unterminated packet");
        OverLimit(()=>bounded.Append(Bytes("5"),0,1),"An oversized fragmented packet bypassed the buffer limit");
        bounded.Reset();
        OverLimit(()=>bounded.Append(Bytes("12345\0"),0,6),"An oversized terminated packet bypassed the buffer limit");
        bounded.Reset();
        byte[] many=Bytes(string.Concat(Enumerable.Repeat("ok\0",100)));
        Check(bounded.Append(many,0,many.Length).Count==100,"Buffer limit was incorrectly applied to a batch of individually valid packets");
        bounded.Reset();
        Check(bounded.Append(Bytes("safe\0"),0,5).Single()=="safe","A reset decoder could not recover after an oversized packet");
        Check(bounded.Append(Bytes("éé\0"),0,5).Single()=="éé","A UTF8 packet exactly at the byte limit was rejected");
        OverLimit(()=>bounded.Append(Bytes("ééé\0"),0,7),"The buffer bound counted characters instead of bytes");
        bounded.Reset();
        bool invalidUtf8=false;
        try{bounded.Append(new byte[]{255,0},0,2);}
        catch(DecoderFallbackException){invalidUtf8=true;}
        Check(invalidUtf8&&bounded.Append(Bytes("ok\0"),0,3).Single()=="ok","Invalid UTF8 did not produce an explicit error followed by a clean decoder state");
    }

    private static TcpClient Client(int port)
    {
        var client=new TcpClient();
        client.ReceiveTimeout=5000;client.SendTimeout=5000;client.NoDelay=true;
        client.Connect(IPAddress.Loopback,port);streams.Add(client,client.GetStream());return client;
    }
    private static TcpClient Accept(TcpListener listener)
    {
        Task<TcpClient> pending=listener.AcceptTcpClientAsync();
        Check(pending.Wait(5000),"Proxy did not connect to the synthetic loopback upstream");
        TcpClient client=pending.Result;
        client.ReceiveTimeout=5000;client.SendTimeout=5000;client.NoDelay=true;streams.Add(client,client.GetStream());return client;
    }
    private static void Send(TcpClient client,byte[] bytes,int split)
    {
        NetworkStream stream=streams[client];
        stream.Write(bytes,0,split);stream.Write(bytes,split,bytes.Length-split);
    }
    private static void ReadExact(TcpClient client,byte[] expected)
    {
        var actual=new byte[expected.Length];int count=0;
        NetworkStream stream=streams[client];
        while(count<actual.Length)
        {
            int read=stream.Read(actual,count,actual.Length-count);
            Check(read>0,"Relay closed before forwarding the complete byte sequence");count+=read;
        }
        Check(actual.SequenceEqual(expected),"Relay changed the bytes sent across the connection");
    }
    private static void Closed(TcpClient client,string message)
    {
        try{Check(streams[client].ReadByte()==-1,message);}
        catch(IOException error)
        {
            var socket=error.InnerException as SocketException;
            if(socket==null||socket.SocketErrorCode==SocketError.TimedOut)throw;
        }
    }
    private static void Stop(PacketCaptureProxy proxy)
    {Check(proxy.StopAsync().Wait(5000),"Stopping capture left the accept loop or a relay pump running");}
    private static void RefusedStart(Action action,string message)
    {
        try{action();}
        catch(InvalidOperationException){return;}
        throw new Exception(message);
    }

    private static void TestRelay()
    {
        var upstream=new TcpListener(IPAddress.Loopback,0);upstream.Start();
        try
        {
            using(var proxy=new PacketCaptureProxy())
            {
                var log=new CaptureLog();proxy.PacketCaptured+=log.Add;
                proxy.Start(0,"127.0.0.1",((IPEndPoint)upstream.LocalEndpoint).Port);
                int listeningPort=proxy.ListeningPort;
                Check(proxy.IsRunning&&listeningPort>0,"Starting capture did not expose its allocated listening port");
                RefusedStart(()=>proxy.Start(0,"127.0.0.1",((IPEndPoint)upstream.LocalEndpoint).Port),"A running proxy could start a second listener");
                using(var first=Client(listeningPort))using(var firstServer=Accept(upstream))
                {
                    byte[] secrets=Bytes("synthetic-user\0#1synthetic-password\0ATsynthetic-ticket\0");
                    Send(first,secrets,3);ReadExact(firstServer,secrets);
                    WaitUntil(()=>log.Snapshot().Count(p=>p.FromClient)==3,"Pre-authentication client frames were not captured");
                    CapturedPacket[] credentials=log.Snapshot().Where(p=>p.FromClient).ToArray();
                    Check(credentials.All(p=>p.Redacted&&!p.Text.Contains("synthetic-")),"A client credential was exposed before authentication");
                    int firstId=credentials[0].ConnectionId;
                    Check(firstId>0&&credentials.All(p=>p.ConnectionId==firstId&&p.Timestamp.Kind==DateTimeKind.Utc),"Capture lost connection identity or UTC timestamps");
                    byte[] unfinished=Bytes("unfinished-");Send(first,unfinished,3);ReadExact(firstServer,unfinished);
                    byte[] auth=Bytes("ATK0\0");Send(firstServer,auth,2);ReadExact(first,auth);
                    log.Wait(p=>!p.FromClient&&p.ConnectionId==firstId&&p.Redacted,"Authentication response was not captured with its sensitive prefix masked");
                    Check(!log.Snapshot().Any(p=>p.Text=="ATK0"),"An authentication response prefix was exposed in capture");
                    byte[] finish=Bytes("frame\0");Send(first,finish,2);ReadExact(firstServer,finish);
                    WaitUntil(()=>log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId)==4,"A frame spanning authentication was not captured");
                    Check(log.Snapshot().Where(p=>p.FromClient&&p.ConnectionId==firstId).Last().Redacted&&!log.Snapshot().Any(p=>p.Text=="unfinished-frame"),"A frame that began before authentication became visible when its terminator arrived after authentication");
                    byte[] game=Bytes("GA001;100,260\0");Send(first,game,4);ReadExact(firstServer,game);
                    log.Wait(p=>p.FromClient&&p.ConnectionId==firstId&&!p.Redacted&&p.Text=="GA001;100,260","Authenticated game packets were not available for capture");
                    byte[] ticket=Bytes("ATsynthetic-later-ticket\0");Send(first,ticket,1);ReadExact(firstServer,ticket);
                    WaitUntil(()=>log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId)==6,"The post-authentication ticket was not captured");
                    Check(log.Snapshot().Where(p=>p.FromClient&&p.ConnectionId==firstId).Last().Redacted&&!log.Snapshot().Any(p=>p.Text.Contains("synthetic-later-ticket")),"A ticket was exposed after authentication");
                    byte[] response=Bytes("GM|+1;été\0GM|+2;next\0");Send(firstServer,response,8);ReadExact(first,response);
                    log.Wait(p=>!p.FromClient&&p.ConnectionId==firstId&&p.Text=="GM|+1;été","Server UTF8 frame was not decoded correctly");
                    log.Wait(p=>!p.FromClient&&p.ConnectionId==firstId&&p.Text=="GM|+2;next","Concatenated server frames were not decoded separately");
                    int serverBefore=log.Snapshot().Count(p=>!p.FromClient&&p.ConnectionId==firstId);
                    byte[] serverTickets=Bytes("AXKsynthetic-a\0AYKsynthetic-b\0");Send(firstServer,serverTickets,2);ReadExact(first,serverTickets);
                    WaitUntil(()=>log.Snapshot().Count(p=>!p.FromClient&&p.ConnectionId==firstId)==serverBefore+2,"Server ticket frames were not captured");
                    Check(log.Snapshot().Where(p=>!p.FromClient&&p.ConnectionId==firstId).Skip(serverBefore).All(p=>p.Redacted)&&!log.Snapshot().Any(p=>p.Text.Contains("synthetic-a")||p.Text.Contains("synthetic-b")),"Server ticket prefixes exposed authentication data");
                    int clientBefore=log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId);
                    byte[] sensitiveGame=Bytes("GAsecret-value\0GAline1\nline2\0");Send(first,sensitiveGame,5);ReadExact(firstServer,sensitiveGame);
                    WaitUntil(()=>log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId)==clientBefore+2,"Sensitive game frames were not captured");
                    Check(log.Snapshot().Where(p=>p.FromClient&&p.ConnectionId==firstId).Skip(clientBefore).All(p=>p.Redacted),"A secret marker or a multiline client frame bypassed masking after authentication");

                    // Authentication and EOF handling must remain independent between clients.
                    using(var second=Client(listeningPort))using(var secondServer=Accept(upstream))
                    {
                        byte[] initial=Bytes("GA002;3,4\0");Send(second,initial,1);ReadExact(secondServer,initial);
                        CapturedPacket secondCapture=log.Wait(p=>p.FromClient&&p.ConnectionId!=firstId,"A second client did not get a distinct capture session");
                        int secondId=secondCapture.ConnectionId;
                        Check(secondCapture.Redacted,"Authentication leaked from the first client into a second session");
                        byte[] secondAuth=Bytes("ALK\0");Send(secondServer,secondAuth,1);ReadExact(second,secondAuth);
                        log.Wait(p=>!p.FromClient&&p.ConnectionId==secondId&&p.Redacted,"Second authentication response was not captured with its sensitive prefix masked");
                        Check(!log.Snapshot().Any(p=>p.ConnectionId==secondId&&p.Text=="ALK"),"The second authentication response was exposed in capture");
                        byte[] secondGame=Bytes("GA003;5,6\0");Send(second,secondGame,2);ReadExact(secondServer,secondGame);
                        log.Wait(p=>p.FromClient&&p.ConnectionId==secondId&&!p.Redacted&&p.Text=="GA003;5,6","The second authenticated session could not capture game packets");
                        second.Client.Shutdown(SocketShutdown.Send);
                        Check(streams[secondServer].ReadByte()==-1,"A client send half-close was not forwarded to its upstream");
                        byte[] late=Bytes("GM|late-response\0");Send(secondServer,late,4);ReadExact(second,late);
                        secondServer.Client.Shutdown(SocketShutdown.Send);
                        Closed(second,"Return traffic did not finish after both directions half-closed");
                        log.Wait(p=>!p.FromClient&&p.ConnectionId==secondId&&p.Text=="GM|late-response","The return direction was discarded after a client half-close");
                    }

                    byte[] reset=Bytes("HCnew-challenge\0");Send(firstServer,reset,1);ReadExact(first,reset);
                    log.Wait(p=>!p.FromClient&&p.ConnectionId==firstId&&p.Text=="HCnew-challenge","Authentication reset was not captured");
                    int before=log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId);
                    byte[] afterReset=Bytes("GA004;7,8\0");Send(first,afterReset,2);ReadExact(firstServer,afterReset);
                    WaitUntil(()=>log.Snapshot().Count(p=>p.FromClient&&p.ConnectionId==firstId)==before+1,"Client frame after authentication reset was not captured");
                    Check(log.Snapshot().Where(p=>p.FromClient&&p.ConnectionId==firstId).Last().Redacted,"A new server challenge did not reset client credential masking");
                    Stop(proxy);
                    Check(!proxy.IsRunning,"A stopped proxy still reports running");
                    Closed(first,"Stop did not close the local client socket");
                    Closed(firstServer,"Stop did not close the upstream socket");
                }
                proxy.Start(listeningPort,"127.0.0.1",((IPEndPoint)upstream.LocalEndpoint).Port);
                Check(proxy.IsRunning&&proxy.ListeningPort==listeningPort,"Stopped proxy could not restart on its released port");
                using(var restarted=Client(listeningPort))using(var restartedServer=Accept(upstream))
                {
                    byte[] bytes=Bytes("restart-check\0");Send(restarted,bytes,2);ReadExact(restartedServer,bytes);
                }
                Stop(proxy);Stop(proxy);
                RefusedStart(()=>proxy.Start(listeningPort,"127.0.0.1",listeningPort),"A proxy could recursively relay to its own loopback port");
                Check(!proxy.IsRunning&&proxy.ListeningPort==0,"Rejecting a recursive relay left its listener running");
                RefusedStart(()=>proxy.Start(listeningPort,"localhost.",listeningPort),"A localhost alias bypassed the recursive-relay guard");
                Check(!proxy.IsRunning,"Rejecting a localhost recursive relay left its listener running");
            }
        }
        finally{upstream.Stop();}
    }

    private static void TestInvalidUtf8Relay()
    {
        var upstream=new TcpListener(IPAddress.Loopback,0);upstream.Start();
        try
        {
            using(var proxy=new PacketCaptureProxy())
            {
                var log=new CaptureLog();proxy.PacketCaptured+=log.Add;
                var statuses=new List<string>();var gate=new object();
                proxy.StatusChanged+=status=>{lock(gate)statuses.Add(status);};
                proxy.Start(0,"127.0.0.1",((IPEndPoint)upstream.LocalEndpoint).Port);
                using(var client=Client(proxy.ListeningPort))using(var server=Accept(upstream))
                {
                    byte[] invalid=new byte[]{255,0};Send(client,invalid,1);ReadExact(server,invalid);
                    WaitUntil(()=>{lock(gate)return statuses.Any(status=>status.IndexOf("capture client",StringComparison.OrdinalIgnoreCase)>=0&&status.IndexOf("désactivée",StringComparison.OrdinalIgnoreCase)>=0);},"Invalid UTF8 did not produce an explicit disabled-capture status");
                    byte[] subsequent=Bytes("GAafter-invalid\0");Send(client,subsequent,2);ReadExact(server,subsequent);
                    Check(!log.Snapshot().Any(p=>p.FromClient),"The disabled client decoder exposed a partial or resynchronized packet");
                    byte[] reply=Bytes("GMreply\0");Send(server,reply,3);ReadExact(client,reply);
                    log.Wait(p=>!p.FromClient&&p.Text=="GMreply","A client decode failure also disabled the independent server capture direction");
                    Stop(proxy);
                }
            }
        }
        finally{upstream.Stop();}
    }

    private static void TestUnavailableUpstream()
    {
        // Reserve a loopback port without listening so the failure cannot accidentally target
        // a newly allocated proxy listener or another concurrently opened synthetic server.
        using(var reserved=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp))
        {
            reserved.Bind(new IPEndPoint(IPAddress.Loopback,0));
            int missing=((IPEndPoint)reserved.LocalEndPoint).Port;
            using(var proxy=new PacketCaptureProxy())
            {
                var statuses=new List<string>();var gate=new object();
                proxy.StatusChanged+=status=>{lock(gate)statuses.Add(status);};
                proxy.Start(0,"127.0.0.1",missing);
                int before;lock(gate)before=statuses.Count;
                using(var client=Client(proxy.ListeningPort))
                {
                    Closed(client,"A failed upstream connection left the local client open");
                    WaitUntil(()=>{lock(gate)return statuses.Skip(before).Any(status=>status.IndexOf("connexion au serveur impossible",StringComparison.OrdinalIgnoreCase)>=0);},"An unavailable upstream produced no explicit error status");
                    Check(proxy.IsRunning,"A single upstream failure unexpectedly stopped the listener");
                }
                Stop(proxy);
            }
        }
    }
}
