using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KNX.NET.Log;

namespace KNX.NET;

internal class KnxReceiverTunneling : KnxReceiver
{
    private static readonly string ClassName = typeof(KnxReceiverTunneling).ToString();

    private UdpClient _udpClient;
    private IPEndPoint _localEndpoint;

    private readonly object _rxSequenceNumberLock = new();
    private byte _rxSequenceNumber;

    internal KnxReceiverTunneling(KnxConnection connection, UdpClient udpClient, IPEndPoint localEndpoint)
        : base(connection)
    {
        _udpClient = udpClient;
        _localEndpoint = localEndpoint;
    }

    private KnxConnectionTunneling KnxConnectionTunneling => (KnxConnectionTunneling)KnxConnection;

    public void SetClient(UdpClient client)
    {
        _udpClient = client;
    }

    public override void ReceiverThreadFlow()
    {
        try
        {
            while (true)
            {
                var datagram = _udpClient.Receive(ref _localEndpoint);
                ProcessDatagram(datagram);
            }
        }
        catch (SocketException e)
        {
            Logger.Error(ClassName, e);
            KnxConnectionTunneling.Disconnected();
        }
        catch (ObjectDisposedException)
        {
            // ignore, probably reconnect happening
        }
        catch (ThreadAbortException)
        {
            Thread.ResetAbort();
        }
    }

    private void ProcessDatagram(byte[] datagram)
    {
        try
        {
            switch (KnxHelper.GetServiceType(datagram))
            {
                case KnxHelper.ServiceType.CONNECT_RESPONSE:
                    ProcessConnectResponse(datagram);
                    break;
                case KnxHelper.ServiceType.CONNECTIONSTATE_RESPONSE:
                    ProcessConnectionStateResponse(datagram);
                    break;
                case KnxHelper.ServiceType.TUNNELLING_ACK:
                    ProcessTunnelingAck(datagram);
                    break;
                case KnxHelper.ServiceType.DISCONNECT_REQUEST:
                    ProcessDisconnectRequest(datagram);
                    break;
                case KnxHelper.ServiceType.DISCONNECT_RESPONSE:
                    ProcessDisconnectResponse(datagram);
                    break;
                case KnxHelper.ServiceType.TUNNELLING_REQUEST:
                    ProcessDatagramHeaders(datagram);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Error(ClassName, e);

            // ignore, missing warning information
        }
    }

    private void ProcessDatagramHeaders(byte[] datagram)
    {
        // HEADER
        // TODO: Might be interesting to take out these magic numbers for the datagram indices
        var knxDatagram = new KnxDatagram
        {
            HeaderLength = datagram[0],
            ProtocolVersion = datagram[1],
            ServiceType = new[] { datagram[2], datagram[3] },
            TotalLength = datagram[4] + datagram[5]
        };

        var channelId = datagram[7];
        if (channelId != KnxConnectionTunneling.ChannelId)
            return;

        var sequenceNumber = datagram[8];
        var process = true;
        lock (_rxSequenceNumberLock)
        {
            if (sequenceNumber <= _rxSequenceNumber)
                process = false;

            _rxSequenceNumber = sequenceNumber;
        }

        if (process)
        {
            // TODO: Magic number 10, what is it?
            var cemi = new byte[datagram.Length - 10];
            Array.Copy(datagram, 10, cemi, 0, datagram.Length - 10);

            ProcessCemi(knxDatagram, cemi);
        }

        ((KnxSenderTunneling)KnxConnectionTunneling.KnxSender).SendTunnelingAck(sequenceNumber);
    }

    private void ProcessDisconnectRequest(byte[] datagram)
    {
        KnxConnectionTunneling.DisconnectRequest();
    }

    private void ProcessDisconnectResponse(byte[] datagram)
    {
        var channelId = datagram[6];
        if (channelId != KnxConnectionTunneling.ChannelId)
            return;

        KnxConnectionTunneling.Disconnect();
    }

    private void ProcessTunnelingAck(byte[] datagram)
    {
        // do nothing
    }

    private void ProcessConnectionStateResponse(byte[] datagram)
    {
        // HEADER
        // 06 10 02 08 00 08 -- 48 21
        var knxDatagram = new KnxDatagram
        {
            HeaderLength = datagram[0],
            ProtocolVersion = datagram[1],
            ServiceType = new[] { datagram[2], datagram[3] },
            TotalLength = datagram[4] + datagram[5],
            ChannelId = datagram[6]
        };

        var response = datagram[7];

        if (response != 0x21)
            return;

        Logger.Debug(ClassName, "Received connection state response - No active connection with channel ID {0}", knxDatagram.ChannelId);

        KnxConnection.Disconnect();
    }

    private void ProcessConnectResponse(byte[] datagram)
    {
        // HEADER
        var knxDatagram = new KnxDatagram
        {
            HeaderLength = datagram[0],
            ProtocolVersion = datagram[1],
            ServiceType = new[] { datagram[2], datagram[3] },
            TotalLength = datagram[4] + datagram[5],
            ChannelId = datagram[6],
            Status = datagram[7]
        };

        if (knxDatagram.ChannelId == 0x00 && knxDatagram.Status == 0x24)
        {
            Logger.Info(ClassName, "KNX.NET received connect response - No more connections available");                
        }
        else
        {
            KnxConnectionTunneling.ChannelId = knxDatagram.ChannelId;
            KnxConnectionTunneling.ResetSequenceNumber();

            KnxConnectionTunneling.Connected();
        }
    }
}