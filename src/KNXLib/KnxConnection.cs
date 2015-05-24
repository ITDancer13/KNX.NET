﻿using System;
using System.Net;
using System.Text;
using KNXLib.DPT;
using KNXLib.Exceptions;

namespace KNXLib
{
    /// <summary>
    /// 
    /// </summary>
    public abstract class KnxConnection
    {
        private static readonly string ClassName = typeof(KnxConnection).ToString();

        /// <summary>
        /// 
        /// </summary>
        public delegate void KnxConnected();
        /// <summary>
        /// 
        /// </summary>
        public KnxConnected KnxConnectedDelegate = null;

        /// <summary>
        /// 
        /// </summary>
        public delegate void KnxDisconnected();
        /// <summary>
        /// 
        /// </summary>
        public KnxDisconnected KnxDisconnectedDelegate = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="state"></param>
        public delegate void KnxEvent(string address, string state);
        /// <summary>
        /// 
        /// </summary>
        public KnxEvent KnxEventDelegate = (address, state) => { };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="state"></param>
        public delegate void KnxStatus(string address, string state);
        /// <summary>
        /// 
        /// </summary>
        public KnxStatus KnxStatusDelegate = (address, state) => { };

        private readonly KnxLockManager _lockManager = new KnxLockManager();

        /// <summary>
        /// Create a new KNX Connection to specified host and port
        /// </summary>
        /// <param name="host">Host to connect</param>
        /// <param name="port">Port to use</param>
        protected KnxConnection(string host, int port)
        {
            ConnectionConfiguration = new KnxConnectionConfiguration(host, port);

            ActionMessageCode = 0x00;
            ThreeLevelGroupAddressing = true;
            Debug = false;
        }

