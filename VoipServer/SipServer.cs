﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Data;
using System.Linq;
using Common;
using Ozeki.Network;
using Ozeki.VoIP;

namespace VoipServer
{
    public class SipServer : PBXBase
    {
        private readonly Dictionary<string, NpgsqlTypes.NpgsqlDateTime> CallStart = new Dictionary<string, NpgsqlTypes.NpgsqlDateTime>();

        private readonly string _localAddress;
        private readonly Database _database;
        private readonly int _localPort;
        private readonly ISessionCache _sessionCache;

        public SipServer(string localAddress, int localPort, int minRtpPort, int maxRtpPort, string dbConnectionString, ISessionCache sessionCache)
            : base(minRtpPort, maxRtpPort)
        {
            _localAddress = localAddress;
            _localPort = localPort;
            _sessionCache = sessionCache;
            _database = new Database(dbConnectionString);
        }

        protected override void OnSIPMessageReceived(SIPMessageInfo sipMessage, string userName, Endpoint remoteEndpoint)
        {
            base.OnSIPMessageReceived(sipMessage, userName, remoteEndpoint);
        }

        protected override void OnSIPMessageSent(SIPMessageInfo sipMessage, string userName, Endpoint remoteEndpoint)
        {
            base.OnSIPMessageSent(sipMessage, userName, remoteEndpoint);
        }

        protected override void OnStart()
        {
            SetListenPort(_localAddress, _localPort, Ozeki.Network.TransportType.Udp);
            base.OnStart();
        }

        protected override AuthenticationResult OnAuthenticationRequest(ISIPExtension extension, RequestAuthenticationInfo authInfo)
        {
            Console.WriteLine("Authentication request received from: " + authInfo.From.UserName);

            var success = _sessionCache.VerifySession(authInfo.From.UserName, authInfo.AuthName);
            
            if (success)
            {
                Console.WriteLine("Authentication accepted. UserName: " + extension.ExtensionID);
            }
            else
            {
                Console.WriteLine("Authentication denied. UserName: " + extension.ExtensionID);
            }

            return new AuthenticationResult(success);
        }

        protected override RegisterResult OnRegisterReceived(ISIPExtension extension, SIPAddress from, int expires)
        {
            Console.WriteLine("Register received from: " + extension.ExtensionID);
            //Save location to DB
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string received = "sip:" + extension.InstanceInfo.Transport.RemoteEndPoint.Address.ToString() + ":" + extension.InstanceInfo.Transport.RemoteEndPoint.Port.ToString();
            double seconds = expires;
            DateTime expiredate = DateTime.Now.AddSeconds(seconds);
            parameters.Add("@username", extension.AuthName);
            parameters.Add("@domain", from.Address);
            parameters.Add("@contact", extension.InstanceInfo.Contact.ToString());
            parameters.Add("@received", received);
            parameters.Add("@expires", expiredate);

            string command = "UPDATE registrar_table  SET domain = @domain, contact = @contact, received = @received, expires = @expires WHERE username = @username;"
                      + "insert into registrar_table (username, domain, contact, received, expires)"
                      + " select @username, @domain, @contact, @received, @expires where not exists (select from registrar_table where username = @username);";
            _database.WriteDataToDB(command, parameters);
            return base.OnRegisterReceived(extension, from, expires);
        }

        protected override void OnUnregisterReceived(ISIPExtension extension)
        {
            Console.WriteLine("Unregister received from: " + extension.ExtensionID);
            base.OnUnregisterReceived(extension);
        }

        protected override void OnCallRequestReceived(ISessionCall call)
        {
            Console.WriteLine("Call request received. Caller: " + call.DialInfo.CallerID + " callee: " + call.DialInfo.Dialed);
            call.CallStateChanged += Call_CallStateChanged;
            base.OnCallRequestReceived(call);
        }

        private void Call_CallStateChanged(object sender, CallStateChangedArgs e)
        {
            SIPCall call1 = (SIPCall)sender;
            if (e.State == CallState.Answered)
            {
                CallStart.Add(call1.CallID, NpgsqlTypes.NpgsqlDateTime.Now);
            }

            if (e.State == CallState.Completed)
            {
                NpgsqlTypes.NpgsqlDateTime startValue;
                if (CallStart.TryGetValue(call1.CallID, out startValue))
                {
                    Dictionary<string, object> parameters = new Dictionary<string, object>();
                    parameters.Add("@calling_user_id", call1.CallerIDAsCaller);
                    parameters.Add("@called_user_id", call1.DialInfo.Dialed);
                    parameters.Add("@source_ip", call1.BasicInfo.Owner.InstanceInfo.Transport.RemoteEndPoint.ToString());
                    parameters.Add("@start_billing", startValue);
                    parameters.Add("@stop_billing", NpgsqlTypes.NpgsqlDateTime.Now);
                    parameters.Add("@call_id", call1.CallID);
                    string command = "insert into billing (calling_user_id, called_user_id, source_ip, start_billing, stop_billing, call_id)"
                                 + "values(@calling_user_id, @called_user_id, @source_ip, @start_billing, @stop_billing, @call_id)";

                    _database.WriteDataToDB(command, parameters);

                    CallStart.Remove(call1.CallID);
                }
            }            
        }
    }
}
