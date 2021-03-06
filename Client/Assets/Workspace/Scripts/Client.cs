﻿using UnityEngine;
using UnityEngine.SceneManagement;
using ENet;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Valk.Networking
{
    class Client : MonoBehaviour
    {
        private const int MAX_FRAMES = 30;
        private const int TIMEOUT_SEND = 1000 * 5;
        private const int TIMEOUT_RECEIVE = 1000 * 30;
        private const byte CHANNEL_ID = 0;
        private const int POSITION_UPDATE_DELAY = 100;

        private const string IP = "127.0.0.1";
        private const ushort PORT = 7777;

        public static Network Network;
        private static Peer peer;
        private static Host client;

        public float speed = 25; //moveSpeed

        private static GameObject oClientPrefab;
        private Dictionary<uint, GameObject> clients;

        private GameObject clientGo;
        private Rigidbody2D clientGoRb;
        private bool inGame = false;

        private void Start()
        {
            oClientPrefab = Resources.Load("Prefabs/Client") as GameObject;
            clients = new Dictionary<uint, GameObject>();

#if !UNITY_WEBGL
            Application.targetFrameRate = MAX_FRAMES;
#endif

            Application.runInBackground = true;
            DontDestroyOnLoad(gameObject);
        }

        public static void Connect()
        {
            ENet.Library.Initialize();

            client = new Host();

            var address = new Address();
            address.SetHost(IP);
            address.Port = PORT;

            client.Create();

            Debug.Log("Connecting...");
            peer = client.Connect(address);
            peer.Timeout(0, TIMEOUT_RECEIVE, TIMEOUT_SEND);
            Network = new Network(CHANNEL_ID, peer);
        }

        private void Update()
        {
            if (!inGame)
                return;

            if (clientGoRb == null)
                return;

            clientGoRb.AddForce(new Vector2(Input.GetAxis("Horizontal") * speed, Input.GetAxis("Vertical") * speed));
        }

        private void FixedUpdate()
        {
            if (client == null)
                return;

            UpdateENet();
        }

        private void UpdateENet()
        {
            ENet.Event netEvent;

            if (client.CheckEvents(out netEvent) <= 0)
            {
                if (client.Service(15, out netEvent) <= 0)
                    return;
            }

            switch (netEvent.Type)
            {
                case ENet.EventType.None:
                    break;

                case ENet.EventType.Connect:
                    Debug.Log("Client connected to server");
                    break;

                case ENet.EventType.Disconnect:
                    Debug.Log("Client disconnected from server");
                    break;

                case ENet.EventType.Timeout:
                    Debug.Log("Client connection timeout");
                    Destroy(gameObject);
                    SceneManager.LoadScene("Main Menu");
                    break;

                case ENet.EventType.Receive:
                    //Debug.Log("Packet received from server - Channel ID: " + netEvent.ChannelID + ", Data length: " + netEvent.Packet.Length);
                    HandlePacket(ref netEvent);
                    netEvent.Packet.Dispose();
                    break;
            }
        }

        private void HandlePacket(ref ENet.Event netEvent)
        {
            try
            {
                var readBuffer = new byte[1024];
                var readStream = new MemoryStream(readBuffer);
                var reader = new BinaryReader(readStream);

                readStream.Position = 0;
                netEvent.Packet.CopyTo(readBuffer);
                var packetID = (PacketType)reader.ReadByte();

                if (packetID == PacketType.ServerCreateAccountDenied)
                {
                    Debug.Log("Account denied");
                    var reason = reader.ReadString();
                    Debug.Log("Reason: " + reason);
                    UIAccountManagement.UpdateText($"Account Denied: {reason}");
                }

                if (packetID == PacketType.ServerCreateAccountAccepted)
                {
                    Debug.Log("Account created");
                    UIAccountManagement.UpdateText($"Account Created");
                }

                if (packetID == PacketType.ServerLoginAccepted)
                {
                    Debug.Log("Login accepted");
                    UIAccountManagement.UpdateText($"Logging in...");
                    StartCoroutine(ASyncLoadGame());
                }

                if (packetID == PacketType.ServerLoginDenied)
                {
                    Debug.Log("Login denied");
                    var reason = reader.ReadString();
                    Debug.Log("Reason: " + reason);
                    UIAccountManagement.UpdateText($"Login Denied: {reason}");
                }

                if (packetID == PacketType.ServerPositionUpdate)
                {
                    //Debug.Log("Received Server Position Update");
                    var players = reader.ReadInt32();
                    for (int i = 0; i < players; i++)
                    {
                        var id = reader.ReadUInt32();
                        var x = reader.ReadSingle();
                        var y = reader.ReadSingle();
                        //Debug.Log($"ID: {id}, X: {x}, Y: {y}");
                        if (clients.ContainsKey(id)) 
                        {
                            Debug.Log("Updated position of oClient");

                            if (clients[id] == null) 
                            {
                                clients.Remove(id);
                            } else {
                                clients[id].transform.position = new Vector2(x, y);
                            }
                        } else {
                            Debug.Log("Added new oClient");
                            GameObject oClient = Instantiate(oClientPrefab, new Vector3(x, 0, y), Quaternion.identity);
                            clients.Add(id, oClient);
                        }
                    }
                }
            }

            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine($"Received packet but buffer was too long. {netEvent.Packet.Length}");
            }
        }

        private IEnumerator ASyncLoadGame()
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Main");
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            Spawn();
        }

        private void Spawn()
        {
            GameObject clientPrefab = Resources.Load("Prefabs/Client") as GameObject;
            clientGo = Instantiate(clientPrefab, Vector3.zero, Quaternion.identity);
            clientGoRb = clientGo.GetComponent<Rigidbody2D>();

            inGame = true;

            StartCoroutine(SendPositionUpdates());
        }

        private IEnumerator SendPositionUpdates()
        {
            while (inGame)
            {
                Vector3 pos = clientGo.transform.position;
                Network.Send(PacketType.ClientPositionUpdate, PacketFlags.None, pos.x, pos.y);

                yield return new WaitForSeconds(POSITION_UPDATE_DELAY / 1000);
            }
        }

        public static bool IsConnected()
        {
            return peer.State == ENet.PeerState.Connected;
        }

        private void OnApplicationQuit()
        {
            Debug.Log("Disposing client");
            client.Dispose();
            ENet.Library.Deinitialize();
        }
    }
}
