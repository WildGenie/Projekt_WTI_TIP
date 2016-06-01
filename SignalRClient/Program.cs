﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Audio;
using Julas.Utils;
using Microsoft.AspNet.SignalR.Client;
using RegistrarChatApiClient;
using RegistrarWebApiClient;
using RegistrarWebApiClient.Models.Account;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var recorder = new AudioRecorder();
            var stream = recorder.GetAudioPacketStream(0, 8000/50);
            var player = new AudioPlayer();
            var playHandle = player.PlayAudioPacketStream(0, stream.PacketSource.Delay(TimeSpan.FromSeconds(1)));
            Thread.Sleep(10000000);
                
            return;
            Task.WaitAll(
                Enumerable.Range(1, 3)
                    .Select(x => Task.Factory.StartNew(() => Application.Run(new MainForm())))
                    .ToArray());
        }
    }
}
