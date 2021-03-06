﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Net;
using Library.Net.Amoeba;
using Library.Net.Connections;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Amoeba")]
    public class Test_Library_Net_Amoeba
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        private const int MaxReceiveCount = 32 * 1024 * 1024;

        [Test]
        public void Test_AmoebaConverter_Node()
        {
            Node node = null;

            {
                var id = new byte[32];
                _random.NextBytes(id);
                var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                node = new Node(id, uris);
            }

            var stringNode = AmoebaConverter.ToNodeString(node);
            var node2 = AmoebaConverter.FromNodeString(stringNode);

            Assert.AreEqual(node, node2, "AmoebaConverter #1");
        }

        [Test]
        public void Test_AmoebaConverter_Seed()
        {
            var key = new Key(HashAlgorithm.Sha256, new byte[32]);
            var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
            var seed = new Seed(metadata);
            seed.Name = "aaaa.zip";
            seed.Keywords.AddRange(new KeywordCollection
            {
                "bbbb",
                "cccc",
                "dddd",
            });
            seed.CreationTime = DateTime.Now;
            seed.Length = 10000;

            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);
            seed.CreateCertificate(digitalSignature);

            var stringSeed = AmoebaConverter.ToSeedString(seed);
            var seed2 = AmoebaConverter.FromSeedString(stringSeed);

            Assert.AreEqual(seed, seed2, "AmoebaConverter #2");
        }

        [Test]
        public void Test_AmoebaConverter_Box()
        {
            var key = new Key(HashAlgorithm.Sha256, new byte[32]);
            var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
            var seed = new Seed(metadata);
            seed.Name = "aaaa.zip";
            seed.Keywords.AddRange(new KeywordCollection
                {
                    "bbbb",
                    "cccc",
                    "dddd",
                });
            seed.CreationTime = DateTime.Now;
            seed.Length = 10000;

            var box = new Box();
            box.Name = "Box";
            box.Seeds.Add(seed);
            box.Boxes.Add(new Box() { Name = "Box" });

            Box box2;

            using (var streamBox = AmoebaConverter.ToBoxStream(box))
            {
                box2 = AmoebaConverter.FromBoxStream(streamBox);
            }

            Assert.AreEqual(box, box2, "AmoebaConverter #3");
        }

        [Test]
        public void Test_Node()
        {
            Node node = null;

            {
                var id = new byte[32];
                _random.NextBytes(id);
                var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                node = new Node(id, uris);
            }

            Node node2;

            using (var nodeStream = node.Export(_bufferManager))
            {
                node2 = Node.Import(nodeStream, _bufferManager);
            }

            Assert.AreEqual(node, node2, "Node #1");
        }

        [Test]
        public void Test_Key()
        {
            Key key = null;

            {
                var id = new byte[32];
                _random.NextBytes(id);

                key = new Key(HashAlgorithm.Sha256, id);
            }

            Key key2;

            using (var keyStream = key.Export(_bufferManager))
            {
                key2 = Key.Import(keyStream, _bufferManager);
            }

            Assert.AreEqual(key, key2, "Key #1");
        }

        [Test]
        public void Test_Seed()
        {
            foreach (var a in new DigitalSignatureAlgorithm[] { DigitalSignatureAlgorithm.Rsa2048_Sha256, DigitalSignatureAlgorithm.Rsa2048_Sha256 })
            {
                var key = new Key(HashAlgorithm.Sha256, new byte[32]);
                var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
                var seed = new Seed(metadata);
                seed.Name = "aaaa.zip";
                seed.Keywords.AddRange(new KeywordCollection
                {
                    "bbbb",
                    "cccc",
                    "dddd",
                });
                seed.CreationTime = DateTime.Now;
                seed.Length = 10000;

                DigitalSignature digitalSignature = new DigitalSignature("123", a);
                seed.CreateCertificate(digitalSignature);

                var seed2 = seed.Clone();

                Assert.AreEqual(seed, seed2, "Seed #1");

                Seed seed3;

                using (var seedStream = seed.Export(_bufferManager))
                {
                    seed3 = Seed.Import(seedStream, _bufferManager);
                }

                Assert.AreEqual(seed, seed3, "Seed #2");
                Assert.IsTrue(seed3.VerifyCertificate(), "Seed #3");
            }
        }

        [Test]
        public void Test_Box()
        {
            var key = new Key(HashAlgorithm.Sha256, new byte[32]);
            var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
            var seed = new Seed(metadata);
            seed.Name = "aaaa.zip";
            seed.Keywords.AddRange(new KeywordCollection
                {
                    "bbbb",
                    "cccc",
                    "dddd",
                });
            seed.CreationTime = DateTime.Now;
            seed.Length = 10000;

            var box = new Box();
            box.Name = "Box";
            box.Seeds.Add(seed);
            box.Boxes.Add(new Box() { Name = "Box" });

            var box2 = box.Clone();

            Assert.AreEqual(box, box2, "Box #1");

            Box box3;

            using (var boxStream = box.Export(_bufferManager))
            {
                box3 = Box.Import(boxStream, _bufferManager);
            }

            Assert.AreEqual(box, box3, "Box #2");

            {
                var parentBox = new Box();
                var childBox = parentBox;

                for (int i = 0; i < 256; i++)
                {
                    var tempBox = new Box();
                    childBox.Boxes.Add(tempBox);
                    childBox = tempBox;
                }

                using (var binaryBox = parentBox.Export(_bufferManager))
                {

                }
            }

            {
                var parentBox = new Box();
                var childBox = parentBox;

                for (int i = 0; i < 256 + 1; i++)
                {
                    var tempBox = new Box();
                    childBox.Boxes.Add(tempBox);
                    childBox = tempBox;
                }

                Assert.Throws<ArgumentException>(() =>
                {
                    using (var binaryBox = parentBox.Export(_bufferManager))
                    {

                    }
                });
            }
        }

        [Test]
        public void Test_Link()
        {
            DigitalSignature digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

            var signatures = new List<string>();
            signatures.Add(digitalSignature.ToString());
            signatures.Add(digitalSignature.ToString());
            signatures.Add(digitalSignature.ToString());

            var link = new Link(signatures, signatures);

            Link link2;

            using (var linkStream = link.Export(_bufferManager))
            {
                link2 = Link.Import(linkStream, _bufferManager);
            }

            Assert.AreEqual(link, link2, "Link #1");
        }

        [Test]
        public void Test_Store()
        {
            var store = new Store();
            store.Boxes.Add(new Box() { Name = "Box" });

            var store2 = store.Clone();

            Assert.AreEqual(store, store2, "Store #1");

            Store store3;

            using (var storeStream = store.Export(_bufferManager))
            {
                store3 = Store.Import(storeStream, _bufferManager);
            }

            Assert.AreEqual(store, store3, "Store #2");
        }

        [Test]
        public void Test_BitmapManager()
        {
            using (BitmapManager bitmapManager = new BitmapManager("bitmap", _bufferManager))
            {
                bitmapManager.SetLength(1024 * 256);

                Random random_a, random_b;

                {
                    var seed = _random.Next();

                    random_a = new Random(seed);
                    random_b = new Random(seed);
                }

                {
                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        bitmapManager.Set(p, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(bitmapManager.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (bitmapManager.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }

                {
                    for (int i = 0; i < 1024 * 256; i++)
                    {
                        bitmapManager.Set(i, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        bitmapManager.Set(p, false);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(!bitmapManager.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (!bitmapManager.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }
            }
        }

        [Test]
        public void Test_ConnectionManager()
        {
            for (int i = 0; i < 4; i++)
            {
                TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
                listener.Start();
                var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

                TcpClient client = new TcpClient();
                client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

                var server = listener.EndAcceptSocket(listenerAcceptSocket);
                listener.Stop();

                var tcpClient = new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Amoeba.MaxReceiveCount, _bufferManager);
                var tcpServer = new BaseConnection(new SocketCap(server), null, Test_Library_Net_Amoeba.MaxReceiveCount, _bufferManager);

                List<ConnectionManager> connectionManagers = new List<ConnectionManager>();

                {
                    ConnectionManager serverConnectionManager;
                    ConnectionManager clientConnectionManager;

                    Node serverNode = null;
                    Node clientNode = null;

                    byte[] serverSessionId = null;
                    byte[] clientSessionId = null;

                    {
                        var id = new byte[32];
                        _random.NextBytes(id);
                        var uris = new string[] { "tcp:localhost:9000", "tcp:localhost:9001", "tcp:localhost:9002" };

                        serverNode = new Node(id, uris);
                    }

                    {
                        var id = new byte[32];
                        _random.NextBytes(id);
                        var uris = new string[] { "tcp:localhost:9000", "tcp:localhost:9001", "tcp:localhost:9002" };

                        clientNode = new Node(id, uris);
                    }

                    {
                        serverSessionId = new byte[32];
                        _random.NextBytes(serverSessionId);
                    }

                    {
                        clientSessionId = new byte[32];
                        _random.NextBytes(clientSessionId);
                    }

                    serverConnectionManager = new ConnectionManager(tcpServer, serverSessionId, serverNode, ConnectDirection.In, _bufferManager);
                    clientConnectionManager = new ConnectionManager(tcpClient, clientSessionId, clientNode, ConnectDirection.Out, _bufferManager);

                    var serverTask = Task.Run(() => serverConnectionManager.Connect());
                    var clientTask = Task.Run(() => clientConnectionManager.Connect());

                    Task.WaitAll(serverTask, clientTask);

                    Assert.IsTrue(CollectionUtils.Equals(serverConnectionManager.SesstionId, clientSessionId), "ConnectionManager SessionId #1");
                    Assert.IsTrue(CollectionUtils.Equals(clientConnectionManager.SesstionId, serverSessionId), "ConnectionManager SessionId #2");

                    Assert.AreEqual(serverConnectionManager.Node, clientNode, "ConnectionManager Node #1");
                    Assert.AreEqual(clientConnectionManager.Node, serverNode, "ConnectionManager Node #2");

                    connectionManagers.Add(serverConnectionManager);
                    connectionManagers.Add(clientConnectionManager);
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullNodesEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullNodesEvent += (object sender, PullNodesEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    List<Node> nodes = new List<Node>();

                    for (int j = 0; j < 32; j++)
                    {
                        Node node = null;

                        {
                            var id = new byte[32];
                            _random.NextBytes(id);
                            var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                            node = new Node(id, uris);
                        }

                        nodes.Add(node);
                    }

                    senderConnection.PushNodes(nodes);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(nodes, item.Nodes), "ConnectionManager #1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlocksLinkEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlocksLinkEvent += (object sender, PullBlocksLinkEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var keys = new List<Key>();

                    for (int j = 0; j < 32; j++)
                    {
                        Key key = null;

                        {
                            var id = new byte[32];
                            _random.NextBytes(id);

                            key = new Key(HashAlgorithm.Sha256, id);
                        }

                        keys.Add(key);
                    }

                    senderConnection.PushBlocksLink(keys);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(keys, item.Keys), "ConnectionManager #2");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlocksRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlocksRequestEvent += (object sender, PullBlocksRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var keys = new List<Key>();

                    for (int j = 0; j < 32; j++)
                    {
                        Key key = null;

                        {
                            var id = new byte[32];
                            _random.NextBytes(id);

                            key = new Key(HashAlgorithm.Sha256, id);
                        }

                        keys.Add(key);
                    }

                    senderConnection.PushBlocksRequest(keys);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(keys, item.Keys), "ConnectionManager #3");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBlockEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBlockEvent += (object sender, PullBlockEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var buffer = _bufferManager.TakeBuffer(1024 * 1024 * 8);
                    var key = new Key(HashAlgorithm.Sha256, Sha256.ComputeHash(buffer));

                    senderConnection.PushBlock(key, new ArraySegment<byte>(buffer, 0, 1024 * 1024 * 4));

                    var item = queue.Dequeue();
                    Assert.AreEqual(key, item.Key, "ConnectionManager #4.1");
                    Assert.IsTrue(CollectionUtils.Equals(buffer, 0, item.Value.Array, item.Value.Offset, 1024 * 1024 * 4), "ConnectionManager #4.2");

                    _bufferManager.ReturnBuffer(buffer);
                    _bufferManager.ReturnBuffer(item.Value.Array);
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBroadcastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBroadcastMetadatasRequestEvent += (object sender, PullBroadcastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

                    var signatures = new SignatureCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        signatures.Add(digitalSignature.ToString());
                    }

                    senderConnection.PushBroadcastMetadatasRequest(signatures);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(signatures, item.Signatures), "ConnectionManager #5.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullBroadcastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullBroadcastMetadatasEvent += (object sender, PullBroadcastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

                    var metadatas1 = new List<BroadcastMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var key = new Key(HashAlgorithm.Sha256, new byte[32]);
                        var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
                        var broadcastMetadata = new BroadcastMetadata("Type", DateTime.UtcNow, metadata, digitalSignature);

                        metadatas1.Add(broadcastMetadata);
                    }

                    senderConnection.PushBroadcastMetadatas(metadatas1);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(metadatas1, item.BroadcastMetadatas), "ConnectionManager #6.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullUnicastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullUnicastMetadatasRequestEvent += (object sender, PullUnicastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

                    var signatures = new SignatureCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        signatures.Add(digitalSignature.ToString());
                    }

                    senderConnection.PushUnicastMetadatasRequest(signatures);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(signatures, item.Signatures), "ConnectionManager #7.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullUnicastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullUnicastMetadatasEvent += (object sender, PullUnicastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

                    var metadatas1 = new List<UnicastMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var key = new Key(HashAlgorithm.Sha256, new byte[32]);
                        var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
                        var unicastMetadata = new UnicastMetadata("Type", digitalSignature.ToString(), DateTime.UtcNow, metadata, digitalSignature);

                        metadatas1.Add(unicastMetadata);
                    }

                    senderConnection.PushUnicastMetadatas(metadatas1);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(metadatas1, item.UnicastMetadatas), "ConnectionManager #8.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullMulticastMetadatasRequestEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullMulticastMetadatasRequestEvent += (object sender, PullMulticastMetadatasRequestEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var tags = new TagCollection();

                    for (int j = 0; j < 32; j++)
                    {
                        var id = new byte[32];
                        _random.NextBytes(id);

                        tags.Add(new Tag(RandomString.GetValue(256), id));
                    }

                    senderConnection.PushMulticastMetadatasRequest(tags);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(tags, item.Tags), "ConnectionManager #9.1");
                }

                connectionManagers.Randomize();

                {
                    var queue = new WaitQueue<PullMulticastMetadatasEventArgs>();

                    var receiverConnection = connectionManagers[0];
                    var senderConnection = connectionManagers[1];

                    receiverConnection.PullMulticastMetadatasEvent += (object sender, PullMulticastMetadatasEventArgs e) =>
                    {
                        queue.Enqueue(e);
                    };

                    var digitalSignature = new DigitalSignature("123", DigitalSignatureAlgorithm.Rsa2048_Sha256);

                    var metadatas1 = new List<MulticastMetadata>();

                    for (int j = 0; j < 4; j++)
                    {
                        var key = new Key(HashAlgorithm.Sha256, new byte[32]);
                        var metadata = new Metadata(1, key, CompressionAlgorithm.Xz, CryptoAlgorithm.Aes256, new byte[32 + 32]);
                        var tag = new Tag("oooo", new byte[32]);
                        var miner = new Miner(CashAlgorithm.Version1, -1, TimeSpan.Zero);
                        var multicastMetadata = new MulticastMetadata("Type", tag, DateTime.UtcNow, metadata, miner, digitalSignature);

                        metadatas1.Add(multicastMetadata);
                    }

                    senderConnection.PushMulticastMetadatas(metadatas1);

                    var item = queue.Dequeue();
                    Assert.IsTrue(CollectionUtils.Equals(metadatas1, item.MulticastMetadatas), "ConnectionManager #10.1");
                }

                foreach (var connectionManager in connectionManagers)
                {
                    connectionManager.Dispose();
                }

                client.Close();
                server.Close();
            }
        }
    }
}