        internal KnxConnectionConfiguration ConnectionConfiguration { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        protected IPEndPoint RemoteEndpoint
        {
            get { return ConnectionConfiguration.EndPoint; }
        }

        internal KnxReceiver KnxReceiver { get; set; }

        internal KnxSender KnxSender { get; set; }

        /// <summary>
        /// Configure this paramenter based on the KNX installation:
        ///  - true: 3-level group address: main/middle/sub(5/3/8 bits)
        ///  - false: 2-level group address: main/sub (5/11 bits)
        /// Default: true
        /// </summary>
        public bool ThreeLevelGroupAddressing { get; set; }

        /// <summary>
        /// Set to true to receive debug log messages
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// Some KNX Routers/Interfaces might need this parameter defined, some of them need this to be 0x29.
        /// Default: 0x00
        /// </summary>
        public byte ActionMessageCode { get; set; }

        /// <summary>
        /// Start the connection
        /// </summary>
        public abstract void Connect();

        /// <summary>
        /// Stop the connection
        /// </summary>
        public abstract void Disconnect();

        /// <summary>
        /// Event triggered by implementing class to notify that the connection has been established
        /// </summary>
        internal virtual void Connected()
        {
            try
            {
                if (KnxConnectedDelegate != null)
                    KnxConnectedDelegate();
            }
            catch
            {
                //ignore
            }

            Log.Logger.Info(ClassName, "KNX is connected");
            Log.Logger.Debug(ClassName, "Unlocking send - {0} free locks", _lockManager.LockCount);

            _lockManager.UnlockConnection();
        }

        /// <summary>
        /// Event triggered by implementing class to notify that the connection has been established
        /// </summary>
        internal virtual void Disconnected()
        {
            _lockManager.LockConnection();

            try
            {
                if (KnxDisconnectedDelegate != null)
                    KnxDisconnectedDelegate();
            }
            catch
            {
                //ignore
            }

            Log.Logger.Debug(ClassName, "KNX is disconnected");
            Log.Logger.Debug(ClassName, "Send locked - {0} free locks", _lockManager.LockCount);
        }

        internal void Event(string address, string state)
        {
            try
            {
                KnxEventDelegate(address, state);
            }
            catch
            {
                //ignore
            }

            Log.Logger.Debug(ClassName, "Device {0} sent event {1}", address, state);
        }

        internal void Status(string address, string state)
        {
            try
            {
                KnxStatusDelegate(address, state);
            }
            catch
            {
                //ignore
            }

            Log.Logger.Debug(ClassName, "Device {0} has status {1}", address, state);
        }

        /// <summary>
        /// Send a bit value as data to specified address
        /// </summary>
        /// <param name="address">KNX Address</param>
        /// <param name="data">Bit value</param>
        /// <exception cref="InvalidKnxDataException"></exception>
        public void Action(string address, bool data)
        {
            byte[] val;

            try
            {
                val = new[] { Convert.ToByte(data) };
            }
            catch
            {
                throw new InvalidKnxDataException(data.ToString());
            }

            if (val == null)
                throw new InvalidKnxDataException(data.ToString());

            Action(address, val);
        }

        /// <summary>
        /// Send a string value as data to specified address
        /// </summary>
        /// <param name="address">KNX Address</param>
        /// <param name="data">String value</param>
        /// <exception cref="InvalidKnxDataException"></exception>
        public void Action(string address, string data)
        {
            byte[] val;
            try
            {
                val = Encoding.ASCII.GetBytes(data);
            }
            catch
            {
                throw new InvalidKnxDataException(data);
            }

            if (val == null)
                throw new InvalidKnxDataException(data);

            Action(address, val);
        }

        /// <summary>
        /// 
        /// Send an int value as data to specified address
        /// </summary>
        /// <param name="address">KNX Address</param>
        /// <param name="data">Int value</param>
        /// <exception cref="InvalidKnxDataException"></exception>
        public void Action(string address, int data)
        {
            var val = new byte[2];
            if (data <= 255)
            {
                val[0] = 0x00;
                val[1] = (byte)data;
            }
            else if (data <= 65535)
            {
                val[0] = (byte)data;
                val[1] = (byte)(data >> 8);
            }
            else
            {
                // allowing only positive integers less than 65535 (2 bytes), maybe it is incorrect...???
                throw new InvalidKnxDataException(data.ToString());
            }

            if (val == null)
                throw new InvalidKnxDataException(data.ToString());

            Action(address, val);
        }

        /// <summary>
        /// Send a byte value as data to specified address
        /// </summary>
        /// <param name="address">KNX Address</param>
        /// <param name="data">byte value</param>
        public void Action(string address, byte data)
        {
            Action(address, new byte[] { 0x00, data });
        }

        /// <summary>
        /// Send a byte array value as data to specified address
        /// </summary>
        /// <param name="address">KNX Address</param>
        /// <param name="data">Byte array value</param>
        public void Action(string address, byte[] data)
        {
            Log.Logger.Debug(ClassName, "Sending {0} to {1}.", data, address);

            _lockManager.PerformLockedOperation(() => KnxSender.Action(address, data));

            Log.Logger.Debug(ClassName, "Sent {0} to {1}.", data, address);
        }

        // TODO: It would be good to make a type for address, to make sure not any random string can be passed in
        /// <summary>
        /// Send a request to KNX asking for specified address current status
        /// </summary>
        /// <param name="address"></param>
        public void RequestStatus(string address)
        {
            Log.Logger.Debug(ClassName, "Sending request status to {0}.", address);

            _lockManager.PerformLockedOperation(() => KnxSender.RequestStatus(address));

            Log.Logger.Debug(ClassName, "Sent request status to {0}.", address);
        }

        /// <summary>
        /// Convert a value received from KNX using datapoint translator, e.g.,
        /// get a temperature value in Celsius
        /// </summary>
        /// <param name="type">Datapoint type, e.g.: 9.001</param>
        /// <param name="data">Data to convert</param>
        /// <returns></returns>
        public object FromDataPoint(string type, string data)
        {
            return DataPointTranslator.Instance.FromDataPoint(type, data);
        }

        /// <summary>
        /// Convert a value received from KNX using datapoint translator, e.g.,
        /// get a temperature value in Celsius
        /// </summary>
        /// <param name="type">Datapoint type, e.g.: 9.001</param>
        /// <param name="data">Data to convert</param>
        /// <returns></returns>
        public object FromDataPoint(string type, byte[] data)
        {
            return DataPointTranslator.Instance.FromDataPoint(type, data);
        }

        /// <summary>
        /// Convert a value to send to KNX using datapoint translator, e.g.,
        /// get a temperature value in Celsius in a byte representation
        /// </summary>
        /// <param name="type">Datapoint type, e.g.: 9.001</param>
        /// <param name="value">Value to convert</param>
        /// <returns></returns>
        public byte[] ToDataPoint(string type, string value)
        {
            return DataPointTranslator.Instance.ToDataPoint(type, value);
        }

        /// <summary>
        /// Convert a value to send to KNX using datapoint translator, e.g.,
        /// get a temperature value in Celsius in a byte representation
        /// </summary>
        /// <param name="type">Datapoint type, e.g.: 9.001</param>
        /// <param name="value">Value to convert</param>
        /// <returns></returns>
        public byte[] ToDataPoint(string type, object value)
        {
            return DataPointTranslator.Instance.ToDataPoint(type, value);
        }
    }
}