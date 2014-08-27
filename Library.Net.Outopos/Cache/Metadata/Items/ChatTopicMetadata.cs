using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatTopicMetadata", Namespace = "http://Library/Net/Outopos")]
    sealed class ChatTopicMetadata : MulticastMetadata<ChatTopicMetadata, Chat>
    {
        public ChatTopicMetadata(Chat tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
